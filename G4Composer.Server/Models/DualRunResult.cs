namespace G4Composer.Server.Models;

/// <summary>
/// Result of a single engine run — contains the PDB bytes and the final Etotal
/// energy parsed from Xplor-NIH output. Energy is null if parsing failed.
/// </summary>
public sealed record SingleRunResult(byte[]? Pdb, double? Etotal, bool Success);

/// <summary>
/// Result of a dual run: standard quadro14L.exe + alternative alternatywa14L.exe,
/// both executed in parallel on the same input.
/// </summary>
public sealed record DualRunResult(SingleRunResult Standard, SingleRunResult Alternative)
{
    /// <summary>
    /// Returns "standard" | "alternative" | null (if neither has energy or both failed).
    /// Lower Etotal wins (CYANA/Xplor energy — lower = better minimization).
    /// </summary>
    public string? Winner =>
        Standard.Etotal is null && Alternative.Etotal is null ? null
        : Alternative.Etotal is null ? "standard"
        : Standard.Etotal is null    ? "alternative"
        : Standard.Etotal <= Alternative.Etotal ? "standard" : "alternative";
}
