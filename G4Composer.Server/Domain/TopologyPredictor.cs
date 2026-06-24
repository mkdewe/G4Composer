namespace G4Composer.Server.Domain;

/// <summary>
/// Predicts canonical (Webba da Silva) G-quadruplex topologies for a sequence from the three
/// loop lengths, using an EMPIRICAL loop-type probability grid learned from deposited unimolecular
/// G4 structures (see <see cref="Outer"/>/<see cref="Central"/>).
///
/// Model: each fold is an assignment of a loop type (Propeller / Lateral / Diagonal) to the three
/// loops. Its prior probability is the product of the per-loop probabilities
///     P(fold) ∝ P(t1 | l1, outer) · P(t2 | l2, central) · P(t3 | l3, outer)
/// restricted to folds that are combinatorially valid and thread all four G-tracts, then
/// normalised over that admissible space. Every fold with P &gt; 0 is returned (and modelled by the
/// pipeline) — loop length narrows but does not uniquely fix the topology, so the computed energy
/// is the final arbiter.
///
/// Provenance: the grid is the smoothed P(type | position, length) measured over ~285 deposited
/// DNA G4 structures (out-quadro/inp), Dirichlet-smoothed (α=0.5). RNA is handled by a gate
/// (2'-OH / C3'-endo → parallel), validated against the same set (canonical RNA = 100% propeller).
/// </summary>
public static class TopologyPredictor
{
    public enum LoopType { Propeller, Lateral, Diagonal }   // enum order = matrix column order

    public sealed record TopologyCandidate(
        string LoopNotation,
        bool IsParallel,
        string Label,
        string Confidence,
        string Rationale);

    /// <summary>A fold with its normalised prior probability (0..1).</summary>
    public sealed record ScoredCandidate(TopologyCandidate Candidate, double Prob);

    private const int StrandCount = 4;

    // ── Empirical loop-type probability grid P(type | position, length) ───────────────
    // Rows = loop length 0..6 (index 6 = tail bin, length ≥6). Columns = {Propeller, Lateral,
    // Diagonal} (LoopType enum order). A 0 entry is sterically forbidden and keeps any fold using
    // it at probability 0. Derived from the deposited DNA G4 set, Dirichlet α=0.5 —
    // regenerate with tools/gen_loop_matrix.py.
    private const int MaxLenBin = 6;
    private static readonly double[][] Outer =
    {
        new[] { 1.0000, 0.0000, 0.0000 },   // 0
        new[] { 0.9669, 0.0331, 0.0000 },   // 1
        new[] { 0.3697, 0.6303, 0.0000 },   // 2
        new[] { 0.4613, 0.5354, 0.0034 },   // 3
        new[] { 0.0704, 0.8310, 0.0986 },   // 4
        new[] { 0.5294, 0.2941, 0.1765 },   // 5
        new[] { 0.0545, 0.9273, 0.0182 },   // 6+
    };
    private static readonly double[][] Central =
    {
        new[] { 1.0000, 0.0000, 0.0000 },   // 0
        new[] { 0.9342, 0.0658, 0.0000 },   // 1
        new[] { 0.7090, 0.2910, 0.0000 },   // 2
        new[] { 0.2487, 0.7056, 0.0457 },   // 3
        new[] { 0.0794, 0.3016, 0.6190 },   // 4
        new[] { 0.2195, 0.0244, 0.7561 },   // 5
        new[] { 0.3333, 0.4359, 0.2308 },   // 6+
    };

    // An A-rich loop is rigid and resists folding back (lateral/diagonal); downweight strongly
    // (enough to override even a high diagonal prior) but keep > 0 so the fold is still offered.
    private const double ATrackPenalty = 0.05;

    private static double LoopProb(bool central, int length, LoopType t)
        => (central ? Central : Outer)[Math.Clamp(length, 0, MaxLenBin)][(int)t];

    private static double FoldWeight(LoopType[] t, int[] loops, bool[] aRich)
    {
        double p = LoopProb(false, loops[0], t[0])
                 * LoopProb(true,  loops[1], t[1])
                 * LoopProb(false, loops[2], t[2]);
        for (int i = 0; i < 3; i++)
            if (aRich[i] && t[i] != LoopType.Propeller) p *= ATrackPenalty;
        return p;
    }

