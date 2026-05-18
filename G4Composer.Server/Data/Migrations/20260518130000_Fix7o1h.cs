using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fix7o1h : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix 7o1h: sequence must use lowercase 'u' (quadro14L chimeric convention),
            // rise must be "3.4;3.4" (two inter-tetrad steps for 3 tetrads).
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET
                  ""Sequence"" = 'ugagggtgggtgggacgcgcagcgtgggtaa',
                  ""Rise""     = '3.4;3.4'
                WHERE ""PdbId"" = '7o1h';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET
                  ""Sequence"" = 'Ugagggtgggtgggacgcgcagcgtgggtaa',
                  ""Rise""     = '3.4'
                WHERE ""PdbId"" = '7o1h';
            ");
        }
    }
}
