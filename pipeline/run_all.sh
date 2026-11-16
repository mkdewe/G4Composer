#!/usr/bin/env bash
# Reproduce the manuscript's main dataset with engine 16 and heavy-atom RMSD.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
JOBS="${JOBS:-12}"

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

"$PYTHON" pipeline/fetch_references.py
"$PYTHON" pipeline/run_models.py --jobs "$JOBS" || echo "WARNING: some model builds failed"
"$PYTHON" pipeline/score_rmsd.py --output work/data/scores.csv || echo "WARNING: some models failed to score"
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

"$PYTHON" pipeline/summarize.py
