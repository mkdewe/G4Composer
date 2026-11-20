# Changelog

All notable changes to G4Composer are recorded here.

## [Unreleased]

### Reproduction corrections

- RMSD is now superposed and calculated over every heavy atom present in both
  mapped residues, including phosphate oxygens. PyMOL `align` is used only for
  residue mapping.
- lDDT reference preparation now aligns reference and model sequences and
  renames only modified reference residues. Canonical bases are never replaced
  by residue number.
- `run_analysis.sh` and `run_table1.sh` force OpenStructure rescoring after a
  reference change.
- Affinity Propagation assignments, clusters, representatives and run summary
  are archived in `reproduction/reports/`.
- The supplementary energy-threshold panel no longer has overlapping tick
  labels, and the reproduction documentation describes the corrected scoring
  protocols.
- The 2KF8 and 5LQH Qplex models were replaced with complete 22-nt models, and
  their manifest hashes were updated.
- The 9UK8 Table 1 target was pinned to the 23-nt assembly1-A chain so that the
  Qplex and G4Composer comparisons use the same observed sequence; the PDB
  entry also contains 23-nt assembly2-B and 24-nt assemblies 3-C/4-D.
- A sequence-length analysis now reports Spearman correlations and
  sequence-length quartile-bin accuracy/energy statistics for the 323
  representatives, archived as `S4_table.csv` and `S4_length_dependence.pdf`.
- The FoldBench comparison now covers all nine DNA and three RNA G4 targets;
  `7ZJ4` is listed as an unmodeled synthetic origami construct, with empty
  G4Composer cells.
- `score_qplex_lddt.py` now deletes any pre-existing per-target lDDT JSON
  before the Docker call and fails explicitly on a nonzero container exit, so
  stale scores can no longer be silently reused.

## [1.0]

G4Composer 1.0 is the public name and version of the engine used for the
manuscript measurements.

### Engine

- The executable is `engine/g4composer.awk`, with its tetrad library in
  `engine/tetrad.lib`.
- Each invocation builds one handedness. The caller generates and runs a
  mirrored input when the opposite-handed stack is required; `--no-mirror` is
  accepted as a compatibility no-op.
- The parser accepts the documented `sugar` keyword and retains `shugar` as an
  input-compatibility alias. Unrecognised keywords are rejected.
- The RNA/DNA alphabet, staged CYANA build-up, two-step Xplor-NIH refinement and
  energy-summary outputs are documented in `docs/`.

### Container and wrappers

- The default image tags are `g4composer:latest` and `g4composer:1.0`.
- The command-line interface, entrypoint, build/run wrappers and environment
  variables use the G4Composer name.
- CYANA 2.1 and Xplor-NIH 2.39 remain externally licensed dependencies supplied
  through BuildKit named contexts.

### Reproduction

- The archive contains all 6,516 G4Composer models and the fifteen
  3D-NuS-Qplex comparison models.
- The analysis reports heavy-atom RMSD and official OpenStructure 2.12.0
  all-atom lDDT, with Xplor-NIH energy summaries and the `-20*N` threshold.
- `run_all.sh`, `run_analysis.sh` and `run_table1.sh` drive the current
  generation and analysis workflow; archived reports and all four figures are
  documented in `reproduction/`.
