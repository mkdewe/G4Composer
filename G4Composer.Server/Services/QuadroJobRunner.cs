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
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class QuadroJobRunner : IQuadroJobRunner
{
    private const int PollIntervalMs  = 150;
    private const int PollMaxAttempts = 40;

    // Matches "Etotal = -605.7" in xplor output / energy files
    private static readonly Regex EtotalRegex =
        new(@"Etotal\s*=\s*([-+]?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches iteration-step PDB names like "6w9p_80.pdb" — captures step number
    private static readonly Regex StepPdbRegex =
        new(@"_(\d+)\.pdb$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken)
    {
        var empty = SingleRunResult.Empty;
        if (items.Count == 0) return new DualRunResult(empty, empty);

        // Writing the .inp files is instant — no separate milestone for it; the first visible
        // stage is the container start (which actually takes a moment).
        foreach (var item in items)
            await File.WriteAllTextAsync(Path.Combine(jobDir, item.InpFileName),
                item.InpContent, cancellationToken);

        var engine = _engineSelector.Active;
        var altExe = _options.AlternativeExecutable;

        if (items.Count > 1)
        {
            // Batch: sequential, standard engine only
            SingleRunResult last = empty;
            var i = 0;
            foreach (var item in items)
            {
                i++;
                progress?.Report(new("minimizing", $"Running structure {i}/{items.Count}", i, items.Count, item.InpFileName, (double)i / items.Count * 100));
                last = await CoreAsync(jobId, jobDir, item.InpFileName,
                    engine.Executable, engine.Image, "std", null, cancellationToken);
            }
            return new DualRunResult(last, empty);
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

        // Only the standard run reports coarse progress — std + alt run in parallel on the
        // same stages, so reporting both would interleave/duplicate milestones.
        var stdTask = CoreAsync(jobId, stdDir, item0.InpFileName,
            engine.Executable, engine.Image, "std", progress, cancellationToken);

        var altTask = altExe is not null
            ? CoreAsync(jobId + "_alt", altDir, item0.InpFileName,
                altExe, engine.Image, "alt", null, cancellationToken)
            : Task.FromResult(SingleRunResult.Empty);

        SingleRunResult stdRes = empty;
        SingleRunResult altRes = empty;

        try   { stdRes = await stdTask; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {J}: standard run failed", jobId);
            _logStore.Append(jobId, $"ERROR (standard): {ex.Message}");
            throw;
        }

        try   { altRes = await altTask; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {J}: alternative run failed (non-fatal)", jobId);
            _logStore.Append(jobId, $"WARNING (alternative): {ex.Message}");
        }

        return new DualRunResult(stdRes, altRes);
    }

    // ── Core runner ─────────────────────────────────────────────────────────

    private async Task<SingleRunResult> CoreAsync(
        string jobId, string jobDir, string inpFile,
        string executable, string image, string prefix,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var safeId  = jobId.Length > 12 ? jobId[..12] : jobId;
        var cname   = $"{_options.ContainerNamePrefix}_{safeId}_{prefix}_{Path.GetFileNameWithoutExtension(inpFile)}";
        var mount   = jobDir.Replace('\\', '/');
        var dataDir = _options.ContainerDataDirectory;
        var workDir = _options.ContainerWorkDirectory;

        _logStore.Append(jobId, $"=== Job {jobId} | {inpFile} | engine {executable} ===");

        progress?.Report(new("starting", "Starting Docker container", 1, 3, Percent: 12));

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

            progress?.Report(new("minimizing", "Running energy minimization", 2, 3, Percent: 45));

            var quadro = await ExecIgnore(jobId, cname, ct,
                "/bin/sh", "-c", $"cd {workDir} && ./{executable} {inpFile}");

            progress?.Report(new("collecting", "Collecting structures", 3, 3, Percent: 90));

            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{workDir}/");
            await ExecChecked(jobId, cname, ct, "/bin/sh", "-c",
                $"cp {workDir}/*.pdb {dataDir}/ 2>/dev/null || true");
            await ExecChecked(jobId, cname, ct, "/bin/sh", "-c",
                $"cp {workDir}/*_energy.txt {dataDir}/ 2>/dev/null || true");
            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{dataDir}/");

            var frames = CollectFrames(jobDir, quadro.Stdout, preferAlt: prefix == "alt");

            if (frames.Count == 0)
            {
                var files = string.Join(", ", Directory.GetFiles(jobDir).Select(Path.GetFileName));
                throw new DockerException(
                    $"[{cname}] {executable} produced no PDB (exit {quadro.ExitCode})",
                    BuildDiag(quadro, files));
            }

            return new SingleRunResult(frames, true);
        }
        finally { await RemoveAsync(jobId, cname); }
    }

    // ── Frame collector ──────────────────────────────────────────────────────

    /// <summary>
    /// Collects per-step PDB frames from jobDir.
    /// Prefers *_{step}.pdb files (multi-step run); falls back to any xplor PDB.
    /// </summary>
    /// <param name="preferAlt">
    /// True when collecting from the alt engine (alternatywa14L.exe).
    /// That script runs two quadro14L calls in the same dir, producing both
    /// <c>name_N.pdb</c> (inner std run) and <c>name_alt_N.pdb</c> (actual alt run).
    /// When true, the <c>_alt_</c> files are selected; otherwise they are excluded.
    /// </param>
    private static List<IterationFrame> CollectFrames(string jobDir, string stdout, bool preferAlt = false)
    {
        var frames = new List<IterationFrame>();

        // Gather all PDB files in jobDir, respecting which engine produced them.
        var allPdbs = Directory.GetFiles(jobDir, "*.pdb");
        var altPdbs = allPdbs.Where(p =>  Path.GetFileName(p).Contains("_alt_", StringComparison.OrdinalIgnoreCase)).ToArray();
        var stdPdbs = allPdbs.Where(p => !Path.GetFileName(p).Contains("_alt_", StringComparison.OrdinalIgnoreCase)).ToArray();
        var pdbs = preferAlt && altPdbs.Length > 0 ? altPdbs : stdPdbs;

        // Try to find step-numbered PDBs: name_80.pdb, name_100.pdb, etc.
        // Exclude raw CYANA checkpoint_*.pdb files — they are unprocessed intermediates.
        // Deduplicate by step: if multiple files share the same step number, keep the first.
        var stepPdbs = pdbs
            .Where(p => !Path.GetFileName(p).StartsWith("checkpoint_", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: p, Match: StepPdbRegex.Match(Path.GetFileName(p))))
            .Where(t => t.Match.Success)
            .Select(t => (t.Path, Step: int.Parse(t.Match.Groups[1].Value)))
            .GroupBy(t => t.Step)
            .Select(g => g.First())
            .OrderBy(t => t.Step)
            .ToList();

        if (stepPdbs.Count > 0)
        {
            foreach (var (pdbPath, step) in stepPdbs)
            {
                var pdbBytes = File.ReadAllBytes(pdbPath);
                var energy   = ReadEnergyFile(jobDir, pdbPath, step) ?? ParseEtotal(stdout);
                frames.Add(new IterationFrame(step, pdbBytes, energy));
            }
            return frames;
        }

        // Fallback: single-step legacy run (no step suffix)
        var best = pdbs.FirstOrDefault(f => f.Contains("xplor", StringComparison.OrdinalIgnoreCase))
                   ?? pdbs.FirstOrDefault();
        if (best is not null)
        {
            var pdbBytes = File.ReadAllBytes(best);
            frames.Add(new IterationFrame(100, pdbBytes, ParseEtotal(stdout)));
        }
        return frames;
    }

    private static double? ReadEnergyFile(string jobDir, string pdbPath, int step)
    {
        // energy file naming: {name}_{step}_energy.txt
        // derive base name from PDB path: strip step suffix
        var pdbName = Path.GetFileNameWithoutExtension(pdbPath); // e.g. "6w9p_80"
        var energyFile = Path.Combine(jobDir, $"{pdbName}_energy.txt");
        if (!File.Exists(energyFile)) return null;
        var text = File.ReadAllText(energyFile);
        var m = EtotalRegex.Matches(text);
        if (m.Count == 0) return null;
        return double.TryParse(m[^1].Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
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
