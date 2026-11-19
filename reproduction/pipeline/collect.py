#!/usr/bin/env python3
"""Collect the per-model scores into the 12-column tables used by the analysis.

Column order is 30n, 30a, 50n, 50a, 70n, 70a, 100n, 100a, 150n, 150a, 300n,
300a.  Rows for inputs with missing models (the mixed DNA/RNA records) are
omitted; the count is reported.
"""

from __future__ import annotations

import argparse
import csv
from pathlib import Path

import common

ORDER = [(depth, mode) for depth in common.DEPTHS for mode in common.MODES]
HEADER = "key " + " ".join(f"{depth}{mode}" for depth, mode in ORDER)


def load_scores(path: Path) -> dict[str, dict[tuple[int, str], float]]:
    out: dict[str, dict[tuple[int, str], float]] = {}
    with path.open(encoding="utf-8") as stream:
        for row in csv.DictReader(stream):
            if row.get("rmsd_heavy"):
                out.setdefault(row["key"], {})[(int(row["depth"]), row["mode"])] = float(row["rmsd_heavy"])
    return out


def write_rmsd(path: Path, rows: list[tuple[str, list[float]]]) -> int:
    lines = [HEADER]
    for key, values in rows:
        lines.append(f"{key:<24} " + " ".join(f"{value:10.3f}" for value in values) + f" | {min(values):8.3f} {max(values):8.3f}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return len(rows)


def write_energy(path: Path, rows: list[tuple[str, list[float], int]]) -> int:
    lines = [HEADER]
    for key, values, n_res in rows:
        threshold = -20.0 * n_res
        below = sum(1 for value in values if value <= threshold)
        argmin = f"{ORDER[values.index(min(values))][0]}{ORDER[values.index(min(values))][1]}"
        argmax = f"{ORDER[values.index(max(values))][0]}{ORDER[values.index(max(values))][1]}"
        lines.append(
            f"{key:<24} "
            + " ".join(f"{value:10.3f}" for value in values)
            + f" | {n_res:4d} {threshold:8.0f} {below:2d} {min(values):12.3f} {argmin:>5s} {max(values):12.3f} {argmax:>5s}"
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return len(rows)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--keys-file", type=Path, help="default: every measured input")
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--scores", type=Path, default=common.DATA / "scores.csv")
    parser.add_argument("--models-dir", type=Path, default=common.MODELS)
    parser.add_argument("--output-dir", type=Path, default=common.DATA)
    parser.add_argument("--rmsd-name", default="G16_rmsd.txt")
    parser.add_argument("--energy-name", default="G16_energy_popr.txt")
    args = parser.parse_args()

    records = {
        record["key"]: record
        for record in map(common.describe_input, sorted(args.inputs_dir.glob("*.inp")))
    }
    if args.keys_file:
        keys = [line.strip() for line in args.keys_file.read_text(encoding="utf-8").splitlines() if line.strip()]
    else:
        keys = [key for key, record in records.items() if record["file"] not in {"7zj4-assembly1-E.inp", "7zj5-assembly1-E.inp"}]

    scores = load_scores(args.scores)
    rmsd_rows: list[tuple[str, list[float]]] = []
    energy_rows: list[tuple[str, list[float], int]] = []
    incomplete: list[str] = []
    for key in keys:
        values = scores.get(key, {})
        rmsd = [values.get(item) for item in ORDER]
        energy = [common.parse_etotal(common.energy_path(key, depth, mode, args.models_dir)) for depth, mode in ORDER]
        if any(value is None for value in rmsd) or any(value is None for value in energy):
            incomplete.append(key)
            continue
        rmsd_rows.append((key, rmsd))
        energy_rows.append((key, energy, len(records[key]["sequence"])))

    args.output_dir.mkdir(parents=True, exist_ok=True)
    n_rmsd = write_rmsd(args.output_dir / args.rmsd_name, rmsd_rows)
    n_energy = write_energy(args.output_dir / args.energy_name, energy_rows)
    print(f"{n_rmsd} rows -> {args.output_dir / args.rmsd_name}")
    print(f"{n_energy} rows -> {args.output_dir / args.energy_name}")
    if incomplete:
        print(f"incomplete inputs omitted: {len(incomplete)}: {', '.join(incomplete)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
