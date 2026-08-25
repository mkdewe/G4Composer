using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

/// <summary>
/// Jedna struktura do policzenia. <see cref="InpFileName"/>/<see cref="InpContent"/> to
/// postać kanoniczna (podgląd, hash cache'a); <see cref="Passes"/> to fizyczne uruchomienia
/// binarki — jedno dla 14G/14L, N dla 14N (po jednym na wartość iteration).
/// </summary>
public sealed record QuadroJobItem(
    int Index, string InpFileName, string InpContent, IReadOnlyList<QuadroPass>? Passes = null)
{
    /// <summary>Przeloty do wykonania; gdy nie podano — jeden, po pliku kanonicznym.</summary>
    public IReadOnlyList<QuadroPass> EffectivePasses =>
        Passes is { Count: > 0 } ? Passes : [new QuadroPass(0, InpFileName, InpContent)];

    /// <summary>Buduje pozycję, pytając silnik o rozbicie na przeloty.</summary>
    public static QuadroJobItem For(IQuadroEngine engine, QuadroInput input, int index)
    {
        var baseName = $"struct_{index:D3}";
        return new QuadroJobItem(
            index, $"{baseName}.inp", engine.SerializeInput(input),
            engine.SerializePasses(input, baseName));
    }
}

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

    /// <summary>How often to re-read the engine log while a pass is running.</summary>
    private const int StagePollIntervalMs = 400;

    // Matches "Etotal = -605.7" in xplor output / energy files
    private static readonly Regex EtotalRegex =
        new(@"Etotal\s*=\s*([-+]?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches iteration-step PDB names — captures the step number.
    //   14L checkpoints:  "6w9p_80.pdb"          → 80
    //   14N passes:       "6w9p_80.pdb"          → 80
    //   14N alt passes:   "6w9p_80_alt.pdb"      → 80   (alternatywa14N appends "_alt" to name)
    private static readonly Regex StepPdbRegex =
        new(@"_(\d+)(?:_alt)?\.pdb$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        // stage is the container start (which actually takes a moment). One file per pass:
        // 14G/14L give a single pass, 14N one per iteration value.
        foreach (var item in items)
            foreach (var pass in item.EffectivePasses)
                await File.WriteAllTextAsync(Path.Combine(jobDir, pass.FileName),
                    pass.Content, cancellationToken);

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
                last = await CoreAsync(jobId, jobDir, item.EffectivePasses,
                    engine.Executable, engine.Image, "std", null, cancellationToken);
            }
            return new DualRunResult(last, empty);
        }

        // Single item: dual parallel run
        var item0  = items[0];
        var passes = item0.EffectivePasses;
        var stdDir = Path.Combine(jobDir, "std");
        var altDir = Path.Combine(jobDir, "alt");
        Directory.CreateDirectory(stdDir);
        if (altExe is not null) Directory.CreateDirectory(altDir);

        foreach (var pass in passes)
        {
            var src = Path.Combine(jobDir, pass.FileName);
            File.Copy(src, Path.Combine(stdDir, pass.FileName), true);
            if (altExe is not null)
                File.Copy(src, Path.Combine(altDir, pass.FileName), true);
        }

        _logStore.Append(jobId,
            $"=== Dual run: {engine.Executable} + {altExe ?? "(no alternative)"} | {passes.Count} pass(es) ===");

        // Only the standard run reports coarse progress — std + alt run in parallel on the
        // same stages, so reporting both would interleave/duplicate milestones.
        var stdTask = CoreAsync(jobId, stdDir, passes,
            engine.Executable, engine.Image, "std", progress, cancellationToken);

        var altTask = altExe is not null
            ? CoreAsync(jobId + "_alt", altDir, passes,
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
        string jobId, string jobDir, IReadOnlyList<QuadroPass> passes,
        string executable, string image, string prefix,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var safeId  = jobId.Length > 12 ? jobId[..12] : jobId;
        var cname   = $"{_options.ContainerNamePrefix}_{safeId}_{prefix}_{Path.GetFileNameWithoutExtension(passes[0].FileName)}";
        var mount   = jobDir.Replace('\\', '/');
        var dataDir = _options.ContainerDataDirectory;
        var workDir = _options.ContainerWorkDirectory;

        _logStore.Append(jobId,
            $"=== Job {jobId} | {passes.Count} pass(es): {string.Join(", ", passes.Select(p => p.FileName))} | engine {executable} ===");

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

            // One engine invocation per pass, all inside the same container so CYANA/Xplor
            // start-up cost is paid once. Passes are independent runs — each writes its own
            // <name>_<K>.pdb / <name>_<K>_energy.txt, so nothing overwrites anything.
            // Stdout of the last pass feeds the Etotal fallback in CollectFrames.
            DockerResult quadro = default;
            var lastStdout = string.Empty;

            // Expected CYANA build-up stages. The engine emits one "angle constraints added."
            // per stage; the count is ~path.Count but varies by one per structure, so it can't
            // be derived up front. The first pass runs on the engine's estimate, then every
            // later pass uses the exact count observed — self-calibrating.
            var expectedStages = passes[0].ExpectedCyanaStages;

            for (var p = 0; p < passes.Count; p++)
            {
                var pass = passes[p];
                await ExecChecked(jobId, cname, ct, "cp", $"{dataDir}/{pass.FileName}", $"{workDir}/{pass.FileName}");

                // Engine output goes to a file on the shared volume rather than being buffered
                // until `docker exec` returns — that is what makes live stage tracking possible.
                var logName  = Path.GetFileNameWithoutExtension(pass.FileName) + ".runlog";
                var hostLog  = Path.Combine(jobDir, logName);
                var passBase = (double)p / passes.Count;
                var passSpan = 1.0 / passes.Count;

                using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var watcher = WatchStagesAsync(hostLog, expectedStages, progress,
                    passBase, passSpan, p + 1, passes.Count, pass.Step, watchCts.Token);

                try
                {
                    quadro = await ExecIgnore(jobId, cname, ct,
                        "/bin/sh", "-c", $"cd {workDir} && ./{executable} {pass.FileName} > {dataDir}/{logName} 2>&1");
                }
                finally
                {
                    await watchCts.CancelAsync();
                    try { await watcher; } catch (OperationCanceledException) { /* expected */ }
                }

                // docker exec captured nothing (output was redirected) — read it back so
                // CollectFrames' Etotal fallback and the failure diagnostics still work.
                lastStdout = await ReadLogSafeAsync(hostLog, ct);

                var observed = QuadroStageTracker.Parse(lastStdout, expectedStages).CyanaStages;
                if (observed > 0) expectedStages = observed;
            }

            progress?.Report(new("collecting", "Collecting structures", passes.Count, passes.Count, Percent: 97));

            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{workDir}/");
            await ExecChecked(jobId, cname, ct, "/bin/sh", "-c",
                $"cp {workDir}/*.pdb {dataDir}/ 2>/dev/null || true");
            await ExecChecked(jobId, cname, ct, "/bin/sh", "-c",
                $"cp {workDir}/*_energy.txt {dataDir}/ 2>/dev/null || true");
            await ExecDebug  (jobId, cname, ct, "ls", "-lh", $"{dataDir}/");

            var frames = CollectFrames(jobDir, lastStdout, preferAlt: prefix == "alt");

            if (frames.Count == 0)
            {
                var files = string.Join(", ", Directory.GetFiles(jobDir).Select(Path.GetFileName));
                throw new DockerException(
                    $"[{cname}] {executable} produced no PDB (exit {quadro.ExitCode})",
                    BuildDiag(quadro with { Stdout = lastStdout }, files));
            }

            return new SingleRunResult(frames, true);
        }
        finally { await RemoveAsync(jobId, cname); }
    }

    // ── Live stage tracking ──────────────────────────────────────────────────

    /// <summary>
    /// Polls the engine's log on the shared volume and reports the stage it has reached.
    /// Runs until cancelled (i.e. until the pass returns).
    /// </summary>
    private async Task WatchStagesAsync(
        string hostLog, int expectedStages, IProgress<JobProgress>? progress,
        double passBase, double passSpan, int passNo, int passCount, int iteration,
        CancellationToken ct)
    {
        if (progress is null) return;

        var lastStage = string.Empty;
        var lastPercent = -1.0;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(StagePollIntervalMs, ct); }
            catch (OperationCanceledException) { return; }

            var text = await ReadLogSafeAsync(hostLog, CancellationToken.None);
            if (text.Length == 0) continue;

            var stage = QuadroStageTracker.Parse(text, expectedStages);

            // Overall percent: 12% reserved for container start, 3% for collection at the end.
            var percent = 12 + 85 * (passBase + passSpan * stage.Fraction);

            // Only report real movement — the UI redraws on every event.
            if (stage.Stage == lastStage && percent - lastPercent < 1.0) continue;
            lastStage = stage.Stage;
            lastPercent = percent;

            var label = passCount > 1
                ? $"{stage.Label} · pass {passNo}/{passCount} (iteration {iteration})"
                : stage.Label;

            progress.Report(new(stage.Stage, label, passNo, passCount, Detail: null, Percent: percent));
        }
    }

    private static async Task<string> ReadLogSafeAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            // The container writes while we read — share everything and tolerate races.
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return await sr.ReadToEndAsync(ct);
        }
        catch (IOException)             { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    // ── Frame collector ──────────────────────────────────────────────────────

    /// <summary>
    /// Collects per-step PDB frames from jobDir.
    /// Prefers *_{step}.pdb files (multi-step run); falls back to any xplor PDB.
    /// </summary>
    /// <param name="preferAlt">
    /// True when collecting from the alt engine (alternatywa14*.exe).
    /// That script runs the standard engine twice in the same dir, so both the inner
    /// standard structure and the actual alternative one land side by side. The two
    /// engines mark the alternative differently:
    /// <list type="bullet">
    ///   <item>14L (checkpoints): <c>name_alt_100.pdb</c> — marker in the middle</item>
    ///   <item>14N (passes):      <c>name_100_alt.pdb</c> — marker at the end</item>
    /// </list>
    /// Matching only the middle form silently returned the STANDARD structure as the
    /// alternative under 14N, so both spellings are recognised here.
    /// </param>
    private static List<IterationFrame> CollectFrames(string jobDir, string stdout, bool preferAlt = false)
    {
        var frames = new List<IterationFrame>();

        // Gather all PDB files in jobDir, respecting which engine produced them.
        var allPdbs = Directory.GetFiles(jobDir, "*.pdb");
        var altPdbs = allPdbs.Where(p =>  IsAltPdb(p)).ToArray();
        var stdPdbs = allPdbs.Where(p => !IsAltPdb(p)).ToArray();
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

    /// <summary>
    /// True for structures produced by alternatywa*.exe. Covers both naming schemes:
    /// <c>name_alt_100.pdb</c> (14L) and <c>name_100_alt.pdb</c> / <c>name_alt.pdb</c> (14N).
    /// </summary>
    private static bool IsAltPdb(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_alt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("_alt_", StringComparison.OrdinalIgnoreCase);
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
