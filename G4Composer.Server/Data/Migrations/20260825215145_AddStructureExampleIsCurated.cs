using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructureExampleIsCurated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wybrany reprezentant kubełka (podtyp Silva × liczba tetrad), nadawany przez
            // tools/curate-examples.sh na podstawie policzonych energii. Domyślnie false —
            // dopóki skrypt nie przeliczy energii, nic nie jest oznaczone.
            //
            // Bez `type:` celowo. EF wygenerował type: "INTEGER", bo lokalny provider to
            // SQLite, a AddColumn wstawia zadeklarowany typ na Npgsql dosłownie — dokładnie
            // tak 20260805110000_AddPdbCache wsadził do Postgresa kolumny integer zamiast
            // boolean i trzeba to było naprawiać osobną migracją. Bez tego pola provider
            // wyprowadza typ przy aplikowaniu: boolean na Postgresie, INTEGER na SQLite.
            migrationBuilder.AddColumn<bool>(
                name: "IsCurated",
                table: "StructureExamples",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCurated",
                table: "StructureExamples");
        }
    }
}
