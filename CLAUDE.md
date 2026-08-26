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
  - `quadro14n:latest` — built from `docker-biotools/quadro14N/Dockerfile`; **currently the active engine** (`Quadro:Version` = `14N`)
  - `quadro14l:latest` — built from `docker-biotools/quadro14L/Dockerfile`; kept for rollback
  - `quadro14g:latest` — separate image for the 14G engine
  - ⚠️ **quadro14L and quadro14N must be built with the repo root as build context** (`-f quadro14N/Dockerfile .`) — CYANA (`cyana-2.1/`) and Xplor-NIH (`xplor-nih-2.39/`) live at the root of docker-quadro and are shared by every quadro version, instead of being duplicated in each `quadro14*/bin/`.
  - `gqrs:latest` — built from `docker-biotools/gqrsMapper/Dockerfile` (compiles qgrs-cpp)
  - `onquadro-aligner:latest` — built from `docker-biotools/onquadroAligner/Dockerfile`
  - `viennarna:latest` — built from `docker-biotools/ViennaRNA/Dockerfile`
  - `eltetrado:latest` — built from `docker-biotools/eltetrado/Dockerfile`
  - `x3dna-dssr:latest` — built from `docker-biotools/x3dna-dssr/Dockerfile`; wraps **DSSR-Basic** (native G-tetrad/G4 detection). Licensed (not open source): the Dockerfile `COPY`s a `x3dna-dssr` binary you obtain once from Columbia Technology Ventures (free for academics, NIH grant R24GM153869) and drop into the build dir — it is `.gitignore`d and never committed. See that dir's README.md.
  - `dnatco:latest` — built from `docker-biotools/dnatco/Dockerfile` (DNATCO v5.0 offline CLI for NtC/CANA conformational analysis; `run.py` normalises input via gemmi then runs `rednatco.js`, emitting `*_extended.cif` + `summary.csv`/`summary.json`)
- **docker-biotools** is a git submodule pointing to `https://github.com/mkdewe/docker-quadro`
  - `qgrs-cpp` is a nested submodule inside docker-biotools — requires `git submodule update --init` after pulling

## Quadro engine: what `iteration` actually controls

`quadro14*.exe` is an awk script that computes nothing itself — it generates inputs for two
different minimizers and runs them:

| stage | program | space | steps | driven by `iteration`? |
|---|---|---|---|---|
| build-up, one per residue added in `path` order | CYANA | torsion angles | `iteration` each | **yes** |
| final CYANA pass | CYANA | torsion angles | 100, hard-wired in 14N | no |
| xplor pass 1 — tetrad core frozen | Xplor-NIH | Cartesian | `nstep=1000` | no |
| xplor pass 2 — everything released, planar+dihedral+NOE restraints | Xplor-NIH | Cartesian | `nstep=1000` | no |

So `iteration` decides **how good a starting structure Xplor receives**, not how good the
answer is — 2000 hard-wired Xplor steps follow regardless. More iterations is therefore *not*
monotonically better; measured on the 70 deposited examples, raising 14L's build-up from 50 to
300 moved convergence 43→45 (12 structures fixed, 10 broken).

### 14N vs 14L

- **14N does not know `iteration_steps`** — that was a local patch on top of 14L. It splits only
  the *final* CYANA pass into checkpoints, so all 14L frames shared one identical build-up
  (hard-wired at 50, because the backend only ever sent `iteration_steps`, never `iteration`).
- **14N runs N independent passes instead**, one per `IterationSteps` value, each with its own
  build-up depth → genuinely different models, and the best is picked. See `Quadro14NEngine`.
- 14N widens the alphabet: uppercase `T` = ribothymidine (`RT`), lowercase `u` = deoxyuridine
  (`DU`). 14L rejects both with `ERROR 2`. `QuadroInputValidator.AllowedChars` must be narrowed
  again if the engine is ever rolled back.
- 14L exits **2 on every run** — three apostrophes in `quadro14L.exe` close the awk program at
  line 859, so the shell parses the trailing 24 lines as shell code and chokes. Harmless only
  because the runner calls it via `ExecIgnore`. 14N exits 0.

## Configuration: what lives where

`deploy.sh` is committed at the repo root and is what the server runs (`/home/G4Composer/deploy.sh`).
Frontend and Docker images are rebuilt by it — do **not** commit `G4Composer.Server/wwwroot/`,
it is gitignored build output.

⚠️ **`/var/www/g4composer/appsettings.Production.json` exists only on the server.**
`dotnet publish` never overwrites it (it is not part of the project), so it silently outranks
everything in `appsettings.json`. On 2026-08-25 it pinned `Quadro:Version` to `14L` for a week
after the repo had moved to 14N — the standard run kept working while the alternative one died
in the wrong image, so the UI showed only one model and nothing logged an error. **Keep that
file down to server-only settings** (connection string, log levels); anything about the engine
belongs in `appsettings.json`.

