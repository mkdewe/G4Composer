#!/usr/bin/env python3
"""Compute every quantitative result reported in the manuscript.

Reads the archived 12-column RMSD, lDDT and energy tables for all 6,516
G4Composer models.  Reports include per-representative selections, pooled
statistics without selection, threshold groups at -20 times sequence length,
and correlations. Table 1 contains RMSD and lDDT for both methods.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

import numpy as np
from scipy.stats import binomtest, pearsonr, spearmanr

import common
import table1

BAND = 0.10
FOLDBENCH = [
    dict(pdb="8D78", key="8d78_assembly1_A", family="Tetrahymena telomeric G4", af3=0.779616, boltz=0.499600, chai=0.810524, hf3=0.040058, protenix=0.861595),
    dict(pdb="8D79", key="8d79_assembly1_A", family="Tetrahymena telomeric G4", af3=0.839642, boltz=0.177338, chai=0.758673, hf3=0.186173, protenix=0.737444),
    dict(pdb="8G2N", key="8g2n_assembly1_A", family="Tetrahymena telomeric G4", af3=0.385517, boltz=0.302286, chai=0.382563, hf3=0.129086, protenix=0.390739),
    dict(pdb="8P6B", key="8p6b_assembly1_A", family="Modified human telomeric G4", af3=0.311, boltz=0.289, chai=0.333, hf3=0.059, protenix=0.314),
    dict(pdb="8TAA", key="8taa_assembly1_A", family="SLC2A1 LH/RH G4", af3=0.639, boltz=0.000, chai=0.112, hf3=0.084, protenix=0.244),
    dict(pdb="8TWH", key="8twh_assembly1_A", family="(GGGTT)3GGG G4", af3=0.687, boltz=0.126, chai=0.601, hf3=0.515, protenix=0.145),
]


def load_table(path: Path) -> dict[str, np.ndarray]:
    out = {}
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines()[1:]:
        if "|" not in line:
            continue
        cells = line.split("|")[0].split()
        if len(cells) >= 13:
            out[cells[0]] = np.array([float(value) for value in cells[1:13]], dtype=float)
    return out


def load_nres(path: Path) -> dict[str, int]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        key, tail = line.split("|")
        out[key.split()[0]] = int(tail.split()[0])
    return out


def representatives(results: Path) -> list[str]:
    reps = []
    with (results / "clusters_ap.csv").open(encoding="utf-8-sig") as stream:
        for row in csv.DictReader(stream):
            if row["representative"] not in common.MANUAL_EXCLUSIONS:
                reps.append(row["representative"].removesuffix(".inp").replace("-", "_"))
    return reps


def summary(values) -> dict:
    array = np.asarray(values, dtype=float)
    q1, median, q3 = np.quantile(array, [0.25, 0.5, 0.75])
    iqr = q3 - q1
    return dict(
        n=int(array.size), mean=float(array.mean()), sd=float(array.std(ddof=1)),
        minimum=float(array.min()), q1=float(q1), median=float(median),
        q3=float(q3), iqr=float(iqr), maximum=float(array.max()),
        whisker_low=float(array[array >= q1 - 1.5 * iqr].min()),
        whisker_high=float(array[array <= q3 + 1.5 * iqr].max()),
    )


def threshold_group(energy: float, threshold: float) -> str:
    if energy < (1 + BAND) * threshold:
        return "below"
    if energy > (1 - BAND) * threshold:
        return "above"
    return "around"


def grouped_stats(rmsd, lddt, energy, keys, nres) -> dict:
    out = {}
    for label in ("below", "around", "above"):
        rv, lv, count = [], [], 0
        for key in keys:
            threshold = -20.0 * nres[key]
            for e, r, l in zip(energy[key], rmsd[key], lddt[key]):
                if threshold_group(e, threshold) == label:
                    rv.append(r)
                    lv.append(l)
                    count += 1
        out[label] = dict(
            n=count, fraction=count / sum(len(rmsd[key]) for key in keys),
            rmsd_median=float(np.median(rv)) if rv else None,
            lddt_median=float(np.median(lv)) if lv else None,
            rmsd_iqr=float(np.percentile(rv, 75) - np.percentile(rv, 25)) if rv else None,
            lddt_iqr=float(np.percentile(lv, 75) - np.percentile(lv, 25)) if lv else None,
        )
    return out


def correlation_stats(rmsd, lddt, energy, keys) -> dict:
    all_r = np.concatenate([rmsd[key] for key in keys])
    all_l = np.concatenate([lddt[key] for key in keys])
    all_e = np.concatenate([energy[key] for key in keys])
    within_lr = np.array([spearmanr(lddt[key], rmsd[key])[0] for key in keys])
    within_le = np.array([spearmanr(lddt[key], energy[key])[0] for key in keys])
    within_re = np.array([spearmanr(rmsd[key], energy[key])[0] for key in keys])

    def within(values):
        return dict(median=float(np.median(values)), q1=float(np.percentile(values, 25)), q3=float(np.percentile(values, 75)), fraction_positive=float(np.mean(values > 0)), fraction_negative=float(np.mean(values < 0)))

    return dict(
        lddt_rmsd=dict(spearman=float(spearmanr(all_l, all_r)[0]), p=float(spearmanr(all_l, all_r)[1]), pearson=float(pearsonr(all_l, all_r)[0]), within=within(within_lr)),
        lddt_energy=dict(spearman=float(spearmanr(all_l, all_e)[0]), p=float(spearmanr(all_l, all_e)[1]), pearson=float(pearsonr(all_l, all_e)[0]), within=within(within_le)),
        rmsd_energy=dict(spearman=float(spearmanr(all_r, all_e)[0]), p=float(spearmanr(all_r, all_e)[1]), pearson=float(pearsonr(all_r, all_e)[0]), within=within(within_re)),
    )


def selections(rmsd, lddt, energy, keys) -> dict:
    out = dict(best_rmsd=[], best_lddt=[], energy_rmsd=[], energy_lddt=[], rmsd_of_best_lddt=[], lddt_of_best_rmsd=[], lddt_of_energy=[], relative_rmsd_penalty=[], relative_lddt_penalty=[])
    same_rmsd_energy = same_lddt_energy = same_rmsd_lddt = 0
    for key in keys:
        ir, il, ie = int(np.argmin(rmsd[key])), int(np.argmax(lddt[key])), int(np.argmin(energy[key]))
        out["best_rmsd"].append(rmsd[key][ir])
        out["best_lddt"].append(lddt[key][il])
        out["energy_rmsd"].append(rmsd[key][ie])
        out["energy_lddt"].append(lddt[key][ie])
        out["rmsd_of_best_lddt"].append(rmsd[key][il])
        out["lddt_of_best_rmsd"].append(lddt[key][ir])
        out["lddt_of_energy"].append(lddt[key][ie])
        out["relative_rmsd_penalty"].append((rmsd[key][ie] - rmsd[key][ir]) / rmsd[key][ir])
        out["relative_lddt_penalty"].append((lddt[key][il] - lddt[key][ie]) / lddt[key][il])
        same_rmsd_energy += int(ir == ie)
        same_lddt_energy += int(il == ie)
        same_rmsd_lddt += int(il == ir)
    out["same_rmsd_energy"] = same_rmsd_energy
    out["same_lddt_energy"] = same_lddt_energy
    out["same_rmsd_lddt"] = same_rmsd_lddt
    out["n"] = len(keys)
    return out


def load_qplex_rmsd(path: Path) -> dict[str, float]:
    return {row["pdb"]: float(row["rmsd_heavy"]) for row in csv.DictReader(path.open(encoding="utf-8"))}


def load_qplex_lddt(path: Path) -> dict[str, float]:
    return {row["pdb"]: float(row["lddt"]) for row in csv.DictReader(path.open(encoding="utf-8"))}


def fmt(value, digits=2) -> str:
    return "" if value is None else f"{value:.{digits}f}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rmsd", type=Path, default=common.DATA / "G16_rmsd.txt")
    parser.add_argument("--lddt", type=Path, default=common.DATA / "G16_lddt.txt")
    parser.add_argument("--energy", type=Path, default=common.DATA / "G16_energy_popr.txt")
    parser.add_argument("--qplex-scores", type=Path, default=common.DATA / "qplex_scores.csv")
    parser.add_argument("--qplex-lddt", type=Path, default=common.DATA / "qplex_lddt.csv")
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--results", type=Path, default=common.RESULTS / "AP_unweighted_548")
    parser.add_argument("--reports", type=Path, default=common.REPORTS)
    args = parser.parse_args()

    rmsd = load_table(args.rmsd)
    lddt = load_table(args.lddt)
    energy = load_table(args.energy)
    nres = load_nres(args.energy)
    keys = sorted(k for k in rmsd if k in lddt and k in energy)
    reps = [key for key in representatives(args.results) if key in rmsd]
    args.reports.mkdir(parents=True, exist_ok=True)

    by_depth = {}
    for index, depth in enumerate(common.DEPTHS):
        depth_rmsd = np.concatenate([rmsd[key][2 * index:2 * index + 2] for key in keys])
        depth_lddt = np.concatenate([lddt[key][2 * index:2 * index + 2] for key in keys])
        by_depth[str(depth)] = dict(
            rmsd_median=float(np.median(depth_rmsd)),
            rmsd_iqr=float(np.percentile(depth_rmsd, 75) - np.percentile(depth_rmsd, 25)),
            lddt_median=float(np.median(depth_lddt)),
            lddt_iqr=float(np.percentile(depth_lddt, 75) - np.percentile(depth_lddt, 25)),
        )
    by_mode = {}
    for index, mode in enumerate(common.MODES):
        mode_rmsd = np.concatenate([rmsd[key][index::2] for key in keys])
        mode_lddt = np.concatenate([lddt[key][index::2] for key in keys])
        by_mode[mode] = dict(
            rmsd_median=float(np.median(mode_rmsd)),
            rmsd_iqr=float(np.percentile(mode_rmsd, 75) - np.percentile(mode_rmsd, 25)),
            lddt_median=float(np.median(mode_lddt)),
            lddt_iqr=float(np.percentile(mode_lddt, 75) - np.percentile(mode_lddt, 25)),
        )

    payload = {"n_models": len(keys) * 12, "n_inputs": len(keys), "n_representatives": len(reps)}
    payload["pooled"] = dict(
        rmsd=summary(np.concatenate([rmsd[key] for key in keys])),
        lddt=summary(np.concatenate([lddt[key] for key in keys])),
        thresholds=grouped_stats(rmsd, lddt, energy, keys, nres),
        correlations=correlation_stats(rmsd, lddt, energy, keys),
        selections=selections(rmsd, lddt, energy, keys),
        by_depth=by_depth,
        by_mode=by_mode,
    )
    payload["representatives"] = dict(
        rmsd=summary([min(rmsd[key]) for key in reps]),
        lddt=summary([max(lddt[key]) for key in reps]),
        energy_rmsd=summary([rmsd[key][int(np.argmin(energy[key]))] for key in reps]),
        energy_lddt=summary([lddt[key][int(np.argmin(energy[key]))] for key in reps]),
        thresholds=grouped_stats(rmsd, lddt, energy, keys, nres) if False else grouped_stats({key: rmsd[key][int(np.argmin(energy[key])):int(np.argmin(energy[key]))+1] for key in reps}, {key: lddt[key][int(np.argmin(energy[key])):int(np.argmin(energy[key]))+1] for key in reps}, {key: energy[key][int(np.argmin(energy[key])):int(np.argmin(energy[key]))+1] for key in reps}, reps, nres),
        selections=selections(rmsd, lddt, energy, reps),
    )
    rep_sel = payload["representatives"]["selections"]
    rel = rep_sel["relative_rmsd_penalty"]
    payload["representatives"]["energy_selection"] = dict(
        within_005=int(sum(value <= 0.05 for value in rel)),
        within_010=int(sum(value <= 0.10 for value in rel)),
        within_015=int(sum(value <= 0.15 for value in rel)),
        within_020=int(sum(value <= 0.20 for value in rel)),
        within_025=int(sum(value <= 0.25 for value in rel)),
        within_050=int(sum(value <= 0.50 for value in rel)),
        median_relative_rmsd_penalty=float(np.median(rel)),
        median_relative_lddt_penalty=float(np.median(rep_sel["relative_lddt_penalty"])),
    )

    # Subgroup table (representatives only).
    inputs = []
    for path in sorted(args.inputs_dir.glob("*.inp")):
        if path.name in common.MANUAL_EXCLUSIONS:
            continue
        record = common.describe_input(path)
        sequence = record["sequence"]
        kind = "DNA" if sequence == sequence.lower() else ("RNA" if sequence == sequence.upper() else "chimeric")
        inputs.append(dict(file=path.name, key=record["key"], kind=kind, tetrads=record["tetrads"]))
    by_file = {row["file"].replace("-", "_").removesuffix(".inp"): row for row in inputs}
    groups = [
        ("All representatives", lambda row: True),
        ("DNA", lambda row: row["kind"] == "DNA"),
        ("RNA", lambda row: row["kind"] == "RNA"),
        ("Chimeric DNA/RNA", lambda row: row["kind"] == "chimeric"),
        ("Two tetrads", lambda row: row["tetrads"] == 2),
        ("Three tetrads", lambda row: row["tetrads"] == 3),
        ("Four tetrads", lambda row: row["tetrads"] == 4),
    ]
    s1 = []
    for label, predicate in groups:
        selected = [key for key in reps if predicate(by_file[key])]
        rmsd_idx = [int(np.argmin(rmsd[key])) for key in selected]
        lddt_idx = [int(np.argmax(lddt[key])) for key in selected]
        energy_idx = [int(np.argmin(energy[key])) for key in selected]
        best_r = [rmsd[key][index] for key, index in zip(selected, rmsd_idx)]
        lddt_at_best_r = [lddt[key][index] for key, index in zip(selected, rmsd_idx)]
        best_l = [lddt[key][index] for key, index in zip(selected, lddt_idx)]
        rmsd_at_best_l = [rmsd[key][index] for key, index in zip(selected, lddt_idx)]
        energy_r = [rmsd[key][index] for key, index in zip(selected, energy_idx)]
        energy_l = [lddt[key][index] for key, index in zip(selected, energy_idx)]
        s1.append(dict(
            subgroup=label, n=len(selected),
            min_rmsd=fmt(np.median(best_r)),
            lddt_of_min_rmsd=fmt(np.median(lddt_at_best_r)),
            best_lddt=fmt(np.median(best_l)),
            rmsd_of_best_lddt=fmt(np.median(rmsd_at_best_l)),
            energy_selected_rmsd=fmt(np.median(energy_r)),
            energy_selected_lddt=fmt(np.median(energy_l)),
            n_with_energy=len(selected),
        ))
    with (args.reports / "S1_table.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, lineterminator="\n", fieldnames=list(s1[0]))
        writer.writeheader(); writer.writerows(s1)
    payload["s1_table"] = s1

    # Whole-dataset and threshold table.
    s2 = []
    for section, label, stats in (
        ("Pooled", "Pooled RMSD", payload["pooled"]["rmsd"]),
        ("Pooled", "Pooled lDDT", payload["pooled"]["lddt"]),
        ("Representatives", "Best-of-12 RMSD", payload["representatives"]["rmsd"]),
        ("Representatives", "Best-of-12 lDDT", payload["representatives"]["lddt"]),
    ):
        is_rmsd = label.endswith("RMSD")
        s2.append(dict(section=section, group=label, n=stats["n"], median_rmsd=fmt(stats["median"]) if is_rmsd else "", q1_rmsd=fmt(stats["q1"]) if is_rmsd else "", q3_rmsd=fmt(stats["q3"]) if is_rmsd else "", iqr_rmsd=fmt(stats["iqr"]) if is_rmsd else "", median_lddt=fmt(stats["median"]) if not is_rmsd else "", q1_lddt=fmt(stats["q1"]) if not is_rmsd else "", q3_lddt=fmt(stats["q3"]) if not is_rmsd else "", iqr_lddt=fmt(stats["iqr"]) if not is_rmsd else "", note=""))
    for depth in common.DEPTHS:
        item = payload["pooled"]["by_depth"][str(depth)]
        s2.append(dict(section="By CYANA depth", group=depth, n=1086, median_rmsd=fmt(item["rmsd_median"]), q1_rmsd="", q3_rmsd="", iqr_rmsd=fmt(item["rmsd_iqr"]), median_lddt=fmt(item["lddt_median"]), q1_lddt="", q3_lddt="", iqr_lddt=fmt(item["lddt_iqr"]), note=""))
    for mode in common.MODES:
        item = payload["pooled"]["by_mode"][mode]
        s2.append(dict(section="By mode", group=mode, n=3258, median_rmsd=fmt(item["rmsd_median"]), q1_rmsd="", q3_rmsd="", iqr_rmsd=fmt(item["rmsd_iqr"]), median_lddt=fmt(item["lddt_median"]), q1_lddt="", q3_lddt="", iqr_lddt=fmt(item["lddt_iqr"]), note=""))
    for scope in ("pooled", "representatives"):
        for group in ("below", "around", "above"):
            item = payload[scope]["thresholds"][group]
            s2.append(dict(section=f"Energy threshold ({scope})", group=group, n=item["n"], median_rmsd=fmt(item["rmsd_median"]), q1_rmsd="", q3_rmsd="", iqr_rmsd=fmt(item["rmsd_iqr"]), median_lddt=fmt(item["lddt_median"]), q1_lddt="", q3_lddt="", iqr_lddt=fmt(item["lddt_iqr"]), note=f"{100*item['fraction']:.1f}%"))
    with (args.reports / "S2_table.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, lineterminator="\n", fieldnames=list(s2[0]))
        writer.writeheader(); writer.writerows(s2)
    payload["s2_table"] = s2

    # Table 1: paired RMSD/lDDT values for the RMSD-, lDDT-, and energy-selected models.
    qplex_rmsd = load_qplex_rmsd(args.qplex_scores)
    qplex_lddt = load_qplex_lddt(args.qplex_lddt)
    rows = []
    for target in table1.TARGETS:
        key = target["key"]
        ir = int(np.argmin(rmsd[key]))
        il = int(np.argmax(lddt[key]))
        ie = int(np.argmin(energy[key]))
        pdb = target["pdb"]
        rows.append(dict(
            pdb=pdb, key=key,
            qplex_rmsd=fmt(qplex_rmsd[pdb]), qplex_lddt=fmt(qplex_lddt[pdb]),
            oracle_rmsd=fmt(rmsd[key][ir]), oracle_lddt=fmt(lddt[key][ir]),
            oracle_best_lddt=fmt(lddt[key][il]), oracle_best_lddt_rmsd=fmt(rmsd[key][il]),
            oracle_energy_lddt=fmt(lddt[key][ie]), oracle_energy_rmsd=fmt(rmsd[key][ie]),
            improvement=fmt(qplex_rmsd[pdb] - rmsd[key][ir]),
        ))
    with (args.reports / "table1.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, lineterminator="\n", fieldnames=list(rows[0]))
        writer.writeheader(); writer.writerows(rows)
    diffs = np.array([qplex_rmsd[row["pdb"]] - float(row["oracle_rmsd"]) for row in rows])
    ldiffs = np.array([float(row["oracle_lddt"]) - qplex_lddt[row["pdb"]] for row in rows])
    payload["table1"] = dict(
        rows=len(rows), qplex_median=float(np.median([qplex_rmsd[t["pdb"]] for t in table1.TARGETS])),
        oracle_median=float(np.median([float(row["oracle_rmsd"]) for row in rows])),
        median_difference=float(np.median(diffs)), oracle_better=int(sum(diffs > 0)),
        sign_test_rmsd_p=float(binomtest(int(sum(diffs > 0)), len(diffs), 0.5).pvalue),
        qplex_lddt_median=float(np.median([qplex_lddt[t["pdb"]] for t in table1.TARGETS])),
        oracle_lddt_median=float(np.median([float(row["oracle_lddt"]) for row in rows])),
        oracle_best_lddt_median=float(np.median([float(row["oracle_best_lddt"]) for row in rows])),
        oracle_best_lddt_rmsd_median=float(np.median([float(row["oracle_best_lddt_rmsd"]) for row in rows])),
        oracle_energy_rmsd_median=float(np.median([float(row["oracle_energy_rmsd"]) for row in rows])),
        oracle_energy_lddt_median=float(np.median([float(row["oracle_energy_lddt"]) for row in rows])),
        oracle_lddt_better=int(sum(ldiffs > 0)), sign_test_lddt_p=float(binomtest(int(sum(ldiffs > 0)), len(ldiffs), 0.5).pvalue),
    )

    # FoldBench values from its public source data, plus paired G4Composer
    # RMSD/lDDT values for the models selected by RMSD, lDDT, and energy.
    s3 = []
    for item in FOLDBENCH:
        key = item["key"]
        ir = int(np.argmin(rmsd[key]))
        il = int(np.argmax(lddt[key]))
        ie = int(np.argmin(energy[key]))
        s3.append(dict(
            pdb=item["pdb"], family=item["family"],
            af3=fmt(item["af3"]), boltz1=fmt(item["boltz"]), chai1=fmt(item["chai"]),
            helixfold3=fmt(item["hf3"]), protenix=fmt(item["protenix"]),
            min_rmsd=fmt(rmsd[key][ir]),
            lddt_of_min_rmsd=fmt(lddt[key][ir]),
            best_lddt=fmt(lddt[key][il]),
            rmsd_of_best_lddt=fmt(rmsd[key][il]),
            energy_selected_rmsd=fmt(rmsd[key][ie]),
            energy_selected_lddt=fmt(lddt[key][ie]),
        ))
    with (args.reports / "S3_table.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, lineterminator="\n", fieldnames=list(s3[0]))
        writer.writeheader(); writer.writerows(s3)
    payload["s3_table"] = s3

    (args.reports / "numbers.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    print(f"models: {payload['n_models']} ({len(keys)} inputs), representatives {len(reps)}")
    pr = payload["pooled"]
    print(f"pooled RMSD median {pr['rmsd']['median']:.2f} IQR {pr['rmsd']['iqr']:.2f}; lDDT median {pr['lddt']['median']:.2f} IQR {pr['lddt']['iqr']:.2f}")
    rs = payload["representatives"]
    print(f"representatives best RMSD {rs['rmsd']['median']:.2f}; energy RMSD {rs['energy_rmsd']['median']:.2f}; best lDDT {rs['lddt']['median']:.2f}; energy lDDT {rs['energy_lddt']['median']:.2f}")
    print(f"energy==best RMSD {rep_sel['same_rmsd_energy']}/{len(reps)}; energy==best lDDT {rep_sel['same_lddt_energy']}/{len(reps)}")
    print(f"within 5% of best RMSD {payload['representatives']['energy_selection']['within_005']}/{len(reps)}; within 25% {payload['representatives']['energy_selection']['within_025']}/{len(reps)}")
    for scope in ("pooled", "representatives"):
        for group in ("below", "around", "above"):
            x = payload[scope]["thresholds"][group]
            print(f"threshold {scope:<16} {group:<7} n={x['n']:4d} RMSD {x['rmsd_median']:.2f} lDDT {x['lddt_median']:.2f}")
    print(f"table1 RMSD medians {payload['table1']['oracle_median']:.2f}/{payload['table1']['qplex_median']:.2f}; lDDT medians {payload['table1']['oracle_lddt_median']:.2f}/{payload['table1']['qplex_lddt_median']:.2f}")
    print(f"wrote {args.reports / 'numbers.json'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
