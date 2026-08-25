using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4Composer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropCacheEntriesMissingAltFrames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 20260825163815_AddPdbCacheFrameVariant taught the cache to store the alternative
            // engine's structures too, but it did NOT change the dedup hash — so entries written
            // by the previous deploy still hold standard-only frames and would keep being served
            // that way forever. That is exactly the "Build shows only standard" symptom: the
            // first run of a structure showed both, every later one hit the cache and lost alt.
            //
            // Drop those entries so they get recomputed once with both variants; their frames go
            // with them via the cascading FK. Bumping EngineContentVersion would also work but
            // would throw away every 14N result, not just the incomplete ones.
            //
            // Entries whose alternative run genuinely failed get recomputed once too and are then
            // saved standard-only again — harmless, this migration runs a single time.
            //
            // Plain ANSI SQL with double-quoted identifiers: valid on both PostgreSQL (server)
            // and SQLite (local dev).
            migrationBuilder.Sql(@"
                DELETE FROM ""PdbCacheEntries""
                WHERE ""Id"" NOT IN (
                    SELECT DISTINCT ""PdbCacheEntryId""
                    FROM ""PdbCacheFrames""
                    WHERE ""Variant"" = 'alt'
                );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted cache rows are regenerable by re-running the engine — nothing to restore.
        }
    }
}
