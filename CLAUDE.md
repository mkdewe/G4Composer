# G4Composer — project notes for Claude

## Database: two environments, two engines

| Environment | Engine | Connection |
|---|---|---|
| Local (dev) | SQLite (`g4composer.db`) | file in `G4Composer.Server/` |
| Server (prod) | PostgreSQL 16 via Npgsql | `Host=127.0.0.1;Port=5433;Database=g4composer` |

**EF Core migrations run against PostgreSQL on the server.**
The local SQLite file is only for manual testing — it is NOT committed and NOT used by migrations.

### SQL dialect rules for migrations

Always write `migrationBuilder.Sql(...)` using **PostgreSQL syntax**. Never use SQLite-only functions.

| Need | SQLite (WRONG) | PostgreSQL (correct) |
|---|---|---|
| Format float to 1 dp | `PRINTF('%.1f', CAST(x AS REAL))` | `ROUND(CAST(x AS NUMERIC), 1)::TEXT` |
| Position of substring | `INSTR(str, sub)` | `STRPOS(str, sub)` |
| Match digit with glob | `col GLOB '[0-9]'` | `col ~ '^[0-9]$'` |
| Literal `_` in LIKE | `LIKE '%_foo'` | `LIKE '%\_foo' ESCAPE '\'` |
| Substring | `SUBSTR(str, pos)` | `SUBSTR(str, pos)` ✓ same |
| String length | `LENGTH(str)` | `LENGTH(str)` ✓ same |
| Replace | `REPLACE(str, a, b)` | `REPLACE(str, a, b)` ✓ same |

## Infrastructure

- **Frontend**: React + Vite (`g4composer.client/`), built with `npm run build` → output to `G4Composer.Server/wwwroot/`
- **Backend**: ASP.NET Core 10, deployed as systemd service `g4composer` on port 5238
- **Docker images** (used by the backend to run bioinformatics tools):
  - `quadro14l:latest` — built from `docker-biotools/quadro14L/Dockerfile`
  - `quadro14g:latest` — separate image for the 14G engine
  - `gqrs:latest` — built from `docker-biotools/gqrsMapper/Dockerfile` (compiles qgrs-cpp)
  - `onquadro-aligner:latest` — built from `docker-biotools/onquadroAligner/Dockerfile`
  - `viennarna:latest` — built from `docker-biotools/ViennaRNA/Dockerfile`
  - `eltetrado:latest` — built from `docker-biotools/eltetrado/Dockerfile`
- **docker-biotools** is a git submodule pointing to `https://github.com/mkdewe/docker-quadro`
  - `qgrs-cpp` is a nested submodule inside docker-biotools — requires `git submodule update --init` after pulling

## Deploy flow (server)

```bash
# Main app (always)
cd /home/G4Composer && ./deploy.sh

# After changes to docker-biotools Dockerfiles
cd /home/G4Composer/docker-biotools
git checkout main && git pull origin main
git submodule update --init
docker build -t quadro14l:latest quadro14L/
docker build -t gqrs:latest gqrsMapper/
docker build -t onquadro-aligner:latest onquadroAligner/
docker build -t viennarna:latest ViennaRNA/
docker build -t eltetrado:latest eltetrado/
```

The server may be in detached HEAD in the submodule — always `git checkout main` before `git pull`.

## Migration conventions

- Migration timestamps use format `YYYYMMDDHHMMSS` (e.g. `20260519100000_CleanupInpNames`)
- Data-only migrations (no schema change): the Designer `.cs` file is a copy of the previous Designer with updated class name and `[Migration(...)]` attribute
- `Down()` for destructive/lossy data migrations can be left empty with a comment explaining why
