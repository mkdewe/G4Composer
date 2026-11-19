#!/usr/bin/env bash
# Rescore the fifteen Table 1 G4Composer and 3D-NuS-Qplex models.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
JOBS="${JOBS:-12}"

if [ -z "${PYTHON:-}" ]; then
    if [ -x "$ROOT/.venv/bin/python" ]; then PYTHON="$ROOT/.venv/bin/python"; else PYTHON=python3; fi
fi

if command -v nix >/dev/null 2>&1; then
    ZLIB="$(nix eval --raw nixpkgs#zlib.outPath 2>/dev/null || true)"
    CCLIB="$(nix eval --raw nixpkgs#stdenv.cc.cc.lib.outPath 2>/dev/null || true)"
    export LD_LIBRARY_PATH="${ZLIB:+$ZLIB/lib:}${CCLIB:+$CCLIB/lib:}${LD_LIBRARY_PATH:-}"
fi

cd "$ROOT"
KEYS="$("$PYTHON" pipeline/table1.py)"

"$PYTHON" pipeline/fetch_references.py --keys "$KEYS"
"$PYTHON" pipeline/score_qplex.py
"$PYTHON" pipeline/prepare_lddt.py --keys "$KEYS" --jobs "$JOBS"
"$PYTHON" pipeline/score_lddt.py --keys "$KEYS" --jobs "$JOBS"
"$PYTHON" pipeline/score_qplex_lddt.py
"$PYTHON" pipeline/summarize.py
"$PYTHON" pipeline/check_paths.py