    /// <summary>Every admissible fold (P &gt; 0), most probable first. All are modelled by Quadro.</summary>
    public static IReadOnlyList<TopologyCandidate> Predict(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
        => RankAll(n, loops, isRna, aRich).Select(s => s.Candidate).ToList();

    /// <summary>
    /// The admissible fold space with normalised prior probabilities — the basis for both
    /// <see cref="Predict"/> and <see cref="Determine"/>.
    /// </summary>
    public static IReadOnlyList<ScoredCandidate> RankAll(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
    {
        aRich ??= new bool[3];
        if (loops.Length != 3)
            return [new(ParallelFallback("loop count ≠ 3"), 1.0)];

        // RNA gate: 2'-OH / C3'-endo almost always forces a parallel propeller fold.
        if (isRna)
            return [new(new TopologyCandidate(
                "-P-P-P", true, "parallel (propeller×3)", "high",
                "RNA: 2'-OH / C3'-endo sugar almost always forces a parallel fold"), 1.0)];

        var allTypes = new[] { LoopType.Propeller, LoopType.Lateral, LoopType.Diagonal };
        var raw = new List<(TopologyCandidate Cand, double W)>();
        foreach (var t1 in allTypes)
        foreach (var t2 in allTypes)
        foreach (var t3 in allTypes)
        {
            var types = new[] { t1, t2, t3 };
            if (!CombinatoriallyValid(types)) continue;       // no D-D, no D-L-D
            var notation = TryBuildValidNotation(types);       // must thread all four tracks
            if (notation is null) continue;
            double w = FoldWeight(types, loops, aRich);
            if (w <= 0) continue;                              // a loop type is forbidden here
            bool parallel = types.All(x => x == LoopType.Propeller);
            raw.Add((new TopologyCandidate(
                notation, parallel, FamilyLabel(types), "", RationaleOf(types, loops, aRich)), w));
        }

        if (raw.Count == 0)                                    // degenerate — always offer parallel
            raw.Add((ParallelFallback("no admissible fold for these loops"), 1.0));

        double sum = raw.Sum(x => x.W);
        return raw
            .Select(x =>
            {
                double prob = sum > 0 ? x.W / sum : 0;
                return new ScoredCandidate(x.Cand with { Confidence = ConfidenceOf(prob) }, prob);
            })
            .OrderByDescending(s => s.Prob)
            .ToList();
    }

    /// <summary>
    /// Human-facing determination: the inputs plus the full fold space. Admissible folds carry a
    /// probability and are all modelled; ruled-out folds appear at 0% with the reason.
    /// </summary>
    public static TopologyDetermination Determine(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
    {
        aRich ??= new bool[3];
        var ranked = RankAll(n, loops, isRna, aRich);
        var admissible = ranked
            .Select(r => new ScoredTopology(
                r.Candidate.LoopNotation, r.Candidate.Label, r.Candidate.Confidence,
                r.Candidate.Rationale,
                Score: (int)Math.Round(r.Prob * 1000),   // prior per-mille (monotonic with probability)
                Probability: Math.Round(r.Prob * 100, 1),
                Built: true))                              // every admissible fold is modelled
            .ToList();

        var seen     = admissible.Select(a => a.LoopNotation).ToHashSet();
        var excluded = EnumerateExcluded(loops, isRna, seen);

        return new TopologyDetermination(
            n, loops.ToList(), isRna, aRich.ToList(),
            admissible.Concat(excluded).ToList());
    }

    // Enumerates the full P/L/D cube and reports the folds that did NOT make the admissible space,
    // each with the reason. Skipped for the RNA gate and the loops≠3 fallback.
    private static IReadOnlyList<ScoredTopology> EnumerateExcluded(
        int[] loops, bool isRna, HashSet<string> already)
    {
        if (isRna || loops.Length != 3) return [];
        var allTypes = new[] { LoopType.Propeller, LoopType.Lateral, LoopType.Diagonal };

        var result = new List<ScoredTopology>();
        foreach (var t1 in allTypes)
        foreach (var t2 in allTypes)
        foreach (var t3 in allTypes)
        {
            var types  = new[] { t1, t2, t3 };
            var reason = ExclusionReason(types, loops);
            if (reason is null) continue;                          // admissible — already listed
            var notation = TryBuildValidNotation(types) ?? DisplayPattern(types);
            if (already.Contains(notation)) continue;
            result.Add(new ScoredTopology(
                notation, FamilyLabel(types), "—", Rationale: "", Score: 0, Probability: 0,
                Built: false, ExcludedReason: reason));
        }
        return result.OrderBy(r => r.ExcludedReason).ThenBy(r => r.LoopNotation).ToList();
    }

    // null if the fold is admissible (P > 0 and threadable), else why it was ruled out.
    private static string? ExclusionReason(LoopType[] t, int[] loops)
    {
        // 1. Structurally forbidden for ANY loop length (Webba da Silva geometry).
        if ((t[0] == LoopType.Diagonal && t[1] == LoopType.Diagonal) ||
            (t[1] == LoopType.Diagonal && t[2] == LoopType.Diagonal))
            return "forbidden: two adjacent diagonal loops (any loop length)";
        if (t[0] == LoopType.Diagonal && t[1] == LoopType.Lateral && t[2] == LoopType.Diagonal)
            return "forbidden: diagonal–lateral–diagonal cannot form (any loop length)";

        // 2. Sequence-specific: a loop type never observed (probability 0) at this length/position.
        for (int i = 0; i < 3; i++)
        {
            bool central = i == 1;
            if (LoopProb(central, loops[i], t[i]) == 0)
                return t[i] switch
                {
                    LoopType.Diagonal => $"diagonal not formed at l{i + 1}={loops[i]} nt (needs ≥3)",
                    LoopType.Lateral  => $"lateral needs l{i + 1} ≥ 1 (l{i + 1}={loops[i]})",
                    _                 => $"propeller excluded at l{i + 1}={loops[i]}",
                };
        }

        if (TryBuildValidNotation(t) is null) return "cannot thread all four G-tracts";
        return null;
    }

    // Sign-free display for a non-threadable triple (real notations use +/- signs, e.g. -P-D+L).
    private static string DisplayPattern(LoopType[] t) => string.Join("·", t.Select(Token));

    private static bool CombinatoriallyValid(LoopType[] t)
    {
        if (t[0] == LoopType.Diagonal && t[1] == LoopType.Diagonal) return false;
        if (t[1] == LoopType.Diagonal && t[2] == LoopType.Diagonal) return false;
        if (t[0] == LoopType.Diagonal && t[1] == LoopType.Lateral && t[2] == LoopType.Diagonal) return false;
        return true;
    }

    // Confidence band from the normalised prior probability.
    private static string ConfidenceOf(double p) => p >= 0.40 ? "high" : p >= 0.15 ? "medium" : "low";

    private static string FamilyLabel(LoopType[] t)
    {
        if (t.All(x => x == LoopType.Propeller)) return "parallel (propeller×3)";
        if (t.All(x => x == LoopType.Lateral))  return "antiparallel chair (lateral×3)";
        if (t.Count(x => x == LoopType.Diagonal) == 1) return "antiparallel basket (one diagonal)";
        return "hybrid (mixed loops)";
    }

    // Spells out the probability arithmetic: the three per-loop priors, the A-track factor if any,
    // their raw product, and the family. The UI normalises this raw product over the admissible
    // space to get the displayed probability (Σ = 100%).
    private static string RationaleOf(LoopType[] t, int[] loops, bool[] aRich)
    {
        double f0 = LoopProb(false, loops[0], t[0]);
        double f1 = LoopProb(true,  loops[1], t[1]);
        double f2 = LoopProb(false, loops[2], t[2]);
        double raw = f0 * f1 * f2;
        bool atrack = false;
        for (int i = 0; i < 3; i++)
            if (aRich[i] && t[i] != LoopType.Propeller) { raw *= ATrackPenalty; atrack = true; }
        return $"{Token(t[0])}@{loops[0]}nt={f0 * 100:F0}% · "
             + $"{Token(t[1])}@{loops[1]}nt={f1 * 100:F0}% · "
             + $"{Token(t[2])}@{loops[2]}nt={f2 * 100:F0}%"
             + (atrack ? " ×A-track" : "")
             + $" = {raw:F4} raw → {FamilyLabel(t)}";
    }

    private static TopologyCandidate ParallelFallback(string why) =>
        new("-P-P-P", true, "parallel (propeller×3)", "low", $"fallback: {why}");

    // ── Sign search: pick a sign assignment that threads all four G-tracts ────────
    private static readonly int[][] SignCombos =
    [
        [-1, -1, -1], [-1, -1, 1], [-1, 1, -1], [1, -1, -1],
        [-1, 1, 1],  [1, -1, 1],  [1, 1, -1],  [1, 1, 1],
    ];

    private static string? TryBuildValidNotation(LoopType[] types)
    {
        foreach (var signs in SignCombos)
        {
            if (!Walk(types, signs)) continue;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 3; i++)
                sb.Append(signs[i] < 0 ? '-' : '+').Append(Token(types[i]));
            return sb.ToString();
        }
        return null;
    }

    private static bool Walk(LoopType[] types, int[] signs)
    {
        int track = 1;
        var seen = new HashSet<int> { track };
        for (int i = 0; i < 3; i++)
        {
            int mag = types[i] == LoopType.Diagonal ? 2 : 1;
            track = Wrap(track + signs[i] * mag);
            seen.Add(track);
        }
        return seen.Count == StrandCount;
    }

    private static int Wrap(int t) => ((t - 1) % StrandCount + StrandCount) % StrandCount + 1;

    private static char Token(LoopType t) => t switch
    {
        LoopType.Propeller => 'P',
        LoopType.Diagonal  => 'D',
        _                  => 'L',
    };
}
