using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fix6r9lChi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 6r9l was seeded with a Chi of 24 chars while its Sequence is 25,
            // so QuadroInputValidator rejected the "Examples → Run" path with
            // "Chi length (24) must match sequence length (25)". The seeder was
            // already corrected to 25 chars, but existing databases keep the old
            // value because seeding only runs on an empty DB. Bring the row in
            // line with the seeder (one trailing '.' was missing).
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Chi"" = '....SS....S...S...S......'
                WHERE ""PdbId"" = '6r9l';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the previous (length-24) value for symmetry.
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Chi"" = '....SS....S...S...S.....'
                WHERE ""PdbId"" = '6r9l';
            ");
        }
    }
}
