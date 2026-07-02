using G4Composer.Server.Domain;

namespace G4Composer.Server.Tests.Domain;

/// <summary>
/// Tests for <see cref="SilvaTopologyMatcher"/> — the reverse mapping that recovers a canonical Silva
/// loop notation from a template's quadro14L path. Ground truth is the catalog's own generated paths
/// (via <see cref="SilvaTopology.BuildPath"/>): feeding a fold's path back in must recover that fold
/// (or null when another catalog fold threads identically), and a pure strand-relabelling must not
/// change the result.
/// </summary>
public class SilvaTopologyMatcherTests
{
    // Catalog folds whose canonical threading is unique among the catalog at this tetrad count —
    // these must round-trip exactly (no ambiguity guard trips).
    private static IEnumerable<(string Notation, int N)> UniquelyThreadedFolds(int n)
    {
        var byPath = new Dictionary<string, List<string>>();
        foreach (var sub in SilvaCatalog.All)
        {
            string path;
            try { path = SilvaTopology.BuildPath(sub.Notation, n); }
            catch { continue; }
            (byPath.TryGetValue(path, out var l) ? l : byPath[path] = new()).Add(sub.Notation);
        }
        foreach (var (path, notations) in byPath)
            if (notations.Count == 1)
                yield return (notations[0], n);
    }

    public static IEnumerable<object[]> UniqueFolds =>
        new[] { 2, 3, 4 }.SelectMany(UniquelyThreadedFolds).Select(f => new object[] { f.Notation, f.N });

    // Rotate every track number by +r (mod 4) — a pure strand relabelling that keeps the same fold.
    private static IReadOnlyList<string> RotateTracks(string path, int r) =>
        path.Split(';').Select(e =>
        {
            var t = int.Parse(e[1..]);
            var z = ((t - 1 + r) % 4 + 4) % 4;
            return $"{e[0]}{z + 1}";
        }).ToList();

    [Theory]
    [MemberData(nameof(UniqueFolds))]
    public void RoundTrips_UniquelyThreadedFold(string notation, int n)
    {
        var path = SilvaTopology.BuildPath(notation, n).Split(';').ToList();
        Assert.Equal(notation, SilvaTopologyMatcher.TryMatchNotation(path, n));
    }

    [Theory]
    [MemberData(nameof(UniqueFolds))]
    public void IsInvariantToStrandRelabelling(string notation, int n)
    {
        var basePath = SilvaTopology.BuildPath(notation, n);
        for (var r = 0; r < 4; r++)
            Assert.Equal(notation, SilvaTopologyMatcher.TryMatchNotation(RotateTracks(basePath, r), n));
    }

    [Fact]
    public void RecoversWideNarrowLateralDistinction_NotInOnquadroTopology()
    {
        // ONQuadro would report "-l-l-l" / "+l+l+l" for both chair variants; the matcher recovers the
        // exact wide/narrow labelling (6a -Lw-Ln-Lw vs 6b +Ln+Lw+Ln) from the geometry.
        var chairA = SilvaTopology.BuildPath("-Lw-Ln-Lw", 3).Split(';').ToList();
        var chairB = SilvaTopology.BuildPath("+Ln+Lw+Ln", 3).Split(';').ToList();
        Assert.Equal("-Lw-Ln-Lw", SilvaTopologyMatcher.TryMatchNotation(chairA, 3));
        Assert.Equal("+Ln+Lw+Ln", SilvaTopologyMatcher.TryMatchNotation(chairB, 3));
    }

    [Fact]
    public void SingleTetrad_ReturnsNull_TypesIndistinguishable()
    {
        // Parallel and chair thread identically at N=1, so no Silva label can be justified.
        var path = SilvaTopology.BuildPath("-P-P-P", 1).Split(';').ToList();
        Assert.Null(SilvaTopologyMatcher.TryMatchNotation(path, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A1;B1")]          // wrong length (not 4×N)
    [InlineData("X;B1;C1;D1")]     // malformed entry
    public void MalformedOrUnknownPath_ReturnsNull(string joined)
    {
        var path = joined.Length == 0 ? new List<string>() : joined.Split(';').ToList();
        Assert.Null(SilvaTopologyMatcher.TryMatchNotation(path, 3));
    }
}
