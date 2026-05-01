using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

/// <summary>Pojedyncze wejście do uruchomienia (jeden plik .inp).</summary>
public sealed record QuadroJobItem(int Index, string InpFileName, string InpContent);

/// <summary>
/// Uruchamia obliczenia Quadro w kontenerze Docker. Krok-po-kroku:
/// 1. <c>docker run -d</c>           — start kontenera (tail -f /dev/null)
/// 1b. poll <c>docker inspect</c>    — czekaj aż state == "running"
/// 2. <c>docker exec cp</c>          — przenieś .inp do /opt/bin
/// 3-5. <c>docker exec quadroXX.exe</c> — wykonaj obliczenia
/// 6-7. <c>docker exec cp *.pdb</c>  — odeślij PDB do /data
/// 8.  <c>docker rm -f</c>           — zawsze, w finally.
/// </summary>
public interface IQuadroJobRunner
{
    /// <summary>
    /// Uruchamia listę .inp jeden po drugim. Zwraca zawartość ostatniego
    /// wygenerowanego pliku PDB (zachowanie zgodne z poprzednią wersją).
    /// </summary>
    Task<byte[]?> RunAsync(string jobId, string jobDir, IReadOnlyList<QuadroJobItem> items, CancellationToken cancellationToken);
}

public sealed class QuadroJobRunner : IQuadroJobRunner
{
    private const int ContainerPollIntervalMs   = 150;
    private const int ContainerPollMaxAttempts  = 40; // 40 × 150 ms = 6 s

    private readonly IDockerCommandRunner _docker;
    private readonly IQuadroEngineSelector _engineSelector;
    private readonly QuadroOptions _options;
    private readonly ILogger<QuadroJobRunner> _logger;

    public QuadroJobRunner(
        IDockerCommandRunner docker,
        IQuadroEngineSelector engineSelector,
        IOptions<QuadroOptions> options,
        ILogger<QuadroJobRunner> logger)
    {
        _docker = docker;
        _engineSelector = engineSelector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]?> RunAsync(
        string jobId,
        string jobDir,
        IReadOnlyList<QuadroJobItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return null;

        // Materialize .inp files on host
        foreach (var item in items)
        {
            var path = Path.Combine(jobDir, item.InpFileName);
            await File.WriteAllTextAsync(path, item.InpContent, cancellationToken);
            _logger.LogDebug("Job {JobId}: wrote {File}", jobId, path);
        }

        byte[]? lastPdb = null;
        foreach (var item in items)
            lastPdb = await RunSingleAsync(jobId, jobDir, item.InpFileName, cancellationToken);

        return lastPdb;
    }

