# Archived manuscript figures

All figures are generated from the committed models by the analysis pipeline:

| file | content |
|---|---|
| `AP_violin.pdf` | best RMSD, best lDDT and energy-selected energy per nucleotide for the representatives (main text) |
| `S1_pooled_accuracy.pdf` | pooled RMSD and lDDT distributions and their joint distribution for all 6,516 models |
| `S2_energy_threshold.pdf` | energy per nucleotide, `-20*N` threshold groups and accuracy correlations |
| `S3_iteration_depth.pdf` | RMSD and lDDT sensitivity to CYANA depth and build mode |
| `S4_length_dependence.pdf` | best RMSD, best lDDT and minimum energy per nucleotide versus input sequence length, with Spearman correlations and quartile bins |

`plot_violin.py`, `plot_pooled.py` and `length_analysis.py` write these PDFs directly to `figures/`.
