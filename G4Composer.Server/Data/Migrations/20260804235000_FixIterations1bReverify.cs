using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixIterations1bReverify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 20260804120000_FillMissingSilvaSubtypes was already applied to production before
            // the quadro14L shugar/sugar+missing-read-ang bug was fixed and these 5 examples were
            // re-verified on the patched engine (full 10-100 iteration scan). Because EF Core
            // tracks migrations by name, editing that file afterwards had no effect on databases
            // that already ran it — this migration brings Iterations in line with the re-verified
            // values by name, not by re-running the old migration.
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 40  WHERE ""PdbId"" = '7d5f';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '7d5d';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '4u5m';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 80  WHERE ""PdbId"" = '2ms9';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 90  WHERE ""PdbId"" = '7d5f';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 50  WHERE ""PdbId"" = '7d5d';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 20  WHERE ""PdbId"" = '4u5m';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '2ms9';");
        }
    }
}
