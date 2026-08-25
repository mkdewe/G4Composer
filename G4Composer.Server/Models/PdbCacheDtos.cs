namespace G4Composer.Server.Models;

/// <summary>
/// One stored structure — metadata only; the PDB is fetched separately by (variant, step).
/// <paramref name="Variant"/> is "std" (quadro14*.exe) or "alt" (alternatywa14*.exe).
/// </summary>
public sealed record PdbCacheFrameDto(int Step, double? Etotal, string Variant);

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
