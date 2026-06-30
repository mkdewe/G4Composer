namespace G4Composer.Server.Domain;

/// <summary>
/// One canonical Webba da Silva G-quadruplex fold (loop-orientation subtype): its short code
/// (e.g. <c>6b</c>), the signed loop notation that fixes the threading (e.g. <c>+Ln+Lw+Ln</c>),
/// its strand-orientation group, whether it is parallel, and whether the fallback predictor
/// should offer it.
/// </summary>
public sealed record SilvaSubtypeInfo(
    string Code,
    string Notation,
    string GroupCode,
    string GroupName,
    bool IsParallel,
    bool Predictable);

/// <summary>
/// The curated, experimentally-grounded catalog of the 26 Webba da Silva unimolecular G4 fold
/// subtypes — the SAME set seeded into the database (see DatabaseSeeder). The fallback predictor
/// considers ONLY these folds, so it never invents non-canonical topologies and always offers
/// both sign variants of a family (e.g. 6a <c>-Lw-Ln-Lw</c> AND 6b <c>+Ln+Lw+Ln</c>) — earlier the
/// predictor enumerated the raw P/L/D cube and kept only the first threadable sign, silently
/// dropping every <c>+</c>-sign partner (6b, 1b, 3b, …).
///
/// <para><see cref="SilvaSubtypeInfo.Predictable"/> is false for folds that should not be auto-offered:
/// 1b (left-handed only), 5a (RNA-only, no DNA structure), and 12a/12b (long-distance diagonal
/// artifacts). They remain in the catalog for transparency (shown as ruled-out in the determination).</para>
/// </summary>
public static class SilvaCatalog
{
    public static readonly IReadOnlyList<SilvaSubtypeInfo> All =
    [
        // Group II — UDUD — antiparallel chair
        new("6a",  "-Lw-Ln-Lw", "UDUD", "antiparallel chair",         false, true),
        new("6b",  "+Ln+Lw+Ln", "UDUD", "antiparallel chair",         false, true),

        // Group I — UUDD — antiparallel basket2
        new("3a",  "-P-Lw-P",   "UUDD", "antiparallel basket2",       false, true),
        new("5a",  "-PD+P",     "UUDD", "antiparallel basket2",       false, false), // RNA-only
        new("8b",  "+Ln+P+Lw",  "UUDD", "antiparallel basket2",       false, true),
        new("11b", "+LnD-Lw",   "UUDD", "antiparallel basket2",       false, true),
        new("12a", "D-PD",      "UUDD", "antiparallel basket2",       false, false), // artifact

        // Group III — UDUU — hybrid3
        new("2b",  "+P+P+Ln",   "UDUU", "hybrid3",                    false, true),
        new("7a",  "-Lw-Ln-P",  "UDUU", "hybrid3",                    false, true),
        new("10b", "+PD-Ln",    "UDUU", "hybrid3",                    false, true),
        new("13a", "-LwD+P",    "UDUU", "hybrid3",                    false, true),

        // Group IV — UUUD — hybrid2
        new("2a",  "-P-P-Lw",   "UUUD", "hybrid2",                    false, true),
        new("7b",  "+Ln+Lw+P",  "UUUD", "hybrid2",                    false, true),
        new("10a", "-PD+Lw",    "UUUD", "hybrid2",                    false, true),
        new("13b", "+LnD-P",    "UUUD", "hybrid2",                    false, true),

        // Group V — UUDU — hybrid1
        new("9a",  "-P-Lw-Ln",  "UUDU", "hybrid1",                    false, true),
        new("9b",  "+P+Ln+Lw",  "UUDU", "hybrid1",                    false, true),

        // Group VI — UDDU — antiparallel basket
        new("3b",  "+P+Ln+P",   "UDDU", "antiparallel basket",        false, true),
        new("5b",  "+PD-P",     "UDDU", "antiparallel basket",        false, true),
        new("8a",  "-Lw-P-Ln",  "UDDU", "antiparallel basket",        false, true),
        new("11a", "-LwD+Ln",   "UDDU", "antiparallel basket",        false, true),
        new("12b", "D+PD",      "UDDU", "antiparallel basket",        false, false), // artifact

        // Group VII — UDDD — hybrid4
        new("4a",  "-Lw-P-P",   "UDDD", "hybrid4",                    false, true),
        new("4b",  "+Ln+P+P",   "UDDD", "hybrid4",                    false, true),

        // Group VIII — UUUU — parallel
        new("1a",  "-P-P-P",    "UUUU", "parallel",                   true,  true),
        new("1b",  "+P+P+P",    "UUUU", "parallel",                   true,  false), // left-handed only
    ];
}
