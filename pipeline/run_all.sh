#!/usr/bin/env bash
# Reproduce the manuscript's main dataset with engine 16 and heavy-atom RMSD.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
JOBS="${JOBS:-12}"

cd "$ROOT"

python3 pipeline/fetch_references.py
python3 pipeline/run_models.py --jobs "$JOBS" || echo "WARNING: some model builds failed"
python3 pipeline/score_rmsd.py --output work/data/scores.csv || echo "WARNING: some models failed to score"
python3 pipeline/collect.py
python3 pipeline/cluster_ap.py --inputs inputs/current_548 --output work/results/AP_unweighted

python3 pipeline/export_plot_data.py \
    --results work/results/AP_unweighted_548 \
    --measurements work/data \
    --output work/results/plot_data.csv
python3 pipeline/visualize.py \
    --results work/results/AP_unweighted_548 \
    --measurements work/data \
    --output work/results/plots || true

python3 pipeline/summarize.py
