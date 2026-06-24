namespace G4Composer.Server.Models;

/// <summary>
/// One coarse progress milestone reported while a Quadro job runs.
/// <see cref="Index"/>/<see cref="Total"/> drive a determinate progress bar (coarse —
/// one tick per pipeline phase, not per minimization iteration); <see cref="Stage"/> is a
/// stable machine key and <see cref="Label"/> is the human-readable status text.
/// <see cref="Percent"/> (0–100), when set, drives the bar directly — preferred over
/// Index/Total because phases are not equal in duration.
/// </summary>
public sealed record JobProgress(
    string Stage, string Label, int Index, int Total, string? Detail = null, double? Percent = null);
