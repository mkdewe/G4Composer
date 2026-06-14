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

        var input = G4TopologyGenerator.TryGenerateFromQrs("GQ", best.MatchedSequence, best.Qrs, matchRnaStructure);
        if (input is null)
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

        await RunQuadroAndEmitAsync("GQ", best.Qrs, input, matchRnaStructure, ct);
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
        QuadroInput? input = null;
        string qrs = "";

        if (motif is not null)
        {
            input = G4TopologyGenerator.TryGenerateFromGqrs(label, sequence, rnaStructure, motif);
            if (input is not null) qrs = BuildQrsFromMotif(sequence, motif);
        }

        if (input is null)
        {
            // Last-resort fallback: scan the raw sequence for four G-tracts directly.
            input = G4TopologyGenerator.TryGenerate(label, sequence, rnaStructure);
            if (input is not null) qrs = BuildQrsFromStructure(input.Structure);
        }

        if (input is null)
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

        await RunQuadroAndEmitAsync(label, qrs, input, rnaStructure, ct);
    }

    // Runs Quadro on an already-generated topology and streams the aligner_quadro_* events.
    // Shared by the ONQuadro-aligner path and the gqrs fallback path.
    private async Task RunQuadroAndEmitAsync(
        string label, string qrs, QuadroInput? input, string? rnaStructure, CancellationToken ct)
    {
        await SendEventAsync("aligner_quadro_start",
            new { type = "aligner_quadro_start", tool = label, qrs }, ct);

        if (input is null)
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

        var jobId  = $"aq_{Guid.NewGuid():N}"[..16];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4_aligner_{jobId}");
        string? inpContent        = null;
        string? combinedStructure = null;
        Directory.CreateDirectory(jobDir);

        try
        {
            var engine = _engineSelector.Active;

            inpContent        = engine.SerializeInput(input);
            combinedStructure = input.Structure;
            var items  = new List<QuadroJobItem> { new(0, "struct_000.inp", inpContent) };

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            jobCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var result = await _jobRunner.RunAsync(jobId, jobDir, items, jobCts.Token);

            var stdOk = result.Standard.Success    && result.Standard.Pdb    is { Length: > 0 };
            var altOk = result.Alternative.Success && result.Alternative.Pdb is { Length: > 0 };

            if (!stdOk)
            {
                await SendEventAsync("aligner_quadro_done", new
                {
                    type              = "aligner_quadro_done",
                    tool              = label,
                    qrs,
                    success           = false,
                    error             = "Quadro produced no PDB",
                    inpContent,
                    combinedStructure,
                }, ct);
                return;
            }

            _pipelinePdbStore.Store(jobId, result.Standard.Pdb!);
            if (altOk && result.Alternative.Pdb is not null)
                _altPdbStore.Store(jobId, result.Alternative.Pdb);

            foreach (var frame in result.Standard.Frames)
                _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);
            if (altOk)
                foreach (var frame in result.Alternative.Frames)
                    _frameStore.Store(jobId, "alt", frame.Step, frame.Pdb);

            // Run eltetrado analysis on the final PDB (non-fatal if it fails)
            string? eltetradoOutput = null;
            string? eltetradoError  = null;
            try
            {
                using var eltCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                eltCts.CancelAfter(TimeSpan.FromSeconds(90));
                var elt = await _eltetrado.AnalyseAsync(result.Standard.Pdb!, eltCts.Token);
                eltetradoOutput = elt.Output;
                eltetradoError  = elt.Error;
            }
            catch (OperationCanceledException) { eltetradoError = "eltetrado timeout"; }
            catch (Exception ex) { eltetradoError = ex.Message; }

            var stdFramesMeta = result.Standard.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });
            var altFramesMeta = result.Alternative.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });

            await SendEventAsync("aligner_quadro_done", new
            {
                type              = "aligner_quadro_done",
                tool              = label,
                qrs,
                rnaStructure,
                success           = true,
                jobId,
                stdEnergy         = result.Standard.Etotal,
                altEnergy         = result.Alternative.Etotal,
                winner            = result.Winner ?? "standard",
                hasAlt            = altOk,
                stdFrames         = stdFramesMeta,
                altFrames         = altFramesMeta,
                stdBestStep       = result.Standard.BestFrame?.Step,
                altBestStep       = result.Alternative.BestFrame?.Step,
                inpContent,
                combinedStructure,
                eltetradoOutput,
                eltetradoError,
            }, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type              = "aligner_quadro_done",
                tool              = label,
                qrs,
                success           = false,
                error             = $"Quadro timeout after {_options.TimeoutSeconds}s",
                inpContent,
                combinedStructure,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Quadro failed for aligner {Label}", label);
            var quadroOutput = ex is DockerException dx ? dx.DockerOutput : null;
            await SendEventAsync("aligner_quadro_done", new
            {
                type              = "aligner_quadro_done",
                tool              = label,
                qrs,
                success           = false,
                error             = ex.Message,
                inpContent,
                combinedStructure,
                quadroOutput,
            }, ct);
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
                l.Trim().Length > 0 && (l.Contains('.') || l.Contains('(') || l.Contains('+')));

            if (structLine is null)
                return new ToolStructureResult(tool, false, null, null,
                    $"Unexpected output: {Truncate(result.Stdout)}");

            var parts     = structLine.Trim().Split(' ', 2);
            // Strip '+' from --g-quadruplex output (replaced by gqrs positions as '^')
            var structure = parts[0].Trim().Replace('+', '.');
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
