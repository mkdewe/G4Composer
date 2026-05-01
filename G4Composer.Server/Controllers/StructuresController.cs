using Microsoft.AspNetCore.Mvc;
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

    // ── GET /api/structures/groups ────────────────────────────────────────
    /// <summary>
    /// Returns all Silva groups with their subtypes and example summaries.
    /// Used to populate the classification picker in the UI.
    /// </summary>
    [HttpGet("groups")]
    [SwaggerOperation(Summary = "All Silva groups with subtypes", Tags = [SwaggerTag])]
    [ProducesResponseType(typeof(IReadOnlyList<SilvaGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SilvaGroupDto>>> GetGroups(CancellationToken ct)
    {
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
                s.Examples.OrderBy(e => e.PdbId).Select(e => new StructureExampleSummaryDto(
                    e.PdbId, e.Note, e.Tetrads, e.IsTheoretical
                )).ToList()
            )).ToList()
        )).ToList();

        return Ok(result);
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
        var e = await _db.StructureExamples
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PdbId == pdbId, ct);

        if (e is null)
            return NotFound(new ErrorDto($"Example '{pdbId}' not found."));

        return Ok(new StructureExampleDetailDto(
            e.PdbId, e.Note, e.Tetrads, e.IsTheoretical,
            e.InpName, e.Sequence, e.Structure, e.Chi,
            e.Orient, e.Rise, e.Twist, e.Path,
            e.IsTest, e.RmLevel, e.Iterations
        ));
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
        string code, CancellationToken ct)
    {
        var subtype = await _db.SilvaSubtypes
            .AsNoTracking()
            .Include(s => s.Examples)
            .FirstOrDefaultAsync(s => s.Code == code, ct);

        if (subtype is null)
            return NotFound(new ErrorDto($"Subtype '{code}' not found."));

        var examples = subtype.Examples
            .OrderBy(e => e.PdbId)
            .Select(e => new StructureExampleSummaryDto(e.PdbId, e.Note, e.Tetrads, e.IsTheoretical))
            .ToList();

        return Ok(examples);
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
