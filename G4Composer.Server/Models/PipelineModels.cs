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
    double LinkerScore,
    string Qrs,
    string MatchedSequence
);

public sealed record OnquadroResult(
    bool Success,
    IReadOnlyList<OnquadroMatch> Matches,
    string? Error
);
