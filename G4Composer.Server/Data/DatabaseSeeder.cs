using G4Composer.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace G4Composer.Server.Data;

/// <summary>
/// Populates the database with Silva classification data and known .inp examples
/// if the tables are empty. Safe to call on every startup — skips if data exists.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Skip if already seeded
        if (await db.SilvaGroups.AnyAsync()) return;

        var groups = BuildGroups();
        db.SilvaGroups.AddRange(groups);
        await db.SaveChangesAsync();
    }

    private static List<SilvaGroup> BuildGroups()
    {
        // Helper to make subtypes less verbose
        static SilvaSubtype Sub(string code, string loop, string silva, string onz,
            string? note = null, List<StructureExample>? examples = null)
            => new()
            {
                Code = code, Loop = loop, Silva = silva, Onz = onz,
                Note = note, Examples = examples ?? [],
            };

        static StructureExample Ex(string pdbId, string note, int tetrads,
            string inpName, string sequence, string structure, string chi,
            string orient, string rise, string twist, string path,
            bool isTest = false, int rmLevel = 0, int iterations = 70,
            bool isTheoretical = false)
            => new()
            {
                PdbId = pdbId, Note = note, Tetrads = tetrads,
                InpName = inpName, Sequence = sequence, Structure = structure,
                Chi = chi, Orient = orient, Rise = rise, Twist = twist,
                Path = path, IsTest = isTest, RmLevel = rmLevel,
                Iterations = iterations, IsTheoretical = isTheoretical,
            };

        return
        [
            // ── Group II — UDUD ──────────────────────────────────────────
            new SilvaGroup { Code = "UDUD", GroupNumber = "II", Name = "antiparallel: chair", Groove = "wnwn",
                Subtypes =
                [
                    Sub("6a", "-Lw-Ln-Lw", "-(lll)", "O", examples:
                    [
                        Ex("1hap", "Oxytricha telomeric G4 (Horvath 1995)", 2,
                            "1hap_js12B_100", "ggttggtgtggttgg", "AB..BA...AB..BA", "S...S....S...S.",
                            "A+;B-", "3.4", "19", "A1;B1;B4;A4;A3;B3;B2;A2", true, 5, 100),
                        Ex("1hut", "Tet repeat d(T4G4)", 2,
                            "1hut_js12B_100", "ggttggtgtggttgg", "AB..BA...AB..BA", "S...S....S...S.",
                            "A+;B-", "3.4", "19", "A1;B1;B4;A4;A3;B3;B2;A2", true, 5, 100),
                        Ex("6jkn", "3-tetrad chair, 1.4 Å", 3,
                            "6jkn_js12B_50", "gggttagggttagggttaggg", "ABC...CBA...ABC...CBA", "S.....SS....S.....SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;C4;B4;A4;A3;B3;C3;C2;B2;A2", true, 5, 50),
                        Ex("7otb_a", "3T chair, mixed syn/anti central quartet", 3,
                            "7otb_a_js12B_60", "gggttagggttagggtttggg", "ABC...CBA...ABC...CBA", "SS....S.....SS....S..",
                            "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C4;B4;A4;A3;B3;C3;C2;B2;A2", true, 0, 100),
                        Ex("5oph", "4-tetrad chair, GGGGCC repeats", 4,
                            "5oph_js12B_100", "ggggccggggccggggccgggg", "ABCD..DCBA..ABCD..DCBA", "S.S...S.S...S.S...S.S.",
                            "A+;B-;C+;D-", "3.4", "19;37;19", "A1;B1;C1;D1;D4;C4;B4;A4;A3;B3;C3;D3;D2;C2;B2;A2", true, 5, 100),
                    ]),
                    Sub("6b", "+Ln+Lw+Ln", "+(lll)", "O", examples:
                    [
                        Ex("1hao", "Oxytricha telomeric G4", 2,
                            "1hao_js12B_50", "ggttggtgtggttgg", "AB..BA...AB..BA", "S...S....S...S.",
                            "A+;B-", "3.4", "19", "A1;B1;B2;A2;A3;B3;B4;A4", true, 5, 50),
                        Ex("1qdh", "Human telomere antiparallel", 2,
                            "1qdh_js12B_50", "ggttggtgtggttgg", "AB..BA...AB..BA", "S...S....S...S.",
                            "A+;B-", "3.4", "19", "A1;B1;B2;A2;A3;B3;B4;A4", true, 5, 50),
                        Ex("5yey", "3T, human telomeric variant in K⁺", 3,
                            "5yey_js12B_100", "gggttagggttagggtttggg", "ABC...CBA...ABC...CBA", "S.....SS....S.....SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;C2;B2;A2;A3;B3;C3;C4;B4;A4", true, 5, 100),
                        Ex("9uk8", "4T, GGGGCC repeats, 2.7 Å", 4,
                            "9uk8_js12B_100", "ggggccggggccggggccggggc", "ABCD..DCBA..ABCD..DCBA.", "S.S...S.S...S.S...S.S..",
                            "A+;B-;C+;D-", "3.4", "19;37;19", "A1;B1;C1;D1;D2;C2;B2;A2;A3;B3;C3;D3;D4;C4;B4;A4", true, 5, 100),
                    ]),
                ]
            },

            // ── Group I — UUDD ───────────────────────────────────────────
            new SilvaGroup { Code = "UUDD", GroupNumber = "I", Name = "antiparallel: basket2", Groove = "mwmn",
                Subtypes =
                [
                    Sub("3a", "-P-Lw-P", "-(plp)", "O", "Long-distance propeller loop", examples:
                    [
                        Ex("_3a", "Theoretical (long-distance propeller loop)", 3,
                            "3a", "gggttagggttagggtttttaggg", "^^^...^^^...^^^......^^^", "S.....S.....SS.......SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;A4;B4;C4;C3;B3;A3;C2;B2;A2", false, 5, 70, isTheoretical: true),
                    ]),
                    Sub("5a", "-PD+P", "-pd+p", "N", "RNA only (Spinach aptamers)", examples:
                    [
                        Ex("_5a", "Theoretical — no DNA experimental structure", 3,
                            "5a", "gggttagggttttagggttaggg", "^^^...^^^.....^^^...^^^", "S.....S.......SS....SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;A4;B4;C4;C2;B2;A2;C3;B3;A3", false, 5, 70, isTheoretical: true),
                    ]),
                    Sub("8b", "+Ln+P+Lw", "+(lpl)", "O", examples:
                    [
                        Ex("2mbj", "3T UUDD basket2 with overhangs, RMSD 5.6", 3,
                            "8b_2mbj", "ttagggttagggttagggttagggtta", "...^^^...^^^...^^^...^^^...", "...SS....S.....S.....SS....",
                            "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C2;B2;A2;C3;B3;A3;A4;B4;C4", false, 5, 70),
                        Ex("_8b", "Generic 3T UUDD basket2, no overhangs", 3,
                            "8b", "gggttagggttagggttaggg", "^^^...^^^...^^^...^^^", "SS....S.....S.....SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;C2;B2;A2;C3;B3;A3;A4;B4;C4", false, 5, 70, isTheoretical: true),
                    ]),
                    Sub("11b", "+LnD-Lw", "+ld-l", "N", examples:
                    [
                        Ex("2kow", "UUDD antiparallel, 2T (inosine modification)", 2,
                            "11b_2kow", "tagggtagggtagggtaggg", "..^^..(^^....^^)..^^", "..S....S.....S....S.",
                            "A+;B-", "3.4", "19", "A1;B1;B2;A2;A4;B4;B3;A3", false, 5, 70),
                        Ex("6f4z", "3T UUDD antiparallel, NMR, RMSD 3.5", 3,
                            "11b_6f4z", "gggatgggacacaggggacggg", "^^^..^^^.....^^^...^^^", "S....SS......S.....SS.",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;C2;B2;A2;A4;B4;C4;C3;B3;A3", false, 5, 70),
                    ]),
                    Sub("12a", "D-PD", "d-pd", "Z", "Long-distance propeller loop — experimental structures produce artifacts", examples:
                    [
                        Ex("_12a", "Theoretical (long-distance propeller loop)", 3,
                            "12a", "gggacacagggacacggggacacaggg", "^^^.....^^^....^^^......^^^", "S.......SS.....SS.......S..",
                            "A+;B-;C-", "3.4", "19;29", "A1;B1;C1;C3;B3;A3;C2;B2;A2;A4;B4;C4", true, 5, 70, isTheoretical: true),
                    ]),
                ]
            },

            // ── Group III — UDUU ─────────────────────────────────────────
            new SilvaGroup { Code = "UDUU", GroupNumber = "III", Name = "hybrid3", Groove = "wnmm",
                Subtypes =
                [
                    Sub("2b", "+P+P+Ln",  "+(ppl)", "O"),
                    Sub("7a", "-Lw-Ln-P", "-(llp)", "O"),
                    Sub("10b", "+PD-Ln",  "-pd-l",  "N"),
                    Sub("13a", "-LwD+P",  "-ldp",   "N"),
                ]
            },

            // ── Group IV — UUUD ──────────────────────────────────────────
            new SilvaGroup { Code = "UUUD", GroupNumber = "IV", Name = "hybrid2", Groove = "mmwn",
                Subtypes =
                [
                    Sub("2a",  "-P-P-Lw",  "-(ppl)", "O"),
                    Sub("7b",  "+Ln+Lw+P", "+l-l-p", "O"),
                    Sub("10a", "-PD+Lw",   "-pd+l",  "N"),
                    Sub("13b", "+LnD-P",   "+ld-p",  "N"),
                ]
            },

            // ── Group V — UUDU ───────────────────────────────────────────
            new SilvaGroup { Code = "UUDU", GroupNumber = "V", Name = "hybrid1", Groove = "mwnm",
                Subtypes =
                [
                    Sub("9a", "-P-Lw-Ln", "-(pll)", "O"),
                    Sub("9b", "+P+Ln+Lw", "+(pll)", "O"),
                ]
            },

            // ── Group VI — UDDU ──────────────────────────────────────────
            new SilvaGroup { Code = "UDDU", GroupNumber = "VI", Name = "antiparallel: basket", Groove = "wmnm",
                Subtypes =
                [
                    Sub("3b",  "+P+Ln+P",  "+(plp)", "O"),
                    Sub("5b",  "+PD-P",    "+pd-p",  "N"),
                    Sub("8a",  "-Lw-P-Ln", "-(lpl)", "O"),
                    Sub("11a", "-LwD+Ln",  "-ld+l",  "N"),
                    Sub("12b", "D+PD",     "dpd",    "Z"),
                ]
            },

            // ── Group VII — UDDD ─────────────────────────────────────────
            new SilvaGroup { Code = "UDDD", GroupNumber = "VII", Name = "hybrid4", Groove = "wmmn",
                Subtypes =
                [
                    Sub("4a", "-Lw-P-P", "-(lpp)", "O"),
                    Sub("4b", "+Ln+P+P", "+(lpp)", "O", examples:
                    [
                        Ex("6r9k", "3T UDDD hybrid4, full sequence", 3,
                            "4b_6r9k", "gcgtgggtcagggtgggtgggacgc", "((((^^^...^^^.^^^.^^^))))", "....SSS..................",
                            "A+;B+;C+", "3.4", "27;27", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4", false, 5, 70),
                        Ex("6r9l", "3T UDDD hybrid4, full sequence (variant orient)", 3,
                            "4b_6r9l", "gcgtgggtcagggtgggtgggacgc", "((((^^^...^^^.^^^.^^^))))", "....SS....S...S...S......",
                            "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4", false, 5, 70),
                        Ex("6r9k_g4", "3T UDDD hybrid4, G4 core only", 3,
                            "4b_6r9k_g4", "gggtcagggtgggtggg", "^^^...^^^.^^^.^^^", "SSS..............",
                            "A+;B+;C+", "3.4", "27;27", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4", false, 5, 70),
                        Ex("6r9l_g4", "3T UDDD hybrid4, G4 core only (variant orient)", 3,
                            "4b_6r9l_g4", "gggtcagggtgggtggg", "^^^...^^^.^^^.^^^", "SS....S...S...S..",
                            "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4", false, 5, 70),
                    ]),
                ]
            },

            // ── Group VIII — UUUU ────────────────────────────────────────
            new SilvaGroup { Code = "UUUU", GroupNumber = "VIII", Name = "parallel", Groove = "mmmm",
                Subtypes =
                [
                    Sub("1a", "-P-P-P", "-(ppp)", "O"),
                    Sub("1b", "+P+P+P", "+(ppp)", "O", "Left-handed only"),
                ]
            },
        ];
    }
}
