using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixPdbCacheColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 20260805110000_AddPdbCache hardcoded SQLite-style column types ("INTEGER"/"TEXT")
            // that CreateTable applies verbatim on Npgsql instead of being re-derived from the
            // CLR type, unlike the property mappings used at query time. Result: IsExample
            // landed as a real Postgres "integer" instead of "boolean", and
            // CreatedAtUtc/LastAccessedAtUtc landed as "text" instead of "timestamp without
            // time zone" — so every INSERT (bool/DateTime parameters) was rejected outright.
            // Table was still empty in production when this was found, so no data migration
            // is needed, just the column types.
            //
            // SQLite has no ALTER COLUMN TYPE at all — but it never had this bug in the first
            // place (type affinity, not strict types, so the original "INTEGER"/"TEXT" columns
            // already read/write bool/DateTime values fine there). Postgres-only fix.
            if (!migrationBuilder.IsNpgsql()) return;

            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""IsExample"" TYPE boolean USING (""IsExample"" <> 0);");
            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""CreatedAtUtc"" TYPE timestamp without time zone USING (""CreatedAtUtc""::timestamp without time zone);");
            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""LastAccessedAtUtc"" TYPE timestamp without time zone USING (""LastAccessedAtUtc""::timestamp without time zone);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.IsNpgsql()) return;

            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""IsExample"" TYPE integer USING (CASE WHEN ""IsExample"" THEN 1 ELSE 0 END);");
            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""CreatedAtUtc"" TYPE text USING (""CreatedAtUtc""::text);");
            migrationBuilder.Sql(@"ALTER TABLE ""PdbCacheEntries"" ALTER COLUMN ""LastAccessedAtUtc"" TYPE text USING (""LastAccessedAtUtc""::text);");
        }
    }
}
