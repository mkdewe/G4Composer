using G4Composer.Server.Domain;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

/// <summary>
/// Auto-generates QuadroInput instances for Quadro14L from a nucleotide sequence.
///
/// Supports two modes:
///   TryGenerate      — legacy: finds G-tracts from sequence directly.
///   TryGenerateFromGqrs — new: uses gqrs motif positions for G-tract placement.
///
/// Topology defaults:
///   RNA (uppercase U present) → parallel topology (+P+P+P), orient all-+, twist 29°.
///   DNA (no uppercase U)       → antiparallel UDUD (-Lw-Ln-Lw), alternating orient, twist 19°/37°/…
/// </summary>
public static class G4TopologyGenerator
{
    private const string ParallelLoops     = "+P+P+P";
    private const string AntiparallelLoops = "-Lw-Ln-Lw";   // UDUD 6a (most common antiparallel chair)

    /// <summary>
    /// Returns a fully populated QuadroInput ready for serialisation, or null
    /// if the sequence cannot form a valid G4 (fewer than 4 G-tracts, or any
    /// G-tract shorter than 1).
    /// </summary>
    /// <param name="toolName">Name of the RNA tool (used as the structure name in .inp).</param>
    /// <param name="sequence">Nucleotide sequence.</param>
    /// <param name="rnaStructure">
    /// Secondary structure predicted by the RNA tool in dot-bracket notation (e.g. "...(....)...").
    /// Used as the base of the Quadro structure field: G-tetrad positions (within the 4 G-tracts)
    /// are replaced with '^', while Watson-Crick pairs ('('/')') and loops ('.') from the RNA
    /// prediction remain. If null or length-mismatched, all-dots is used as the base.
    /// </param>
    public static QuadroInput? TryGenerate(string toolName, string sequence, string? rnaStructure)
    {
        if (string.IsNullOrWhiteSpace(sequence)) return null;

        // Uppercase T is invalid in RNA — treat the whole sequence as DNA.
        if (sequence.Contains('T'))
            sequence = sequence.ToLowerInvariant();

        var gTracts = FindGTracts(sequence);
        if (gTracts.Count < 4) return null;

        // Use only the first 4 G-tracts (standard intramolecular G4).
        var four = gTracts.Take(4).ToList();
        int n    = Math.Min(4, four.Min(t => t.Length));
        if (n < 1) return null;

        var structure = BuildCombinedStructure(sequence, four, n, rnaStructure);
        var chi       = new string('.', sequence.Length);
        var shugar    = BuildShugar(sequence);
        var orient    = BuildParallelOrient(n);
        var rise      = BuildRise(n);
        var twist     = BuildTwist(n);
        var path      = SilvaTopology.BuildPath(ParallelLoops, n)
                            .Split(';')
                            .ToList();

        return new QuadroInput
        {
            Name       = toolName,
            Sequence   = sequence,
            Structure  = structure,
            Chi        = chi,
            Sugar      = shugar,
            Orient     = orient,
            Rise       = rise,
            Twist      = twist,
            Path       = path,
        };
    }

