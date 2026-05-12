using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── New subtypes ──────────────────────────────────────────────────
            // Group III (UDUU) – subtype 7a
            migrationBuilder.Sql(@"
                INSERT INTO ""SilvaSubtypes"" (""Code"",""Loop"",""Silva"",""Onz"",""Note"",""SilvaGroupId"")
                SELECT '7a','-Lw-Ln-P','3(-Lw-Ln-P)','O',NULL,g.""Id""
                FROM ""SilvaGroups"" g WHERE g.""Code""='UDUU'
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // Group V (UUDU) – subtype 9a
            migrationBuilder.Sql(@"
                INSERT INTO ""SilvaSubtypes"" (""Code"",""Loop"",""Silva"",""Onz"",""Note"",""SilvaGroupId"")
                SELECT '9a','-P-Lw-Ln','3(-P-Lw-Ln)','O',NULL,g.""Id""
                FROM ""SilvaGroups"" g WHERE g.""Code""='UUDU'
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // Group VI (UDDU) – subtype 11a
            migrationBuilder.Sql(@"
                INSERT INTO ""SilvaSubtypes"" (""Code"",""Loop"",""Silva"",""Onz"",""Note"",""SilvaGroupId"")
                SELECT '11a','-LwD+Ln','2(-LwD+Ln)','O',NULL,g.""Id""
                FROM ""SilvaGroups"" g WHERE g.""Code""='UDDU'
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // Group VIII (UUUU) – subtype 1a
            migrationBuilder.Sql(@"
                INSERT INTO ""SilvaSubtypes"" (""Code"",""Loop"",""Silva"",""Onz"",""Note"",""SilvaGroupId"")
                SELECT '1a','-P-P-P','3(-P-P-P)','O',NULL,g.""Id""
                FROM ""SilvaGroups"" g WHERE g.""Code""='UUUU'
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // ── Group II (UDUD) – 7otb_a into existing subtype 6a ─────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7otb_a',
                  'central quartet with 50:50 mix of syn/anti; 1.6Å',
                  3,false,'7otb_a_js12B_60',
                  'gggttagggttagggtttggg','ABC...CBA...ABC...CBA',
                  'SS....S.....SS....S..','A+;B+;C-',
                  '3.4','27;19','A1;B1;C1;C4;B4;A4;A3;B3;C3;C2;B2;A2',
                  false,0,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='6a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Group III (UDUU) – 7a ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '8r6d','hybrid DNA, -; subtype 7a',
                  3,false,'7a_8r6d',
                  'gggcgcgaagcattcgcggggttagggtggg',
                  '^^^((((((...))))))^^^...^^^.^^^',
                  'S.................SS....S...S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;C4;B4;A4;A3;B3;C3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='7a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Group V (UUDU) – 9a ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2e4i','human telomeric DNA UUDU 9a',
                  3,false,'2e4i_test',
                  'agggttagggttagggttaggg','.^^^...^^^...^^^...^^^',
                  '.S.....S.....SS....S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C3;B3;A3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='9a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '2gku','UUDU 9a; 5'' overhang; WC pair at 5''-lateral junction',
                  3,false,'2gku_test',
                  'ttgggttagggttagggttaggga','(.^^^...^^^...^^^..)^^^.',
                  '..S.....S.....SS....S...','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C3;B3;A3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='9a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7cv3','UUDU 9a; G4-duplex junction (hairpin lateral loop)',
                  3,false,'7cv3_test',
                  'gcgggagggcgcgccagcggggtcggg',
                  '..^^^.^^^(((....)))^^^..^^^',
                  '..S...S............SS...S..','A+;B-;C-',
                  '3.4','19;29','A1;B1;C1;A4;B4;C4;C3;B3;A3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='9a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Group VI (UDDU) – 11a ─────────────────────────────────────────
            // 2T
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '5j4p','UDDU 11a 2T RL; dna -',
                  2,false,'11a_5j4p',
                  'ggtttggttttggtttgg','^^...^^....^^...^^',
                  'S....S.....S....S.','A+;B-',
                  '3.4','19','A1;B1;B4;A4;A2;B2;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
            // 3T LRL
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '143d','UDDU 11a 3T LRL; dna -',
                  3,false,'11a_143d',
                  'agggttagggttagggttaggg','.^^^...^^^...^^^...^^^',
                  '..S.....S.....S.....S.','A-;B+;C-',
                  '3.4','37;19','A1;B1;C1;C4;B4;A4;A2;B2;C2;C3;B3;A3',
                  false,5,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
            // 3T RLR
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '5j05','UDDU 11a 3T RLR; dna -',
                  3,false,'11a_5j05',
                  'agggttagggttagggttaggg','.^^^...^^^...^^^...^^^',
                  '.S.S....S....S.S....S.','A+;B-;C+',
                  '3.4','19;37','A1;B1;C1;C4;B4;A4;A2;B2;C2;C3;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
            // 3T RRL
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '8psc','UDDU 11a 3T RRL; hybrid -',
                  3,false,'11a_8psc',
                  'agggtagggcggcggggcagggt','.^^^..^^^(...)^^^..^^^.',
                  '.SS...S.......SS...S...','A+;B+;C-',
                  '3.4','27;19','A1;B1;C1;C4;B4;A4;A2;B2;C2;C3;B3;A3',
                  false,5,100,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
            // 4T RLRL
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '5j6u','UDDU 11a 4T RLRL; dna -',
                  4,false,'11a_5j6u',
                  'ggggtttggggttttggggaagggg','^^^^...^^^^....^^^^..^^^^',
                  '.S.S...S.S.....S.S...S.S.','A+;B-;C+;D-',
                  '3.4','19;37;19',
                  'A1;B1;C1;D1;D4;C4;B4;A4;A2;B2;C2;D2;D3;C3;B3;A3',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='11a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Group VIII (UUUU) – 1a ────────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '7qa2','RNA parallel G4; nmr; RMSD 5.97',
                  3,false,'7qa2_1a',
                  'GGGCCAUGGGGUGGGAGCUGGG','^^^.....^^^.^^^....^^^',
                  '......................','A-;B-;C-',
                  '3.4','29','A1;B1;C1;A4;B4;C4;A3;B3;C3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                SELECT '9cb5','DNA parallel G4; x-ray',
                  3,false,'9cb5_1a',
                  'tagggagggtttttagggtgggt','..^^^.^^^......^^^.^^^.',
                  '.......................','A-;B-;C-',
                  '3.4','29','A1;B1;C1;A4;B4;C4;A3;B3;C3;A2;B2;C2',
                  false,5,70,s.""Id""
                FROM ""SilvaSubtypes"" s WHERE s.""Code""='1a'
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");

            // ── Non-canonical – 8jfq ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('8jfq',
                   '2(-P-P-P) non-canonical; dna nmr; 3'' snapback diagonal through propeller',
                   2,false,'8jfq_test',
                   'tcggggaggcagggcgggaggaagat',
                   '...^^^.^^..^^^.^^^.....^..',
                   '..........................','A-;B-;C-',
                   '3.4','29;29',
                   'A1;B1;C1;A4;B4;A3;B3;C3;A2;B2;C2;C4',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" IN
                ('7otb_a','8r6d','2e4i','2gku','7cv3','5j4p','143d','5j05','8psc','5j6u','7qa2','9cb5','8jfq');");
            migrationBuilder.Sql(@"DELETE FROM ""SilvaSubtypes"" WHERE ""Code"" IN ('7a','9a','11a','1a');");
        }
    }
}