    // ── Pojedynczy job (jeden .inp) ──────────────────────────────────────────
    private async Task<byte[]?> RunSingleAsync(
        string jobId, string jobDir, string inpFileName, CancellationToken cancellationToken)
    {
        var engine = _engineSelector.Active;

        var containerName = $"{_options.ContainerNamePrefix}_{jobId}_{Path.GetFileNameWithoutExtension(inpFileName)}";
        var mountPath     = jobDir.Replace('\\', '/');
        var dataDir       = _options.ContainerDataDirectory;
        var workDir       = _options.ContainerWorkDirectory;

        // ── Step 1: Start container in detached mode ──────────────────────────
        // --entrypoint /bin/sh nadpisuje obrazowy /bin/bash, który kończy się
        // bez TTY. "tail -f /dev/null" trzyma kontener przy życiu bez CPU.
        _logger.LogDebug("Job {JobId} [{Container}]: STEP 1 — starting container", jobId, containerName);
        var runResult = await _docker.RunAsync(
            [
                "run", "-d",
                "--name", containerName,
                "--entrypoint", "/bin/sh",
                "-v", $"{mountPath}:{dataDir}",
                engine.Image,
                "-c", "tail -f /dev/null"
            ],
            cancellationToken);

        LogStep(jobId, containerName, "run -d", runResult);

        if (runResult.ExitCode != 0)
            throw new DockerException(
                $"[{containerName}] Failed to start container (exit {runResult.ExitCode})",
                $"STDOUT:\n{runResult.Stdout}\nSTDERR:\n{runResult.Stderr}");

        try
        {
            // ── Step 1b: Wait until state == "running" ────────────────────────
            await WaitForContainerRunningAsync(jobId, containerName, cancellationToken);

            // ── Step 2: Copy .inp → /opt/bin ──────────────────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 2 — copying .inp → {WorkDir}", jobId, containerName, workDir);
            await ExecCheckedAsync(jobId, containerName, cancellationToken,
                "cp", $"{dataDir}/{inpFileName}", $"{workDir}/{inpFileName}");

            // ── Step 3: Debug — list workDir before run ───────────────────────
            await ExecDebugAsync(jobId, containerName, cancellationToken, "ls", "-lh", $"{workDir}/");

            // ── Step 4: Run quadroXX.exe ──────────────────────────────────────
            // quadro14L.exe always exits with code 2 due to a cleanup bug at line 858
            // (failed `rm Q.seq` after the computation). The PDB is produced before that,
            // so we intentionally ignore the exit code here and verify success by PDB existence.
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 4 — executing {Exe} {Inp}",
                jobId, containerName, engine.Executable, inpFileName);
            var quadroResult = await ExecIgnoreExitCodeAsync(jobId, containerName, cancellationToken,
                "/bin/sh", "-c", $"cd {workDir} && ./{engine.Executable} {inpFileName}");

            // ── Step 5: Debug — list workDir after run ────────────────────────
            await ExecDebugAsync(jobId, containerName, cancellationToken, "ls", "-lh", $"{workDir}/");

            // ── Step 6: Copy PDB files back ───────────────────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 6 — copying *.pdb → {DataDir}", jobId, containerName, dataDir);
            await ExecCheckedAsync(jobId, containerName, cancellationToken,
                "/bin/sh", "-c", $"cp {workDir}/*.pdb {dataDir}/ 2>/dev/null || true");

            // ── Step 7: Debug — list dataDir ─────────────────────────────────
            await ExecDebugAsync(jobId, containerName, cancellationToken, "ls", "-lh", $"{dataDir}/");

            // ── Read resulting PDB from host-side job directory ───────────────
            var pdbFiles = Directory.GetFiles(jobDir, "*.pdb");
            if (pdbFiles.Length == 0)
            {
                var jobFiles = string.Join(", ", Directory.GetFiles(jobDir).Select(Path.GetFileName));
                _logger.LogWarning(
                    "Job {JobId} [{Container}]: no .pdb produced. jobDir contents: {Files}. " +
                    "Quadro exit code: {Code}",
                    jobId, containerName, jobFiles, quadroResult.ExitCode);

                throw new DockerException(
                    $"[{containerName}] {engine.Executable} did not produce a PDB file " +
                    $"(quadro exit code {quadroResult.ExitCode}). See container output for details.",
                    BuildDiagnosticOutput(quadroResult, jobFiles));
            }

            // Preferuj plik z Xplor refinement, gdy istnieje
            var best = pdbFiles.FirstOrDefault(f => f.Contains("xplor", StringComparison.OrdinalIgnoreCase))
                       ?? pdbFiles[0];

            _logger.LogInformation("Job {JobId} [{Container}]: returning {File}",
                jobId, containerName, Path.GetFileName(best));

            return await File.ReadAllBytesAsync(best, cancellationToken);
        }
        finally
        {
            // ── Step 8: Always stop and remove ────────────────────────────────
            await StopAndRemoveAsync(jobId, containerName);
        }
    }

    private static string BuildDiagnosticOutput(DockerResult quadroResult, string jobFiles)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("=== quadro STDOUT ===\n").Append(quadroResult.Stdout.TrimEnd()).Append("\n\n");
        sb.Append("=== quadro STDERR ===\n").Append(quadroResult.Stderr.TrimEnd()).Append("\n\n");
        sb.Append("=== Job directory contents ===\n").Append(jobFiles);
        return sb.ToString();
    }

    private async Task WaitForContainerRunningAsync(string jobId, string containerName, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= ContainerPollMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _docker.RunAsync(
                ["inspect", "--format", "{{.State.Status}}", containerName], ct);

            var status = result.Stdout.Trim();

            _logger.LogDebug(
                "Job {JobId} [{Container}]: poll {Attempt}/{Max} — status='{Status}' exit={Code}",
                jobId, containerName, attempt, ContainerPollMaxAttempts, status, result.ExitCode);

            if (result.ExitCode == 0 && status == "running")
                return;

            // "created" = jeszcze nie wystartował entrypoint; pollujemy dalej.
            // Inny stan (exited/dead) = twardy błąd.
            if (result.ExitCode == 0 && status is not "created" and not "running")
                throw new DockerException(
                    $"[{containerName}] Container in unexpected state '{status}' before exec",
                    $"docker inspect status: {status}");

            await Task.Delay(ContainerPollIntervalMs, ct);
        }

        throw new TimeoutException(
            $"[{containerName}] Container did not reach 'running' state within " +
            $"{ContainerPollMaxAttempts * ContainerPollIntervalMs / 1000.0:F1}s");
    }

    /// <summary>
    /// Runs docker exec but never throws on non-zero exit code — only logs a warning.
    /// Use for steps where the command is known to exit non-zero even on success
    /// (e.g. quadro14L.exe cleanup bug at line 858).
    /// </summary>
    private async Task<DockerResult> ExecIgnoreExitCodeAsync(
        string jobId, string containerName, CancellationToken ct, params string[] execArgs)
    {
        var args = new List<string>(execArgs.Length + 2) { "exec", containerName };
        args.AddRange(execArgs);

        var result = await _docker.RunAsync(args, ct);
        LogStep(jobId, containerName, string.Join(' ', execArgs), result);

        if (result.ExitCode != 0)
            _logger.LogWarning(
                "Job {JobId} [{Container}]: exec '{Cmd}' exited with code {Code} " +
                "(ignored — success is determined by PDB output). Stderr: {Err}",
                jobId, containerName, execArgs[0], result.ExitCode,
                result.Stderr.Trim().Split('\n').LastOrDefault());

        return result;
    }

    private async Task ExecCheckedAsync(
        string jobId, string containerName, CancellationToken ct, params string[] execArgs)
    {
        var args = new List<string>(execArgs.Length + 2) { "exec", containerName };
        args.AddRange(execArgs);

        var result = await _docker.RunAsync(args, ct);
        LogStep(jobId, containerName, string.Join(' ', execArgs), result);

        if (result.ExitCode != 0)
            throw new DockerException(
                $"[{containerName}] exec '{execArgs[0]}' exited with code {result.ExitCode}. " +
                $"Last stderr: {result.Stderr.Trim().Split('\n').LastOrDefault()}",
                $"STDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
    }

    private async Task ExecDebugAsync(
        string jobId, string containerName, CancellationToken ct, params string[] execArgs)
    {
        var args = new List<string>(execArgs.Length + 2) { "exec", containerName };
        args.AddRange(execArgs);

        var result = await _docker.RunAsync(args, ct);
        LogStep(jobId, containerName, string.Join(' ', execArgs), result);
        // Bez throw — tylko diagnostyka.
    }

    private async Task StopAndRemoveAsync(string jobId, string containerName)
    {
        try
        {
            var result = await _docker.RunAsync(["rm", "-f", containerName], CancellationToken.None);
            LogStep(jobId, containerName, $"rm -f {containerName}", result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Job {JobId} [{Container}]: could not remove container: {Msg}",
                jobId, containerName, ex.Message);
        }
    }

    private void LogStep(string jobId, string containerName, string step, DockerResult result)
    {
        _logger.LogDebug("Job {JobId} [{Container}] ▶ {Step} → exit {Code}",
            jobId, containerName, step, result.ExitCode);

        if (result.Stdout.Length > 0)
            _logger.LogDebug("Job {JobId} [{Container}] ▶ {Step} STDOUT:\n{Out}",
                jobId, containerName, step, result.Stdout.TrimEnd());

        if (result.Stderr.Length > 0)
            _logger.LogWarning("Job {JobId} [{Container}] ▶ {Step} STDERR:\n{Err}",
                jobId, containerName, step, result.Stderr.TrimEnd());
    }
}
