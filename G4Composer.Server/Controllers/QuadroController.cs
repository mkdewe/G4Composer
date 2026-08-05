using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.Filters;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Examples;
using G4Composer.Server.Models;
using G4Composer.Server.Services;
using G4Composer.Server.Validation;

namespace G4Composer.Server.Controllers;

/// <summary>
/// API obliczeń G-kwadrupleksu. Cienki kontroler — cała logika domenowa
/// znajduje się w warstwie serwisów (<see cref="IQuadroJobRunner"/>,
/// <see cref="IDockerHealthService"/>) i silników (<see cref="IQuadroEngine"/>).
///
/// Route <c>api/quadro11</c> zachowany dla zgodności z istniejącym frontendem.
/// W przyszłości można rozważyć migrację do <c>api/quadro</c>.
/// </summary>
[ApiController]
[Route("api/quadro11")]
[Produces("application/json")]
public sealed class QuadroController : ControllerBase
{
    private const string SwaggerTag = "Quadro";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly ILogger<QuadroController> _logger;
    private readonly IQuadroEngineSelector _engineSelector;
    private readonly IDockerHealthService _healthService;
    private readonly IQuadroJobRunner _jobRunner;
    private readonly IValidator<QuadroInput> _validator;
    private readonly QuadroOptions _options;
    private readonly IJobLogStore _logStore;
    private readonly IAltPdbStore _altPdbStore;
    private readonly IFrameStore _frameStore;
    private readonly IPdbCacheService _cacheService;

    public QuadroController(
        ILogger<QuadroController> logger,
        IQuadroEngineSelector engineSelector,
        IDockerHealthService healthService,
        IQuadroJobRunner jobRunner,
        IValidator<QuadroInput> validator,
        IOptions<QuadroOptions> options,
        IJobLogStore logStore,
        IAltPdbStore altPdbStore,
        IFrameStore frameStore,
        IPdbCacheService cacheService)
    {
        _logger = logger;
        _engineSelector = engineSelector;
        _healthService = healthService;
        _jobRunner = jobRunner;
        _validator = validator;
        _options = options.Value;
        _logStore = logStore;
        _altPdbStore = altPdbStore;
        _frameStore = frameStore;
        _cacheService = cacheService;
    }

    // ── Health ───────────────────────────────────────────────────────────────

    [HttpGet("health")]
    [SwaggerOperation(Summary = "Health check", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(HealthDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HealthDto>> Health(CancellationToken cancellationToken)
    {
        var engine = _engineSelector.Active;
        var dockerAvailable = await _healthService.IsDockerAvailableAsync(cancellationToken);
        var imageExists = dockerAvailable
            && await _healthService.ImageExistsAsync(engine.Image, cancellationToken);

        return Ok(new HealthDto
        {
            Status          = imageExists ? "ready" : "degraded",
            EngineVersion   = engine.Version,
            DockerAvailable = dockerAvailable,
            ImageExists     = imageExists,
            ImageName       = engine.Image,
        });
    }

    // ── Example ──────────────────────────────────────────────────────────────

    [HttpGet("example")]
    [SwaggerOperation(Summary = "Example input", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(List<QuadroInput>), StatusCodes.Status200OK)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(QuadroInputListExample))]
    public ActionResult<List<QuadroInput>> GetExample()
        => Ok(new QuadroInputListExample().GetExamples());

    // ── Run ──────────────────────────────────────────────────────────────────

    [HttpPost("run")]
    [Consumes("application/json")]
    [Produces("chemical/x-pdb", "application/json")]
    [SwaggerOperation(
        Summary     = "Run Quadro computation",
        Description = "Generates .inp files, runs the Quadro Docker container and returns the resulting .pdb file.",
        Tags        = [SwaggerTag])]
    [SwaggerRequestExample(typeof(List<QuadroInput>), typeof(QuadroInputListExample))]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "chemical/x-pdb")]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Run([FromBody] List<QuadroInput> inputs, CancellationToken cancellationToken)
    {
        if (inputs is null || inputs.Count == 0)
            return BadRequest(new ErrorDto("No input data. A list of QuadroInput is required."));

        // ── Walidacja (poza kontrolerem — przez wstrzyknięty walidator) ─────
        var validationErrors = inputs
            .SelectMany((inp, i) => _validator.Validate(inp).Errors.Select(e => $"[{i}] {e}"))
            .ToList();

        if (validationErrors.Count > 0)
            return BadRequest(new ValidationErrorDto("Validation failed.", validationErrors));

        var engine = _engineSelector.Active;

        // Cache: only single-structure runs have a well-defined dedup key (batch runs are a
        // sequential sweep over many distinct inputs — not a good caching candidate).
        string? cacheHash = null;
        if (inputs.Count == 1)
        {
            cacheHash = _cacheService.ComputeHash(inputs[0], engine);
            var cached = await TryLookupCacheAsync(cacheHash, inputs[0].IterationSteps, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Cache hit (entry {EntryId}): skipping Docker run.", cached.EntryId);
                return ServeCachedResult(cached);
            }
        }

        var jobId  = Guid.NewGuid().ToString("N")[..12];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4composer_{jobId}");
        Directory.CreateDirectory(jobDir);

        _logger.LogInformation(
            "Job {JobId}: started, engine={Version}, {Count} structure(s)",
            jobId, engine.Version, inputs.Count);

        // Pojedynczy CTS dla całego joba — timeout z konfiguracji.
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            // Build .inp items — engine decyduje o formacie pliku.
            var items = inputs
                .Select((inp, i) => new QuadroJobItem(
                    Index: i,
                    InpFileName: $"struct_{i:D3}.inp",
                    InpContent: engine.SerializeInput(inp)))
                .ToList();

            var result = await _jobRunner.RunAsync(jobId, jobDir, items, progress: null, jobCts.Token);

            if (!result.Standard.Success || result.Standard.Pdb is null || result.Standard.Pdb.Length == 0)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorDto("Container did not produce a PDB file."));

