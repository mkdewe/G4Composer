namespace G4Composer.Server.Services;

public sealed record EltetradoResult(
    bool    Success,
    string? Output,   // plain-text g4composer export, or null when not applicable
    string? Error
);

public interface IEltetradoService
{
    Task<EltetradoResult> AnalyseAsync(byte[] pdb, CancellationToken ct);
}

public sealed class EltetradoService : IEltetradoService
{
    private readonly IDockerCommandRunner      _docker;
    private readonly ILogger<EltetradoService> _logger;

    public EltetradoService(IDockerCommandRunner docker, ILogger<EltetradoService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public async Task<EltetradoResult> AnalyseAsync(byte[] pdb, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"eltetrado_{Guid.NewGuid():N}"[..16]);
        Directory.CreateDirectory(workDir);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(workDir, "structure.pdb"), pdb, ct);

            var mount = workDir.Replace('\\', '/');

            var result = await _docker.RunAsync(
                ["run", "--rm",
                 "-v", $"{mount}:/work",
                 "eltetrado:latest",
                 "--input",             "/work/structure.pdb",
                 "--g4composer-output", "/work/g4composer.txt"], ct);

            if (result.ExitCode != 0)
            {
                var errLine = result.Stderr.Trim().Split('\n').LastOrDefault() ?? result.Stderr.Trim();
                _logger.LogWarning("eltetrado exited {Code}: {Err}", result.ExitCode, errLine);
                return new EltetradoResult(false, null, $"exit {result.ExitCode}: {errLine}");
            }

            var g4composerPath = Path.Combine(workDir, "g4composer.txt");
            if (File.Exists(g4composerPath))
            {
                var text = (await File.ReadAllTextAsync(g4composerPath, ct)).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return new EltetradoResult(true, text, null);
            }

            // No g4composer file means eltetrado found no suitable unimolecular quadruplex
            _logger.LogInformation("eltetrado: no g4composer output (no unimolecular quadruplex with ≥2 tetrads)");
            return new EltetradoResult(false, null, "No unimolecular quadruplex with ≥2 tetrads detected");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "eltetrado analysis failed");
            return new EltetradoResult(false, null, ex.Message);
        }
        finally
        {
            _ = Task.Run(() =>
            {
                try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
                catch { }
            });
        }
    }
}
