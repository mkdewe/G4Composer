namespace G4Composer.Server.Domain;

/// <summary>
/// Predicts the most probable canonical (Webba da Silva) G-quadruplex topologies for an
/// arbitrary nucleotide sequence, from the number of tetrads (N) and the three loop lengths.
///
/// The output is a RANKED list of candidates (most probable first). Multiple candidates are
/// returned whenever the rules do not single out one fold — the pipeline then builds and runs
/// quadro on each, letting the computed energy arbitrate. This deliberately mirrors the
/// experimental reality that loop length narrows, but does not uniquely fix, the topology.
///
/// Scientific basis:
///   * Webba da Silva, Chem. Eur. J. 2007, 13, 9738 — geometric formalism; steric minimum
///     loop lengths per loop type (1 nt → propeller; ≥3 nt → diagonal possible; etc.),
///     forbidden combinations (no two adjacent diagonals; no diagonal-lateral-diagonal).
///   * Woś et al., Nucleic Acids Res. 2026, gkag590 — for TWO-tetrad G4s the central loop
///     length is the main determinant (l2=3 → antiparallel chair; l2=4 → antiparallel basket;
///     l2∈{1,2} → basket/chair). Two-tetrad short-loop folds are essentially all antiparallel.
///   * Guédin / Bugaut-Balasubramanian consensus — three-tetrad: all-short loops (with a 1-nt
///     loop) → parallel propeller; long central loop → antiparallel/hybrid.
///   * RNA G4s are almost exclusively parallel (2'-OH + C3'-endo disfavour syn).
///
/// A 0-nt loop forces a propeller at that position (parallel contribution) — it cannot be
/// lateral or diagonal.
/// </summary>
public static class TopologyPredictor
{
    public enum LoopType { Propeller, Lateral, Diagonal }

    /// <param name="LoopNotation">Silva loop notation in <see cref="SilvaTopology"/> vocabulary
    /// (e.g. <c>-P-P-P</c>, <c>-L-L-L</c>, <c>-L+D+L</c>) — guaranteed to thread all four G-tracts.</param>
    /// <param name="IsParallel">True for the all-propeller parallel fold (drives orient/twist defaults).</param>
    /// <param name="Label">Human-readable family name for UI.</param>
    /// <param name="Confidence">"high" | "medium" | "low".</param>
    /// <param name="Rationale">Which rule produced this candidate (logs/UI).</param>
    public sealed record TopologyCandidate(
        string LoopNotation,
        bool IsParallel,
        string Label,
        string Confidence,
        string Rationale);

    private const int StrandCount = 4;

    /// <summary>
    /// Ranks the probable topologies. <paramref name="loops"/> must have exactly three elements
    /// (l1, l2, l3). <paramref name="aRich"/> (optional) flags loops that are ≥50% adenine and
    /// thus too rigid to fold as lateral/diagonal (A-track effect).
    /// </summary>
    public static IReadOnlyList<TopologyCandidate> Predict(
        int n, int[] loops, bool isRna, bool[]? aRich = null)
    {
        aRich ??= new bool[3];
        if (loops.Length != 3)
            return [ParallelFallback("loop count ≠ 3")];

        // R2 — RNA gate: parallel, high confidence.
        if (isRna)
            return [new TopologyCandidate(
                "-P-P-P", true, "parallel (propeller×3)", "high",
                "RNA: 2'-OH / C3'-endo sugar almost always forces a parallel fold")];

        int central = loops[1];
        bool anyZero    = loops.Any(l => l == 0);
        bool allShort   = loops.All(l => l <= 2);
        bool hasOne     = loops.Any(l => l <= 1);

        var scored = new List<(TopologyCandidate Cand, int Score)>();

        // Enumerate all sterically allowed loop-type triples, score by literature priors.
        var allowed = loops.Select(AllowedTypes).ToArray();
        foreach (var t1 in allowed[0])
        foreach (var t2 in allowed[1])
        foreach (var t3 in allowed[2])
        {
            var types = new[] { t1, t2, t3 };
            if (!CombinatoriallyValid(types)) continue;          // R4.5 — no D-D, no D-L-D
            var notation = TryBuildValidNotation(types);          // must thread all 4 tracks
            if (notation is null) continue;

            int score = Score(types, n, central, loops, aRich, anyZero, allShort, hasOne);
            if (score <= 0) continue;

            bool parallel = types.All(t => t == LoopType.Propeller);
            scored.Add((new TopologyCandidate(
                notation, parallel, FamilyLabel(types),
                ConfidenceOf(score), RationaleOf(types, n, central, anyZero)), score));
        }

        // Always keep a parallel option on the table (most common / most stable fold).
        if (!scored.Any(s => s.Cand.IsParallel))
            scored.Add((ParallelFallback("parallel always offered as fallback"), 1));

        return scored
            .GroupBy(s => s.Cand.LoopNotation)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(s => s.Score)
            .Take(3)
            .Select(s => s.Cand)
            .ToList();
    }

