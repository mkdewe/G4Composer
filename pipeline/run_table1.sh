#!/usr/bin/env bash
# Reproduce the Table 1 default-parameter branch (nominal 3.4 A / 29 deg).
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

"$PYTHON" pipeline/make_table1_inputs.py
"$PYTHON" pipeline/run_models.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --models-dir work/models_table1 \
    --runs-dir work/runs_table1 \
    --logs-dir work/logs_table1 \
    --jobs "$JOBS"
"$PYTHON" pipeline/score_rmsd.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --models-dir work/models_table1 \
    --output work/data/table1_scores.csv
"$PYTHON" pipeline/collect.py \
    --inputs-dir work/inputs/table1 \
    --keys-file work/inputs/table1_keys.txt \
    --scores work/data/table1_scores.csv \
    --models-dir work/models_table1 \
    --output-dir work/data \
    --rmsd-name table1_rmsd.txt \
    --energy-name table1_energy_popr.txt
"$PYTHON" pipeline/summarize.py
