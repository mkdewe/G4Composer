#!/usr/bin/env python3
"""Sequence-length dependence of representative accuracy and energy (S4).

For the 323 Affinity Propagation representatives, correlate input sequence
length with best-of-12 RMSD, best-of-12 lDDT and minimum energy per nucleotide.
The same correlations are reported for all modellable inputs and for the DNA
and RNA subsets, together with a Spearman partial correlation controlling for
tetrad count. Sequence-length quartile bins summarise the representative
distributions. Writes ``work/reports/S4_table.csv`` and
``figures/S4_length_dependence.pdf``.
"""

from __future__ import annotations

import argparse
import csv
from pathlib import Path

import common
import matplotlib
import numpy as np

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from scipy.stats import spearmanr, t as student_t

from plot_pooled import load_nres, load_table

KINDS = ("DNA", "RNA", "chimeric")
COLORS = {"DNA": "#2b7a6b", "RNA": "#b05a48", "chimeric": "#6b6b6b"}
COLUMNS = [
    "panel",
    "group",
    "n",
    "median_length",
    "n_dna",
    "n_rna",
    "n_chimeric",
    "rho_rmsd",
    "p_rmsd",
    "rho_lddt",
    "p_lddt",
    "rho_energy",
    "p_energy",
    "median_best_rmsd",
    "q1_best_rmsd",
    "q3_best_rmsd",
    "median_best_lddt",
    "q1_best_lddt",
    "q3_best_lddt",
    "median_energy_per_nt",
    "q1_energy_per_nt",
    "q3_energy_per_nt",
]


def sequence_kind(sequence: str) -> str:
    """Classify an input record from the case of its sequence string."""
    if sequence == sequence.lower():
        return "DNA"
    if sequence == sequence.upper():
        return "RNA"
    return "chimeric"


def load_records(inputs: Path, table_keys: set[str], nres: dict[str, int]) -> dict[str, dict]:
    records = {}
    for path in sorted(inputs.glob("*.inp")):
        record = common.describe_input(path)
        key = record["key"]
        if key not in table_keys:
            continue
        length = len(record["sequence"])
        if length != nres[key]:
            raise ValueError(
                f"{path.name}: sequence length {length} != table nucleotide count {nres[key]}"
            )
        record["length"] = length
        record["kind"] = sequence_kind(record["sequence"])
        records[key] = record
    return records


def load_representatives(results: Path, table_keys: set[str]) -> list[str]:
    keys = []
    with (results / "clusters_ap.csv").open(encoding="utf-8-sig") as stream:
        for row in csv.DictReader(stream):
            representative = row["representative"]
            if representative in common.MANUAL_EXCLUSIONS:
                continue
            key = representative.removesuffix(".inp").replace("-", "_")
            if key in table_keys:
                keys.append(key)
    if len(keys) != len(set(keys)):
        raise ValueError("duplicate representative keys in clusters_ap.csv")
    return sorted(keys)


def measure(keys: list[str], records: dict[str, dict], rmsd, lddt, energy, nres) -> dict:
    energy_index = [int(np.argmin(energy[key])) for key in keys]
    return dict(
        length=np.array([records[key]["length"] for key in keys], dtype=float),
        best_rmsd=np.array([rmsd[key].min() for key in keys], dtype=float),
        best_lddt=np.array([lddt[key].max() for key in keys], dtype=float),
        energy_per_nt=np.array(
            [energy[key].min() / nres[key] for key in keys], dtype=float
        ),
        energy_selected_rmsd=np.array(
            [rmsd[key][index] for key, index in zip(keys, energy_index)], dtype=float
        ),
        energy_selected_lddt=np.array(
            [lddt[key][index] for key, index in zip(keys, energy_index)], dtype=float
        ),
    )


def correlation(x, y) -> tuple[float, float]:
    result = spearmanr(x, y)
    return float(result.statistic), float(result.pvalue)


def partial_correlation(x, y, control) -> tuple[float, float]:
    """Spearman partial correlation of x and y controlling for control."""
    r_xy = float(spearmanr(x, y).statistic)
    r_xc = float(spearmanr(x, control).statistic)
    r_yc = float(spearmanr(y, control).statistic)
    rho = (r_xy - r_xc * r_yc) / np.sqrt((1.0 - r_xc**2) * (1.0 - r_yc**2))
    statistic = rho * np.sqrt((len(x) - 3) / (1.0 - rho**2))
    p_value = float(2.0 * student_t.sf(abs(statistic), len(x) - 3))
    return rho, p_value


def upper_quartiles(values) -> tuple[float, float, float]:
    q1, median, q3 = np.quantile(np.asarray(values, dtype=float), [0.25, 0.5, 0.75])
    return float(q1), float(median), float(q3)


