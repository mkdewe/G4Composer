using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

public sealed record QuadroJobItem(int Index, string InpFileName, string InpContent);

public interface IQuadroJobRunner
{
    Task<DualRunResult> RunAsync(
        string jobId, string jobDir,
        IReadOnlyList<QuadroJobItem> items,
        CancellationToken cancellationToken);
}

public sealed class QuadroJobRunner : IQuadroJobRunner
{
    private const int PollIntervalMs  = 150;
    private const int PollMaxAttempts = 40;

    private static readonly Regex EtotalRegex =
        new(@"Etotal\s*=\s*([-+]?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDockerCommandRunner    _docker;
    private readonly IQuadroEngineSelector   _engineSelector;
    private readonly QuadroOptions           _options;
    private readonly ILogger<QuadroJobRunner> _logger;
    private readonly IJobLogStore            _logStore;

    public QuadroJobRunner(
        IDockerCommandRunner docker, IQuadroEngineSelector engineSelector,
        IOptions<QuadroOptions> options, ILogger<QuadroJobRunner> logger,
        IJobLogStore logStore)
    {
        _docker         = docker;
        _engineSelector = engineSelector;
        _options        = options.Value;
        _logger         = logger;
        _logStore       = logStore;
    }

    public async Task<DualRunResult> RunAsync(
        string jobId, string jobDir,
        IReadOnlyList<QuadroJobItem> items,
        CancellationToken cancellationToken)
    {
        var empty = new SingleRunResult(null, null, false);
        if (items.Count == 0) return new DualRunResult(empty, empty);

        foreach (var item in items)
            await File.WriteAllTextAsync(Path.Combine(jobDir, item.InpFileName),
                item.InpContent, cancellationToken);

        var engine = _engineSelector.Active;
        var altExe = _options.AlternativeExecutable;

        if (items.Count > 1)
        {
            // Batch: sequential, standard engine only
            (byte[]? pdb, double? e, string _) last = default;
            foreach (var item in items)
                last = await CoreAsync(jobId, jobDir, item.InpFileName,
                    engine.Executable, engine.Image, "std", cancellationToken);
            return new DualRunResult(
                new SingleRunResult(last.pdb, last.e, last.pdb is not null),
                empty);
        }

        // Single item: dual parallel run
        var item0  = items[0];
        var stdDir = Path.Combine(jobDir, "std");
        var altDir = Path.Combine(jobDir, "alt");
        Directory.CreateDirectory(stdDir);
        if (altExe is not null) Directory.CreateDirectory(altDir);

        var src = Path.Combine(jobDir, item0.InpFileName);
        File.Copy(src, Path.Combine(stdDir, item0.InpFileName), true);
        if (altExe is not null)
            File.Copy(src, Path.Combine(altDir, item0.InpFileName), true);

        _logStore.Append(jobId, $"=== Dual run: {engine.Executable} + {altExe ?? "(no alternative)"} ===");

        var stdTask = CoreAsync(jobId, stdDir, item0.InpFileName,
            engine.Executable, engine.Image, "std", cancellationToken);

        var altTask = altExe is not null
            ? CoreAsync(jobId + "_alt", altDir, item0.InpFileName,
                altExe, engine.Image, "alt", cancellationToken)
            : Task.FromResult<(byte[]?, double?, string)>((null, null, ""));

        (byte[]? pdb, double? e, string log) stdRes = default;
        (byte[]? pdb, double? e, string log) altRes = default;

        try   { stdRes = await stdTask; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {J}: standard run failed", jobId);
            _logStore.Append(jobId, $"ERROR (standard): {ex.Message}");
            throw;   // standard failure = job failure
        }

        try   { altRes = await altTask; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {J}: alternative run failed (non-fatal)", jobId);
            _logStore.Append(jobId, $"WARNING (alternative): {ex.Message}");
        }

        return new DualRunResult(
            new SingleRunResult(stdRes.pdb, stdRes.e ?? ParseEtotal(stdRes.log), stdRes.pdb is not null),
            new SingleRunResult(altRes.pdb, altRes.e ?? ParseEtotal(altRes.log), altRes.pdb is not null));
    }

    // ── Core runner ─────────────────────────────────────────────────────────

    private async Task<(byte[]? Pdb, double? Etotal, string Stdout)> CoreAsync(
        string jobId, string jobDir, string inpFile,
        string executable, string image, string prefix,
        CancellationToken ct)
    {
        var safeId  = jobId.Length > 12 ? jobId[..12] : jobId;
        var cname   = $"{_options.ContainerNamePrefix}_{safeId}_{prefix}_{Path.GetFileNameWithoutExtension(inpFile)}";
        var mount   = jobDir.Replace('\\', '/');
        var dataDir = _options.ContainerDataDirectory;
        var workDir = _options.ContainerWorkDirectory;

        _logStore.Append(jobId, $"=== Job {jobId} | {inpFile} | engine {executable} ===");

        var run = await _docker.RunAsync(
            ["run", "-d", "--name", cname, "--entrypoint", "/bin/sh",
             "-v", $"{mount}:{dataDir}", image, "-c", "tail -f /dev/null"], ct);

        LogStep(jobId, cname, "run -d", run);
        if (run.ExitCode != 0)
            throw new DockerException(
                $"[{cname}] container start failed (exit {run.ExitCode})",
                $"STDOUT:\n{run.Stdout}\nSTDERR:\n{run.Stderr}");

        try
        {
            await WaitRunningAsync(jobId, cname, ct);
            await ExecChecked(jobId, cname, ct, "cp", $"{dataDir}/{inpFile}", $"{workDir}/{inpFile}");
            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{workDir}/");

            var quadro = await ExecIgnore(jobId, cname, ct,
                "/bin/sh", "-c", $"cd {workDir} && ./{executable} {inpFile}");

            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{workDir}/");
            await ExecChecked(jobId, cname, ct, "/bin/sh", "-c",
                $"cp {workDir}/*.pdb {dataDir}/ 2>/dev/null || true");
            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{dataDir}/");

            var pdbs = Directory.GetFiles(jobDir, "*.pdb");
            if (pdbs.Length == 0)
            {
                var files = string.Join(", ", Directory.GetFiles(jobDir).Select(Path.GetFileName));
                throw new DockerException(
                    $"[{cname}] {executable} produced no PDB (exit {quadro.ExitCode})",
                    BuildDiag(quadro, files));
            }

            var best  = pdbs.FirstOrDefault(f => f.Contains("xplor", StringComparison.OrdinalIgnoreCase)) ?? pdbs[0];
            var bytes = await File.ReadAllBytesAsync(best, ct);
            return (bytes, ParseEtotal(quadro.Stdout), quadro.Stdout);
        }
        finally { await RemoveAsync(jobId, cname); }
    }

    // ── Energy parser ────────────────────────────────────────────────────────

    private static double? ParseEtotal(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;
        var m = EtotalRegex.Matches(stdout);
        if (m.Count == 0) return null;
        return double.TryParse(m[^1].Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ── Docker helpers ───────────────────────────────────────────────────────

    private static string BuildDiag(DockerResult r, string files) =>
        $"=== STDOUT ===\n{r.Stdout.TrimEnd()}\n\n=== STDERR ===\n{r.Stderr.TrimEnd()}\n\n=== Files ===\n{files}";

    private async Task WaitRunningAsync(string jobId, string cname, CancellationToken ct)
    {
        for (var i = 1; i <= PollMaxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r  = await _docker.RunAsync(["inspect", "--format", "{{.State.Status}}", cname], ct);
            var st = r.Stdout.Trim();
            _logger.LogDebug("Job {J} [{C}]: poll {I}/{M} status='{S}'", jobId, cname, i, PollMaxAttempts, st);
            if (r.ExitCode == 0 && st == "running") return;
            if (r.ExitCode == 0 && st is not "created" and not "running")
                throw new DockerException($"[{cname}] unexpected state '{st}'", $"status: {st}");
            await Task.Delay(PollIntervalMs, ct);
        }
        throw new TimeoutException($"[{cname}] did not reach 'running' in {PollMaxAttempts * PollIntervalMs / 1000.0:F1}s");
    }

    private async Task<DockerResult> ExecIgnore(string jobId, string cname, CancellationToken ct, params string[] args)
    {
        var a = new List<string> { "exec", cname }; a.AddRange(args);
        var r = await _docker.RunAsync(a, ct);
        LogStep(jobId, cname, string.Join(' ', args), r);
        return r;
    }

    private async Task ExecChecked(string jobId, string cname, CancellationToken ct, params string[] args)
    {
        var a = new List<string> { "exec", cname }; a.AddRange(args);
        var r = await _docker.RunAsync(a, ct);
        LogStep(jobId, cname, string.Join(' ', args), r);
        if (r.ExitCode != 0)
            throw new DockerException(
                $"[{cname}] exec '{args[0]}' exit {r.ExitCode}: {r.Stderr.Trim().Split('\n').LastOrDefault()}",
                $"STDOUT:\n{r.Stdout}\nSTDERR:\n{r.Stderr}");
    }

    private async Task ExecDebug(string jobId, string cname, CancellationToken ct, params string[] args)
    {
        var a = new List<string> { "exec", cname }; a.AddRange(args);
        var r = await _docker.RunAsync(a, ct);
        LogStep(jobId, cname, string.Join(' ', args), r);
    }

    private async Task RemoveAsync(string jobId, string cname)
    {
        try   { var r = await _docker.RunAsync(["rm", "-f", cname], CancellationToken.None); LogStep(jobId, cname, $"rm -f {cname}", r); }
        catch (Exception ex) { _logger.LogWarning("Job {J} [{C}]: remove failed: {M}", jobId, cname, ex.Message); }
    }

    private void LogStep(string jobId, string cname, string step, DockerResult r)
    {
        _logger.LogDebug("Job {J} [{C}] ▶ {S} → {X}", jobId, cname, step, r.ExitCode);
        _logStore.Append(jobId, $"▶ [{cname}] {step} → exit {r.ExitCode}");
        if (r.Stdout.Length > 0) { _logger.LogDebug("Job {J} [{C}] STDOUT:\n{O}", jobId, cname, r.Stdout.TrimEnd()); _logStore.Append(jobId, $"STDOUT:\n{r.Stdout.TrimEnd()}"); }
        if (r.Stderr.Length > 0) { _logger.LogWarning("Job {J} [{C}] STDERR:\n{E}", jobId, cname, r.Stderr.TrimEnd()); _logStore.Append(jobId, $"STDERR:\n{r.Stderr.TrimEnd()}"); }
    }
}
