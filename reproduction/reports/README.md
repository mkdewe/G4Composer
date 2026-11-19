# Archived manuscript outputs

Reference snapshots produced from the committed models:

| file | content |
|---|---|
| `numbers.json` | pooled and representative statistics for all 6,516 G4Composer models |
| `table1.csv` | fifteen-target comparison with RMSD and all-atom lDDT for 3D-NuS-Qplex and for the RMSD-, lDDT-, and energy-selected G4Composer models |
| `S1_table.csv` | representative subgroup RMSD/lDDT values for the models selected by minimum RMSD, maximum lDDT, and minimum energy |
| `S2_table.csv` | pooled accuracy, iteration-depth, mode and `-20*N` energy-threshold summaries |
| `S3_table.csv` | FoldBench targets with published lDDT and G4Composer RMSD/lDDT for the RMSD-, lDDT-, and energy-selected models |
| `rmsd_scores.csv` | per-model heavy-atom RMSD and shared-heavy-atom count |
| `lddt_scores.csv` | one official OpenStructure 2.12.0 all-atom lDDT score per G4Composer model |
| `G16_lddt.txt` | archived 12-column lDDT analysis table (internal data filename) |
| `qplex_scores.csv` | heavy-atom RMSD and atom counts for the fifteen 3D-NuS-Qplex models |
| `qplex_lddt.csv` | official OpenStructure 2.12.0 all-atom lDDT scores for the 3D-NuS-Qplex models |

The analysis scripts write working copies to `work/reports/` and `work/data/`;
these files are the archived snapshots. Run `bash pipeline/run_analysis.sh`
from `reproduction/` to recompute the analyses, then
`bash pipeline/archive_reports.sh` to refresh this directory.
