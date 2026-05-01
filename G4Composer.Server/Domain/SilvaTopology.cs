using System.Text.RegularExpressions;

namespace G4Composer.Server.Domain;

/// <summary>
/// Derives the quadro14L <c>path</c> string and a default <c>orient</c> from
/// Silva loop notation (e.g. <c>+Ln+P+Lw</c>) and the number of tetrad planes.
///
/// Algorithm:
///   * G-quadruplexes always have 4 strands (G-tracks numbered 1..4).
///   * The number of tetrad planes equals the requested N (1..4); each plane
///     gets a letter A, B, C, D.
///   * Walk starts at <c>(track=1, direction=Forward)</c>. For each of the 3
///     loops in the notation:
///       - L (Lw/Ln) → ±1 track, flips direction
///       - P         → ±1 track, keeps direction
///       - D         → ±2 tracks, flips direction (unsigned <c>D</c> = +D)
///   * Track wraps modulo 4 so that <c>1 - 1 → 4</c>.
///   * After 3 loops we have 4 stops (tracks). For each stop emit the N plane
///     letters with the current track number; reverse the letter order when
///     direction is Reverse.
///
/// Verified against every example in <c>StructureExamples</c> that has a path.
/// JS mirror lives at <c>g4composer.client/src/utils/silvaTopology.js</c>.
/// </summary>
public static class SilvaTopology
{
    private const int StrandCount = 4;
    private static readonly char[] PlaneLetters = ['A', 'B', 'C', 'D'];

    private static readonly Regex LoopTokenRegex = new(
        @"(?<sign>[+-]?)(?<kind>Lw|Ln|L|P|D)",
        RegexOptions.Compiled);

    /// <summary>
    /// Builds the tetrad path, e.g. <c>("+Ln+P+Lw", 3)</c>
    /// → <c>"A1;B1;C1;C2;B2;A2;C3;B3;A3;A4;B4;C4"</c>.
    /// </summary>
    public static string BuildPath(string loopNotation, int tetrads)
    {
        if (string.IsNullOrWhiteSpace(loopNotation))
            throw new ArgumentException("Loop notation is required.", nameof(loopNotation));
        if (tetrads is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(tetrads), "Tetrads must be between 1 and 4.");

        var loops = ParseLoops(loopNotation);
        if (loops.Count != 3)
            throw new ArgumentException(
                $"Expected exactly 3 loops in '{loopNotation}', got {loops.Count}.",
                nameof(loopNotation));

        var track = 1;
        var forward = true;
        var visited = new List<(int Track, bool Forward)> { (track, forward) };

        foreach (var loop in loops)
        {
            var delta = loop.Sign * (loop.IsDiagonal ? 2 : 1);
            track = WrapTrack(track + delta);
            if (loop.FlipsDirection) forward = !forward;
            visited.Add((track, forward));
        }

        var letters = PlaneLetters[..tetrads];
        var parts = new List<string>(visited.Count * tetrads);

        foreach (var (t, fwd) in visited)
        {
            IEnumerable<char> ordered = fwd ? letters : letters.Reverse();
            foreach (var letter in ordered)
                parts.Add($"{letter}{t}");
        }

        return string.Join(';', parts);
    }

    /// <summary>
    /// Default orient string per number of tetrads, based on the most common
    /// patterns observed in the deposited examples. User can always override.
    /// </summary>
    public static string BuildDefaultOrient(int tetrads) => tetrads switch
    {
        1 => "A+",
        2 => "A+;B-",
        3 => "A+;B-;C-",
        4 => "A+;B-;C+;D-",
        _ => throw new ArgumentOutOfRangeException(nameof(tetrads)),
    };

    private static int WrapTrack(int t)
    {
        // 1-indexed mod StrandCount, handles negative deltas.
        var zeroBased = ((t - 1) % StrandCount + StrandCount) % StrandCount;
        return zeroBased + 1;
    }

    private static List<Loop> ParseLoops(string notation)
    {
        var loops = new List<Loop>();
        foreach (Match m in LoopTokenRegex.Matches(notation))
        {
            var sign = m.Groups["sign"].Value == "-" ? -1 : 1; // missing or '+' → positive
            var kind = m.Groups["kind"].Value;
            var isDiagonal = kind == "D";
            var flipsDirection = kind != "P"; // L (Lw/Ln) and D flip; P keeps
            loops.Add(new Loop(sign, isDiagonal, flipsDirection));
        }
        return loops;
    }

    private sealed record Loop(int Sign, bool IsDiagonal, bool FlipsDirection);
}