`AlternativeExecutable` is configured **per engine**, inside `Quadro:Engines:<ver>`, because the
alternative binary runs in that engine's own image. `QuadroReadinessCheck` verifies at startup
that the active image really contains both binaries and reports it in `/health`
(`status: "degraded"` + `configProblem`).

## Curated examples (`StructureExamples.IsCurated`)

`tools/curate-examples.sh` picks one representative per **(Silva subtype × tetrad count)** bucket
— lowest `Etotal` across all frames of both variants wins — and flags it. Non-canonical examples
(`SilvaSubtypeId IS NULL`) are all flagged: they have no topology to represent. Nothing is
deleted, so re-running with new energies simply moves the flag.

```bash
cd /home/G4Composer
./tools/curate-examples.sh --dry-run   # policz brakujące, pokaż wybór, nie zapisuj
./tools/curate-examples.sh             # policz + zapisz flagę
./tools/curate-examples.sh --select    # tylko przelicz wybór z tego, co już w cache'u
```

Step 1 POSTs each example **individually** to `/api/quadro11/run` — that is the only path that
produces both variants (a multi-input batch runs the standard engine only) and the only one that
links the result to its `StructureExample`. It is idempotent: examples already cached with alt
frames are skipped, so an interrupted run resumes.

The examples browser defaults to the curated subset; the `representatives` / `all` button toggles
it. `?curatedOnly=` on `/api/structures/{groups,noncanonical,subtypes/{code}/examples}` drives it
and is **ignored when no example is curated yet**, so a server that has not run the script shows
all 70 instead of an unexplained empty list.

Running the script is also what makes examples load instantly: the PDB cache is filled lazily, so
before it runs, the first click on any structure computes it (≈6 passes × 2 variants) and only
later clicks are served from the database.

## Deploy flow (server)

```bash
# Main app (always)
cd /home/G4Composer && ./deploy.sh

# After changes to docker-biotools Dockerfiles
cd /home/G4Composer/docker-biotools
git checkout main && git pull origin main
git submodule update --init
# quadro14N / quadro14L: build context = repo root (shared cyana-2.1/ + xplor-nih-2.39/)
docker build -f quadro14N/Dockerfile -t quadro14n:latest .
docker build -f quadro14L/Dockerfile -t quadro14l:latest .
# pozostałe narzędzia: kontekst = własny podkatalog
docker build -t gqrs:latest gqrsMapper/
docker build -t onquadro-aligner:latest onquadroAligner/
docker build -t viennarna:latest ViennaRNA/
docker build -t eltetrado:latest eltetrado/
docker build -t dnatco:latest dnatco/
# x3dna-dssr needs the licensed `x3dna-dssr` binary dropped into x3dna-dssr/ first (see that dir's README.md)
docker build -t x3dna-dssr:latest x3dna-dssr/
```

The server may be in detached HEAD in the submodule — always `git checkout main` before `git pull`.

## ⚠️ `bool` columns land in PostgreSQL as `integer`

`AppDbContextModelSnapshot.cs` is generated locally, i.e. against **SQLite**, so every bool is
recorded as `b.Property<bool>("X").HasColumnType("INTEGER")`. The column type comes from the
**model**, so a new bool column becomes a real Postgres `integer` — deleting the `type:` argument
from `AddColumn` does *not* help (that was tried and failed on `IsCurated`).

The symptom is delayed and misleading: reads work (Npgsql maps integer→bool), but the first query
that puts the column in a SQL predicate dies with `42804: argument of WHERE must be type boolean`.
`IsTest` and `IsTheoretical` sat broken for months; `IsCurated` blew up `/api/structures/groups`
the day it first appeared in an `.AnyAsync(...)`.

**When adding a bool (or `DateTime`) column, pair it with a Postgres-only repair migration** —
`if (!migrationBuilder.IsNpgsql()) return;` then `ALTER COLUMN … TYPE boolean USING (… <> 0)`,
dropping and restoring the `DEFAULT` around it. Precedents:
`20260805130000_FixPdbCacheColumnTypes`, `20260826080000_FixStructureExampleBoolColumns`.

## Migration conventions

- Migration timestamps use format `YYYYMMDDHHMMSS` (e.g. `20260519100000_CleanupInpNames`)
- Data-only migrations (no schema change): the Designer `.cs` file is a copy of the previous Designer with updated class name and `[Migration(...)]` attribute
- `Down()` for destructive/lossy data migrations can be left empty with a comment explaining why
