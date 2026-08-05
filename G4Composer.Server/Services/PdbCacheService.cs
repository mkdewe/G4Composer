using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Data;
using G4Composer.Server.Data.Entities;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

/// <summary>A previously computed result served from the database instead of Docker.</summary>
public sealed record CachedRun(
    int EntryId,
    string? PdbId,
    bool IsExample,
    IReadOnlyList<IterationFrame> Frames);

public interface IPdbCacheService
{
    /// <summary>
    /// Canonical dedup key for an input: SHA-256 over the engine-resolved .inp fields
    /// (everything <see cref="QuadroEngineBase.SerializeInput"/> would write except
    /// <c>name</c>, <c>rm_level</c> and <c>iteration_steps</c> — name is irrelevant per spec,
    /// rm_level provably doesn't affect geometry/energy, and iteration_steps only changes
    /// which checkpoints get computed for the same physical model) plus the engine version.
    /// Two inputs that resolve to the same physical model hash identically.
    /// </summary>
    string ComputeHash(QuadroInput input, IQuadroEngine engine);

    /// <summary>
    /// Looks up a cache entry by hash. Only returns a hit if the stored frames cover every
    /// step in <paramref name="requestedSteps"/> — a partial match is treated as a miss so a
    /// cached response is never thinner than a fresh run would have been.
    /// </summary>
    Task<CachedRun?> TryGetAsync(string hash, IReadOnlyList<int> requestedSteps, CancellationToken ct);

    /// <summary>
    /// Persists a fresh run's standard-engine frames. If the input matches a curated
    /// StructureExample (by Sequence + Structure + Path — the same fields the hash is built
    /// from), all frames are stored and the entry is linked to that example. Otherwise only
    /// the best (lowest-energy) frame is kept, and the ad-hoc cache is trimmed to
    /// <see cref="QuadroOptions.PdbCacheMaxAdHocEntries"/> by evicting the least-recently-used.
    /// </summary>
    Task SaveAsync(QuadroInput input, IQuadroEngine engine, string hash, SingleRunResult result, CancellationToken ct);

    Task<PdbCacheEntry?> GetByIdAsync(int id, CancellationToken ct);

    Task<PdbCacheEntry?> GetByPdbIdAsync(string pdbId, CancellationToken ct);
}

public sealed class PdbCacheService : IPdbCacheService
{
    // Bump this whenever the quadro14L/14G engine script (docker-biotools submodule) changes
    // behavior WITHOUT the engine's own Version string changing — e.g. the 2026-08-04
    // shugar/sugar + missing-read-ang fix. Otherwise stale pre-fix PDBs would be served
    // forever after a silent engine content change, since IQuadroEngine.Version alone
    // wouldn't invalidate them.
    private const string EngineContentVersion = "2026-08-04-sugar-fix";

    private static readonly string[] HashIgnoredFieldNames = ["name", "rm_level", "iteration_steps"];

    private readonly AppDbContext _db;
    private readonly QuadroOptions _options;