def composition(keys: list[str], records: dict[str, dict]) -> dict:
    kinds = [records[key]["kind"] for key in keys]
    return dict(
        n=len(keys),
        median_length=float(np.median([records[key]["length"] for key in keys])),
        n_dna=kinds.count("DNA"),
        n_rna=kinds.count("RNA"),
        n_chimeric=kinds.count("chimeric"),
    )


def correlation_row(group: str, keys: list[str], records: dict[str, dict], data: dict, control=None) -> dict:
    counts = composition(keys, records)
    row = {column: "" for column in COLUMNS}
    row.update(
        panel="correlation",
        group=group,
        n=str(counts["n"]),
        median_length=f"{counts['median_length']:.2f}",
        n_dna=str(counts["n_dna"]),
        n_rna=str(counts["n_rna"]),
        n_chimeric=str(counts["n_chimeric"]),
    )
    for metric, field in (("rmsd", "best_rmsd"), ("lddt", "best_lddt"), ("energy", "energy_per_nt")):
        if control is None:
            rho, p_value = correlation(data["length"], data[field])
        else:
            rho, p_value = partial_correlation(data["length"], data[field], control)
        row[f"rho_{metric}"] = f"{rho:+.3f}"
        row[f"p_{metric}"] = f"{p_value:.2e}"
    return row


def quartile_bins(lengths) -> list[dict]:
    values = np.asarray(lengths, dtype=float)
    q1, q2, q3 = np.quantile(values, [0.25, 0.5, 0.75])
    index = np.searchsorted([q1, q2, q3], values, side="left")
    bins = []
    for position in range(4):
        mask = index == position
        if not mask.any():
            raise ValueError(f"empty sequence-length quartile bin {position + 1}")
        selected = values[mask]
        bins.append(
            dict(
                position=position,
                mask=mask,
                label=f"{int(selected.min())}-{int(selected.max())}",
                n=int(mask.sum()),
                median_length=float(np.median(selected)),
            )
        )
    return bins


def bin_rows(keys: list[str], records: dict[str, dict], data: dict) -> tuple[list[dict], list[dict]]:
    bins = quartile_bins(data["length"])
    rows = []
    for item in bins:
        mask = item["mask"]
        row = {column: "" for column in COLUMNS}
        row.update(
            panel="bin",
            group=item["label"],
            n=str(item["n"]),
            median_length=f"{item['median_length']:.2f}",
        )
        kinds = [records[key]["kind"] for key in np.asarray(keys)[mask]]
        row["n_dna"] = str(kinds.count("DNA"))
        row["n_rna"] = str(kinds.count("RNA"))
        row["n_chimeric"] = str(kinds.count("chimeric"))
        stats = {}
        for field in ("best_rmsd", "best_lddt", "energy_per_nt"):
            q1, median, q3 = upper_quartiles(data[field][mask])
            row[f"median_{field}"] = f"{median:.2f}"
            row[f"q1_{field}"] = f"{q1:.2f}"
            row[f"q3_{field}"] = f"{q3:.2f}"
            stats[field] = (q1, median, q3)
        item["stats"] = stats
        rows.append(row)
    return bins, rows


