using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add5zev : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""StructureExamples""
                  (""PdbId"",""Note"",""Tetrads"",""IsTheoretical"",""InpName"",
                   ""Sequence"",""Structure"",""Chi"",""Orient"",""Rise"",""Twist"",""Path"",
                   ""IsTest"",""RmLevel"",""Iterations"",""SilvaSubtypeId"")
                VALUES
                  ('5zev',
                   'non-canonical DNA; D-loop + bulge; unusual rise/twist; Chi path worsens model; RRL polarity',
                   3,false,'5zev',
                   'gggtacccgggtgaggtgcggggt',
                   '^^......^^^.^.^^...^^^^.',
                   '........................','A-;B+;C-',
                   '-2.9;6.3','-31.5;63.0',
                   'A1;B1;B3;A3;C3;B2;A2;C2;C1;B4;A4;C4',
                   false,5,70,NULL)
                ON CONFLICT (""PdbId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" = '5zev';");
        }
    }
}
