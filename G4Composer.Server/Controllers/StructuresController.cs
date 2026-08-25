using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using G4Composer.Server.Data;
using G4Composer.Server.Domain;
using G4Composer.Server.Models;

namespace G4Composer.Server.Controllers;

/// <summary>
/// Read-only API for the Silva classification and known .inp examples.
/// All data is seeded from <see cref="DatabaseSeeder"/> on startup.
/// </summary>
[ApiController]
[Route("api/structures")]
[Produces("application/json")]
public sealed class StructuresController : ControllerBase
{
    private const string SwaggerTag = "Structures";

    private readonly AppDbContext _db;

    public StructuresController(AppDbContext db) => _db = db;

    /// <summary>
    /// Czy naprawdę filtrować po <c>IsCurated</c>. Zabezpieczenie przed pustą listą: na bazie,
    /// gdzie <c>tools/curate-examples.sh</c> jeszcze nie chodził, żaden przykład nie jest
    /// wybrany, więc filtr wyciąłby wszystko i UI pokazałoby pustkę bez wyjaśnienia.
    /// W takim wypadku filtr jest ignorowany.
    /// </summary>
    private async Task<bool> ShouldFilterCuratedAsync(bool requested, CancellationToken ct)
        => requested && await _db.StructureExamples.AnyAsync(e => e.IsCurated, ct);

    // ── GET /api/structures/groups ────────────────────────────────────────
    /// <summary>
    /// Returns all Silva groups with their subtypes and example summaries.
    /// Used to populate the classification picker in the UI.
    /// </summary>
    [HttpGet("groups")]
    [SwaggerOperation(Summary = "All Silva groups with subtypes", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(IReadOnlyList<SilvaGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SilvaGroupDto>>> GetGroups(
        CancellationToken ct, [FromQuery] bool curatedOnly = false)
    {
        try
        {
            var filterCurated = await ShouldFilterCuratedAsync(curatedOnly, ct);

            var groups = await _db.SilvaGroups
                .AsNoTracking()
                .OrderBy(g => g.GroupNumber)
                .Include(g => g.Subtypes)
                    .ThenInclude(s => s.Examples)
                .ToListAsync(ct);

            var result = groups.Select(g => new SilvaGroupDto(
                g.Code,
                g.GroupNumber,
                g.Name,
                g.Groove,
                g.Subtypes.OrderBy(s => s.Code).Select(s => new SilvaSubtypeDto(
                    s.Code,
                    s.Loop,
                    s.Silva,
                    s.Onz,
                    s.Note,
                    s.Examples
                        .Where(e => !filterCurated || e.IsCurated)
                        .OrderBy(e => e.PdbId)
                        .Select(e => new StructureExampleSummaryDto(
                            e.PdbId, e.Note, e.Tetrads, e.IsTheoretical, e.IsCurated
                        )).ToList()
                )).ToList()
            )).ToList();

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return NoContent(); // Client cancelled the request — not an error
        }
    }

    // ── GET /api/structures/examples/{pdbId} ─────────────────────────────
    /// <summary>
    /// Returns full .inp parameters for a single example (triggered by "Load" button).
    /// </summary>
    [HttpGet("examples/{pdbId}")]
    [SwaggerOperation(Summary = "Full .inp data for a single example", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(StructureExampleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StructureExampleDetailDto>> GetExample(
        string pdbId, CancellationToken ct)
    {
        try
        {
            var e = await _db.StructureExamples
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PdbId == pdbId, ct);

            if (e is null)
                return NotFound(new ErrorDto($"Example '{pdbId}' not found."));

            return Ok(new StructureExampleDetailDto(
                e.PdbId, e.Note, e.Tetrads, e.IsTheoretical,
                e.InpName, e.Sequence, e.Structure, e.Chi,
                e.Orient, e.Rise, e.Twist, e.Path,
                e.IsTest, e.RmLevel, e.Iterations, e.IsCurated
            ));
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }
    }

    // ── GET /api/structures/subtypes/{code}/examples ──────────────────────
    /// <summary>
    /// Returns all example summaries for a given subtype code (e.g. "6a").
    /// </summary>
    [HttpGet("subtypes/{code}/examples")]
    [SwaggerOperation(Summary = "Example summaries for a subtype", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(IReadOnlyList<StructureExampleSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StructureExampleSummaryDto>>> GetSubtypeExamples(
        string code, CancellationToken ct, [FromQuery] bool curatedOnly = false)
    {
        try
        {
            var filterCurated = await ShouldFilterCuratedAsync(curatedOnly, ct);

            var subtype = await _db.SilvaSubtypes
                .AsNoTracking()
                .Include(s => s.Examples)
                .FirstOrDefaultAsync(s => s.Code == code, ct);

            if (subtype is null)
                return NotFound(new ErrorDto($"Subtype '{code}' not found."));

            var examples = subtype.Examples
                .Where(e => !filterCurated || e.IsCurated)
                .OrderBy(e => e.PdbId)
                .Select(e => new StructureExampleSummaryDto(
                    e.PdbId, e.Note, e.Tetrads, e.IsTheoretical, e.IsCurated))
                .ToList();

            return Ok(examples);
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }
    }

    // ── GET /api/structures/noncanonical ─────────────────────────────────
    /// <summary>
    /// Returns all structure examples that are not assigned to any Silva subtype
    /// (non-canonical structures that don't fit the standard topology classification).
    /// </summary>
    [HttpGet("noncanonical")]
    [SwaggerOperation(Summary = "Non-canonical structure examples", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(IReadOnlyList<StructureExampleDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StructureExampleDetailDto>>> GetNonCanonical(
        CancellationToken ct, [FromQuery] bool curatedOnly = false)
    {
        try
        {
            // curatedOnly nic tu nie wycina przy obecnej regule (kurator oznacza wszystkie
            // non-canonical), ale parametr jest przyjmowany, żeby klient mógł go podawać
            // jednolicie i żeby zmiana reguły nie wymagała zmiany kontraktu.
            var filterCurated = await ShouldFilterCuratedAsync(curatedOnly, ct);

            var examples = await _db.StructureExamples
                .AsNoTracking()
                .Where(e => e.SilvaSubtypeId == null)
                .Where(e => !filterCurated || e.IsCurated)
                .OrderBy(e => e.PdbId)
                .ToListAsync(ct);

            var result = examples.Select(e => new StructureExampleDetailDto(
                e.PdbId, e.Note, e.Tetrads, e.IsTheoretical,
                e.InpName, e.Sequence, e.Structure, e.Chi,
                e.Orient, e.Rise, e.Twist, e.Path,
                e.IsTest, e.RmLevel, e.Iterations, e.IsCurated
            )).ToList();

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }
    }

    // ── GET /api/structures/derive ────────────────────────────────────────
    /// <summary>
    /// Derives the path string (and a default orient) from a Silva loop notation
    /// and the number of tetrad planes. The frontend uses a JS mirror of the
    /// same algorithm; this endpoint is provided for backend validation,
    /// scripting and testing.
    /// </summary>
    [HttpGet("derive")]
    [SwaggerOperation(Summary = "Derive path/orient from Silva loop notation", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(DerivedTopologyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorDto), StatusCodes.Status400BadRequest)]
    public ActionResult<DerivedTopologyDto> Derive(
        [FromQuery] string loop,
        [FromQuery] int tetrads)
    {
        try
        {
            var path = SilvaTopology.BuildPath(loop, tetrads);
            var orient = SilvaTopology.BuildDefaultOrient(tetrads);
            return Ok(new DerivedTopologyDto(path, orient));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorDto(ex.Message));
        }
    }
}
