# G4Composer 1.0 manuscript reproduction

Pipeline and archived models for

> Popenda, Dziel, Sarzynska, Szachniuk, Zok.
> *G4Composer: topology-guided 3D structure modeling of canonical and
> non-canonical G-quadruplexes.*

The repository contains the full pipeline, all **6,516 G4Composer models** and
the **15 3D-NuS-Qplex comparison models**, together with their coordinates,
energy summaries and archived RMSD/lDDT analyses. Rebuilding the models from
scratch requires the `g4composer:latest` image, which embeds licensed CYANA and
Xplor-NIH. Rescoring the archived models requires neither of those programs.

## Layout

```text
inputs/            548 input records and their manifest
qplex/             15 3D-NuS-Qplex models, manifest and provenance
references/        pinned RCSB manifest (mmCIF files are downloaded into work/)
models/            6,516 G4Composer PDBs and 6,516 energy summaries
reports/           archived statistics, tables, cluster assignments and per-model scores
figures/           archived main and supplementary PDF figures
pipeline/          generation, scoring, plotting and reporting scripts
work/              generated data and working outputs (git-ignored)
```

## Analysis from the archived models

Requirements are Python 3, PyMOL on `PATH`, and Docker for the **official
OpenStructure 2.12.0 image**
(`registry.scicore.unibas.ch/schwede/openstructure:2.12.0`). No CYANA or
Xplor-NIH installation is needed. Network access is required when downloading
references or pulling the OpenStructure image.

From this directory:

```bash
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt

JOBS=12 bash pipeline/run_analysis.sh
```

`run_analysis.sh` checks all 6,504 model/energy pairs of the 542 analyzed inputs,
downloads the pinned RCSB
references, computes all-heavy-atom RMSD with PyMOL, computes all-atom lDDT
with OpenStructure 2.12.0, reruns Affinity Propagation, and regenerates the
current tables, statistics and figures. It never starts a G4Composer model
build.
`OPENSTRUCTURE_IMAGE` can override the image name for a locally mirrored copy;
the pinned default is the official image above.

For the focused Table 1 lDDT refresh, after the full analysis tables and AP
clusters exist, run:

```bash
JOBS=12 bash pipeline/run_table1.sh
```

This rescores the 15 × 12 G4Composer models and the 15 3D-NuS-Qplex models at the Table
1 targets, retains the other archived per-model lDDT rows, and refreshes the
summary reports. Run `run_analysis.sh` first in a fresh checkout so that the
whole-dataset RMSD, energy and clustering inputs are available.

## Rebuilding from scratch

Model generation additionally requires Docker with the locally built
`g4composer:latest` image and licensed CYANA 2.1 and Xplor-NIH 2.39. Build the
image from the parent repository with `tools/build.sh`. Set
`G4COMPOSER_IMAGE` to use another local tag.

```bash
# build missing models, then run all analyses
JOBS=12 bash pipeline/run_all.sh

# rebuild all 6,516 models even if present, then run all analyses
FORCE=1 JOBS=12 bash pipeline/run_all.sh
```

`run_all.sh` first calls `check_models.py`. When the archive is complete it skips
generation and runs only the analysis. If models are missing, only missing
model/energy pairs are built; `FORCE=1` bypasses that reuse. The subsequent
steps are the same RMSD, lDDT, clustering, plotting and summary steps as in
`run_analysis.sh`.

To verify completeness without starting Docker:

```bash
python3 pipeline/run_models.py --no-build
```

## Protocol

**Engine.** G4Composer 1.0 in the `g4composer:latest` image includes the `sugar`
keyword fix. Each input is built at six CYANA iteration depths (30, 50, 70, 100,
150 and 300) in standard and alternative modes, giving 12 models per input.
G4Composer is single-pass, so the pipeline generates the alternative input:
`orient`, `rise` and `twist` signs are inverted and positions 2 and 4 are
exchanged in every `path` token.

**RMSD.** Hydrogens are removed from both the model and the reference. The
reference is restricted to the author chain named by the input record
(`auth_asym_id`, which PyMOL uses as its chain identifier), not to whichever
chain PyMOL would prefer. The two phosphate-oxygen aliases `OP1`/`O1P` and
`OP2`/`O2P` are normalised on both structures. PyMOL `align` is used only to
establish the reference-to-model residue mapping; the model is then superposed,
and the RMSD is calculated, over all heavy atoms present in both mapped
residues, including phosphate oxygens. The per-model table records the number
of common heavy atoms and how many are phosphate oxygens.

**All-atom lDDT.** Every G4Composer model and every 3D-NuS-Qplex comparison model is
scored with `compare-structures --lddt` in the **official OpenStructure 2.12.0
Docker image**. The prepared reference is the requested experimental chain,
mapped to model chain A. Reference and model sequences are aligned, and only
non-standard reference residue names are replaced by their model counterpart;
canonical reference bases are never renamed by residue number. The lDDT values
are the global all-atom scores returned by OpenStructure; they are not
derivatives of RMSD and are not estimated with an in-house implementation.

**3D-NuS-Qplex.** The fifteen 3D-NuS-Qplex models in `qplex/` are rescored with the same
RMSD and all-atom lDDT protocols, not taken from published values. Their
`ADE`/`GUA`/`THY`/`CYT` residue names and blank chain ID are standardised for
both scoring paths. Hydrogens are removed for RMSD. The corrected 2KF8 and
5LQH models now match their 22-nt targets. PDB entry 9UK8 contains 23-nt
(assemblies 1-A and 2-B) and 24-nt (3-C and 4-D) observed variants; Table 1
uses the 23-nt assembly1-A chain, matching the 23-nt Qplex model.

