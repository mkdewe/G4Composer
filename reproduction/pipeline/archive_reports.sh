#!/usr/bin/env bash
# Copy the latest analysis outputs into the archived snapshot directories.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

cd "$ROOT"
mkdir -p reports figures
cp work/reports/numbers.json reports/
cp work/reports/table1.csv reports/
cp work/reports/S1_table.csv reports/
cp work/reports/S2_table.csv reports/
cp work/reports/S3_table.csv reports/
cp work/reports/S4_table.csv reports/
cp work/data/scores.csv reports/rmsd_scores.csv
cp work/data/qplex_scores.csv reports/
cp work/data/qplex_lddt.csv reports/
cp work/data/lddt_scores.csv reports/
cp work/results/AP_unweighted_548/assignments.csv reports/
cp work/results/AP_unweighted_548/clusters_ap.csv reports/
cp work/results/AP_unweighted_548/representatives_ap.txt reports/
cp work/results/AP_unweighted_548/summary.json reports/cluster_summary.json
cp work/reports/lddt_reference_changes.csv reports/
cp work/data/G16_lddt.txt reports/
# Figures are generated directly into figures/ by plot_violin.py and plot_pooled.py.
echo "archived reports/ and figures/"
