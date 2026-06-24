using G4Composer.Server.Domain;

namespace G4Composer.Server.Tests.Domain;

/// <summary>
/// Tests for <see cref="TopologyPredictor"/> — the empirical loop-type probability model that
/// ranks canonical (Webba da Silva) topologies from the three loop lengths. The probability grid
/// is learned from deposited unimolecular G4 structures; predictions are data-driven, so these
/// tests assert structural invariants (threading, RNA gate, propeller-forcing, A-track) rather
/// than fixed literature outcomes.
/// </summary>
public class TopologyPredictorTests
{
    // ── RNA gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Rna_IsAlwaysParallel_HighConfidence()
    {
        var r = TopologyPredictor.Predict(3, [1, 1, 1], isRna: true);
        Assert.Single(r);
        Assert.True(r[0].IsParallel);
        Assert.Equal("-P-P-P", r[0].LoopNotation);
        Assert.Equal("high", r[0].Confidence);
    }

    // ── Parallel is always an offered fold for short loops ───────────────────────
    [Theory]
    [InlineData(new[] { 1, 1, 1 })]
    [InlineData(new[] { 1, 2, 1 })]
    [InlineData(new[] { 2, 1, 2 })]
    public void Dna_ShortLoops_OfferParallel(int[] loops)
    {
        var r = TopologyPredictor.Predict(3, loops, isRna: false);
        Assert.Contains(r, c => c.IsParallel);
    }

    // ── A 0-nt loop forces a propeller at that position (it cannot fold back) ─────
    [Fact]
    public void ZeroCentralLoop_ForcesPropellerCentre()
    {
        var r = TopologyPredictor.Predict(3, [3, 0, 3], isRna: false);
        Assert.NotEmpty(r);
        // The central token of "-X-Y-Z" is at index 3; a 0-nt loop admits only a propeller there.
        Assert.All(r, c => Assert.Equal('P', c.LoopNotation[3]));
    }

    // ── Central-loop length drives the antiparallel sub-type (data-derived) ──────
    [Fact]
    public void CentralThree_RanksChairTop()
    {
        // Outer 2-nt loops favour lateral, a 3-nt central loop favours lateral → all-lateral chair.
        var r = TopologyPredictor.Predict(2, [2, 3, 2], isRna: false);
        Assert.False(r[0].IsParallel);
        Assert.Contains("chair", r[0].Label);
    }

    [Fact]
    public void CentralFour_RanksBasketTop()
    {
        // A 4-nt central loop is mostly diagonal → antiparallel basket on top.
        var r = TopologyPredictor.Predict(2, [2, 4, 2], isRna: false);
        Assert.Contains("basket", r[0].Label);
        Assert.Equal('D', r[0].LoopNotation[3]);   // central loop is the diagonal
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

    // ── The full admissible space is returned (no cap) and always includes parallel ─
    [Fact]
    public void ReturnsFullAdmissibleSpace_IncludingParallel()
    {
        var r = TopologyPredictor.Predict(3, [3, 3, 3], isRna: false);
        Assert.Contains(r, c => c.IsParallel);
        Assert.True(r.Count >= 3, "all P>0 folds should be returned, not a small fixed cap");
    }

    // ── A-track rigidity pushes an A-rich loop toward propeller, away from diagonal ─
    [Fact]
    public void ARichCentralLoop_DrivesPropellerCentre()
    {
        // 4-nt central loop would normally be diagonal (basket); when A-rich it is too rigid,
        // so the top fold's central loop becomes a propeller instead.
        var plain = TopologyPredictor.Predict(3, [3, 4, 3], isRna: false);
        Assert.Equal('D', plain[0].LoopNotation[3]);          // baseline: diagonal central

        var aRich = TopologyPredictor.Predict(3, [3, 4, 3], isRna: false, [false, true, false]);
        Assert.Equal('P', aRich[0].LoopNotation[3]);          // A-track → propeller central
    }

    // ── Determine exposes the full space: admissible (P>0) + ruled-out (0%) ──────
    [Fact]
    public void Determine_ListsRuledOutFolds_WithReasons_AndAdmissibleSumTo100()
    {
        // Short loops (len 1) forbid diagonals, so diagonal folds are ruled out at 0%.
        var d = TopologyPredictor.Determine(3, [1, 1, 1], isRna: false);

        Assert.Equal(3, d.Tetrads);
        Assert.Equal([1, 1, 1], d.Loops);
        Assert.False(d.IsRna);

        var admissible = d.Ranked.Where(r => r.ExcludedReason is null).ToList();
        var excluded   = d.Ranked.Where(r => r.ExcludedReason is not null).ToList();

        Assert.NotEmpty(admissible);
        Assert.NotEmpty(excluded);
        Assert.All(excluded, r => Assert.Equal(0, r.Score));
        Assert.All(excluded, r => Assert.Equal(0, r.Probability));
        Assert.Contains(excluded, r => r.ExcludedReason!.Contains("diagonal"));
        Assert.All(admissible, r => Assert.True(r.Built));    // every admissible fold is modelled

        Assert.InRange(admissible.Sum(r => r.Probability), 99.0, 101.0);
    }

    [Fact]
    public void Determine_DiagonalLateralDiagonal_ReportsStructuralForbiddance_NotShortLoops()
    {
        var d = TopologyPredictor.Determine(4, [2, 2, 2], isRna: false);
        var dld = d.Ranked.FirstOrDefault(r =>
            new string([.. r.LoopNotation.Where(char.IsLetter)]) == "DLD");

        Assert.NotNull(dld);
        Assert.NotNull(dld!.ExcludedReason);
        Assert.Contains("forbidden", dld.ExcludedReason);
        Assert.DoesNotContain("≥3", dld.ExcludedReason);
    }

    [Fact]
    public void Determine_Rna_IsSingleForcedFold_NoRuledOutList()
    {
        var d = TopologyPredictor.Determine(3, [1, 1, 1], isRna: true);
        Assert.Single(d.Ranked);
        Assert.Null(d.Ranked[0].ExcludedReason);
        Assert.Equal("-P-P-P", d.Ranked[0].LoopNotation);
    }
}
