using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRiseValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite") return;

            // Single-step rises: '3.40000009536743' → '3.4'
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" = ROUND(CAST(""Rise"" AS NUMERIC), 1)::TEXT
                WHERE ""Rise"" NOT LIKE '%;%';
            ");

            // Two-step rises: 'x;y' → each part formatted to 1 dp
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" =
                    ROUND(CAST(SUBSTR(""Rise"", 1, STRPOS(""Rise"", ';') - 1) AS NUMERIC), 1)::TEXT
                    || ';' ||
                    ROUND(CAST(SUBSTR(""Rise"", STRPOS(""Rise"", ';') + 1) AS NUMERIC), 1)::TEXT
                WHERE LENGTH(""Rise"") - LENGTH(REPLACE(""Rise"", ';', '')) = 1;
            ");

            // Three-step rises: 'x;y;z'
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" =
                    ROUND(CAST(SUBSTR(""Rise"", 1, STRPOS(""Rise"", ';') - 1) AS NUMERIC), 1)::TEXT
                    || ';' ||
                    ROUND(CAST(
                        SUBSTR(
                            SUBSTR(""Rise"", STRPOS(""Rise"", ';') + 1),
                            1,
                            STRPOS(SUBSTR(""Rise"", STRPOS(""Rise"", ';') + 1), ';') - 1
                        ) AS NUMERIC), 1)::TEXT
                    || ';' ||
                    ROUND(CAST(
                        SUBSTR(
                            SUBSTR(""Rise"", STRPOS(""Rise"", ';') + 1),
                            STRPOS(SUBSTR(""Rise"", STRPOS(""Rise"", ';') + 1), ';') + 1
                        ) AS NUMERIC), 1)::TEXT
                WHERE LENGTH(""Rise"") - LENGTH(REPLACE(""Rise"", ';', '')) = 2;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Normalization is lossy — original float representations cannot be restored
        }
    }
}
