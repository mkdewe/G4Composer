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
  - `dnatco:latest` — built from `docker-biotools/dnatco/Dockerfile` (DNATCO v5.0 offline CLI for NtC/CANA conformational analysis; `run.py` normalises input via gemmi then runs `rednatco.js`, emitting `*_extended.cif` + `summary.csv`/`summary.json`)
  - `openmm-utils:latest` — built from `docker-biotools/openmmUtils/Dockerfile` (wraps `tzok/openmm-utils`; Amber OL15 DNA / OL3 RNA with electrostatics OFF; reads a PDB on stdin, prints potential energy BEFORE and AFTER minimization + optional minimized PDB). git-clones upstream HEAD → rebuild with `--no-cache`.
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
# onquadro-aligner and openmm-utils git-clone upstream HEAD — use --no-cache to pick up new versions
docker build --no-cache -t onquadro-aligner:latest onquadroAligner/
docker build -t viennarna:latest ViennaRNA/
docker build -t eltetrado:latest eltetrado/
docker build -t dnatco:latest dnatco/
docker build --no-cache -t openmm-utils:latest openmmUtils/
```

The server may be in detached HEAD in the submodule — always `git checkout main` before `git pull`.

## Migration conventions

- Migration timestamps use format `YYYYMMDDHHMMSS` (e.g. `20260519100000_CleanupInpNames`)
- Data-only migrations (no schema change): the Designer `.cs` file is a copy of the previous Designer with updated class name and `[Migration(...)]` attribute
- `Down()` for destructive/lossy data migrations can be left empty with a comment explaining why
