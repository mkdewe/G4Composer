using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPdbCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PdbCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StructureExampleId = table.Column<int>(type: "INTEGER", nullable: true),
                    PdbId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsExample = table.Column<bool>(type: "INTEGER", nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdbCacheEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdbCacheEntries_StructureExamples_StructureExampleId",
                        column: x => x.StructureExampleId,
                        principalTable: "StructureExamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PdbCacheFrames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PdbCacheEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    Etotal = table.Column<double>(type: "REAL", nullable: true),
                    Pdb = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdbCacheFrames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdbCacheFrames_PdbCacheEntries_PdbCacheEntryId",
                        column: x => x.PdbCacheEntryId,
                        principalTable: "PdbCacheEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheEntries_Hash",
                table: "PdbCacheEntries",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheEntries_PdbId",
                table: "PdbCacheEntries",
                column: "PdbId");

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheEntries_LastAccessedAtUtc",
                table: "PdbCacheEntries",
                column: "LastAccessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheEntries_StructureExampleId",
                table: "PdbCacheEntries",
                column: "StructureExampleId");

            migrationBuilder.CreateIndex(
                name: "IX_PdbCacheFrames_PdbCacheEntryId_Step",
                table: "PdbCacheFrames",
                columns: new[] { "PdbCacheEntryId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PdbCacheFrames");

            migrationBuilder.DropTable(
                name: "PdbCacheEntries");
        }
    }
}
