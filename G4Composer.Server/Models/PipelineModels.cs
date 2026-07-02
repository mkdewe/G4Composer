namespace G4Composer.Server.Models;

public sealed record PipelineRequest(string Sequence, bool UseGquadruplex = false);

public sealed record ToolStructureResult(
    string ToolName,
    bool Success,
    string? Structure,
    double? Energy,
    string? Error
);

public sealed record GqrsMotif(
    int Id,
    int Tetrad1,
    int Tetrad2,
    int Tetrad3,
    int Tetrad4,
    int Tetrads,
    int GScore,
    string MotifSequence
);

public sealed record GqrsResult(
    bool Success,
    IReadOnlyList<GqrsMotif> Motifs,
    string? Error
);

public sealed record OnquadroMatch(
    string Files,
    int TetradCount,
    string Molecule,
    double TractDistance,
    double LinkerDistance,
    string Qrs,
    string MatchedSequence,
    string Viability = "",
    string LoopLengths = "",
    string Topology = ""
);

/// <summary>
/// A ready-made g4composer input produced by the aligner's <c>--g4composer-output-dir</c>.
/// Carries the matched template's real geometry (orient/rise/twist/path) so the pipeline can
/// model it directly instead of reconstructing a topology from the QRS string.
/// </summary>
public sealed record OnquadroInpCandidate(
    int Rank,
    string Template,
    string Viability,
    string Topology,
    string LoopLengths,
    double TractDistance,
    double LinkerDistance,
    QuadroInput Input
);

public sealed record OnquadroResult(
    bool Success,
    IReadOnlyList<OnquadroMatch> Matches,
    string? Error,
    IReadOnlyList<OnquadroInpCandidate>? InpCandidates = null
);
