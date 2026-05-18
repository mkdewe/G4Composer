using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fix7o1hSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Quadro14L rejects lowercase 'u' (ERROR 2: Nieprawidłowa reszta u1 w sekwencji).
            // RNA uridine must be stored as uppercase 'U'.
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Sequence"" = 'Ugagggtgggtgggacgcgcagcgtgggtaa'
                WHERE ""PdbId"" = '7o1h';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Sequence"" = 'ugagggtgggtgggacgcgcagcgtgggtaa'
                WHERE ""PdbId"" = '7o1h';
            ");
        }
    }
}
