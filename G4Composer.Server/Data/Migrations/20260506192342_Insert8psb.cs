using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Insert8psb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        INSERT INTO ""StructureExamples""
          (""PdbId"", ""Note"", ""Tetrads"", ""IsTheoretical"", ""InpName"",
           ""Sequence"", ""Structure"", ""Chi"", ""Orient"",
           ""Rise"", ""Twist"", ""Path"",
           ""IsTest"", ""RmLevel"", ""Iterations"", ""SilvaSubtypeId"")
        VALUES
          ('8psb',
           '3T non-canonical, DNA/RNA mixed backbone, 3-prime snapback lateral, RMSD 4.0',
           3, false, '8psb_test',
           'agggtagggcggcggggacgggt',
           '.^^^..^^^.^^.^^^.....^.',
           '.......................',
           'A-;B-;C-',
           '3.4', '29;29',
           'A1;B1;C1;A4;B4;C4;A3;B3;A2;B2;C2;C3',
           false, 0, 100,
           NULL);
    ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""StructureExamples"" WHERE ""PdbId"" = '8psb';");
        }

    }
}
