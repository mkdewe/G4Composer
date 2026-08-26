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
            // UWAGA: usunięcie `type: "INTEGER"` wygenerowanego przez EF NIE wystarczyło.
            // Typ kolumny bierze się z modelu, a AppDbContextModelSnapshot.cs jest generowany
            // lokalnie, czyli pod SQLite, i ma zaszyte
            // `b.Property<bool>("IsCurated").HasColumnType("INTEGER")` — więc na Postgresie i
            // tak powstała kolumna integer. Naprawia to dopiero
            // 20260826080000_FixStructureExampleBoolColumns; tamten komentarz opisuje pełny
            // mechanizm. Nie zmieniaj tej migracji — jest już zaaplikowana na produkcji.
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
