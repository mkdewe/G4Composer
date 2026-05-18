namespace G4Composer.Server.Models;

public sealed record PipelineRequest(string Sequence);

public sealed record ToolStructureResult(
    string ToolName,
    bool Success,
    string? Structure,
    double? Energy,
    string? Error
);
