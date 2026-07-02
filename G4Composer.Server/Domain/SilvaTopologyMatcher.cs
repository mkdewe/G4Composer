namespace G4Composer.Server.Domain;

/// <summary>
/// Reverse of <see cref="SilvaTopology"/>: given the quadro14L <c>path</c> of a matched template
/// (the exact strand threading of a deposited G4), recovers the canonical Webba da Silva loop
/// notation (e.g. <c>-Lw-Ln-Lw</c>) whose threading matches.
///
/// <para>Motivation: the ONQuadro/ElTetrado <c>topology</c> string only carries loop TYPES
/// (<c>p</c>/<c>l</c>/<c>d</c>) with a progression sign — it does NOT encode the wide/narrow lateral
/// distinction (<c>Lw</c> vs <c>Ln</c>) that the sequence-prediction tab shows in Silva notation.
/// That distinction is a property of the geometry, so the ONQuadro aligner tab and the prediction
/// tab ended up labelling the same fold two different ways. This matcher re-derives the Silva
/// subtype from the template's own geometry, so both tabs speak one notation.</para>
///
/// <para>Deliberately conservative: it returns a notation ONLY when exactly one catalog fold's
/// canonical threading matches the template path (up to a pure strand-relabelling rotation, which is
/// the same physical fold). Anything ambiguous or unrecognised returns <c>null</c>, and the caller
/// keeps the ONQuadro notation — the matcher never invents a Silva label it cannot justify.</para>
/// </summary>
public static class SilvaTopologyMatcher
{
    /// <summary>
    /// The canonical Silva loop notation matching this template path, or <c>null</c> when no single
    /// catalog fold matches. <paramref name="path"/> is the quadro14L path entries (e.g.
    /// <c>["A1","B1","C1","C4",…]</c>); <paramref name="tetrads"/> is the number of tetrad planes.
    /// </summary>
    public static string? TryMatchNotation(IReadOnlyList<string> path, int tetrads)
    {
        if (path is null || tetrads is < 1 or > 4) return null;

        // A single tetrad plane carries no intra-stop direction, so P/L/D are indistinguishable from
        // the path alone (parallel and chair thread identically at N=1). Don't guess.
        if (tetrads == 1) return null;
        if (path.Count != 4 * tetrads) return null;

        var normalized = NormalizeToFirstTrackOne(path);
        if (normalized is null) return null;

        string? match = null;
        foreach (var sub in SilvaCatalog.All)
        {
            string canonical;
            try { canonical = SilvaTopology.BuildPath(sub.Notation, tetrads); }
            catch { continue; }   // a notation SilvaTopology cannot thread at this N
            if (canonical != normalized) continue;
            if (match is not null) return null;   // two folds share this threading — ambiguous
            match = sub.Notation;
        }
        return match;
    }

    // Shift every track number by a constant so the first stop lands on track 1. Renumbering which
    // strand is "1" is a pure relabelling — it does not change the physical fold — so this lets a
    // template threaded from any starting strand line up with SilvaTopology's track-1 convention.
    // Returns null if the path is malformed.
    private static string? NormalizeToFirstTrackOne(IReadOnlyList<string> path)
    {
        var letters = new char[path.Count];
        var tracks  = new int[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var s = path[i].Trim();
            if (s.Length < 2) return null;
            letters[i] = char.ToUpperInvariant(s[0]);
            if (!int.TryParse(s[1..], out var t) || t is < 1 or > 4) return null;
            tracks[i] = t;
        }

        var shift = 1 - tracks[0];
        var parts = new string[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var z = ((tracks[i] - 1 + shift) % 4 + 4) % 4;
            parts[i] = $"{letters[i]}{z + 1}";
        }
        return string.Join(';', parts);
    }
}