**Energy and threshold.** Each `_energy.txt` contains the final Xplor-NIH
`Etotal` summary. Energy is used to compare models of the same input, and the
12-column energy table includes the threshold **`-20*N`**, where `N` is the
number of residues in that input. The supplementary threshold analysis reports
below/around/above groups with a 10% band around this threshold, alongside the
pooled RMSD and lDDT statistics.

## Archived models

`models/` contains all 6,516 coordinate files and all 6,516 energy files: 12
models for each of the 543 buildable inputs (the 542 analyzed inputs plus the
excluded 8DUT chain B duplex partner). Naming is
`<key>_<depth>_<mode>.pdb` / `_energy.txt`; see
[`models/README.md`](models/README.md) for details.

The archived files are sanitized: PDB files have no run-specific
`REMARK FILENAME`/`REMARK DATE` lines, and energy files are reduced to the
`Etotal` summary under a G4Composer 1.0 header. No software or parameter files
from CYANA/Xplor-NIH are included. Coordinates and energies are research outputs
generated under the authors' licences.

`pipeline/sanitize.py` applies this formatting after a rebuild.
`pipeline/check_paths.py` fails if an archived artifact contains a host-specific
absolute path.

## Inputs and references

- `inputs/current_548/` — 548 records. Six of them are excluded before
  clustering and in every downstream analysis (see below), leaving **542
  analyzed inputs**.
  `inputs/manifest.csv` pins every file by SHA256.
- `inputs/excluded_inputs.txt` — the 23 records removed from 571 candidates.
- `references/manifest.csv` — per key, the RCSB URL and SHA256 of the
  decompressed mmCIF. `fetch_references.py` refuses a file whose hash differs.
- `qplex/` — the fifteen 3D-NuS-Qplex models, SHA256 manifest and provenance.

Six inputs are excluded from clustering and from all analyses
(`MANUAL_EXCLUSIONS` and `UNBUILDABLE` in `pipeline/common.py`): the two
374-nt synthetic RNA origami constructs `7zj4-assembly1-E` and
`7zj5-assembly1-E`, the three mixed DNA/RNA records `4ni7-assembly1-B`,
`4ni9-assembly1-B` and `4ni9-assembly1-D`, which cannot be built
(`ERROR 106 bledna para`), and `8dut-assembly1-B`, the duplex-partner chain of
a dimeric duplex–G4–duplex assembly whose 15+15 nt duplex arms are unconstrained
overhangs in the record. Thus the pooled analysis covers 6,504 models from 542
inputs; the representative analysis covers 322 representatives.

## Current outputs

| path | content |
|---|---|
| `work/data/scores.csv` | per-model all-heavy-atom RMSD, common-heavy-atom count, and phosphate-oxygen count |
| `work/data/lddt_scores.csv` | per-model official OpenStructure all-atom lDDT |
| `work/data/G16_rmsd.txt` | 12-column G4Composer RMSD table (internal data filename) |
| `work/data/G16_lddt.txt` | 12-column G4Composer lDDT table (internal data filename) |
| `work/data/G16_energy_popr.txt` | 12-column G4Composer energy table and `-20*N` threshold (internal data filename) |
| `work/data/qplex_scores.csv` | all-heavy-atom RMSD and atom counts for the fifteen 3D-NuS-Qplex models |
| `work/data/qplex_lddt.csv` | all-atom lDDT of the fifteen 3D-NuS-Qplex models |
| `work/results/AP_unweighted_542/` | Affinity Propagation clusters |
| `work/results/plot_data.csv` | per-representative exported measurements |
| `work/reports/numbers.json` | pooled and representative manuscript statistics |
| `work/reports/table1.csv` | fifteen-target RMSD/lDDT comparison |
| `work/reports/S1_table.csv` | representative subgroup table |
| `work/reports/S2_table.csv` | pooled accuracy, depth, mode and energy-threshold table |
| `work/reports/S3_table.csv` | FoldBench comparison table |
| `work/reports/S4_table.csv` | sequence-length correlations and quartile-bin statistics |
| `figures/` | main RMSD/lDDT/energy violin and four supplementary PDF figures |
| `reports/` | archived snapshots: table1.csv, numbers.json, S1_table.csv–S4_table.csv, rmsd_scores.csv, lddt_scores.csv, qplex_scores.csv, qplex_lddt.csv, G16_lddt.txt, assignments.csv, clusters_ap.csv, representatives_ap.txt, cluster_summary.json, lddt_reference_changes.csv, README.md |

Refresh the archived report and figure snapshots with
`bash pipeline/archive_reports.sh` after recomputing the current outputs.

## Path policy

Committed artifacts use only repository-relative names. Runtime code may
resolve absolute paths locally, but must not write them into PDB, energy, JSON
or CSV outputs. Run `python3 pipeline/check_paths.py` to verify the archive.

## Licensing

CYANA and Xplor-NIH are not included in the reproduction archive. CYANA is
commercial; Xplor-NIH is free for non-profit institutions but cannot be
redistributed. The `g4composer:latest` image must be built locally with each
user's own licensed copies. See the parent repository's `docs/THIRD-PARTY.md`.
