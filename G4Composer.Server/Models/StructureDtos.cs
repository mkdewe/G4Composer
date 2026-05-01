namespace G4Composer.Server.Models;

/// <summary>Silva group with its subtypes — used in the classification picker.</summary>
public sealed record SilvaGroupDto(
    string Code,
    string GroupNumber,
    string Name,
    string Groove,
    IReadOnlyList<SilvaSubtypeDto> Subtypes
);

public sealed record SilvaSubtypeDto(
    string Code,
    string Loop,
    string Silva,
    string Onz,
    string? Note,
    IReadOnlyList<StructureExampleSummaryDto> Examples
);

/// <summary>Lightweight summary shown in the examples list (no .inp fields).</summary>
public sealed record StructureExampleSummaryDto(
    string PdbId,
    string Note,
    int Tetrads,
    bool IsTheoretical
);

/// <summary>Full .inp data returned when user clicks "Load" on an example.</summary>
public sealed record StructureExampleDetailDto(
    string PdbId,
    string Note,
    int Tetrads,
    bool IsTheoretical,
    string InpName,
    string Sequence,
    string Structure,
    string Chi,
    string Orient,
    float Rise,
    string Twist,
    string Path,
    bool IsTest,
    int RmLevel,
    int Iterations
);

/// <summary>Result of deriving path/orient from Silva loop notation.</summary>
public sealed record DerivedTopologyDto(string Path, string Orient);
