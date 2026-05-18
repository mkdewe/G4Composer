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
            // Single-step rises: '3.40000009536743' → '3.4'
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" = PRINTF('%.1f', CAST(""Rise"" AS REAL))
                WHERE ""Rise"" NOT LIKE '%;%';
            ");

            // Two-step rises: 'x;y' → each part formatted to 1 dp
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" =
                    PRINTF('%.1f', CAST(SUBSTR(""Rise"", 1, INSTR(""Rise"", ';') - 1) AS REAL))
                    || ';' ||
                    PRINTF('%.1f', CAST(SUBSTR(""Rise"", INSTR(""Rise"", ';') + 1) AS REAL))
                WHERE LENGTH(""Rise"") - LENGTH(REPLACE(""Rise"", ';', '')) = 1;
            ");

            // Three-step rises: 'x;y;z'
            migrationBuilder.Sql(@"
                UPDATE ""StructureExamples""
                SET ""Rise"" =
                    PRINTF('%.1f', CAST(SUBSTR(""Rise"", 1, INSTR(""Rise"", ';') - 1) AS REAL))
                    || ';' ||
                    PRINTF('%.1f', CAST(
                        SUBSTR(
                            SUBSTR(""Rise"", INSTR(""Rise"", ';') + 1),
                            1,
                            INSTR(SUBSTR(""Rise"", INSTR(""Rise"", ';') + 1), ';') - 1
                        ) AS REAL))
                    || ';' ||
                    PRINTF('%.1f', CAST(
                        SUBSTR(
                            SUBSTR(""Rise"", INSTR(""Rise"", ';') + 1),
                            INSTR(SUBSTR(""Rise"", INSTR(""Rise"", ';') + 1), ';') + 1
                        ) AS REAL))
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
