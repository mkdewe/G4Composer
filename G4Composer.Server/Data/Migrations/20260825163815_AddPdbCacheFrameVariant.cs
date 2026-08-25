using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPdbCacheFrameVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PdbCacheFrames_PdbCacheEntryId_Step",
                table: "PdbCacheFrames");

            // No explicit `type:` on purpose. EF generated type: "TEXT" because the local dev
            // provider is SQLite, and CreateTable/AddColumn apply a hardcoded type verbatim on
            // Npgsql — that is exactly how 20260805110000_AddPdbCache put text/integer columns
            // into Postgres and had to be repaired by 20260805130000_FixPdbCacheColumnTypes.
            // Leaving it out makes the provider derive the type at apply time:
            // character varying(8) on Postgres, TEXT on SQLite.
            //
            // defaultValue "std" also backfills every existing row, so frames cached before
            // this migration keep working and are correctly labelled as standard-engine output.
            migrationBuilder.AddColumn<string>(
                name: "Variant",
                table: "PdbCacheFrames",
                maxLength: 8,
                nullable: false,
                defaultValue: "std");

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheFrames_PdbCacheEntryId_Variant_Step",
                table: "PdbCacheFrames",
                columns: new[] { "PdbCacheEntryId", "Variant", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PdbCacheFrames_PdbCacheEntryId_Variant_Step",
                table: "PdbCacheFrames");

            migrationBuilder.DropColumn(
                name: "Variant",
                table: "PdbCacheFrames");

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheFrames_PdbCacheEntryId_Step",
                table: "PdbCacheFrames",
                columns: new[] { "PdbCacheEntryId", "Step" },
                unique: true);
        }
    }
}
