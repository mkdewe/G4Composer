using System.Text;
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

    private readonly ILogger<QuadroController> _logger;
    private readonly IQuadroEngineSelector _engineSelector;
    private readonly IDockerHealthService _healthService;
    private readonly IQuadroJobRunner _jobRunner;
    private readonly IValidator<QuadroInput> _validator;
    private readonly QuadroOptions _options;
    private readonly IJobLogStore _logStore;

    public QuadroController(
        ILogger<QuadroController> logger,
        IQuadroEngineSelector engineSelector,
        IDockerHealthService healthService,
        IQuadroJobRunner jobRunner,
        IValidator<QuadroInput> validator,
        IOptions<QuadroOptions> options,
        IJobLogStore logStore)
    {
        _logger = logger;
        _engineSelector = engineSelector;
        _healthService = healthService;
        _jobRunner = jobRunner;
        _validator = validator;
        _options = options.Value;
        _logStore = logStore;
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

            var pdbContent = await _jobRunner.RunAsync(jobId, jobDir, items, jobCts.Token);

            if (pdbContent is null || pdbContent.Length == 0)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorDto("Container did not produce a PDB file."));

            _logger.LogInformation("Job {JobId}: success, {Bytes} bytes", jobId, pdbContent.Length);

            Response.Headers["X-Job-Id"]                       = jobId;
            Response.Headers["X-Atom-Count"]                   = CountPdbAtoms(pdbContent).ToString();
            Response.Headers["Access-Control-Expose-Headers"]  = "X-Job-Id, X-Atom-Count";

            return File(pdbContent, "chemical/x-pdb", $"g4_{jobId}.pdb");
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
                new ErrorDto($"Internal error: {ex.Message}"));
        }
        finally
        {
            // Sprzątanie w tle — nie blokuje odpowiedzi.
            _ = Task.Run(() => CleanupJobDir(jobDir));
        }
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

    // ── Utility ──────────────────────────────────────────────────────────────

    private static int CountPdbAtoms(byte[] pdbContent)
    {
        var text = Encoding.UTF8.GetString(pdbContent);
        return text.Split('\n').Count(l =>
            l.StartsWith("ATOM",   StringComparison.Ordinal) ||
            l.StartsWith("HETATM", StringComparison.Ordinal));
    }

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
