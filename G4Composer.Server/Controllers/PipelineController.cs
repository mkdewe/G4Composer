using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;
using G4Composer.Server.Services;

namespace G4Composer.Server.Controllers;

[ApiController]
[Route("api/pipeline")]
public sealed class PipelineController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static readonly System.Text.RegularExpressions.Regex EnergyRegex =
        new(@"\(\s*([-+]?\d+(?:\.\d+)?)\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly IDockerCommandRunner          _docker;
    private readonly IGqrsService                  _gqrs;
    private readonly IOnquadroService              _onquadro;
    private readonly IEltetradoService             _eltetrado;
    private readonly IQuadroJobRunner              _jobRunner;
    private readonly IQuadroEngineSelector         _engineSelector;
    private readonly IPipelinePdbStore             _pipelinePdbStore;
    private readonly IAltPdbStore                  _altPdbStore;
    private readonly IFrameStore                   _frameStore;
    private readonly QuadroOptions                 _options;
    private readonly ILogger<PipelineController>   _logger;

    public PipelineController(
        IDockerCommandRunner docker,
        IGqrsService gqrs,
        IOnquadroService onquadro,
        IEltetradoService eltetrado,
        IQuadroJobRunner jobRunner,
        IQuadroEngineSelector engineSelector,
        IPipelinePdbStore pipelinePdbStore,
        IAltPdbStore altPdbStore,
        IFrameStore frameStore,
        IOptions<QuadroOptions> options,
        ILogger<PipelineController> logger)
    {
        _docker           = docker;
        _gqrs             = gqrs;
        _onquadro         = onquadro;
        _eltetrado        = eltetrado;
        _jobRunner        = jobRunner;
        _engineSelector   = engineSelector;
        _pipelinePdbStore = pipelinePdbStore;
        _altPdbStore      = altPdbStore;
        _frameStore       = frameStore;
        _options          = options.Value;
        _logger           = logger;
    }

    // ── SSE pipeline: ONQuadro aligner → Quadro for best QRS ────────────────

    [HttpPost("run")]
    public async Task RunPipeline([FromBody] PipelineRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Connection"]        = "keep-alive";

        var sequence = request?.Sequence?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(sequence))
        {
            await SendEventAsync("error", new { message = "Sequence is required." }, cancellationToken);
            return;
        }

        await SendEventAsync("start", new { type = "start" }, cancellationToken);

        var rnaResult = await RunViennaRnaAsync(sequence, cancellationToken);
        await RunOnquadroAsync(sequence, rnaResult.Success ? rnaResult.Structure : null, cancellationToken);

        await SendEventAsync("complete", new { type = "complete" }, cancellationToken);
    }

    // ── PDB retrieval ─────────────────────────────────────────────────────────

    [HttpGet("pdb/{jobId}")]
    public ActionResult GetPdb(string jobId)
    {
        var pdb = _pipelinePdbStore.Get(jobId);
        if (pdb is null) return NotFound();
        return File(pdb, "chemical/x-pdb", $"pipeline_{jobId}.pdb");
    }

    [HttpGet("alt-pdb/{jobId}")]
    public ActionResult GetAltPdb(string jobId)
    {
        var pdb = _altPdbStore.Get(jobId);
        if (pdb is null) return NotFound();
        return File(pdb, "chemical/x-pdb", $"pipeline_alt_{jobId}.pdb");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunOnquadroAsync(string sequence, string? rnaStructure, CancellationToken ct)
    {
        OnquadroResult? result = null;
        string? failReason = null;
        try
        {
            result = await _onquadro.AlignAsync(sequence, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            failReason = "onquadro-aligner timeout";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "onquadro-aligner failed");
            failReason = $"onquadro-aligner: {ex.Message}";
        }

        // Best match: tract_distance = 0, highest linker_score
        var best = result is { Success: true }
            ? result.Matches
                .Where(m => m.TractDistance == 0 && !string.IsNullOrWhiteSpace(m.MatchedSequence))
                .OrderByDescending(m => m.LinkerScore)
                .FirstOrDefault()
            : null;

        if (best is null)
        {
            // No similar structure in the ONQuadro database — fall back to predicting the
            // G4 topology directly from the sequence with gqrs, so we still compute a 3D model.
            var reason = failReason
                ?? (result is { Success: false } ? (result.Error ?? "aligner error")
                    : result is null || result.Matches.Count == 0 ? "no matching structures"
                    : "no match with tract_distance = 0");
            _logger.LogInformation(
                "No ONQuadro DB match ({Reason}); falling back to gqrs prediction", reason);
            await RunGqrsFallbackAsync(sequence, rnaStructure, ct);
            return;
        }

        // Extract the ViennaRNA sub-structure for the matched G4 region
        string? matchRnaStructure = null;
        if (rnaStructure != null)
        {
            int matchStart = sequence.IndexOf(best.MatchedSequence, StringComparison.OrdinalIgnoreCase);
            if (matchStart >= 0 && matchStart + best.MatchedSequence.Length <= rnaStructure.Length)
                matchRnaStructure = rnaStructure.Substring(matchStart, best.MatchedSequence.Length);
        }

        var candidates = G4TopologyGenerator.GenerateCandidatesFromQrs(
            "GQ", best.MatchedSequence, best.Qrs, best.TetradCount, matchRnaStructure);
        if (candidates.Count == 0)
        {
            // The aligner matched, but its QRS could not be turned into a topology
            // (e.g. QRS/sequence length mismatch, or fewer than four G-runs). Fall back to
            // sequence-based prediction so a 3D model is still produced.
            _logger.LogInformation(
                "ONQuadro QRS topology generation failed (qrs='{Qrs}', match='{Match}'); falling back to gqrs prediction",
                best.Qrs, best.MatchedSequence);
            await RunGqrsFallbackAsync(sequence, rnaStructure, ct);
            return;
        }

        await RunQuadroCandidatesAndEmitAsync("GQ", best.Qrs, candidates, matchRnaStructure, ct);
    }

    // Fallback when the ONQuadro aligner finds no similar structure in the database:
    // predict the G4 motif directly from the sequence with gqrs and build the topology
    // from it. Emits the same aligner_quadro_* events so the UI path is unchanged.
    private async Task RunGqrsFallbackAsync(string sequence, string? rnaStructure, CancellationToken ct)
    {
        const string label = "GQ";

        GqrsResult? gqrs = null;
        string? failReason = null;
        try
        {
            gqrs = await _gqrs.PredictAsync(sequence, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            failReason = "gqrs timeout";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "gqrs fallback failed");
            failReason = $"gqrs: {ex.Message}";
        }

        // Strongest motif: most tetrads, then highest G-score.
        var motif = gqrs is { Success: true }
            ? gqrs.Motifs
                .Where(m => m.Tetrads is >= 1 and <= 4)
                .OrderByDescending(m => m.Tetrads)
                .ThenByDescending(m => m.GScore)
                .FirstOrDefault()
            : null;

        if (motif is null)
            _logger.LogInformation(
                "gqrs produced no usable motif ({Reason}); falling back to direct G-tract scan",
                failReason ?? gqrs?.Error ?? "no motif");

        // Build the topology from the gqrs motif when available; otherwise fall back to a
        // direct G-tract scan of the sequence. A 3D model is always attempted when the
        // sequence can fold into a G4 — only a sequence with fewer than four G-tracts fails.
        IReadOnlyList<G4TopologyGenerator.GeneratedTopology> candidates = [];
        string qrs = "";

        if (motif is not null)
        {
            candidates = G4TopologyGenerator.GenerateCandidatesFromGqrs(label, sequence, rnaStructure, motif);
            if (candidates.Count > 0) qrs = BuildQrsFromMotif(sequence, motif);
        }

        if (candidates.Count == 0)
        {
            // Last-resort fallback: scan the raw sequence for four G-tracts directly.
            candidates = G4TopologyGenerator.GenerateCandidates(label, sequence, rnaStructure);
            if (candidates.Count > 0) qrs = BuildQrsFromStructure(candidates[0].Input.Structure);
        }

        if (candidates.Count == 0)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = label,
                success = false,
                error   = "The sequence does not contain four G-tracts, so it cannot fold into a G-quadruplex.",
            }, ct);
            return;
        }

        await RunQuadroCandidatesAndEmitAsync(label, qrs, candidates, rnaStructure, ct);
    }

    // Runs Quadro on every predicted topology candidate and streams a single
    // aligner_quadro_done event whose primary fields describe the lowest-energy model,
    // with a `candidates` array carrying each alternative (own jobId, energy, .inp).
    // Shared by the ONQuadro-aligner path and the gqrs fallback path.
    private async Task RunQuadroCandidatesAndEmitAsync(
        string label, string qrs,
        IReadOnlyList<G4TopologyGenerator.GeneratedTopology> candidates,
        string? rnaStructure, CancellationToken ct)
    {
        await SendEventAsync("aligner_quadro_start",
            new { type = "aligner_quadro_start", tool = label, qrs }, ct);

        if (candidates.Count == 0)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = label,
                qrs,
                success = false,
                error   = "Could not generate Quadro input from QRS topology.",
            }, ct);
            return;
        }

        // Run quadro for each candidate (cap at 3 to bound compute time).
        var runs = new List<CandidateRun>();
        foreach (var cand in candidates.Take(3))
            runs.Add(await RunOneCandidateAsync(label, cand, ct));

        object ProjectCandidate(CandidateRun r) => new
        {
            jobId             = r.JobId,
            label             = r.Label,
            confidence        = r.Confidence,
            rationale         = r.Rationale,
            loopNotation      = r.LoopNotation,
            success           = r.Success,
            error             = r.Error,
            stdEnergy         = r.StdEnergy,
            altEnergy         = r.AltEnergy,
            winner            = r.Winner,
            hasAlt            = r.HasAlt,
            stdFrames         = r.StdFrames,
            altFrames         = r.AltFrames,
            stdBestStep       = r.StdBestStep,
            altBestStep       = r.AltBestStep,
            inpContent        = r.InpContent,
            combinedStructure = r.CombinedStructure,
        };

        var succeeded = runs.Where(r => r.Success).ToList();
        if (succeeded.Count == 0)
        {
            var first = runs[0];
            await SendEventAsync("aligner_quadro_done", new
            {
                type              = "aligner_quadro_done",
                tool              = label,
                qrs,
                success           = false,
                error             = first.Error ?? "Quadro produced no model for any topology",
                inpContent        = first.InpContent,
                combinedStructure = first.CombinedStructure,
                candidates        = runs.Select(ProjectCandidate).ToList(),
            }, ct);
            return;
        }

        // Best = lowest standard energy (CYANA/Xplor — lower is better).
        var best = succeeded.OrderBy(r => r.StdEnergy ?? double.PositiveInfinity).First();

        // ElTetrado on the winning model only (non-fatal).
        string? eltetradoOutput = null;
        string? eltetradoError  = null;
        if (best.StdPdb is { Length: > 0 })
        {
            try
            {
                using var eltCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                eltCts.CancelAfter(TimeSpan.FromSeconds(90));
                var elt = await _eltetrado.AnalyseAsync(best.StdPdb, eltCts.Token);
                eltetradoOutput = elt.Output;
                eltetradoError  = elt.Error;
            }
            catch (OperationCanceledException) { eltetradoError = "eltetrado timeout"; }
            catch (Exception ex) { eltetradoError = ex.Message; }
        }

        await SendEventAsync("aligner_quadro_done", new
        {
            type               = "aligner_quadro_done",
            tool               = label,
            qrs,
            rnaStructure,
            success            = true,
            jobId              = best.JobId,
            stdEnergy          = best.StdEnergy,
            altEnergy          = best.AltEnergy,
            winner             = best.Winner,
            hasAlt             = best.HasAlt,
            stdFrames          = best.StdFrames,
            altFrames          = best.AltFrames,
            stdBestStep        = best.StdBestStep,
            altBestStep        = best.AltBestStep,
            inpContent         = best.InpContent,
            combinedStructure  = best.CombinedStructure,
            topologyLabel      = best.Label,
            topologyConfidence = best.Confidence,
            topologyRationale  = best.Rationale,
            loopNotation       = best.LoopNotation,
            candidates         = runs.Select(ProjectCandidate).ToList(),
            eltetradoOutput,
            eltetradoError,
        }, ct);
    }

    private sealed record CandidateRun(
        string JobId, string Label, string Confidence, string Rationale, string LoopNotation,
        bool Success, string? Error, string InpContent, string CombinedStructure,
        double? StdEnergy, double? AltEnergy, string Winner, bool HasAlt,
        object StdFrames, object AltFrames, int? StdBestStep, int? AltBestStep,
        byte[]? StdPdb);

    // Runs quadro for a single topology candidate, stores its PDB/frames under a fresh jobId,
    // and returns the data needed to surface it. Never throws — failures become a failed run.
    private async Task<CandidateRun> RunOneCandidateAsync(
        string label, G4TopologyGenerator.GeneratedTopology cand, CancellationToken ct)
    {
        var jobId  = $"aq_{Guid.NewGuid():N}"[..16];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4_aligner_{jobId}");
        Directory.CreateDirectory(jobDir);

        var inpContent        = _engineSelector.Active.SerializeInput(cand.Input);
        var combinedStructure = cand.Input.Structure ?? "";

        CandidateRun Failed(string error) => new(
            jobId, cand.Label, cand.Confidence, cand.Rationale, cand.LoopNotation,
            false, error, inpContent, combinedStructure,
            null, null, "standard", false,
            Array.Empty<object>(), Array.Empty<object>(), null, null, null);

        try
        {
            var items = new List<QuadroJobItem> { new(0, "struct_000.inp", inpContent) };
            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            jobCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var result = await _jobRunner.RunAsync(jobId, jobDir, items, jobCts.Token);

            var stdOk = result.Standard.Success    && result.Standard.Pdb    is { Length: > 0 };
            var altOk = result.Alternative.Success && result.Alternative.Pdb is { Length: > 0 };
            if (!stdOk) return Failed("Quadro produced no PDB");

            _pipelinePdbStore.Store(jobId, result.Standard.Pdb!);
            if (altOk && result.Alternative.Pdb is not null)
                _altPdbStore.Store(jobId, result.Alternative.Pdb);

            foreach (var frame in result.Standard.Frames)
                _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);
            if (altOk)
                foreach (var frame in result.Alternative.Frames)
                    _frameStore.Store(jobId, "alt", frame.Step, frame.Pdb);

            var stdFramesMeta = result.Standard.Frames
                .Select(f => (object)new { step = f.Step, energy = f.Etotal }).ToList();
            var altFramesMeta = result.Alternative.Frames
                .Select(f => (object)new { step = f.Step, energy = f.Etotal }).ToList();

            return new CandidateRun(
                jobId, cand.Label, cand.Confidence, cand.Rationale, cand.LoopNotation,
                true, null, inpContent, combinedStructure,
                result.Standard.Etotal, result.Alternative.Etotal, result.Winner ?? "standard", altOk,
                stdFramesMeta, altFramesMeta,
                result.Standard.BestFrame?.Step, result.Alternative.BestFrame?.Step,
                result.Standard.Pdb);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed($"Quadro timeout after {_options.TimeoutSeconds}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Quadro failed for candidate {Label} ({Notation})", cand.Label, cand.LoopNotation);
            return Failed(ex.Message);
        }
        finally
        {
            _ = Task.Run(() =>
            {
                try { if (Directory.Exists(jobDir)) Directory.Delete(jobDir, recursive: true); }
                catch (Exception ex) { _logger.LogWarning("Could not delete {Dir}: {Msg}", jobDir, ex.Message); }
            });
        }
    }

    private async Task<ToolStructureResult> RunViennaRnaAsync(
        string sequence, CancellationToken ct)
    {
        const string tool = "ViennaRNA";
        var cmd = $"echo '{sequence}' | RNAfold --noPS --gquad";

        try
        {
            var result = await _docker.RunAsync(
                ["run", "--rm", "--entrypoint", "/bin/sh", "viennarna:latest", "-c", cmd], ct);

            if (result.ExitCode != 0)
                return new ToolStructureResult(tool, false, null, null,
                    $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

            var lines = result.Stdout.Split('\n', StringSplitOptions.None);
            var structLine = lines.LastOrDefault(l =>
                l.Trim().Length > 0 &&
                (l.Contains('.') || l.Contains('(') || l.Contains('+') || l.Contains('~')));

            if (structLine is null)
                return new ToolStructureResult(tool, false, null, null,
                    $"Unexpected output: {Truncate(result.Stdout)}");

            var parts     = structLine.Trim().Split(' ', 2);
            // Neutralise ViennaRNA's G-quadruplex markers to '.' (these positions are
            // re-stamped from the QRS/gqrs motif as '^'). RNAfold --gquad marks every G of
            // a quadruplex with VRNA_GQUAD_DB_SYMBOL '+' and the LAST G with the terminator
            // VRNA_GQUAD_DB_SYMBOL_END '~' — both must be stripped, not just '+'.
            var structure = parts[0].Trim().Replace('+', '.').Replace('~', '.');
            double? energy = parts.Length > 1 ? ParseParenEnergy(parts[1]) : null;

            return new ToolStructureResult(tool, true, structure, energy, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ViennaRNA failed");
            return new ToolStructureResult(tool, false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Builds a QRS-style display string (same length as the sequence) from a gqrs motif:
    /// 'G' at each of the four G-tract positions, '.' elsewhere. Used purely for display in
    /// the gqrs fallback path — the topology itself is built by
    /// <see cref="G4TopologyGenerator.TryGenerateFromGqrs"/> from the same positions.
    /// </summary>
    private static string BuildQrsFromMotif(string sequence, GqrsMotif motif)
    {
        var chars = new char[sequence.Length];
        Array.Fill(chars, '.');
        int n = motif.Tetrads;
        foreach (var start in new[] { motif.Tetrad1, motif.Tetrad2, motif.Tetrad3, motif.Tetrad4 })
            for (int k = 0; k < n; k++)
            {
                int pos = start + k;
                if (pos >= 0 && pos < chars.Length) chars[pos] = 'G';
            }
        return new string(chars);
    }

    /// <summary>
    /// Builds a QRS-style display string from a generated structure field by mapping every
    /// G-tetrad marker ('^') to 'G' and everything else to '.'. Used by the direct G-tract
    /// fallback, which has no motif to derive positions from.
    /// </summary>
    private static string BuildQrsFromStructure(string? structure)
    {
        if (string.IsNullOrEmpty(structure)) return "";
        var chars = structure.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            chars[i] = chars[i] == '^' ? 'G' : '.';
        return new string(chars);
    }

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private async Task SendEventAsync(string eventType, object data, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var json  = JsonSerializer.Serialize(data, JsonOpts);
        var line  = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _writeLock.WaitAsync(ct);
        try
        {
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning("SSE write failed: {Msg}", ex.Message); }
        finally { _writeLock.Release(); }
    }

    private static double? ParseParenEnergy(string text)
    {
        var m = EnergyRegex.Match(text);
        return m.Success ? ParseDouble(m.Groups[1].Value) : null;
    }

    private static double? ParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string Truncate(string text, int max = 200)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..max];
    }
}
