using System.Text.RegularExpressions;

namespace G4Composer.Server.Domain;

/// <summary>
/// Predicts canonical (Webba da Silva) G-quadruplex topologies for a sequence from the three loop
/// lengths. The candidate space is the curated <see cref="SilvaCatalog"/> of experimentally-observed
/// folds — NOT a free enumeration of the P/L/D cube. Each catalog fold is scored by an EMPIRICAL
/// loop-type probability grid learned from deposited unimolecular G4 structures
/// (see <see cref="Outer"/>/<see cref="Central"/>); folds whose loop types are never seen at the
/// query loop lengths score 0 and are dropped.
///
/// Model: a fold's prior is the product of the per-loop probabilities
///     P(fold) ∝ P(t1 | l1, outer) · P(t2 | l2, central) · P(t3 | l3, outer)
/// over the catalog folds that remain admissible, then normalised over that space. Because the
/// candidates are the canonical subtypes (with their exact signed notation), both sign variants of
/// a family are offered (e.g. 6a -Lw-Ln-Lw AND 6b +Ln+Lw+Ln) — loop length narrows but does not
/// uniquely fix the topology, so the computed Quadro energy is the final arbiter.
///
/// Provenance: the grid is the smoothed P(type | position, length) measured over ~285 deposited DNA
/// G4 structures (out-quadro/inp), Dirichlet-smoothed (α=0.5). RNA is handled by a gate
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

    // The fallback models the top-ranked canonical folds and lets Quadro energy pick the winner.
    // Bounded so a permissive loop length (many admissible subtypes) does not spawn dozens of runs.
    public const int ModelCap = 10;

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

    // ── Natural-occurrence prior P(topology) ──────────────────────────────────────────
    // How often each loop-TYPE topology (sign-agnostic, ordered P/L/D) actually appears among
    // deposited single-chain G4 structures (ONQuadro corpus: 251 annotated). Parallel P-P-P
    // dominates (~51%). Combined multiplicatively with the loop-length likelihood so the ranked
    // probability reflects BOTH "what these loop lengths allow" AND "what nature actually prefers".
    // Counts are baked from the corpus; regenerate if the structure database grows substantially.
    private const double OccurrenceAlpha = 0.5;    // Laplace smoothing for unobserved triples
    private static readonly Dictionary<string, int> OccurrenceCounts = new()
    {
        ["PPP"] = 127, ["LLL"] = 37, ["LDL"] = 26, ["LLP"] = 21, ["PLL"] = 10,
        ["PDP"] = 7,   ["PDL"] = 6,  ["PPL"] = 6,  ["DPL"] = 4,  ["LPP"] = 3,
        ["DPD"] = 2,   ["LDP"] = 1,  ["LPL"] = 1,
    };

    private static double OccurrenceWeight(LoopType[] t)
    {
        OccurrenceCounts.TryGetValue($"{Token(t[0])}{Token(t[1])}{Token(t[2])}", out var c);
        return c + OccurrenceAlpha;
    }

    private static readonly Regex LoopTokenRegex =
        new(@"(?<kind>Lw|Ln|L|P|D)", RegexOptions.Compiled);

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

    // Parses a canonical Silva notation (e.g. "-LwD+Ln") into its three loop types; null if it does
    // not contain exactly three loop tokens.
    private static LoopType[]? ParseLoopTypes(string notation)
    {
        var types = LoopTokenRegex.Matches(notation)
            .Select(m => m.Groups["kind"].Value switch
            {
                "P" => LoopType.Propeller,
                "D" => LoopType.Diagonal,
                _   => LoopType.Lateral,   // L / Lw / Ln
            })
            .ToArray();
        return types.Length == 3 ? types : null;
    }

    /// <summary>The canonical folds modelled by Quadro, most probable first.</summary>
    public static IReadOnlyList<TopologyCandidate> Predict(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
        => ModeledFolds(RankAll(n, loops, isRna, aRich)).Select(s => s.Candidate).ToList();

    // The folds actually modelled: the top ModelCap by prior, but ALWAYS including the canonical
    // parallel baseline (-P-P-P) when admissible. Parallel is the most common G4 fold (and the one
    // the aligner confirms for canonical 4-tetrad cases), so the computed energy — not the
    // loop-length prior — should decide whether it wins. Without this guard a permissive set of
    // long loops can rank parallel out of the modelled window entirely.
    private static List<ScoredCandidate> ModeledFolds(IReadOnlyList<ScoredCandidate> ranked)
    {
        var top = ranked.Take(ModelCap).ToList();
        if (top.Any(s => s.Candidate.LoopNotation == ParallelNotation)) return top;

        var parallel = ranked.FirstOrDefault(s => s.Candidate.LoopNotation == ParallelNotation);
        if (parallel is null) return top;                  // parallel not admissible for these loops

        if (top.Count >= ModelCap) top[^1] = parallel;     // swap in for the weakest modelled fold
        else top.Add(parallel);
        return top;
    }

    private const string ParallelNotation = "-P-P-P";

    /// <summary>
    /// The admissible canonical-fold space with normalised prior probabilities — the basis for both
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
                "-P-P-P", true, "parallel (1a)", "high",
                "RNA: 2'-OH / C3'-endo sugar almost always forces a parallel fold"), 1.0)];

        var raw = new List<(TopologyCandidate Cand, double W)>();
        foreach (var sub in SilvaCatalog.All)
        {
            if (!sub.Predictable) continue;
            var types = ParseLoopTypes(sub.Notation);
            if (types is null) continue;
            // Posterior ∝ likelihood(loop lengths) × P(topology in nature).
            double w = FoldWeight(types, loops, aRich) * OccurrenceWeight(types);
            if (w <= 0) continue;                              // a loop type is forbidden here
            raw.Add((new TopologyCandidate(
                sub.Notation, sub.IsParallel, $"{sub.GroupName} ({sub.Code})",
                "", RationaleOf(types, loops, aRich, sub)), w));
        }

        if (raw.Count == 0)                                    // degenerate — always offer parallel
            raw.Add((ParallelFallback("no canonical Silva fold fits these loops"), 1.0));

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
    /// Human-facing determination: the inputs plus the full canonical-fold space. The top
    /// <see cref="ModelCap"/> admissible folds are modelled (<see cref="ScoredTopology.Built"/>);
    /// catalog subtypes ruled out for these loops appear at 0% with the reason.
    /// </summary>
    public static TopologyDetermination Determine(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
    {
        aRich ??= new bool[3];
        var ranked = RankAll(n, loops, isRna, aRich);
        var modeled = ModeledFolds(ranked).Select(s => s.Candidate.LoopNotation).ToHashSet();
        var admissible = ranked
            .Select(r => new ScoredTopology(
                r.Candidate.LoopNotation, r.Candidate.Label, r.Candidate.Confidence,
                r.Candidate.Rationale,
                Score: (int)Math.Round(r.Prob * 1000),   // prior per-mille (monotonic with probability)
                Probability: Math.Round(r.Prob * 100, 1),
                Built: modeled.Contains(r.Candidate.LoopNotation)))  // the folds actually sent to Quadro
            .ToList();

        var seen     = admissible.Select(a => a.LoopNotation).ToHashSet();
        var excluded = EnumerateExcluded(loops, isRna, seen);

        return new TopologyDetermination(
            n, loops.ToList(), isRna, aRich.ToList(),
            admissible.Concat(excluded).ToList());
    }

    // Canonical Silva subtypes that did NOT make the admissible space, each with the reason — so the
    // choice of set is transparent. Skipped for the RNA gate and the loops≠3 fallback.
    private static IReadOnlyList<ScoredTopology> EnumerateExcluded(
        int[] loops, bool isRna, HashSet<string> already)
    {
        if (isRna || loops.Length != 3) return [];

        var result = new List<ScoredTopology>();
        foreach (var sub in SilvaCatalog.All)
        {
            if (already.Contains(sub.Notation)) continue;      // admissible — already listed
            var reason = ExclusionReason(sub, loops);
            if (reason is null) continue;
            result.Add(new ScoredTopology(
                sub.Notation, $"{sub.GroupName} ({sub.Code})", "—", Rationale: "",
                Score: 0, Probability: 0, Built: false, ExcludedReason: reason));
        }
        return result.OrderBy(r => r.ExcludedReason).ThenBy(r => r.LoopNotation).ToList();
    }

    // null if the canonical subtype is admissible for these loops, else why it was ruled out.
    private static string? ExclusionReason(SilvaSubtypeInfo sub, int[] loops)
    {
        if (!sub.Predictable)
            return sub.Code switch
            {
                "1b"           => "left-handed only (not auto-predicted)",
                "5a"           => "RNA-only (no DNA experimental structure)",
                "12a" or "12b" => "long-distance diagonal artifact (excluded)",
                _              => "not auto-predicted",
            };

        var types = ParseLoopTypes(sub.Notation);
        if (types is null) return "unparseable notation";

        // A loop type never observed (probability 0) at this length/position.
        for (int i = 0; i < 3; i++)
        {
            bool central = i == 1;
            if (LoopProb(central, loops[i], types[i]) == 0)
                return types[i] switch
                {
                    LoopType.Diagonal => $"diagonal not formed at l{i + 1}={loops[i]} nt (needs ≥3)",
                    LoopType.Lateral  => $"lateral needs l{i + 1} ≥ 1 (l{i + 1}={loops[i]})",
                    _                 => $"propeller excluded at l{i + 1}={loops[i]}",
                };
        }
        return null;   // admissible (should already be listed)
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
    // their raw product, and the subtype. The UI normalises this raw product over the admissible
    // space to get the displayed probability (Σ = 100%).
    private static string RationaleOf(LoopType[] t, int[] loops, bool[] aRich, SilvaSubtypeInfo sub)
    {
        double f0 = LoopProb(false, loops[0], t[0]);
        double f1 = LoopProb(true,  loops[1], t[1]);
        double f2 = LoopProb(false, loops[2], t[2]);
        double raw = f0 * f1 * f2;
        bool atrack = false;
        for (int i = 0; i < 3; i++)
            if (aRich[i] && t[i] != LoopType.Propeller) { raw *= ATrackPenalty; atrack = true; }
        // Natural-occurrence fraction of this loop-type topology in the deposited corpus.
        double natPct = 100.0 * OccurrenceCounts.GetValueOrDefault(
            $"{Token(t[0])}{Token(t[1])}{Token(t[2])}") / OccurrenceTotal;
        return $"Silva {sub.Code} ({sub.GroupCode}): "
             + $"{Token(t[0])}@{loops[0]}nt={f0 * 100:F0}% · "
             + $"{Token(t[1])}@{loops[1]}nt={f1 * 100:F0}% · "
             + $"{Token(t[2])}@{loops[2]}nt={f2 * 100:F0}%"
             + (atrack ? " ×A-track" : "")
             + $" × nature {natPct:F1}% = {raw * (natPct / 100.0):F4} → {FamilyLabel(t)}";
    }

    private static readonly int OccurrenceTotal = OccurrenceCounts.Values.Sum();

    private static TopologyCandidate ParallelFallback(string why) =>
        new("-P-P-P", true, "parallel (1a)", "low", $"fallback: {why}");

    private static char Token(LoopType t) => t switch
    {
        LoopType.Propeller => 'P',
        LoopType.Diagonal  => 'D',
        _                  => 'L',
    };
}
