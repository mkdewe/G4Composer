#!/usr/bin/env bash
# Reproduce the Table 1 default-parameter branch (nominal 3.4 A / 29 deg).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
JOBS="${JOBS:-12}"

cd "$ROOT"

python3 pipeline/make_table1_inputs.py
python3 pipeline/run_models.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --models-dir work/models_table1 \
    --runs-dir work/runs_table1 \
    --logs-dir work/logs_table1 \
    --jobs "$JOBS"
python3 pipeline/score_rmsd.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --models-dir work/models_table1 \
    --output work/data/table1_scores.csv
python3 pipeline/collect.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --scores work/data/table1_scores.csv \
    --models-dir work/models_table1 \
    --output-dir work/data \
    --rmsd-name table1_rmsd.txt \
    --energy-name table1_energy_popr.txt
python3 pipeline/summarize.py
