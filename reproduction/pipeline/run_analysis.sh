#!/usr/bin/env bash
# Recompute RMSD/lDDT/energy analyses from the archived models.
# CYANA/Xplor are not needed; OpenStructure 2.12.0 (Docker) is required for lDDT.
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

"$PYTHON" pipeline/check_models.py

"$PYTHON" pipeline/fetch_references.py
"$PYTHON" pipeline/score_qplex.py
"$PYTHON" pipeline/score_rmsd.py --output work/data/scores.csv
"$PYTHON" pipeline/collect.py
"$PYTHON" pipeline/prepare_lddt.py --jobs "$JOBS"
"$PYTHON" pipeline/score_lddt.py --jobs "$JOBS"
"$PYTHON" pipeline/score_qplex_lddt.py
"$PYTHON" pipeline/cluster_ap.py --inputs inputs/current_548 --output work/results/AP_unweighted

"$PYTHON" pipeline/export_plot_data.py \
    --results work/results/AP_unweighted_548 \
    --measurements work/data \
    --output work/results/plot_data.csv
"$PYTHON" pipeline/plot_violin.py \
    --rmsd work/data/G16_rmsd.txt \
    --lddt work/data/G16_lddt.txt \
    --energy work/data/G16_energy_popr.txt \
    --results work/results/AP_unweighted_548 \
    --output figures/AP_violin.pdf
"$PYTHON" pipeline/plot_pooled.py

"$PYTHON" pipeline/summarize.py
"$PYTHON" pipeline/check_paths.py
