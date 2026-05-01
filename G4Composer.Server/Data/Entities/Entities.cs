namespace G4Composer.Server.Data.Entities;

/// <summary>
/// Silva topological group (e.g. UDUD, UUDD).
/// One group contains multiple subtypes (loop arrangements).
/// </summary>
public sealed class SilvaGroup
{
    public int Id { get; set; }

    /// <summary>4-letter strand polarity code, e.g. "UDUD".</summary>
    public required string Code { get; set; }

    /// <summary>Roman numeral group index, e.g. "II".</summary>
    public required string GroupNumber { get; set; }

    /// <summary>Human-readable topology name, e.g. "antiparallel: chair".</summary>
    public required string Name { get; set; }

    /// <summary>Groove widths, e.g. "wnwn".</summary>
    public required string Groove { get; set; }

    public ICollection<SilvaSubtype> Subtypes { get; set; } = [];
}

/// <summary>
/// Silva loop subtype within a group (e.g. "6a", "11b").
/// One subtype contains multiple known PDB examples.
/// </summary>
public sealed class SilvaSubtype
{
    public int Id { get; set; }

    /// <summary>Subtype code, e.g. "6a".</summary>
    public required string Code { get; set; }

    /// <summary>Loop arrangement in Webba da Silva notation, e.g. "-Lw-Ln-Lw".</summary>
    public required string Loop { get; set; }

    /// <summary>Silva notation shorthand, e.g. "-(lll)".</summary>
    public required string Silva { get; set; }

    /// <summary>ONZ classification letter: O, N, or Z.</summary>
    public required string Onz { get; set; }

    /// <summary>Optional note, e.g. "RNA only".</summary>
    public string? Note { get; set; }

    public int SilvaGroupId { get; set; }
    public SilvaGroup Group { get; set; } = null!;

    public ICollection<StructureExample> Examples { get; set; } = [];
}

/// <summary>
/// A known G-quadruplex structure with its complete quadro14L .inp parameters.
/// Each example belongs to one Silva subtype.
/// </summary>
public sealed class StructureExample
{
    public int Id { get; set; }

    /// <summary>PDB ID or synthetic key for theoretical structures (prefixed with "_").</summary>
    public required string PdbId { get; set; }

    /// <summary>Short description shown in the UI, e.g. "3-tetrad chair, 1.4 Å".</summary>
    public required string Note { get; set; }

    /// <summary>Number of G-tetrad planes.</summary>
    public int Tetrads { get; set; }

    /// <summary>True for structures with no deposited experimental coordinates.</summary>
    public bool IsTheoretical { get; set; }

    // ── quadro14L .inp fields ─────────────────────────────────────────────

    /// <summary>Name field in .inp (e.g. "1hap_js12B_100").</summary>
    public required string InpName { get; set; }

    /// <summary>Nucleotide sequence, lowercase.</summary>
    public required string Sequence { get; set; }

    /// <summary>Dot-bracket / strand-label structure string.</summary>
    public required string Structure { get; set; }

    /// <summary>Chi pattern (S/. per position). Empty = backend auto-generates.</summary>
    public string Chi { get; set; } = string.Empty;

    /// <summary>Strand orientation, e.g. "A+;B-".</summary>
    public required string Orient { get; set; }

    /// <summary>Helical rise in Å.</summary>
    public float Rise { get; set; } = 3.4f;

    /// <summary>
    /// Helical twist in degrees. Can be multi-step, e.g. "19;29".
    /// Stored as string to support multi-step values.
    /// </summary>
    public required string Twist { get; set; }

    /// <summary>Tetrad path, e.g. "A1;B1;B4;A4;A3;B3;B2;A2".</summary>
    public required string Path { get; set; }

    public bool IsTest { get; set; } = false;
    public int RmLevel { get; set; } = 0;
    public int Iterations { get; set; } = 70;

    public int SilvaSubtypeId { get; set; }
    public SilvaSubtype Subtype { get; set; } = null!;
}