    // ── gqrs-based generation ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a QuadroInput from a gqrs motif. Uses gqrs G-tract positions instead
    /// of searching for G-tracts in the sequence. Topology defaults: RNA → parallel,
    /// DNA → antiparallel UDUD (6a).
    /// </summary>
    public static QuadroInput? TryGenerateFromGqrs(
        string name, string sequence, string? rnaStructure, GqrsMotif motif)
    {
        if (string.IsNullOrWhiteSpace(sequence)) return null;
        if (motif.Tetrads < 1 || motif.Tetrads > 4) return null;

        // Normalise: uppercase T → treat as DNA
        if (sequence.Contains('T'))
            sequence = sequence.ToLowerInvariant();

        int n = motif.Tetrads;
        var gTracts = new List<(int Start, int Length)>
        {
            (motif.Tetrad1, n),
            (motif.Tetrad2, n),
            (motif.Tetrad3, n),
            (motif.Tetrad4, n),
        };

        // Validate all G-tract positions are within the sequence
        foreach (var (start, len) in gTracts)
        {
            if (start < 0 || start + len > sequence.Length) return null;
        }

        var structure = BuildCombinedStructure(sequence, gTracts, n, rnaStructure);
        var chi       = new string('.', sequence.Length);
        var shugar    = BuildShugar(sequence);

        bool isRna = sequence.Any(char.IsUpper);

        // Antiparallel lateral loops (Lw/Ln) need ≥2 nt to span the groove.
        // For DNA, fall back to parallel propeller when any loop is too short.
        int loopLen1 = motif.Tetrad2 - motif.Tetrad1 - n;
        int loopLen2 = motif.Tetrad3 - motif.Tetrad2 - n;
        int loopLen3 = motif.Tetrad4 - motif.Tetrad3 - n;
        bool useAntiparallel = !isRna && loopLen1 >= 2 && loopLen2 >= 2 && loopLen3 >= 2;

        var loops  = useAntiparallel ? AntiparallelLoops : ParallelLoops;
        var orient = useAntiparallel ? SilvaTopology.BuildDefaultOrient(n) : BuildParallelOrient(n);
        var twist  = BuildTwistFromOrient(orient);
        var rise   = BuildRise(n);
        var path   = SilvaTopology.BuildPath(loops, n).Split(';').ToList();

        return new QuadroInput
        {
            Name       = name,
            Sequence   = sequence,
            Structure  = structure,
            Chi        = chi,
            Sugar      = shugar,
            Orient     = orient,
            Rise       = rise,
            Twist      = twist,
            Path       = path,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the Quadro structure field by combining the RNA secondary structure prediction
    /// with G-tetrad markers.
    ///
    /// Algorithm:
    ///   1. Start from the RNA dot-bracket prediction (or all-dots if unavailable/length mismatch).
    ///   2. Build a bracket pair-map so we can maintain parenthesis balance.
    ///   3. For each G in the 4 G-tracts (up to nTetrads), stamp '^'.
    ///      - If the position was '(' or ')' (Watson-Crick paired), first clear its
    ///        matching partner to '.' — otherwise the resulting structure would have
    ///        unmatched brackets that cause Quadro ERROR 105.
    ///   4. All other positions keep their RNA prediction character ('.', '(', ')').
    /// </summary>
    private static string BuildCombinedStructure(
        string sequence,
        IReadOnlyList<(int Start, int Length)> gTracts,
        int nTetrads,
        string? rnaStructure)
    {
        // Base: RNA prediction if valid, otherwise all-dots
        var chars = (rnaStructure is not null && rnaStructure.Length == sequence.Length)
            ? rnaStructure.ToCharArray()
            : new string('.', sequence.Length).ToCharArray();

        // Build pair map from the original RNA prediction so we know which '(' matches which ')'.
        var pairMap = BuildPairMap(chars);

        foreach (var (start, length) in gTracts)
        {
            for (int k = 0; k < Math.Min(nTetrads, length); k++)
            {
                int pos = start + k;
                if (chars[pos] == '(' || chars[pos] == ')')
                {
                    // Remove the Watson-Crick partner bracket to keep parens balanced.
                    int partner = pairMap[pos];
                    if (partner >= 0) chars[partner] = '.';
                }
                chars[pos] = '^';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Returns a map where map[i] = j if structure[i] and structure[j] are matched brackets,
    /// or -1 if the position is unpaired or unmatched.
    /// </summary>
    private static int[] BuildPairMap(char[] structure)
    {
        var map = new int[structure.Length];
        Array.Fill(map, -1);
        var stack = new Stack<int>();
        for (int i = 0; i < structure.Length; i++)
        {
            if (structure[i] == '(')
                stack.Push(i);
            else if (structure[i] == ')' && stack.Count > 0)
            {
                int j = stack.Pop();
                map[i] = j;
                map[j] = i;
            }
        }
        return map;
    }

    private static string BuildShugar(string sequence)
        => new(sequence.Select(c => char.IsUpper(c) ? 'N' : 'S').ToArray());

    private static string BuildParallelOrient(int n)
        => string.Join(";", "ABCD".Take(n).Select(l => $"{l}+"));

    private static string BuildRise(int n)
        => n <= 1 ? "3.4" : string.Join(";", Enumerable.Repeat("3.4", n - 1));

    private static string BuildTwist(int n)
        => n <= 1 ? "29" : string.Join(";", Enumerable.Repeat("29", n - 1));

    /// <summary>
    /// Computes helical twist (N-1 values for N tetrad planes) from the orient string.
    /// Lookup: ++→27°, --→29°, +−→19°, −+→37°.
    /// Works for both parallel (all +) and antiparallel topologies.
    /// </summary>
    private static string BuildTwistFromOrient(string orient)
    {
        var signs = orient.Split(';').Select(s => s.TrimStart('A', 'B', 'C', 'D')).ToArray();
        if (signs.Length <= 1) return "27";

        static string Twist(string a, string b) => (a, b) switch
        {
            ("+", "-") => "19",
            ("-", "+") => "37",
            ("+", "+") => "27",
            _          => "29",
        };

        return string.Join(";", Enumerable.Range(0, signs.Length - 1)
            .Select(i => Twist(signs[i], signs[i + 1])));
    }

    // Only G-runs of length >= 2 are considered. A single isolated G is not a G-tract
    // and causes nTetrads=1 when it happens to be one of the first four runs found.
    private const int MinTractLength = 2;

    private static List<(int Start, int Length)> FindGTracts(string sequence)
    {
        var tracts = new List<(int Start, int Length)>();
        int i = 0;
        while (i < sequence.Length)
        {
            if (char.ToLowerInvariant(sequence[i]) == 'g')
            {
                int start = i;
                while (i < sequence.Length && char.ToLowerInvariant(sequence[i]) == 'g')
                    i++;
                int length = i - start;
                if (length >= MinTractLength)
                    tracts.Add((start, length));
            }
            else { i++; }
        }
        return tracts;
    }
}
