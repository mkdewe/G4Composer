using System.Text.RegularExpressions;
using G4Composer.Server.Domain;

namespace G4Composer.Server.Tests.Domain;

/// <summary>
/// Tests for <see cref="TopologyPredictor"/> — the empirical loop-type probability model that ranks
/// canonical (Webba da Silva) topologies from the three loop lengths. Candidates are drawn from the
/// curated <see cref="SilvaCatalog"/> (experimentally-observed folds only) and scored by a grid
/// learned from deposited unimolecular G4 structures, so these tests assert structural invariants
/// (canonical membership, threading, RNA gate, propeller-forcing, A-track, both sign variants)
/// rather than fixed literature outcomes.
/// </summary>
public class TopologyPredictorTests
{
    // Loop kinds collapsed to P/L/D in order (Lw/Ln → L); index 1 is the central loop.
    private static char CentralLoopKind(string notation)
        => Regex.Matches(notation, "Lw|Ln|L|P|D").Select(m => m.Value[0]).ToArray()[1];

    private static readonly HashSet<string> CanonicalNotations =
        SilvaCatalog.All.Select(s => s.Notation).ToHashSet();

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

    // ── Only canonical Silva folds are ever offered (no invented cube topologies) ─
    [Theory]
    [InlineData(new[] { 2, 2, 2 })]
    [InlineData(new[] { 3, 4, 3 })]
    [InlineData(new[] { 1, 1, 1 })]
    [InlineData(new[] { 2, 5, 2 })]
    public void EveryCandidate_IsACanonicalSilvaSubtype(int[] loops)
    {
        var r = TopologyPredictor.Predict(3, loops, isRna: false);
        Assert.NotEmpty(r);
        Assert.All(r, c => Assert.Contains(c.LoopNotation, CanonicalNotations));
    }

    // ── Both sign variants of a family are offered (the 6a/6b regression fix) ─────
    [Fact]
    public void Dna_AllLateral_OffersBothSignVariants_6a_and_6b()
    {
        // Outer 2-nt + central 3-nt loops favour an all-lateral fold; both chair sign variants
        // must appear — earlier the predictor kept only the first threadable sign and dropped 6b.
        var r = TopologyPredictor.Predict(2, [2, 3, 2], isRna: false);
        Assert.Contains(r, c => c.LoopNotation == "-Lw-Ln-Lw");   // 6a
        Assert.Contains(r, c => c.LoopNotation == "+Ln+Lw+Ln");   // 6b
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
        Assert.All(r, c => Assert.Equal('P', CentralLoopKind(c.LoopNotation)));
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
        Assert.Equal('D', CentralLoopKind(r[0].LoopNotation));   // central loop is the diagonal
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

    // ── Several folds are offered (parallel included), bounded by the model cap ───
    [Fact]
    public void ReturnsRankedSpace_IncludingParallel_UpToCap()
    {
        var r = TopologyPredictor.Predict(3, [3, 3, 3], isRna: false);
        Assert.Contains(r, c => c.IsParallel);
        Assert.True(r.Count >= 3, "multiple admissible folds should be returned");
        Assert.True(r.Count <= TopologyPredictor.ModelCap, "modelled set is bounded by the cap");
    }

    // ── A-track rigidity pushes an A-rich loop toward propeller, away from diagonal ─
    [Fact]
    public void ARichCentralLoop_DrivesPropellerCentre()
    {
        // 4-nt central loop would normally be diagonal (basket); when A-rich it is too rigid,
        // so the top fold's central loop becomes a propeller instead.
        var plain = TopologyPredictor.Predict(3, [3, 4, 3], isRna: false);
        Assert.Equal('D', CentralLoopKind(plain[0].LoopNotation));    // baseline: diagonal central

        var aRich = TopologyPredictor.Predict(3, [3, 4, 3], isRna: false, [false, true, false]);
        Assert.Equal('P', CentralLoopKind(aRich[0].LoopNotation));    // A-track → propeller central
    }

    // ── Determine exposes the full canonical space: admissible (P>0) + ruled-out (0%) ─
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

        // The top folds (up to the cap) are modelled; the rest stay admissible-but-not-built.
        Assert.Contains(admissible, r => r.Built);
        Assert.True(admissible.Count(r => r.Built) <= TopologyPredictor.ModelCap);

        // Probabilities are normalised over the whole admissible space (built or not).
        Assert.InRange(admissible.Sum(r => r.Probability), 99.0, 101.0);
        Assert.All(admissible, r => Assert.Contains(r.LoopNotation, CanonicalNotations));
    }

    [Fact]
    public void Determine_ShortLoops_RuleOutDiagonalSubtype_WithReason()
    {
        // 11a (-LwD+Ln) has a central diagonal; at a 2-nt central loop it cannot form.
        var d = TopologyPredictor.Determine(4, [2, 2, 2], isRna: false);
        var diag = d.Ranked.FirstOrDefault(r => r.LoopNotation == "-LwD+Ln");

        Assert.NotNull(diag);
        Assert.NotNull(diag!.ExcludedReason);
        Assert.Contains("diagonal", diag.ExcludedReason);
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