    // ── Steric allowed loop types per loop length (Webba da Silva minima) ─────────
    private static LoopType[] AllowedTypes(int len) => len switch
    {
        0 => [LoopType.Propeller],                                   // forced propeller
        1 => [LoopType.Propeller, LoopType.Lateral],
        2 => [LoopType.Lateral, LoopType.Propeller],
        3 => [LoopType.Diagonal, LoopType.Lateral, LoopType.Propeller],
        _ => [LoopType.Lateral, LoopType.Diagonal, LoopType.Propeller], // ≥4 (lateral = wide)
    };

    private static bool CombinatoriallyValid(LoopType[] t)
    {
        // No two adjacent diagonals; diagonal-lateral-diagonal is essentially impossible.
        if (t[0] == LoopType.Diagonal && t[1] == LoopType.Diagonal) return false;
        if (t[1] == LoopType.Diagonal && t[2] == LoopType.Diagonal) return false;
        if (t[0] == LoopType.Diagonal && t[1] == LoopType.Lateral && t[2] == LoopType.Diagonal) return false;
        return true;
    }

    // ── Literature-prior scoring ──────────────────────────────────────────────────
    private static int Score(
        LoopType[] t, int n, int central, int[] loops, bool[] aRich,
        bool anyZero, bool allShort, bool hasOne)
    {
        bool parallel = t.All(x => x == LoopType.Propeller);
        bool chair    = t.All(x => x == LoopType.Lateral);
        bool basket   = t.Count(x => x == LoopType.Diagonal) == 1 && !parallel;

        int score = parallel ? 50 : chair ? 30 : basket ? 30 : 18; // base by family (hybrid=18)

        // Strong parallel drivers (Guédin consensus; 0-nt loop forces propeller).
        if (parallel)
        {
            if (anyZero)                 score += 60;
            if (allShort && hasOne)      score += 45;
        }

        if (n == 2)
        {
            // Woś gkag590 — two-tetrad central-loop determinant (antiparallel family).
            switch (central)
            {
                case 1: if (basket) score += 40; break;   // d+pd
                case 2: if (chair)  score += 35; else if (basket) score += 12; break; // −l−l−l
                case 3: if (chair)  score += 48; break;   // +l+l+l (most prevalent)
                case 4: if (basket) score += 40; break;   // −ld+l
            }
            if (parallel) score -= 10; // two-tetrad short-loop folds are rarely parallel
        }
        else // n ≥ 3
        {
            if (central >= 4 && (basket || chair)) score += 35;  // long central → antiparallel/hybrid
            if (allShort && hasOne && parallel)    score += 30;
        }

        // A-track effect: a rigid A-rich loop resists lateral/diagonal folding.
        for (int i = 0; i < 3; i++)
            if (aRich[i] && t[i] != LoopType.Propeller) score -= 25;

        return score;
    }

    private static string ConfidenceOf(int s) => s >= 85 ? "high" : s >= 45 ? "medium" : "low";

    private static string FamilyLabel(LoopType[] t)
    {
        if (t.All(x => x == LoopType.Propeller)) return "parallel (propeller×3)";
        if (t.All(x => x == LoopType.Lateral))  return "antiparallel chair (lateral×3)";
        if (t.Count(x => x == LoopType.Diagonal) == 1) return "antiparallel basket (one diagonal)";
        return "hybrid (mixed loops)";
    }

    private static string RationaleOf(LoopType[] t, int n, int central, bool anyZero)
    {
        if (t.All(x => x == LoopType.Propeller))
            return anyZero
                ? "0-nt loop forces propeller → parallel"
                : $"short loops favour parallel propeller (N={n})";
        if (n == 2)
            return $"two-tetrad, central loop = {central} (Woś gkag590)";
        return $"central loop = {central} admits {FamilyLabel(t)}";
    }

    private static TopologyCandidate ParallelFallback(string why) =>
        new("-P-P-P", true, "parallel (propeller×3)", "low", $"fallback: {why}");

    // ── Sign search: pick a sign assignment that threads all four G-tracts ────────
    // Tries all-negative first so the canonical parallel fold comes out as "-P-P-P".
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

    // Walks the four G-tracts; returns true iff all four distinct tracks are visited.
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
