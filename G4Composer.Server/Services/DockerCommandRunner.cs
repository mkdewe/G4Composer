using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace G4Composer.Server.Services;

/// <summary>Wynik pojedynczego polecenia docker.</summary>
public readonly record struct DockerResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Cienki wrapper na proces <c>docker</c>. Hermetyzuje uruchamianie, redirect
/// stdout/stderr, anulowanie i zabicie procesu w razie timeoutu.
/// </summary>
public interface IDockerCommandRunner
{
    Task<DockerResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken);
}

public sealed class DockerCommandRunner : IDockerCommandRunner
{
    private const string DockerExecutable = "docker";
    private readonly ILogger<DockerCommandRunner> _logger;

    public DockerCommandRunner(ILogger<DockerCommandRunner> logger) => _logger = logger;

    public async Task<DockerResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = DockerExecutable,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        return new DockerResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
