# Archived manuscript outputs

Reference snapshots produced from the committed models:

| file | content |
|---|---|
| `numbers.json` | pooled and representative statistics for all 6,516 G4Composer models |
| `table1.csv` | fifteen-target comparison with RMSD and all-atom lDDT for 3D-NuS-Qplex and for the RMSD-, lDDT-, and energy-selected G4Composer models |
| `S1_table.csv` | representative subgroup RMSD/lDDT values for the models selected by minimum RMSD, maximum lDDT, and minimum energy |
| `S2_table.csv` | pooled accuracy, iteration-depth, mode and `-20*N` energy-threshold summaries |
| `S3_table.csv` | all nine DNA and three RNA FoldBench G4 targets, with published lDDT and G4Composer RMSD/lDDT for the RMSD-, lDDT-, and energy-selected models; `7ZJ4` is an unmodeled synthetic RNA origami construct, so its G4Composer cells are empty |
| `S4_table.csv` | sequence-length correlations and quartile-bin accuracy/energy statistics for the 323 representatives |
| `rmsd_scores.csv` | per-model all-heavy-atom RMSD, common-heavy-atom count, and phosphate-oxygen count |
| `lddt_scores.csv` | one official OpenStructure 2.12.0 all-atom lDDT score per G4Composer model; `status=reused` means the score was retained from a previous full scoring pass, `status=ok` means it was recomputed in the current pass |
| `G16_lddt.txt` | archived 12-column lDDT analysis table (internal data filename) |
| `qplex_scores.csv` | all-heavy-atom RMSD and atom counts for the fifteen 3D-NuS-Qplex models |
| `qplex_lddt.csv` | official OpenStructure 2.12.0 all-atom lDDT scores for the 3D-NuS-Qplex models |
| `assignments.csv` | per-input Affinity Propagation bucket, cluster, and representative assignment |
| `clusters_ap.csv` | Affinity Propagation clusters with members and rise/twist spread |
| `representatives_ap.txt` | the 327 clustering representatives, including the two synthetic constructs |
| `cluster_summary.json` | Affinity Propagation run statistics and library versions |
| `lddt_reference_changes.csv` | modified reference residues renamed to their model counterparts; canonical bases are not listed because they are never renamed |

The analysis scripts write working copies to `work/reports/` and `work/data/`;
these files are the archived snapshots. Run `bash pipeline/run_analysis.sh`
from `reproduction/` to recompute the analyses, then
`bash pipeline/archive_reports.sh` to refresh this directory.
