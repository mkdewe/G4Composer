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

    private readonly IRnaSecondaryStructureService _rna;
    private readonly IQuadroJobRunner              _jobRunner;
    private readonly IQuadroEngineSelector         _engineSelector;
    private readonly IPipelinePdbStore             _pipelinePdbStore;
    private readonly IAltPdbStore                  _altPdbStore;
    private readonly QuadroOptions                 _options;
    private readonly ILogger<PipelineController>   _logger;

    public PipelineController(
        IRnaSecondaryStructureService rna,
        IQuadroJobRunner jobRunner,
        IQuadroEngineSelector engineSelector,
        IPipelinePdbStore pipelinePdbStore,
        IAltPdbStore altPdbStore,
        IOptions<QuadroOptions> options,
        ILogger<PipelineController> logger)
    {
        _rna              = rna;
        _jobRunner        = jobRunner;
        _engineSelector   = engineSelector;
        _pipelinePdbStore = pipelinePdbStore;
        _altPdbStore      = altPdbStore;
        _options          = options.Value;
        _logger           = logger;
    }

    // ── SSE pipeline ─────────────────────────────────────────────────────────

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

        var toolNames = _rna.ToolNames;
        await SendEventAsync("start", new { type = "start", tools = toolNames, count = toolNames.Count }, cancellationToken);

        // ── Phase 1: Run all RNA tools in parallel ───────────────────────────
        var rnaTasks = toolNames
            .Select(name => RunRnaToolAsync(name, sequence, cancellationToken))
            .ToList();

        var rnaResults = new Dictionary<string, ToolStructureResult>();

        // Use WhenAny loop so we emit events as each tool completes
        var pending = rnaTasks.ToList();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);

            ToolStructureResult rnaResult;
            try   { rnaResult = await completed; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RNA task faulted unexpectedly");
                continue;
            }

            rnaResults[rnaResult.ToolName] = rnaResult;

            if (rnaResult.Success)
            {
                await SendEventAsync("rna_done", new
                {
                    type      = "rna_done",
                    tool      = rnaResult.ToolName,
                    success   = true,
                    structure = rnaResult.Structure,
                    energy    = rnaResult.Energy,
                }, cancellationToken);
            }
            else
            {
                await SendEventAsync("rna_done", new
                {
                    type    = "rna_done",
                    tool    = rnaResult.ToolName,
                    success = false,
                    error   = rnaResult.Error,
                }, cancellationToken);
            }
        }

        // ── Phase 2: Deduplicate by structure, then run Quadro in parallel ───────
        // Process tools in the fixed canonical order so deduplication is deterministic.
        var orderedSuccessful = _rna.ToolNames
            .Where(name => rnaResults.TryGetValue(name, out var r) && r.Success)
            .Select(name => rnaResults[name])
            .ToList();

        var seenStructures = new HashSet<string>(StringComparer.Ordinal);
        var uniqueRna      = new List<ToolStructureResult>();

        foreach (var rna in orderedSuccessful)
        {
            var s = rna.Structure ?? string.Empty;
            if (!seenStructures.Add(s))
            {
                var original = uniqueRna.FirstOrDefault(r => r.Structure == s)?.ToolName ?? "another tool";
                await SendEventAsync("quadro_skip", new
                {
                    type     = "quadro_skip",
                    tool     = rna.ToolName,
                    original,
                }, cancellationToken);
            }
            else
            {
                uniqueRna.Add(rna);
            }
        }

        var quadroTasks = uniqueRna
            .Select(r => RunQuadroForToolAsync(r, sequence, cancellationToken))
            .ToList();

        await Task.WhenAll(quadroTasks);

        await SendEventAsync("complete", new { type = "complete" }, cancellationToken);
    }

    // ── PDB retrieval ─────────────────────────────────────────────────────────

    [HttpGet("pdb/{jobId}")]
    public ActionResult GetPdb(string jobId)
    {
        var pdb = _pipelinePdbStore.Get(jobId);
        if (pdb is null)
            return NotFound();
        return File(pdb, "chemical/x-pdb", $"pipeline_{jobId}.pdb");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<ToolStructureResult> RunRnaToolAsync(string toolName, string sequence, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Pipeline: starting RNA tool {Tool}", toolName);
            var result = await _rna.PredictAsync(toolName, sequence, ct);
            _logger.LogInformation("Pipeline: RNA tool {Tool} {Status}", toolName, result.Success ? "succeeded" : "failed");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline: RNA tool {Tool} threw", toolName);
            return new ToolStructureResult(toolName, false, null, null, ex.Message);
        }
    }

    private async Task RunQuadroForToolAsync(ToolStructureResult rnaResult, string sequence, CancellationToken ct)
    {
        var toolName = rnaResult.ToolName;
        await SendEventAsync("quadro_start", new { type = "quadro_start", tool = toolName }, ct);

        var jobId      = $"pl_{Guid.NewGuid():N}"[..16];
        var jobDir     = Path.Combine(Path.GetTempPath(), $"g4_pipeline_{jobId}");
        string? inpContent = null;   // hoisted so catch blocks can include it
        Directory.CreateDirectory(jobDir);

        try
        {
            var engine = _engineSelector.Active;

            var input = G4TopologyGenerator.TryGenerate(toolName, sequence, rnaResult.Structure);
            if (input is null)
            {
                await SendEventAsync("quadro_done", new
                {
                    type    = "quadro_done",
                    tool    = toolName,
                    success = false,
                    error   = "Sequence does not contain 4 G-tracts required for G4 topology.",
                }, ct);
                return;
            }

            inpContent      = engine.SerializeInput(input);
            var inpFileName = "struct_000.inp";
            var items       = new List<QuadroJobItem>
            {
                new(0, inpFileName, inpContent),
            };

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            jobCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var result = await _jobRunner.RunAsync(jobId, jobDir, items, jobCts.Token);

            var stdOk  = result.Standard.Success    && result.Standard.Pdb    is { Length: > 0 };
            var altOk  = result.Alternative.Success && result.Alternative.Pdb is { Length: > 0 };

            if (!stdOk)
            {
                await SendEventAsync("quadro_done", new
                {
                    type    = "quadro_done",
                    tool    = toolName,
                    success = false,
                    error   = "Quadro produced no PDB",
                    inpContent,
                }, ct);
                return;
            }

            // Store PDBs
            _pipelinePdbStore.Store(jobId, result.Standard.Pdb!);
            if (altOk && result.Alternative.Pdb is not null)
                _altPdbStore.Store(jobId, result.Alternative.Pdb);

            await SendEventAsync("quadro_done", new
            {
                type       = "quadro_done",
                tool       = toolName,
                success    = true,
                jobId,
                stdEnergy  = result.Standard.Etotal,
                altEnergy  = result.Alternative.Etotal,
                winner     = result.Winner ?? "standard",
                hasAlt     = altOk,
                inpContent,
            }, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendEventAsync("quadro_done", new
            {
                type    = "quadro_done",
                tool    = toolName,
                success = false,
                error   = $"Quadro timeout after {_options.TimeoutSeconds}s",
                inpContent,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pipeline: Quadro failed for tool {Tool}", toolName);
            var quadroOutput = ex is DockerException dx ? dx.DockerOutput : null;
            await SendEventAsync("quadro_done", new
            {
                type         = "quadro_done",
                tool         = toolName,
                success      = false,
                error        = ex.Message,
                inpContent,
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

        var json = JsonSerializer.Serialize(data, JsonOpts);
        var line = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _writeLock.WaitAsync(ct);
        try
        {
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex) { _logger.LogWarning("SSE write failed: {Msg}", ex.Message); }
        finally { _writeLock.Release(); }
    }
}
