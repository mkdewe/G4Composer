using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── UDUU (Group III), subtype 7a — 2T example ────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2mfu','UDUU hybrid3, 7a, 2T; DNA NMR',
                  2,false,'7a_2mfu',
                  'tgggtttgggttgggtttggg','..^^....^^...^^...^^.',
                  '..S.....S....S....S..','A+;B-',
                  '3.4','29','A1;B1;B4;A4;A3;B3;B2;A2',
                  false,0,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='7a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UUUU (Group VIII), subtype 1a — 3T DNA example ───────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '8rzx','UUUU parallel DNA; 3T; propeller loops; WC pair in loop',
                  3,false,'1a_8rzx',
                  'tgggcggggcacaggtgggcgggg','.^^^.^^^........^^^.^^^.',
                  '........................','A-;B-;C-',
                  '3.4;3.4','29;29','A1;B1;C1;A4;B4;C4;A3;B3;C3;A2;B2;C2',
                  false,0,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── UUUU (Group VIII), subtype 1a — 4T DNA example ───────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '6w9p','UUUU parallel DNA; 4T; GGGG tetrad columns; x-ray',
                  4,false,'1a_6w9p',
                  'gttggggttggggttggggttggggt','...^^^^..^^^^..^^^^..^^^^.',
                  '..........................','A-;B-;C-;D-',
                  '3.4;3.4;3.4','29','A1;B1;C1;D1;A4;B4;C4;D4;A3;B3;C3;D3;A2;B2;C2;D2',
                  false,0,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Non-canonical — 8psb (3'' snapback lateral, 2(-P-P-P) like) ──────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('8psb',
                   'non-canonical 2(-P-P-P); 3'' snapback lateral loop; RMSD 4.04',
                   3,false,'8psb_test',
                   'agggtagggcggcggggacgggt','.^^^..^^^.^^.^^^.....^.',
                   '.......................','A-;B-;C-',
                   '3.4','29;29','A1;B1;C1;A4;B4;C4;A3;B3;A2;B2;C2;C3',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" IN
                ('2mfu','8rzx','6w9p','8psb');");
        }
    }
}
