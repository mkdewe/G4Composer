using G4Composer.Server.Domain;

namespace G4Composer.Server.Tests.Domain;

/// <summary>
/// Tests for <see cref="TopologyPredictor"/> — the multi-stage rule set that ranks probable
/// canonical (Webba da Silva) topologies from the tetrad count and the three loop lengths.
/// Grounded in: Webba da Silva 2007 (steric loop minima), Woś et al. 2026 gkag590 (two-tetrad
/// central-loop determinant), Guédin/Bugaut consensus (three-tetrad short loops → parallel).
/// </summary>
public class TopologyPredictorTests
{
    // ── R2: RNA gate ─────────────────────────────────────────────────────────────
    [Fact]
    public void Rna_IsAlwaysParallel_HighConfidence()
    {
        var r = TopologyPredictor.Predict(3, [1, 1, 1], isRna: true);
        Assert.Single(r);
        Assert.True(r[0].IsParallel);
        Assert.Equal("-P-P-P", r[0].LoopNotation);
        Assert.Equal("high", r[0].Confidence);
    }

    // ── R5.B1 / Guédin: short DNA loops → parallel ranked first ──────────────────
    [Theory]
    [InlineData(new[] { 1, 1, 1 })]
    [InlineData(new[] { 1, 2, 1 })]
    [InlineData(new[] { 2, 1, 2 })]
    public void Dna_AllShortLoops_ParallelRankedFirst(int[] loops)
    {
        var r = TopologyPredictor.Predict(3, loops, isRna: false);
        Assert.True(r[0].IsParallel, $"loops [{string.Join(',', loops)}] should rank parallel first");
    }

    // ── R3.2 / R4.0: a 0-nt loop forces a propeller (parallel) ───────────────────
    [Fact]
    public void ZeroCentralLoop_ForcesParallelFirst()
    {
        var r = TopologyPredictor.Predict(3, [3, 0, 3], isRna: false);
        Assert.True(r[0].IsParallel);
        // The central token (index 3 of "-X-Y-Z") must be a propeller for every candidate,
        // because a 0-nt loop cannot be lateral or diagonal.
        Assert.All(r, c => Assert.Equal('P', c.LoopNotation[3]));
    }

    // ── R5.A / Woś gkag590: two-tetrad central-loop determinant ──────────────────
    [Fact]
    public void TwoTetrad_CentralThree_PrefersChair()
    {
        var r = TopologyPredictor.Predict(2, [2, 3, 2], isRna: false);
        Assert.False(r[0].IsParallel);                       // two-tetrad short folds are antiparallel
        Assert.Contains(r, c => c.Label.Contains("chair"));
    }

    [Fact]
    public void TwoTetrad_CentralFour_OffersBasket()
    {
        var r = TopologyPredictor.Predict(2, [2, 4, 2], isRna: false);
        Assert.Contains(r, c => c.Label.Contains("basket"));
    }

    // ── Every returned notation must thread all four G-tracts ────────────────────
    [Theory]
    [InlineData(new[] { 3, 3, 3 })]
    [InlineData(new[] { 1, 4, 1 })]
    [InlineData(new[] { 2, 5, 2 })]
    [InlineData(new[] { 3, 1, 1 })]
    [InlineData(new[] { 0, 1, 0 })]
    public void EveryCandidate_ThreadsFourDistinctTracks(int[] loops)
    {
        var r = TopologyPredictor.Predict(3, loops, isRna: false);
        Assert.NotEmpty(r);
        foreach (var c in r)
        {
            var path = SilvaTopology.BuildPath(c.LoopNotation, 3).Split(';');
            Assert.Equal(12, path.Length);                   // 4 stops × 3 planes
            Assert.Equal(4, path.Select(p => p[1]).Distinct().Count());
        }
    }

    // ── A parallel option is always available, ranking is capped ─────────────────
    [Fact]
    public void AlwaysOffersParallelFallback_AndCapsAtThree()
    {
        var r = TopologyPredictor.Predict(3, [3, 3, 3], isRna: false);
        Assert.InRange(r.Count, 1, 3);
        Assert.Contains(r, c => c.IsParallel);
    }

    // ── A-track rigidity (R4.6) penalises lateral/diagonal central loops ─────────
    [Fact]
    public void ARichCentralLoop_DoesNotRankAntiparallelFirst()
    {
        // central loop = 4 nt but A-rich → too rigid to fold lateral/diagonal.
        var aRich = new[] { false, true, false };
        var r = TopologyPredictor.Predict(3, [3, 4, 3], isRna: false, aRich);
        Assert.True(r[0].IsParallel);
    }
}
