using System.Text.Json;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

public interface IGqrsService
{
    Task<GqrsResult> PredictAsync(string sequence, CancellationToken ct);
}

public sealed class GqrsService : IGqrsService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly IDockerCommandRunner _docker;
    private readonly ILogger<GqrsService> _logger;

    public GqrsService(IDockerCommandRunner docker, ILogger<GqrsService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public async Task<GqrsResult> PredictAsync(string sequence, CancellationToken ct)
    {
        try
        {
            // gqrs searches only uppercase G — normalise before passing
            var upper = sequence.ToUpperInvariant();
            // Use minimum tetrad=2, minimum gscore=0 to get all motifs
            var cmd = $"printf '%s' '{upper}' | qgrs -json -t 2 -s 0";
            var result = await _docker.RunAsync(
                ["run", "--rm", "--entrypoint", "/bin/sh", "gqrs:latest", "-c", cmd], ct);

            if (result.ExitCode != 0)
            {
                var err = result.Stderr.Trim().Split('\n').LastOrDefault() ?? "unknown error";
                return new GqrsResult(false, [], $"gqrs exit {result.ExitCode}: {err}");
            }

            return ParseJson(result.Stdout);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gqrs prediction failed");
            return new GqrsResult(false, [], ex.Message);
        }
    }

    private static GqrsResult ParseJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json.Trim());
            if (!doc.RootElement.TryGetProperty("results", out var arr))
                return new GqrsResult(true, [], null);

            var motifs = new List<GqrsMotif>();
            int id = 1;
            foreach (var item in arr.EnumerateArray())
            {
                motifs.Add(new GqrsMotif(
                    Id:             id++,
                    Tetrad1:        item.GetProperty("tetrad1").GetInt32(),
                    Tetrad2:        item.GetProperty("tetrad2").GetInt32(),
                    Tetrad3:        item.GetProperty("tetrad3").GetInt32(),
                    Tetrad4:        item.GetProperty("tetrad4").GetInt32(),
                    Tetrads:        item.GetProperty("tetrads").GetInt32(),
                    GScore:         item.GetProperty("gscore").GetInt32(),
                    MotifSequence:  item.GetProperty("sequence").GetString() ?? ""
                ));
            }
            return new GqrsResult(true, motifs, null);
        }
        catch (Exception ex)
        {
            return new GqrsResult(false, [], $"Failed to parse gqrs output: {ex.Message}");
        }
    }
}
