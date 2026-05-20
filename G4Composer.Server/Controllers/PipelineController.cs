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

        await RunOnquadroAsync(sequence, cancellationToken);

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

    private async Task RunOnquadroAsync(string sequence, CancellationToken ct)
    {
        OnquadroResult? result = null;
        try
        {
            result = await _onquadro.AlignAsync(sequence, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = "GQ",
                success = false,
                error   = "onquadro-aligner timeout",
            }, ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "onquadro-aligner failed");
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = "GQ",
                success = false,
                error   = $"onquadro-aligner: {ex.Message}",
            }, ct);
            return;
        }

        if (!result.Success || result.Matches.Count == 0)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = "GQ",
                success = false,
                error   = result.Error ?? "No matching structures found",
            }, ct);
            return;
        }

        // Best match: tract_distance = 0, highest linker_score
        var best = result.Matches
            .Where(m => m.TractDistance == 0 && !string.IsNullOrWhiteSpace(m.MatchedSequence))
            .OrderByDescending(m => m.LinkerScore)
            .FirstOrDefault();

        if (best is null)
        {
            await SendEventAsync("aligner_quadro_done", new
            {
                type    = "aligner_quadro_done",
                tool    = "GQ",
                success = false,
                error   = "No match with tract_distance = 0",
            }, ct);
            return;
        }

        await RunQuadroForAlignerAsync("GQ", best.MatchedSequence, best.Qrs, ct);
    }

    private async Task RunQuadroForAlignerAsync(
        string label, string matchedSequence, string qrs, CancellationToken ct)
    {
        await SendEventAsync("aligner_quadro_start",
            new { type = "aligner_quadro_start", tool = label, qrs }, ct);

        var jobId  = $"aq_{Guid.NewGuid():N}"[..16];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4_aligner_{jobId}");
        string? inpContent        = null;
        string? combinedStructure = null;
        Directory.CreateDirectory(jobDir);

        try
        {
            var engine = _engineSelector.Active;
            var input  = G4TopologyGenerator.TryGenerateFromQrs(label, matchedSequence, qrs);

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

            var stdFramesMeta = result.Standard.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });
            var altFramesMeta = result.Alternative.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });

            await SendEventAsync("aligner_quadro_done", new
            {
                type              = "aligner_quadro_done",
                tool              = label,
                qrs,
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

    private async Task RunQuadroForMotifAsync(
        GqrsMotif motif, string sequence, string? rnaStructure, CancellationToken ct)
    {
        var label = $"G4_{motif.Id}";
        await SendEventAsync("quadro_start", new { type = "quadro_start", tool = label, motifId = motif.Id }, ct);

        var jobId  = $"pl_{Guid.NewGuid():N}"[..16];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4_pipeline_{jobId}");
        string? inpContent        = null;
        string? combinedStructure = null;
        Directory.CreateDirectory(jobDir);

        try
        {
            var engine = _engineSelector.Active;
            var input  = G4TopologyGenerator.TryGenerateFromGqrs(label, sequence, rnaStructure, motif);

            if (input is null)
            {
                await SendEventAsync("quadro_done", new
                {
                    type    = "quadro_done",
                    tool    = label,
                    motifId = motif.Id,
                    success = false,
                    error   = "Could not generate Quadro input from gqrs motif (positions out of range?).",
                }, ct);
                return;
            }

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
                await SendEventAsync("quadro_done", new
                {
                    type              = "quadro_done",
                    tool              = label,
                    motifId           = motif.Id,
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

            var stdFramesMeta = result.Standard.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });
            var altFramesMeta = result.Alternative.Frames
                .Select(f => new { step = f.Step, energy = f.Etotal });

            await SendEventAsync("quadro_done", new
            {
                type              = "quadro_done",
                tool              = label,
                motifId           = motif.Id,
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
            }, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendEventAsync("quadro_done", new
            {
                type              = "quadro_done",
                tool              = label,
                motifId           = motif.Id,
                success           = false,
                error             = $"Quadro timeout after {_options.TimeoutSeconds}s",
                inpContent,
                combinedStructure,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Quadro failed for motif {Id}", motif.Id);
            var quadroOutput = ex is DockerException dx ? dx.DockerOutput : null;
            await SendEventAsync("quadro_done", new
            {
                type              = "quadro_done",
                tool              = label,
                motifId           = motif.Id,
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
