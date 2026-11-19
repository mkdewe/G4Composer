# Changelog

All notable changes to G4Composer are recorded here.

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
