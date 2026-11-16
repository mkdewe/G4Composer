#!/usr/bin/env python3
"""Compute every quantitative result reported in the manuscript.

Writes ``work/reports/numbers.json`` with the headline statistics,
``work/reports/S1_table.csv`` with the subgroup table, and
``work/reports/table1.csv`` with the G4Composer columns of Table 1.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
from pathlib import Path

import numpy as np
from scipy.stats import binomtest, spearmanr

import common

MANUAL_EXCLUSIONS = {"7zj4-assembly1-E.inp", "7zj5-assembly1-E.inp"}

# 3D-NuS-Qplex column of Table 1.  These are the published comparator values and
# are not regenerated here.
QPLEX = {
    "1KF1": 5.005, "5LQH": 6.209, "2MBJ": 7.420, "2KF8": 7.102, "2MFT": 5.312,
    "1HUT": 5.892, "5OPH": 4.539, "1HAO": 4.959, "9UK8": 4.502, "2HY9": 6.715,
    "2LOD": 6.352, "6L92": 5.424, "2MFU": 6.576, "6R9K": 8.082, "6R9L": 7.845,
}
TABLE1_KEYS = {
    "1KF1": "1kf1_assembly1_A", "5LQH": "5lqh_assembly1_A", "2MBJ": "2mbj_assembly1_A",
    "2KF8": "2kf8_assembly1_A", "2MFT": "2mft_assembly1_A", "1HUT": "1hut_assembly1_D",
    "5OPH": "5oph_assembly1_A", "1HAO": "1hao_assembly1_D", "9UK8": "9uk8_assembly3_C",
    "2HY9": "2hy9_assembly1_1", "2LOD": "2lod_assembly1_A", "6L92": "6l92_assembly1_A",
    "2MFU": "2mfu_assembly1_A", "6R9K": "6r9k_assembly1_A", "6R9L": "6r9l_assembly1_A",
}


def load_table(path: Path) -> dict[str, list[float]]:
    out: dict[str, list[float]] = {}
    if not path.exists():
        return out
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines()[1:]:
        if not line.strip() or "|" not in line:
            continue
        cells = line.split("|")[0].split()
        if len(cells) < 13:
            continue
        out[cells[0]] = [float(value) for value in cells[1:13]]
    return out


def summary(values) -> dict:
    array = np.array(values, dtype=float)
    q1, median, q3 = np.quantile(array, [0.25, 0.5, 0.75])
    iqr = q3 - q1
    return dict(
        n=int(array.size),
        minimum=float(array.min()),
        q1=float(q1),
        median=float(median),
        q3=float(q3),
        maximum=float(array.max()),
        whisker_low=float(array[array >= q1 - 1.5 * iqr].min()),
        whisker_high=float(array[array <= q3 + 1.5 * iqr].max()),
    )


def load_inputs(path: Path) -> list[dict]:
    rows = []
    for file in sorted(path.glob("*.inp")):
        if file.name in MANUAL_EXCLUSIONS:
            continue
        fields = dict(
            line.partition(" ")[::2]
            for line in file.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        )
        sequence = fields["sequence"]
        structure = fields["structure"]
        tetrads = structure.count("^") // 4 if "^" in structure else sum(ch.isupper() for ch in structure) // 4
        kind = "DNA" if sequence == sequence.lower() else ("RNA" if sequence == sequence.upper() else "chimeric")
        rows.append(dict(file=file.name, key=file.stem.replace("-", "_"), pdb=file.name[:4].upper(), kind=kind, tetrads=tetrads))
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rmsd", type=Path, default=common.DATA / "G16_rmsd.txt")
    parser.add_argument("--energy", type=Path, default=common.DATA / "G16_energy_popr.txt")
    parser.add_argument("--table1-rmsd", type=Path, default=common.DATA / "table1_rmsd.txt")
    parser.add_argument("--table1-energy", type=Path, default=common.DATA / "table1_energy_popr.txt")
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--results", type=Path, default=common.RESULTS / "AP_unweighted_548")
    parser.add_argument("--reports", type=Path, default=common.REPORTS)
    args = parser.parse_args()

    inputs = load_inputs(args.inputs_dir)
    clusters = [row for row in csv.DictReader((args.results / "clusters_ap.csv").open(encoding="utf-8-sig")) if row["representative"] not in MANUAL_EXCLUSIONS]
    reps = sorted(row["representative"] for row in clusters)
    rmsd = load_table(args.rmsd)
    energy = load_table(args.energy)

    best_rmsd, best_energy, energy_selected, paired, within = [], [], [], [], []
    same = 0
    for rep in reps:
        key = rep.removesuffix(".inp").replace("-", "_")
        if key not in rmsd or key not in energy:
            continue
        r = np.array(rmsd[key])
        e = np.array(energy[key])
        ir, ie = int(np.argmin(r)), int(np.argmin(e))
        best_rmsd.append(r[ir])
        best_energy.append(e[ie])
        energy_selected.append(r[ie])
        paired.append(r[ir])
        same += int(ir == ie)
        if np.std(r) > 0 and np.std(e) > 0:
            within.append(float(spearmanr(e, r)[0]))

    rmsd_stats = summary(best_rmsd)
    energy_stats = summary(best_energy)
    selected_stats = summary(energy_selected)
    rho, p_rho = spearmanr(paired, best_energy)
    degradation = float(np.median(np.array(energy_selected) - np.array(paired)))

    payload = dict(
        dataset=dict(
            instances=len(inputs),
            pdb_entries=len({row["pdb"] for row in inputs}),
            kinds={kind: sum(row["kind"] == kind for row in inputs) for kind in ("DNA", "RNA", "chimeric")},
            tetrads={n: sum(row["tetrads"] == n for row in inputs) for n in (2, 3, 4)},
            clusters=len(clusters),
            representatives=len(reps),
        ),
        rmsd=rmsd_stats,
        energy=energy_stats,
        energy_selected=dict(
            **selected_stats,
            same_model=same,
            same_fraction=100.0 * same / len(energy_selected) if energy_selected else None,
            median_degradation=degradation,
            spearman_rho=float(rho),
            spearman_p=float(p_rho),
            within_input_spearman_median=float(np.median(within)) if within else None,
            at_most_9A=int(sum(value <= 9 for value in best_rmsd)),
        ),
    )

    args.reports.mkdir(parents=True, exist_ok=True)
    (args.reports / "numbers.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    groups = [
        ("All representatives", lambda row: True),
        ("DNA", lambda row: row["kind"] == "DNA"),
        ("RNA", lambda row: row["kind"] == "RNA"),
        ("Chimeric DNA/RNA", lambda row: row["kind"] == "chimeric"),
        ("Two tetrads", lambda row: row["tetrads"] == 2),
        ("Three tetrads", lambda row: row["tetrads"] == 3),
        ("Four tetrads", lambda row: row["tetrads"] == 4),
    ]
    by_file = {row["file"]: row for row in inputs}
    s1 = []
    for label, predicate in groups:
        minimum, selected = [], []
        for rep in reps:
            row = by_file.get(rep)
            key = rep.removesuffix(".inp").replace("-", "_")
            if row is None or not predicate(row) or key not in rmsd:
                continue
            minimum.append(min(rmsd[key]))
            if key in energy:
                selected.append(rmsd[key][int(np.argmin(energy[key]))])
        s1.append(dict(subgroup=label, n=len(minimum), min_rmsd=round(float(np.median(minimum)), 3), energy_selected_rmsd=round(float(np.median(selected)), 3), n_with_energy=len(selected)))
    with (args.reports / "S1_table.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(s1[0]))
        writer.writeheader()
        writer.writerows(s1)
    payload["s1_table"] = s1

    default = load_table(args.table1_rmsd)
    rows = []
    for pdb, key in TABLE1_KEYS.items():
        oracle = min(rmsd[key]) if key in rmsd else None
        default_value = min(default[key]) if key in default else None
        rows.append(dict(pdb=pdb, key=key, qplex=QPLEX[pdb], oracle=oracle, default=default_value, improvement=QPLEX[pdb] - oracle if oracle is not None else None))
    with (args.reports / "table1.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    oracles = np.array([row["oracle"] for row in rows if row["oracle"] is not None])
    qplex = np.array([row["qplex"] for row in rows])
    diffs = qplex - oracles
    comparable = [row for row in rows if row["default"] is not None and row["oracle"] is not None]
    payload["table1"] = dict(
        rows=len(rows),
        qplex_median=float(np.median(qplex)),
        oracle_median=float(np.median(oracles)),
        median_difference=float(np.median(diffs)),
        oracle_better=int(sum(diffs > 0)),
        sign_test_p=float(binomtest(int(sum(diffs > 0)), len(diffs), 0.5).pvalue),
        default_better=sum(row["default"] < row["oracle"] for row in comparable),
        default_worse=sum(row["default"] > row["oracle"] for row in comparable),
        default_ties=sum(row["default"] == row["oracle"] for row in comparable),
    )
    (args.reports / "numbers.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    dataset = payload["dataset"]
    print("DATASET")
    print(f"  instances                 {dataset['instances']}")
    print(f"  PDB entries               {dataset['pdb_entries']}")
    print(f"  DNA / RNA / chimeric      {dataset['kinds']['DNA']} / {dataset['kinds']['RNA']} / {dataset['kinds']['chimeric']}")
    print(f"  tetrads 2 / 3 / 4         {dataset['tetrads'][2]} / {dataset['tetrads'][3]} / {dataset['tetrads'][4]}")
    print(f"  AP clusters               {dataset['clusters']}")
    print("\nMODEL QUALITY")
    print(f"  best RMSD: n={rmsd_stats['n']} median={rmsd_stats['median']:.3f} A "
          f"whiskers={rmsd_stats['whisker_low']:.3f}-{rmsd_stats['whisker_high']:.3f}")
    print(f"  best energy: n={energy_stats['n']} median={energy_stats['median']:.3f}")
    print(f"  energy-selected RMSD: n={selected_stats['n']} median={selected_stats['median']:.3f} A")
    print(f"  same model: {same}/{len(energy_selected)}")
    print(f"  median degradation: {degradation:.3f} A")
    print(f"  Spearman across inputs: rho={rho:.3f}, p={p_rho:.3f}")
    print(f"  within-input Spearman median: {payload['energy_selected']['within_input_spearman_median']:.3f}")
    print(f"  best RMSD <= 9 A: {payload['energy_selected']['at_most_9A']}/{rmsd_stats['n']}")
    print("\nS1 TABLE")
    for row in s1:
        print(f"  {row['subgroup']:<22} n={row['n']:3d} min={row['min_rmsd']:.3f} A "
              f"energy-selected={row['energy_selected_rmsd']:.3f} A n_energy={row['n_with_energy']}")
    print("\nTABLE 1")
    t1 = payload["table1"]
    print(f"  Qplex / G4Composer medians {t1['qplex_median']:.3f} / {t1['oracle_median']:.3f} A")
    print(f"  median difference          {t1['median_difference']:.3f} A")
    print(f"  oracle lower in            {t1['oracle_better']}/{t1['rows']} rows (p={t1['sign_test_p']:.3e})")
    print(f"  default vs oracle          better {t1['default_better']}, worse {t1['default_worse']}, tie {t1['default_ties']}")
    print(f"\nwrote {args.reports / 'numbers.json'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
