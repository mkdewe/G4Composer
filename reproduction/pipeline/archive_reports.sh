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
cp work/data/qplex_scores.csv reports/
cp work/results/AP_violin.pdf figures/
echo "archived reports/ and figures/"
