using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SilvaGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    GroupNumber = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Groove = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SilvaGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SilvaSubtypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Loop = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Silva = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Onz = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SilvaGroupId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SilvaSubtypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SilvaSubtypes_SilvaGroups_SilvaGroupId",
                        column: x => x.SilvaGroupId,
                        principalTable: "SilvaGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StructureExamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PdbId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Tetrads = table.Column<int>(type: "INTEGER", nullable: false),
                    IsTheoretical = table.Column<bool>(type: "INTEGER", nullable: false),
                    InpName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Structure = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Chi = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Orient = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Rise = table.Column<float>(type: "REAL", nullable: false),
                    Twist = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IsTest = table.Column<bool>(type: "INTEGER", nullable: false),
                    RmLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Iterations = table.Column<int>(type: "INTEGER", nullable: false),
                    SilvaSubtypeId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructureExamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StructureExamples_SilvaSubtypes_SilvaSubtypeId",
                        column: x => x.SilvaSubtypeId,
                        principalTable: "SilvaSubtypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SilvaGroups_Code",
                table: "SilvaGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SilvaSubtypes_Code",
                table: "SilvaSubtypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SilvaSubtypes_SilvaGroupId",
                table: "SilvaSubtypes",
                column: "SilvaGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StructureExamples_PdbId",
                table: "StructureExamples",
                column: "PdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructureExamples_SilvaSubtypeId",
                table: "StructureExamples",
                column: "SilvaSubtypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StructureExamples");

            migrationBuilder.DropTable(
                name: "SilvaSubtypes");

            migrationBuilder.DropTable(
                name: "SilvaGroups");
        }
    }
}
