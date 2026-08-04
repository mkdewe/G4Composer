using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FillMissingSilvaSubtypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Theoretical minimal representatives for the 9 Silva subtypes that had
            // zero examples of any kind. Data ported verbatim from out-quadro/missing-subtype-inp/
            // (loop-length grid search, quadro14L-verified — see that dir's README.md for methodology
            // and Etotal figures). All are 3-tetrad, telomeric-style (ggg / tta) minimal builds — not
            // models of a real sequence, just the smallest buildable instance of each topology.

            // ── UDDD (Group VII, hybrid4), subtype 4a ────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_4a','Theoretical (loop-length grid search)',
                  3,true,'4a',
                  'gggtttagggtttagggtttaggg','^^^....^^^....^^^....^^^',
                  '........................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;C4;B4;A4;C3;B3;A3;C2;B2;A2',
                  false,5,900,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='4a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDDU (Group VI, antiparallel:basket), subtype 3b ─────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_3b','Theoretical (loop-length grid search)',
                  3,true,'3b',
                  'gggtttagggttagggtttaggg','^^^....^^^...^^^....^^^',
                  '.......................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;A2;B2;C2;C3;B3;A3;C4;B4;A4',
                  false,5,1000,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='3b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDDU (Group VI, antiparallel:basket), subtype 5b ─────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_5b','Theoretical (loop-length grid search)',
                  3,true,'5b',
                  'gggttagggtttagggtttaggg','^^^...^^^....^^^....^^^',
                  '.......................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;A2;B2;C2;C4;B4;A4;C3;B3;A3',
                  false,5,1000,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='5b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDDU (Group VI, antiparallel:basket), subtype 8a ─────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_8a','Theoretical (loop-length grid search)',
                  3,true,'8a',
                  'gggttttagggtttagggttaggg','^^^.....^^^....^^^...^^^',
                  '........................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;C4;B4;A4;C3;B3;A3;A2;B2;C2',
                  false,5,1000,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='8a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDUU (Group III, hybrid3), subtype 10b ───────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_10b','Theoretical (loop-length grid search)',
                  3,true,'10b',
                  'gggtttagggtttagggtttaggg','^^^....^^^....^^^....^^^',
                  '........................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;A2;B2;C2;C4;B4;A4;A3;B3;C3',
                  false,5,700,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='10b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDUU (Group III, hybrid3), subtype 13a ───────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_13a','Theoretical (loop-length grid search)',
                  3,true,'13a',
                  'gggtttagggtttagggttaggg','^^^....^^^....^^^...^^^',
                  '.......................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;C4;B4;A4;A2;B2;C2;A3;B3;C3',
                  false,5,700,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='13a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UDUU (Group III, hybrid3), subtype 2b ────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_2b','Theoretical (loop-length grid search)',
                  3,true,'2b',
                  'gggttagggtttagggtttaggg','^^^...^^^....^^^....^^^',
                  '.......................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;A2;B2;C2;A3;B3;C3;C4;B4;A4',
                  false,5,400,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='2b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UUDU (Group V, hybrid1), subtype 9b ──────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_9b','Theoretical (loop-length grid search)',
                  3,true,'9b',
                  'gggtttagggttttagggtttaggg','^^^....^^^.....^^^....^^^',
                  '.........................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;A2;B2;C2;C3;B3;A3;A4;B4;C4',
                  false,5,500,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='9b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UUUD (Group IV, hybrid2), subtype 7b ─────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '_7b','Theoretical (loop-length grid search)',
                  3,true,'7b',
                  'gggttagggttttagggttaggg','^^^...^^^.....^^^...^^^',
                  '.......................','A+;B-;C-',
                  '3.4;3.4','19;29','A1;B1;C1;C2;B2;A2;A3;B3;C3;A4;B4;C4',
                  false,5,1000,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='7b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Real examples supplied by Joanna, .inp data from Tomek ───────────
            // UUUU (Group VIII, parallel), subtype 1b — two-block interlocked G4s,
            // 2T+2T = 4T total. 9p3a and 7dfy excluded (worse energy per Joanna's review,
            // and no .inp available yet). 9o70 excluded — its path/twist geometry does not
            // match the other four/five (different strand-visit order, unusual -63.8° twist
            // step); needs confirmation it is actually 1b before it's added.

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7d5f','4T UUUU parallel, 2-block interlocked DNA G4',
                  4,false,'1b_7d5f',
                  'ggtgtgtgtgtgtgtgtggtggtggtg','^^.^.^.^.^.^.^.^.^^.^^.^^.^',
                  '...........................','A+;B+;C-;D-',
                  '3.4;4;3.4','-30.6;-18.1;-27.7','A1;B1;A2;B2;A3;B3;A4;B4;C4;D3;C3;D2;C2;D1;C1;D4',
                  false,5,90,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // NOTE (dev-only, not user-facing): out-quadro/manifest.csv records E=+3402.9 @step50
            // for this structure — positive, unlike the other four 1b examples. Kept in pending
            // Joanna/Tomek's confirmation that their parameters give a negative Etotal.
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7d5d','4T UUUU parallel, 2-block interlocked DNA G4',
                  4,false,'1b_7d5d',
                  'ggtgtgtggtggtgtggtggtggtgtt','^^.^.^.^^.^^.^.^^.^^.^^.^..',
                  '...........................','A+;B+;C-;D-',
                  '3.3;3.3;3.3','-29.2;-10.5;-25.9','A1;B1;A2;B2;A3;B3;A4;B4;C4;D3;C3;D2;C2;D1;C1;D4',
                  false,5,50,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '4u5m','4T UUUU parallel, 2-block interlocked DNA G4',
                  4,false,'1b_4u5m',
                  'tggtggtggtggttgtggtggtggtgtt','.^^.^^.^^.^^..^.^^.^^.^^.^..',
                  '.S..........S...............','A+;B+;C-;D-',
                  '3.3;3.3;3.3','-28.2;-10.8;-25.8','A1;B1;A2;B2;A3;B3;A4;B4;C4;D3;C3;D2;C2;D1;C1;D4',
                  false,5,20,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2ms9','4T UUUU parallel, 2-block interlocked DNA G4',
                  4,false,'1b_2ms9',
                  'tggtggtggtggttgtggtggtggtgtt','.^^.^^.^^.^^..^.^^.^^.^^.^..',
                  '.S................S..S......','A+;B+;C-;D-',
                  '3.5;3.4;3.5','-35.3;-11.1;-26.9','A1;B1;A2;B2;A3;B3;A4;B4;C4;D3;C3;D2;C2;D1;C1;D4',
                  false,5,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '9mxo','4T UUUU parallel, 2-block interlocked DNA G4',
                  4,false,'1b_9mxo',
                  'ggtggtggtgtgttgtggtggtggtg','^^.^^.^^.^.^..^.^^.^^.^^.^',
                  '..........................','A+;B+;C-;D-',
                  '3.3;3.4;3.3','-26.8;-11.6;-25.9','A1;B1;A2;B2;A3;B3;A4;B4;C4;D3;C3;D2;C2;D1;C1;D4',
                  false,5,80,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // UUDD (Group I, antiparallel:basket2), subtype 5a — RNA, G4 as internal
            // loop within a long hairpin (Spinach/Broccoli-type fluorogenic aptamer), 2T.
            // Added alongside the existing theoretical placeholder ""_5a"" (kept as-is).

            // NOTE (dev-only, not user-facing): out-quadro/manifest.csv records E=+17945.1 @step30 —
            // strongly positive. Kept pending Joanna/Tomek's confirmation of parameters that fold it.
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '6b14','2T UUDD antiparallel:basket2, RNA fluorogenic aptamer (Spinach/Broccoli-type)',
                  2,false,'5a_6b14',
                  'GACGCGACCGAAAUGGUGAAGGACGGGUCCAGUGCGAAACACGCACUGUUGAGUAGAGUGUGAGCUCCGUAACUGGUCGCGUC',
                  '((((((((((..((((.(..^^.(^^.(((((((((.....)))))))..))^..^.^.^.)..).))))..).)))))))))',
                  '....................................................S...SS.S.......................',
                  'A-;B-','3.3','36.5','A1;B1;A4;B4;B2;A2;B3;A3',
                  false,5,30,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='5a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // NOTE (dev-only, not user-facing): out-quadro/manifest.csv records E=+5457.9 @step100 —
            // positive. Kept pending Joanna/Tomek's confirmation of parameters that fold it.
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '4kze','2T UUDD antiparallel:basket2, RNA fluorogenic aptamer (Spinach/Broccoli-type)',
                  2,false,'5a_4kze',
                  'GGACGCGACCGAAAUGGUGAAGGACGGGUCCAGUGCGAAACACGCACUGUUGAGUAGAGUGUGAGCUCCGUAACUGGUCGCGUC',
                  '.((((((((((..((((.(..^^.(^^.(((((((((.....)))))))..))^..^.^.^.)..).))))..).)))))))))',
                  '.....................................................S...SS.S.......................',
                  'A-;B-','3.3','36.9','A1;B1;A4;B4;B2;A2;B3;A3',
                  false,5,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='5a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" IN
                ('_4a','_3b','_5b','_8a','_10b','_13a','_2b','_9b','_7b',
                 '7d5f','7d5d','4u5m','2ms9','9mxo','6b14','4kze');");
        }
    }
}