            _logger.LogInformation("Job {JobId}: success, {Bytes} bytes (std), altSuccess={AltOk}",
                jobId, result.Standard.Pdb.Length, result.Alternative.Success);

            // Store all frames for later retrieval
            foreach (var frame in result.Standard.Frames)
                _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);
            if (result.Alternative.Success)
                foreach (var frame in result.Alternative.Frames)
                    _frameStore.Store(jobId, "alt", frame.Step, frame.Pdb);

            // Store alt best-frame PDB for legacy GET /alt-pdb/{jobId}
            if (result.Alternative.Success && result.Alternative.Pdb is not null)
                _altPdbStore.Store(jobId, result.Alternative.Pdb);

            if (cacheHash is not null)
                await TrySaveToCacheAsync(cacheHash, inputs[0], engine, result.Standard, jobId, cancellationToken);

            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var stdEnergy    = result.Standard.Etotal?.ToString("F3", ic) ?? "";
            var altEnergy    = result.Alternative.Etotal?.ToString("F3", ic) ?? "";
            var stdSteps     = string.Join(",", result.Standard.Frames.Select(f => f.Step));
            var altSteps     = string.Join(",", result.Alternative.Frames.Select(f => f.Step));
            var stdEnergies  = string.Join(",", result.Standard.Frames.Select(f => f.Etotal?.ToString("F3", ic) ?? ""));
            var altEnergies  = string.Join(",", result.Alternative.Frames.Select(f => f.Etotal?.ToString("F3", ic) ?? ""));
            var stdBestStep  = result.Standard.BestFrame?.Step.ToString() ?? "";
            var altBestStep  = result.Alternative.BestFrame?.Step.ToString() ?? "";

            Response.Headers["X-Job-Id"]         = jobId;
            Response.Headers["X-Atom-Count"]      = CountPdbAtoms(result.Standard.Pdb).ToString();
            Response.Headers["X-Std-Energy"]      = stdEnergy;
            Response.Headers["X-Alt-Energy"]      = altEnergy;
            Response.Headers["X-Has-Alt"]         = result.Alternative.Success ? "1" : "0";
            Response.Headers["X-Winner"]          = result.Winner ?? "standard";
            Response.Headers["X-Std-Steps"]       = stdSteps;
            Response.Headers["X-Alt-Steps"]       = altSteps;
            Response.Headers["X-Std-Energies"]    = stdEnergies;
            Response.Headers["X-Alt-Energies"]    = altEnergies;
            Response.Headers["X-Std-Best-Step"]   = stdBestStep;
            Response.Headers["X-Alt-Best-Step"]   = altBestStep;
            Response.Headers["Access-Control-Expose-Headers"] =
                "X-Job-Id, X-Atom-Count, X-Std-Energy, X-Alt-Energy, X-Has-Alt, X-Winner, " +
                "X-Std-Steps, X-Alt-Steps, X-Std-Energies, X-Alt-Energies, X-Std-Best-Step, X-Alt-Best-Step";

            return File(result.Standard.Pdb, "chemical/x-pdb", $"g4_{jobId}.pdb");
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId}: timeout after {Sec}s", jobId, _options.TimeoutSeconds);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorDto($"Container timeout after {_options.TimeoutSeconds}s"));
        }
        catch (DockerException ex)
        {
            _logger.LogError(ex, "Job {JobId}: Docker error", jobId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorDto($"Docker container error: {ex.Message}", ex.DockerOutput));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: unexpected error", jobId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorDto($"Internal error: {DescribeError(ex)}"));
        }
        finally
        {
            // Sprzątanie w tle — nie blokuje odpowiedzi.
            _ = Task.Run(() => CleanupJobDir(jobDir));
        }
    }

    // ── Run (streaming) ────────────────────────────────────────────────────────

    /// <summary>
    /// Same computation as <see cref="Run"/>, but streamed over Server-Sent Events so the
    /// client can show real progress. Emits <c>progress</c> events for each coarse stage and a
    /// terminal <c>done</c> event carrying the metadata (jobId, energies, frames). The PDB
    /// itself is fetched afterwards via <c>GET frame/{jobId}/std/{bestStep}</c> and
    /// <c>GET alt-pdb/{jobId}</c> — identical retrieval to the pipeline path.
    /// </summary>
    [HttpPost("run-stream")]
    [Consumes("application/json")]
    public async Task RunStream([FromBody] List<QuadroInput> inputs, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Connection"]        = "keep-alive";

        if (inputs is null || inputs.Count == 0)
        {
            await SendEventAsync("error", new { type = "error", message = "No input data. A list of QuadroInput is required." }, cancellationToken);
            return;
        }

        var validationErrors = inputs
            .SelectMany((inp, i) => _validator.Validate(inp).Errors.Select(e => $"[{i}] {e}"))
            .ToList();
        if (validationErrors.Count > 0)
        {
            await SendEventAsync("error", new { type = "error", message = "Validation failed.", details = validationErrors }, cancellationToken);
            return;
        }

        var engine = _engineSelector.Active;

        string? cacheHash = null;
        if (inputs.Count == 1)
        {
            cacheHash = _cacheService.ComputeHash(inputs[0], engine);
            var cached = await TryLookupCacheAsync(cacheHash, inputs[0].IterationSteps, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Cache hit (entry {EntryId}): skipping Docker run (stream).", cached.EntryId);
                await SendCachedDoneEventAsync(cached, cancellationToken);
                return;
            }
        }

        var jobId  = Guid.NewGuid().ToString("N")[..12];
        var jobDir = Path.Combine(Path.GetTempPath(), $"g4composer_{jobId}");
        Directory.CreateDirectory(jobDir);

        _logger.LogInformation(
            "Job {JobId}: streaming run, engine={Version}, {Count} structure(s)",
            jobId, engine.Version, inputs.Count);

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        // Bridge IProgress (reported from the background run) → SSE writes (drained here).
        var channel  = Channel.CreateUnbounded<JobProgress>();
        var progress = new ChannelProgress(channel.Writer);

        var items = inputs
            .Select((inp, i) => new QuadroJobItem(i, $"struct_{i:D3}.inp", engine.SerializeInput(inp)))
            .ToList();

        var runTask = Task.Run(async () =>
        {
            try { return await _jobRunner.RunAsync(jobId, jobDir, items, progress, jobCts.Token); }
            finally { channel.Writer.TryComplete(); }
        });

        try
        {
            // Forward every milestone as it arrives; loop ends when the run completes the writer.
            await foreach (var p in channel.Reader.ReadAllAsync(cancellationToken))
                await SendEventAsync("progress", new
                {
                    type  = "progress",
                    stage = p.Stage, label = p.Label, index = p.Index, total = p.Total,
                    detail = p.Detail, percent = p.Percent,
                }, cancellationToken);

            var result = await runTask;   // surfaces any exception thrown by the run

            if (!result.Standard.Success || result.Standard.Pdb is null || result.Standard.Pdb.Length == 0)
            {
                await SendEventAsync("error", new { type = "error", message = "Container did not produce a PDB file." }, cancellationToken);
                return;
            }

            // Store frames + alt PDB so the client can fetch them by jobId (same as the blob path).
            foreach (var frame in result.Standard.Frames)
                _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);
            if (result.Alternative.Success)
                foreach (var frame in result.Alternative.Frames)
                    _frameStore.Store(jobId, "alt", frame.Step, frame.Pdb);
            if (result.Alternative.Success && result.Alternative.Pdb is not null)
                _altPdbStore.Store(jobId, result.Alternative.Pdb);

            if (cacheHash is not null)
                await TrySaveToCacheAsync(cacheHash, inputs[0], engine, result.Standard, jobId, cancellationToken);

            await SendEventAsync("done", new
            {
                type        = "done",
                jobId,
                atoms       = CountPdbAtoms(result.Standard.Pdb),
                stdEnergy   = result.Standard.Etotal,
                altEnergy   = result.Alternative.Etotal,
                hasAlt      = result.Alternative.Success,
                winner      = result.Winner ?? "standard",
                stdFrames   = result.Standard.Frames.Select(f => new { step = f.Step, energy = f.Etotal }).ToList(),
                altFrames   = result.Alternative.Frames.Select(f => new { step = f.Step, energy = f.Etotal }).ToList(),
                stdBestStep = result.Standard.BestFrame?.Step,
                altBestStep = result.Alternative.BestFrame?.Step,
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId}: timeout after {Sec}s", jobId, _options.TimeoutSeconds);
            await SendEventAsync("error", new { type = "error", message = $"Container timeout after {_options.TimeoutSeconds}s" }, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — nothing to send.
        }
        catch (DockerException ex)
        {
            _logger.LogError(ex, "Job {JobId}: Docker error", jobId);
            await SendEventAsync("error", new { type = "error", message = $"Docker container error: {ex.Message}", details = ex.DockerOutput }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: unexpected stream error", jobId);
            await SendEventAsync("error", new { type = "error", message = $"Internal error: {DescribeError(ex)}" }, CancellationToken.None);
        }
        finally
        {
            _ = Task.Run(() => CleanupJobDir(jobDir));
        }
    }

    // ── Frame PDB ────────────────────────────────────────────────────────────

    [HttpGet("frame/{jobId}/{engine}/{step:int}")]
    [SwaggerOperation(Summary = "Get PDB for a specific iteration frame", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "chemical/x-pdb")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetFrame(string jobId, string engine, int step)
    {
        var pdb = _frameStore.Get(jobId, engine, step);
        if (pdb is null)
            return NotFound(new ErrorDto($"Frame {engine}/{step} for job '{jobId}' not found."));

        return File(pdb, "chemical/x-pdb", $"g4_{jobId}_{engine}_{step}.pdb");
    }

    // ── Alt PDB ──────────────────────────────────────────────────────────────

    [HttpGet("alt-pdb/{jobId}")]
    [SwaggerOperation(Summary = "Get alternative-engine PDB for a completed job", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "chemical/x-pdb")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetAltPdb(string jobId)
    {
        var pdb = _altPdbStore.Get(jobId);
        if (pdb is null)
            return NotFound(new ErrorDto($"Alternative PDB for job '{jobId}' not found (may have expired or not been generated)."));

        return File(pdb, "chemical/x-pdb", $"g4_{jobId}_alt.pdb");
    }

    // ── Log ──────────────────────────────────────────────────────────────────

    [HttpGet("log/{jobId}")]
    [SwaggerOperation(Summary = "Get Docker execution log for a completed job", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<string> GetLog(string jobId)
    {
        var log = _logStore.Get(jobId);
        if (string.IsNullOrEmpty(log))
            return NotFound(new ErrorDto($"Log for job '{jobId}' not found (may have expired)."));

        return Content(log, "text/plain");
    }

    // ── Cache retrieve ───────────────────────────────────────────────────────

    /// <summary>Look up a cached result by its numeric id (the "Retrieve" identifier — not name).</summary>
    [HttpGet("cache/{id:int}")]
    [SwaggerOperation(Summary = "Get cached result metadata by id", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(PdbCacheEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PdbCacheEntryDto>> GetCacheEntry(int id, CancellationToken cancellationToken)
    {
        var entry = await _cacheService.GetByIdAsync(id, cancellationToken);
        if (entry is null)
            return NotFound(new ErrorDto($"Cached result '{id}' not found."));

        return Ok(ToDto(entry));
    }

    /// <summary>Look up a cached result by PDB id (only set for entries matching a curated Example).</summary>
    [HttpGet("cache/by-pdbid/{pdbId}")]
    [SwaggerOperation(Summary = "Get cached result metadata by PDB id", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(PdbCacheEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PdbCacheEntryDto>> GetCacheEntryByPdbId(string pdbId, CancellationToken cancellationToken)
    {
        var entry = await _cacheService.GetByPdbIdAsync(pdbId, cancellationToken);
        if (entry is null)
            return NotFound(new ErrorDto($"Cached result for PDB id '{pdbId}' not found."));

        return Ok(ToDto(entry));
    }

    /// <summary>PDB bytes for one specific iteration of a cached entry (preview of a chosen checkpoint).</summary>
    [HttpGet("cache/{id:int}/frame/{step:int}")]
    [SwaggerOperation(Summary = "Get PDB for a specific cached iteration", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "chemical/x-pdb")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetCacheFrame(int id, int step, CancellationToken cancellationToken)
    {
        var entry = await _cacheService.GetByIdAsync(id, cancellationToken);
        var frame = entry?.Frames.FirstOrDefault(f => f.Step == step);
        if (frame is null)
            return NotFound(new ErrorDto($"Cached frame {id}/{step} not found."));

        return File(Encoding.UTF8.GetBytes(frame.Pdb), "chemical/x-pdb", $"g4_cache_{id}_{step}.pdb");
    }

    private static PdbCacheEntryDto ToDto(G4Composer.Server.Data.Entities.PdbCacheEntry entry) => new(
        entry.Id, entry.PdbId, entry.IsExample, entry.EngineVersion,
        entry.CreatedAtUtc, entry.LastAccessedAtUtc,
        entry.Frames.OrderBy(f => f.Step).Select(f => new PdbCacheFrameDto(f.Step, f.Etotal)).ToList());

    /// <summary>
    /// A broken cache lookup should degrade to "run it fresh," never fail the request outright.
    /// </summary>
    private async Task<CachedRun?> TryLookupCacheAsync(string hash, IReadOnlyList<int> requestedSteps, CancellationToken ct)
    {
        try
        {
            return await _cacheService.TryGetAsync(hash, requestedSteps, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache lookup failed (non-fatal, running fresh): {Msg}", DescribeError(ex));
            return null;
        }
    }

    /// <summary>
    /// Caching a result is a nice-to-have, never a reason to fail a request whose Docker
    /// computation already succeeded — log and swallow any DB error instead of propagating it.
    /// </summary>
    private async Task TrySaveToCacheAsync(
        string cacheHash, QuadroInput input, IQuadroEngine engine, SingleRunResult result,
        string jobId, CancellationToken ct)
    {
        try
        {
            await _cacheService.SaveAsync(input, engine, cacheHash, result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: failed to save result to PDB cache (non-fatal): {Msg}",
                jobId, DescribeError(ex));
        }
    }

    // ── Cache helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the exact same response shape as a fresh <see cref="Run"/> call from a cache hit —
    /// same headers, same file — so the frontend cannot tell the difference except for timing
    /// and the added X-Cache-Hit header. Frames are re-published into the (short-lived)
    /// in-memory FrameStore under a synthetic job id so the per-step GET /frame endpoint keeps
    /// working when the user switches iterations in the UI.
    /// </summary>
    private IActionResult ServeCachedResult(CachedRun cached)
    {
        var jobId = $"cache-{cached.EntryId}-{Guid.NewGuid():N}"[..24];
        foreach (var frame in cached.Frames)
            _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);

        var best = PickBest(cached.Frames);
        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var steps    = string.Join(",", cached.Frames.Select(f => f.Step));
        var energies = string.Join(",", cached.Frames.Select(f => f.Etotal?.ToString("F3", ic) ?? ""));

        Response.Headers["X-Job-Id"]        = jobId;
        Response.Headers["X-Cache-Hit"]     = "1";
        Response.Headers["X-Cache-Entry-Id"] = cached.EntryId.ToString();
        Response.Headers["X-Atom-Count"]    = CountPdbAtoms(best.Pdb).ToString();
        Response.Headers["X-Std-Energy"]    = best.Etotal?.ToString("F3", ic) ?? "";
        Response.Headers["X-Alt-Energy"]    = "";
        Response.Headers["X-Has-Alt"]       = "0";
        Response.Headers["X-Winner"]        = "standard";
        Response.Headers["X-Std-Steps"]     = steps;
        Response.Headers["X-Alt-Steps"]     = "";
        Response.Headers["X-Std-Energies"]  = energies;
        Response.Headers["X-Alt-Energies"]  = "";
        Response.Headers["X-Std-Best-Step"] = best.Step.ToString();
        Response.Headers["X-Alt-Best-Step"] = "";
        Response.Headers["Access-Control-Expose-Headers"] =
            "X-Job-Id, X-Cache-Hit, X-Cache-Entry-Id, X-Atom-Count, X-Std-Energy, X-Alt-Energy, X-Has-Alt, X-Winner, " +
            "X-Std-Steps, X-Alt-Steps, X-Std-Energies, X-Alt-Energies, X-Std-Best-Step, X-Alt-Best-Step";

        return File(best.Pdb, "chemical/x-pdb", $"g4_{jobId}.pdb");
    }

    private async Task SendCachedDoneEventAsync(CachedRun cached, CancellationToken ct)
    {
        var jobId = $"cache-{cached.EntryId}-{Guid.NewGuid():N}"[..24];
        foreach (var frame in cached.Frames)
            _frameStore.Store(jobId, "std", frame.Step, frame.Pdb);

        var best = PickBest(cached.Frames);

        await SendEventAsync("done", new
        {
            type        = "done",
            jobId,
            atoms       = CountPdbAtoms(best.Pdb),
            stdEnergy   = best.Etotal,
            altEnergy   = (double?)null,
            hasAlt      = false,
            winner      = "standard",
            stdFrames   = cached.Frames.Select(f => new { step = f.Step, energy = f.Etotal }).ToList(),
            altFrames   = Array.Empty<object>(),
            stdBestStep = best.Step,
            altBestStep = (int?)null,
        }, ct);
    }

    private static IterationFrame PickBest(IReadOnlyList<IterationFrame> frames) =>
        frames.Where(f => f.Etotal.HasValue).MinBy(f => f.Etotal!.Value) ?? frames[0];

    // ── SSE helper ─────────────────────────────────────────────────────────────

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private async Task SendEventAsync(string eventType, object data, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var line  = $"data: {JsonSerializer.Serialize(data, JsonOpts)}\n\n";
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

    // ── Utility ──────────────────────────────────────────────────────────────

    private static int CountPdbAtoms(byte[] pdbContent)
    {
        var text = Encoding.UTF8.GetString(pdbContent);
        return text.Split('\n').Count(l =>
            l.StartsWith("ATOM",   StringComparison.Ordinal) ||
            l.StartsWith("HETATM", StringComparison.Ordinal));
    }

    /// <summary>
    /// EF Core wraps DB failures in DbUpdateException, whose own .Message is a generic
    /// "See the inner exception for details." — surface the real (innermost) driver error
    /// too, so a failure is diagnosable from the UI/SSE response alone.
    /// </summary>
    private static string DescribeError(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} — {ex.GetBaseException().Message}";

    private void CleanupJobDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not delete {Dir}: {Msg}", dir, ex.Message);
        }
    }
}
