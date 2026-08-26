using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixStructureExampleBoolColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Boolowskie kolumny StructureExamples wylądowały w Postgresie jako integer.
            // Przyczyna NIE leży w `type:` przy AddColumn/CreateTable — leży w modelu:
            // AppDbContextModelSnapshot.cs jest generowany lokalnie, czyli pod SQLite, i ma
            // zaszyte `b.Property<bool>("IsCurated").HasColumnType("INTEGER")`. Typ kolumny
            // bierze się z modelu, więc migracja tworzy integer niezależnie od tego, czy
            // `type:` podano, czy nie. Dotyczy to każdego boola w tej tabeli.
            //
            // Objaw jest opóźniony i mylący: odczyt działa (Npgsql zmapuje integer na bool),
            // ale każdy predykat trafiający do SQL-a wywala się na 42804 "argument of WHERE
            // must be type boolean". Dlatego IsTest i IsTheoretical przeleżały tak miesiącami
            // bez szkody, a IsCurated wysadziło /api/structures/groups w dniu, w którym
            // pierwszy raz pojawiło się w zapytaniu (`.AnyAsync(e => e.IsCurated)`).
            //
            // SQLite tego problemu nie ma (affinity, nie typy ścisłe) i nie zna ALTER COLUMN
            // TYPE — poprawka wyłącznie dla Postgresa.
            if (!migrationBuilder.IsNpgsql()) return;

            // Idempotentnie i tylko tam, gdzie trzeba: na bazie założonej po tej poprawce
            // kolumny są już boolean i pętla ich nie dotknie.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE col text;
                BEGIN
                    FOREACH col IN ARRAY ARRAY['IsTest', 'IsTheoretical', 'IsCurated'] LOOP
                        IF EXISTS (
                            SELECT 1 FROM information_schema.columns
                            WHERE table_name = 'StructureExamples'
                              AND column_name = col
                              AND data_type = 'integer')
                        THEN
                            -- DEFAULT 0 trzeba zdjąć przed zmianą typu, inaczej Postgres
                            -- odrzuci ALTER: domyślna wartość nie da się rzutować.
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I DROP DEFAULT', col);
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I TYPE boolean USING (%I <> 0)', col, col);
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I SET DEFAULT false', col);
                        END IF;
                    END LOOP;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.IsNpgsql()) return;

            migrationBuilder.Sql(@"
                DO $$
                DECLARE col text;
                BEGIN
                    FOREACH col IN ARRAY ARRAY['IsTest', 'IsTheoretical', 'IsCurated'] LOOP
                        IF EXISTS (
                            SELECT 1 FROM information_schema.columns
                            WHERE table_name = 'StructureExamples'
                              AND column_name = col
                              AND data_type = 'boolean')
                        THEN
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I DROP DEFAULT', col);
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I TYPE integer USING (CASE WHEN %I THEN 1 ELSE 0 END)', col, col);
                            EXECUTE format('ALTER TABLE ""StructureExamples"" ALTER COLUMN %I SET DEFAULT 0', col);
                        END IF;
                    END LOOP;
                END $$;");
        }
    }
}