def write_table(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=COLUMNS, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def draw_figure(path: Path, keys: list[str], records: dict[str, dict], data: dict, bins: list[dict], rhos: dict) -> None:
    kinds = np.array([records[key]["kind"] for key in keys])
    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9, "pdf.fonttype": 42})
    fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.5))
    fig.subplots_adjust(left=0.08, right=0.985, bottom=0.30, top=0.86, wspace=0.35)

    panels = (
        ("best_rmsd", "Best-of-12 RMSD (Å)", rhos["rmsd"], None, (0.0, 13.0)),
        ("best_lddt", "Best-of-12 lDDT", rhos["lddt"], None, (0.0, 1.0)),
        (
            "energy_per_nt",
            "Minimum energy per nucleotide",
            rhos["energy"],
            -20.0,
            (-50.0, 105.0),
        ),
    )
    for ax, (field, ylabel, rho, threshold, ylim), label in zip(axes, panels, "ABC"):
        if threshold is not None:
            ax.axhline(threshold, color="#b7413a", linestyle="--", linewidth=1.1, zorder=1)
        for kind in KINDS:
            mask = kinds == kind
            ax.scatter(
                data["length"][mask],
                data[field][mask],
                s=14,
                color=COLORS[kind],
                alpha=0.55,
                linewidths=0,
                label=kind,
                zorder=2,
            )
        x = np.array([item["median_length"] for item in bins])
        medians = np.array([item["stats"][field][1] for item in bins])
        low = medians - np.array([item["stats"][field][0] for item in bins])
        high = np.array([item["stats"][field][2] for item in bins]) - medians
        ax.errorbar(
            x,
            medians,
            yerr=np.vstack([low, high]),
            fmt="s",
            color="#111111",
            markersize=4.5,
            capsize=2.0,
            elinewidth=1.0,
            capthick=1.0,
            zorder=5,
        )
        ax.set_xlabel("Input sequence length (nt)")
        ax.set_ylabel(ylabel)
        ax.set_xlim(10.0, 86.0)
        ax.set_ylim(*ylim)
        ax.grid(axis="y", color="#dddddd", linewidth=0.8)
        ax.set_axisbelow(True)
        for side in ("top", "right"):
            ax.spines[side].set_visible(False)
        ax.text(-0.14, 1.04, label, transform=ax.transAxes, fontsize=13, weight="bold")
        ax.text(
            0.03,
            0.96,
            f"$\\rho = {rho:+.3f}$\n$N = {len(keys)}$",
            transform=ax.transAxes,
            va="top",
        )

    handles = [
        Line2D(
            [0],
            [0],
            marker="o",
            color="none",
            markerfacecolor=COLORS[kind],
            markeredgecolor="none",
            markersize=5,
            alpha=0.75,
            label=kind,
        )
        for kind in KINDS
    ]
    handles.append(
        Line2D(
            [0],
            [0],
            marker="s",
            color="#111111",
            linestyle="none",
            markersize=4.5,
            label="Quartile median (IQR)",
        )
    )
    fig.legend(
        handles=handles,
        loc="lower center",
        ncol=4,
        frameon=False,
        bbox_to_anchor=(0.5, 0.0),
        handletextpad=0.4,
        columnspacing=1.4,
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(path, facecolor="white")
    plt.close(fig)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rmsd", type=Path, default=common.DATA / "G16_rmsd.txt")
    parser.add_argument("--lddt", type=Path, default=common.DATA / "G16_lddt.txt")
    parser.add_argument("--energy", type=Path, default=common.DATA / "G16_energy_popr.txt")
    parser.add_argument("--inputs", type=Path, default=common.INPUTS)
    parser.add_argument("--results", type=Path, default=common.RESULTS / "AP_unweighted_548")
    parser.add_argument("--reports", type=Path, default=common.REPORTS)
    parser.add_argument("--figures", type=Path, default=common.FIGURES)
    args = parser.parse_args()

    rmsd = load_table(args.rmsd)
    lddt = load_table(args.lddt)
    energy = load_table(args.energy)
    nres = load_nres(args.energy)
    table_keys = set(rmsd) & set(lddt) & set(energy)
    if len(table_keys) != 543:
        raise SystemExit(f"expected 543 modellable inputs, found {len(table_keys)}")

    records = load_records(args.inputs, table_keys, nres)
    if set(records) != table_keys:
        missing = sorted(table_keys - set(records))
        raise SystemExit(f"no input record for {len(missing)} table keys: {missing[:5]}")

    representatives = load_representatives(args.results, table_keys)
    if len(representatives) != 323:
        raise SystemExit(f"expected 323 representatives, found {len(representatives)}")

    rep_data = measure(representatives, records, rmsd, lddt, energy, nres)
    all_keys = sorted(table_keys)
    all_data = measure(all_keys, records, rmsd, lddt, energy, nres)
    dna_keys = [key for key in representatives if records[key]["kind"] == "DNA"]
    rna_keys = [key for key in representatives if records[key]["kind"] == "RNA"]
    dna_data = measure(dna_keys, records, rmsd, lddt, energy, nres)
    rna_data = measure(rna_keys, records, rmsd, lddt, energy, nres)
    tetrads = np.array([records[key]["tetrads"] for key in representatives], dtype=float)

    rows = [
        correlation_row("Representatives", representatives, records, rep_data),
        correlation_row("All modellable inputs", all_keys, records, all_data),
        correlation_row("DNA representatives", dna_keys, records, dna_data),
        correlation_row("RNA representatives", rna_keys, records, rna_data),
        correlation_row(
            "Representatives (partial, tetrads)",
            representatives,
            records,
            rep_data,
            control=tetrads,
        ),
    ]
    for row in rows:
        for metric in ("rmsd", "lddt", "energy"):
            p_value = row[f"p_{metric}"]
            if float(p_value) >= 1e-3:
                raise SystemExit(f"{row['group']} {metric}: p={p_value} is not < 0.001")
    rhos = {metric: float(rows[0][f"rho_{metric}"]) for metric in ("rmsd", "lddt", "energy")}
    bins, rows_bins = bin_rows(representatives, records, rep_data)
    rows.extend(rows_bins)

    table_path = args.reports / "S4_table.csv"
    figure_path = args.figures / "S4_length_dependence.pdf"
    write_table(table_path, rows)
    draw_figure(figure_path, representatives, records, rep_data, bins, rhos)
    print(
        f"energy-selected median RMSD/lDDT: "
        f"{np.median(rep_data['energy_selected_rmsd']):.2f} / "
        f"{np.median(rep_data['energy_selected_lddt']):.2f}"
    )
    print(f"wrote {table_path} and {figure_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
