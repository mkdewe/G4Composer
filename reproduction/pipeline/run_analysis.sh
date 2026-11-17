#!/usr/bin/env bash
# Recompute all analyses from the archived models, without CYANA/Xplor or Docker.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

if [ -z "${PYTHON:-}" ]; then
    if [ -x "$ROOT/.venv/bin/python" ]; then PYTHON="$ROOT/.venv/bin/python"; else PYTHON=python3; fi
fi

# NixOS: the manylinux wheels need the Nix zlib/libstdc++ runtimes.
if command -v nix >/dev/null 2>&1; then
    ZLIB="$(nix eval --raw nixpkgs#zlib.outPath 2>/dev/null || true)"
    CCLIB="$(nix eval --raw nixpkgs#stdenv.cc.cc.lib.outPath 2>/dev/null || true)"
    export LD_LIBRARY_PATH="${ZLIB:+$ZLIB/lib:}${CCLIB:+$CCLIB/lib:}${LD_LIBRARY_PATH:-}"
fi

cd "$ROOT"

"$PYTHON" pipeline/check_models.py
"$PYTHON" pipeline/check_models.py --table1

"$PYTHON" pipeline/fetch_references.py
"$PYTHON" pipeline/score_qplex.py

"$PYTHON" pipeline/score_rmsd.py --output work/data/scores.csv
"$PYTHON" pipeline/collect.py
"$PYTHON" pipeline/cluster_ap.py --inputs inputs/current_548 --output work/results/AP_unweighted

"$PYTHON" pipeline/export_plot_data.py \
    --results work/results/AP_unweighted_548 \
    --measurements work/data \
    --output work/results/plot_data.csv
"$PYTHON" pipeline/plot_violin.py \
    --rmsd work/data/G16_rmsd.txt \
    --energy work/data/G16_energy_popr.txt \
    --results work/results/AP_unweighted_548 \
    --output work/results/AP_violin.pdf

"$PYTHON" pipeline/make_table1_inputs.py
"$PYTHON" pipeline/score_rmsd.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --models-dir models_table1 \
    --output work/data/table1_scores.csv
"$PYTHON" pipeline/collect.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --scores work/data/table1_scores.csv \
    --models-dir models_table1 \
    --output-dir work/data \
    --rmsd-name table1_rmsd.txt \
    --energy-name table1_energy_popr.txt

"$PYTHON" pipeline/summarize.py
"$PYTHON" pipeline/check_paths.py
