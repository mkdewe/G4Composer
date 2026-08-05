namespace G4Composer.Server.Models;

/// <summary>One stored iteration checkpoint — metadata only, PDB fetched separately by step.</summary>
public sealed record PdbCacheFrameDto(int Step, double? Etotal);

/// <summary>Metadata for a "Retrieve" lookup — the PDB itself is fetched per-frame.</summary>
public sealed record PdbCacheEntryDto(
    int Id,
    string? PdbId,
    bool IsExample,
    string EngineVersion,
    DateTime CreatedAtUtc,
    DateTime LastAccessedAtUtc,
    IReadOnlyList<PdbCacheFrameDto> Frames
);