    public PdbCacheService(AppDbContext db, IOptions<QuadroOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public string ComputeHash(QuadroInput input, IQuadroEngine engine)
    {
        var serialized = engine.SerializeInput(input);
        var relevantLines = serialized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !HashIgnoredFieldNames.Contains(FieldName(line)))
            .Select(line => line.TrimEnd('\r').Trim());

        var header = new List<string> { engine.Version, EngineContentVersion };
        header.AddRange(relevantLines);

        var canonical = string.Join('\n', header);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes);
    }

    public async Task<CachedRun?> TryGetAsync(string hash, IReadOnlyList<int> requestedSteps, CancellationToken ct)
    {
        var entry = await _db.PdbCacheEntries
            .Include(e => e.Frames)
            .FirstOrDefaultAsync(e => e.Hash == hash, ct);

        if (entry is null) return null;

        var storedSteps = entry.Frames.Select(f => f.Step).ToHashSet();
        if (!requestedSteps.All(storedSteps.Contains))
            return null; // partial match — let the caller run Docker for the full set

        entry.LastAccessedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var frames = entry.Frames
            .OrderBy(f => f.Step)
            .Select(f => new IterationFrame(f.Step, Encoding.UTF8.GetBytes(f.Pdb), f.Etotal))
            .ToList();

        return new CachedRun(entry.Id, entry.PdbId, entry.IsExample, frames);
    }

    public async Task SaveAsync(QuadroInput input, IQuadroEngine engine, string hash, SingleRunResult result, CancellationToken ct)
    {
        if (!result.Success || result.Frames.Count == 0) return;

        // Someone else may have raced us to save the same hash — nothing to do.
        if (await _db.PdbCacheEntries.AnyAsync(e => e.Hash == hash, ct)) return;

        var serialized = engine.SerializeInput(input);
        var sequence  = ExtractField(serialized, "sequence");
        var structure = ExtractField(serialized, "structure");
        var path      = ExtractField(serialized, "path");

        var match = await _db.StructureExamples
            .FirstOrDefaultAsync(x =>
                x.Sequence == sequence && x.Structure == structure && x.Path == path, ct);

        var now = DateTime.UtcNow;
        var entry = new PdbCacheEntry
        {
            Hash                = hash,
            StructureExampleId  = match?.Id,
            PdbId               = match?.PdbId,
            IsExample           = match is not null,
            EngineVersion       = engine.Version,
            CreatedAtUtc        = now,
            LastAccessedAtUtc   = now,
        };

        IReadOnlyList<IterationFrame> framesToStore = match is not null
            ? result.Frames                        // Examples: keep every displayed checkpoint
            : new[] { result.BestFrame! };          // Ad-hoc: keep only the best result

        foreach (var frame in framesToStore)
        {
            entry.Frames.Add(new PdbCacheFrame
            {
                Step   = frame.Step,
                Etotal = frame.Etotal,
                Pdb    = Encoding.UTF8.GetString(frame.Pdb),
            });
        }

        _db.PdbCacheEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        if (match is null)
            await EvictOldestAdHocEntriesAsync(ct);
    }

    public Task<PdbCacheEntry?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.PdbCacheEntries.Include(e => e.Frames).FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Case-insensitive, and tolerant of the leading underscore theoretical PdbIds use
    /// internally (e.g. "_2b") — the UI strips that underscore for display (SequenceForm's
    /// example buttons show "2B"), so a user typing what they see on screen must still find
    /// the entry.
    /// </summary>
    public async Task<PdbCacheEntry?> GetByPdbIdAsync(string pdbId, CancellationToken ct)
    {
        var normalized = pdbId.Trim().ToLowerInvariant();

        var entry = await _db.PdbCacheEntries.Include(e => e.Frames)
            .FirstOrDefaultAsync(e => e.PdbId != null && e.PdbId.ToLower() == normalized, ct);
        if (entry is not null || normalized.StartsWith('_')) return entry;

        var withPrefix = "_" + normalized;
        return await _db.PdbCacheEntries.Include(e => e.Frames)
            .FirstOrDefaultAsync(e => e.PdbId != null && e.PdbId.ToLower() == withPrefix, ct);
    }

    private async Task EvictOldestAdHocEntriesAsync(CancellationToken ct)
    {
        var adHocCount = await _db.PdbCacheEntries.CountAsync(e => !e.IsExample, ct);
        var overflow = adHocCount - _options.PdbCacheMaxAdHocEntries;
        if (overflow <= 0) return;

        var stale = await _db.PdbCacheEntries
            .Where(e => !e.IsExample)
            .OrderBy(e => e.LastAccessedAtUtc)
            .Take(overflow)
            .ToListAsync(ct);

        _db.PdbCacheEntries.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    private static string FieldName(string line)
    {
        var idx = 0;
        while (idx < line.Length && !char.IsWhiteSpace(line[idx])) idx++;
        return line[..idx];
    }

    private static string ExtractField(string serialized, string key)
    {
        foreach (var rawLine in serialized.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            var name = FieldName(line);
            if (name == key)
                return line[name.Length..].Trim();
        }
        return string.Empty;
    }
}
