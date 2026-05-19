namespace G4Composer.Server.Models;

/// <summary>
/// One minimization checkpoint: the complete Xplor-refined structure at a given
/// cumulative CYANA iteration count.
/// </summary>
public sealed record IterationFrame(int Step, byte[] Pdb, double? Etotal);

/// <summary>
/// Result of a single engine run. Frames contains one entry per iteration_steps
/// checkpoint. Pdb and Etotal are derived from the best-energy frame.
/// </summary>
public sealed record SingleRunResult(IReadOnlyList<IterationFrame> Frames, bool Success)
{
    public static readonly SingleRunResult Empty =
        new([], false);

    /// <summary>Frame with the lowest (best) Etotal, or first frame if no energy available.</summary>
    public IterationFrame? BestFrame =>
        Frames.Count == 0 ? null
        : Frames.Where(f => f.Etotal.HasValue).MinBy(f => f.Etotal!.Value)
          ?? Frames[0];

    public byte[]? Pdb    => BestFrame?.Pdb;
    public double? Etotal => BestFrame?.Etotal;
}

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
