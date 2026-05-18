using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Change Rise column from REAL (float) to TEXT (string) ─────────
            // Needed to support multi-step rise values like "3.4;-6.8" for
            // non-canonical G4 structures that start from the middle tetrad.
            // EF Core handles provider differences: PostgreSQL → ALTER COLUMN TYPE,
            // SQLite → full table rebuild.
            migrationBuilder.AlterColumn<string>(
                name: "Rise",
                table: "StructureExamples",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "3.4",
                oldClrType: typeof(float),
                oldType: "REAL",
                oldNullable: false);

            // ── 2. UDUU (Group III) – subtype 7a ─────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '186d','UDUU hybrid3, 3(-Lw-Ln-P); NMR',
                  3,false,'7a_186d',
                  'ttggggttggggttggggttgggg','..^^^....^^^...^^^..^^^.',
                  '..S......SS....S....S...','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;C4;B4;A4;A3;B3;C3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='7a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 3. UUUD (Group IV) – subtype 2a ──────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7o1h','UUUD hybrid2, 3(-P-P-Lw); chimeric RNA/DNA; only known experimental example',
                  3,false,'2a_7o1h',
                  'ugagggtgggtgggacgcgcagcgtgggtaa','...^^^.^^^.^^^((((...))))^^^...',
                  '.........................SSS...','A-;B-;C-',
                  '3.4;3.4','29;29','A1;B1;C1;A4;B4;C4;A3;B3;C3;C2;B2;A2',
                  false,5,30,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='2a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 4. UUUD (Group IV) – subtype 10a ─────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2lod','UUUD 10a; -PD+Lw',
                  3,false,'10a_2lod',
                  'gggatgggacacaggggacggg','ABC..ABC.....CBA...ABC',
                  'S....S.......SS....S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C2;B2;A2;A3;B3;C3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='10a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '8s1w','UUUD 10a; -PD+Lw; hairpin propeller loop',
                  3,false,'10a_8s1w',
                  'gggcgcgaagcgttcgcggggttagggttaggg','^^^.(((((...)))))^^^....^^^...^^^',
                  'S.......................SS....S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C2;B2;A2;A3;B3;C3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='10a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 5. UUUD (Group IV) – subtype 13b ─────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '6l92','UUUD 13b; +LnD-P; dna',
                  3,false,'13b_6l92',
                  'gggccaccgggcagtgggcggg','^^^.....^^^....^^^.^^^',
                  'S.......SS.....S...S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;C2;B2;A2;A4;B4;C4;A3;B3;C3',
                  false,5,30,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='13b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 6. UUDU (Group V) – subtype 9a ───────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '5z80','UUDU 9a; WC pair 3'' end / lateral loop',
                  3,false,'5z80_test',
                  'aaagggttagggttagggttagggaa','...^^^...^^^.(.^^^...^^^).',
                  '...S.....S.....SS....S....','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C3;B3;A3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='9a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 7. UDDU (Group VI) – subtype 11a (2T examples) ───────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2m6v','UDDU 11a 2T RL; WC pair between lateral loops',
                  2,false,'11a_2m6v',
                  'gggttgggttttgggtggg','^^...(^^....^^.)^^.',
                  'S....SS.....S...S..','A+;B-',
                  '3.4','19','A1;B1;B4;A4;A2;B2;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2m91','UDDU 11a 2T RL; diagonal loop as hairpin',
                  2,false,'11a_2m91',
                  'gggaagggcgcgaagcattcgcgaggtagg','^^...^^.((((((...)))))).^^..^^',
                  'S....S..................S...S.','A+;B-',
                  '3.4','19','A1;B1;B4;A4;A2;B2;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 8. UDDU (Group VI) – subtype 11a (3T RLR) ────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '6zx6','UDDU 11a 3T RLR; hybrid',
                  3,false,'11a_6zx6',
                  'cgggcgcgggaggaagggggcggg','.^^^..(^^^.....^^^[)]^^^',
                  '.S.S....S......S.SS.S.S.','A+;B-;C+',
                  '3.4','19;37','A1;B1;C1;C4;B4;A4;A2;B2;C2;C3;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 9. UDDU (Group VI) – subtype 12b ─────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2mft','UDDU 12b 3T RLL; diagonal loop',
                  3,false,'12b_2mft',
                  'gggttttgggtgggttttggg','^^^....^^^.^^^....^^^',
                  'S......SS..SS.....S..','A-;B+;C-',
                  '3.4','19;29','A1;B1;C1;C3;B3;A3;C4;B4;A4;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='12b'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 10. Non-canonical – canonical parallel variants ────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('3qxr',
                   'non-canonical 2(-PX+P); V-loop + long-distance propeller; BrU at pos 12',
                   3,false,'3qxr_test',
                   'agggagggcgctgggaggaggg','.^^^.^^^.^..^^^.....^^',
                   '......................','A-;B-;C-',
                   '3.4','29','A1;B1;C1;A4;B4;C4;A3;A2;B2;C2;B3;C3',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('2m92',
                   'non-canonical 2(-PX+P); extended long-distance propeller as hairpin',
                   3,false,'2m92_test',
                   'agggtgggtgctggggcgcgaagcattcgcgagg','(^^^.^^^.^.)^^^.((((((...)))))).^^',
                   '..................................','A-;B-;C-',
                   '3.4','29','A1;B1;C1;A4;B4;C4;A3;A2;B2;C2;B3;C3',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('2m53',
                   'non-canonical 2(-P-P-P); D-loop in 2nd column; non-standard twist step1=12',
                   3,false,'2m53_test',
                   'tgtgggggtggacgggccgggtaga','...^^^^^..^..^^^..^^^....',
                   '.........................','A-;B-;C-',
                   '3.4','12;29','A1;B1;C1;B4;C4;A4;A3;B3;C3;A2;B2;C2',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── 11. Non-canonical – UDDD 2(-LwX+P) with negative rise/twist ───────
            // These structures start from the middle tetrad — rise and twist for
            // the T2→T1 step are negative to model the downward helical direction.
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('6tc8',
                   'non-canonical UDDD 2(-LwX+P); starts from middle tetrad; RL twist=19/-46',
                   3,false,'lxp_6tc8',
                   'gggatgggacacaggggacggg','^^...^^^.....^^^^..^^^',
                   'S....S.......S.....S..','A+;B-;C+',
                   '3.4;-6.8','19;-46','A1;B1;B4;A4;C4;C1;B2;A2;C2;B3;A3;C3',
                   false,5,100,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('6tcg',
                   'non-canonical UDDD 2(-LwX+P); variant polarity RRR; twist=27/-54',
                   3,false,'lxp_6tcg',
                   'gggatgggacacaggggacggg','^^...^^^.....^^^^..^^^',
                   'SS...........S........','A+;B+;C+',
                   '3.4;-6.8','27;-54','A1;B1;B4;A4;C4;C1;B2;A2;C2;B3;A3;C3',
                   false,5,100,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('2kpr',
                   'non-canonical UDDD 2(-LwX+P); simplest structure; RL twist=19/-46',
                   3,false,'lxp_2kpr',
                   'gggtggggaaggggtgggt','^^..^^^...^^^^.^^^.',
                   'S...S.....S....S...','A+;B-;C+',
                   '3.4;-6.8','19;-46','A1;B1;B4;A4;C4;C1;B2;A2;C2;B3;A3;C3',
                   false,5,100,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('6l8m',
                   'non-canonical UDDD 2(-LwX+P); bulge + hairpin lateral loop; RR twist=27/-54',
                   3,false,'lxp_6l8m',
                   'gggtcacgggcagtgggcggg','^^(...)^^^..^.^^^.^^^',
                   'SS.........S.........','A+;B+;C+',
                   '3.4;-6.8','27;-54','A1;B1;B4;A4;C4;C1;B2;A2;C2;B3;A3;C3',
                   false,5,30,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" IN (
                '186d','7o1h','2lod','8s1w','6l92','5z80',
                '2m6v','2m91','6zx6','2mft',
                '3qxr','2m92','2m53','6tc8','6tcg','2kpr','6l8m');");

            // Revert Rise column to REAL — multi-step values will be lost.
            // Best-effort: default back to 3.4 for any non-numeric rows.
            migrationBuilder.AlterColumn<float>(
                name: "Rise",
                table: "StructureExamples",
                type: "REAL",
                nullable: false,
                defaultValue: 3.4f,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldNullable: false);
        }
    }
}
