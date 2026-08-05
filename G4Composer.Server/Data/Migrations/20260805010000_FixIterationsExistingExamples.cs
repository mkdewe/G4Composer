using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixIterationsExistingExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Full re-verification of every pre-existing StructureExample on the quadro14L
            // engine patched for the shugar/sugar + missing-read-ang bug (see
            // 20260804120000_FillMissingSilvaSubtypes and 20260804235000_FixIterations1bReverify).
            // 27 of 54 examples came back with positive Etotal at their stored Iterations value.
            // A full 10-300 scan (step 10) on the patched engine found a negative-energy point
            // for 24 of them; Iterations below is that point. Four of the 24 (2m6v, 6f4z, 7o1h,
            // _5a) only have one or two isolated negative hits surrounded by chaotic positive
            // swings rather than a real negative cluster — kept per instruction, but treat with
            // caution; a real re-verification would need more than an iteration-count search.
            //
            // Three examples never went negative anywhere in 10-300 and are left untouched here:
            // 2lod (subtype 10a, 2 siblings), 6zx6 and 8psc (subtype 11a, 8 siblings) — same
            // category as 6b14/4kze from the earlier migration.
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 170 WHERE ""PdbId"" = '2m6v';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 200 WHERE ""PdbId"" = '2m91';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 180 WHERE ""PdbId"" = '2m92';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 230 WHERE ""PdbId"" = '2mft';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 270 WHERE ""PdbId"" = '2mfu';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 120 WHERE ""PdbId"" = '3qxr';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 210 WHERE ""PdbId"" = '5j05';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 300 WHERE ""PdbId"" = '5j6u';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 230 WHERE ""PdbId"" = '5z80';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 260 WHERE ""PdbId"" = '6f4z';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 290 WHERE ""PdbId"" = '6l92';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 240 WHERE ""PdbId"" = '6r9k';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 270 WHERE ""PdbId"" = '6tc8';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 20  WHERE ""PdbId"" = '7cv3';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 180 WHERE ""PdbId"" = '7o1h';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 110 WHERE ""PdbId"" = '7otb_a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 250 WHERE ""PdbId"" = '7qa2';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 300 WHERE ""PdbId"" = '8jfq';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 190 WHERE ""PdbId"" = '8r6d';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 210 WHERE ""PdbId"" = '8s1w';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 200 WHERE ""PdbId"" = '9uk8';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 150 WHERE ""PdbId"" = '_12a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 260 WHERE ""PdbId"" = '_5a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 60  WHERE ""PdbId"" = '_8b';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '2m6v';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '2m91';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '2m92';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '2mft';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '2mfu';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '3qxr';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '5j05';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '5j6u';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '5z80';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '6f4z';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 30  WHERE ""PdbId"" = '6l92';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '6r9k';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '6tc8';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '7cv3';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 30  WHERE ""PdbId"" = '7o1h';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '7otb_a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '7qa2';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '8jfq';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '8r6d';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 70  WHERE ""PdbId"" = '8s1w';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '9uk8';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '_12a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '_5a';");
            migrationBuilder.Sql(@"UPDATE ""StructureExamples"" SET ""Iterations"" = 100 WHERE ""PdbId"" = '_8b';");
        }
    }
}
