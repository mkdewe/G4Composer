using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupInpNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite") return;

            // Remove subtype prefix like "11a_", "4b_", "1a_" (digits+letter, underscore at position ≤ 4)
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""InpName"" = SUBSTR(""InpName"", STRPOS(""InpName"", '_') + 1)
                WHERE STRPOS(""InpName"", '_') BETWEEN 1 AND 4
                  AND SUBSTR(""InpName"", 1, 1) ~ '^[0-9]$'
                  AND LENGTH(""InpName"") > STRPOS(""InpName"", '_');
            ");

            // Remove subtype suffix like "_1a"
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""InpName"" = SUBSTR(""InpName"", 1, LENGTH(""InpName"") - 3)
                WHERE ""InpName"" LIKE '%\_1a' ESCAPE '\';
            ");

            // Remove _js12B_N iteration suffixes (e.g. _js12B_100, _js12B_50, _js12B_60)
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""InpName"" = SUBSTR(""InpName"", 1, STRPOS(""InpName"", '_js12B') - 1)
                WHERE ""InpName"" LIKE '%\_js12B%' ESCAPE '\';
            ");

            // Remove _test suffix
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""InpName"" = SUBSTR(""InpName"", 1, LENGTH(""InpName"") - 5)
                WHERE ""InpName"" LIKE '%\_test' ESCAPE '\';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Name cleanup is intentional — originals cannot be meaningfully restored
        }
    }
}
