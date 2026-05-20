using System.Text;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

public interface IOnquadroService
{
    Task<OnquadroResult> AlignAsync(string sequence, CancellationToken ct);
}

public sealed class OnquadroService : IOnquadroService
{
    private readonly IDockerCommandRunner      _docker;
    private readonly ILogger<OnquadroService>  _logger;

    public OnquadroService(IDockerCommandRunner docker, ILogger<OnquadroService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public async Task<OnquadroResult> AlignAsync(string sequence, CancellationToken ct)
    {
        try
        {
            var result = await _docker.RunAsync(
                ["run", "--rm", "onquadro-aligner:latest", sequence], ct);

            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.Stdout))
                return new OnquadroResult(false, [],
                    $"onquadro exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

            var matches = ParseCsv(result.Stdout);
            return new OnquadroResult(true, matches, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "onquadro-aligner failed");
            return new OnquadroResult(false, [], ex.Message);
        }
    }

    // ── CSV parser ───────────────────────────────────────────────────────────

    private static List<OnquadroMatch> ParseCsv(string csv)
    {
        var matches = new List<OnquadroMatch>();
        if (string.IsNullOrWhiteSpace(csv)) return matches;

        bool first = true;
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (first) { first = false; continue; } // skip header
            var cols = SplitCsvLine(line);
            if (cols.Length < 7) continue;

            if (!int.TryParse(cols[1].Trim(), out var tetradCount)) continue;
            if (!double.TryParse(cols[3].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tractDist)) continue;
            if (!double.TryParse(cols[4].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var linkerScore)) continue;

            matches.Add(new OnquadroMatch(
                Files:           cols[0].Trim(),
                TetradCount:     tetradCount,
                Molecule:        cols[2].Trim(),
                TractDistance:   tractDist,
                LinkerScore:     linkerScore,
                Qrs:             cols[6].Trim(),
                MatchedSequence: cols[5].Trim()
            ));
        }
        return matches;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result  = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            switch (ch)
            {
                case '"': inQuotes = !inQuotes; break;
                case ',' when !inQuotes: result.Add(current.ToString()); current.Clear(); break;
                default: current.Append(ch); break;
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
