#!/usr/bin/env python3
"""Draw the RMSD, lDDT and threshold-relative energy violins (Fig. 2).

One value per Affinity Propagation representative: minimum RMSD (A), maximum
lDDT (B), and minimum Xplor-NIH energy per nucleotide (C).  The horizontal
line at -20 marks the RNAComposer-derived threshold used in the manuscript.
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
from scipy.stats import gaussian_kde


def load_measurements(path: Path) -> dict[str, list[float]]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        cells = line.split("|")[0].split()
        out[cells[0]] = [float(value) for value in cells[1:13]]
    return out


def load_nres(path: Path) -> dict[str, int]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        left, right = line.split("|")
        out[left.split()[0]] = int(right.split()[0])
    return out


def draw(ax, values, *, transform=lambda x: np.asarray(x, dtype=float), color, label, ticks, tick_labels, limits, threshold=None):
    values = np.asarray(values, dtype=float)
    transformed = transform(values)
    low, high = transformed.min(), transformed.max()
    pad = 0.08 * (high - low) if high > low else 0.5
    grid = np.linspace(low - pad, high + pad, 400)
    density = gaussian_kde(transformed, bw_method=0.35)(grid)
    width = density / density.max() * 0.42
    ax.fill_betweenx(grid, 1 - width, 1 + width, color=color, alpha=0.9, linewidth=0)
    q1, median, q3 = np.quantile(transformed, [0.25, 0.5, 0.75])
    iqr = q3 - q1
    low_w = transformed[transformed >= q1 - 1.5 * iqr].min()
    high_w = transformed[transformed <= q3 + 1.5 * iqr].max()
    ax.plot([1, 1], [low_w, high_w], color="#222222", linewidth=1.2, zorder=3)
    for endpoint in (low_w, high_w):
        ax.plot([0.94, 1.06], [endpoint, endpoint], color="#222222", linewidth=1.2, zorder=3)
    ax.plot([1], [median], marker="o", markersize=4, color="#111111", zorder=4)
    if threshold is not None:
        ax.axhline(threshold, color="#b7413a", linestyle="--", linewidth=1.2)
    ax.set_xlim(0.35, 1.65)
    ax.set_xticks([])
    ax.set_ylabel(label)
    ax.set_yticks(transform(np.array(ticks, dtype=float)))
    ax.set_yticklabels(tick_labels)
    ax.set_ylim(transform(np.array([limits[0]]))[0], transform(np.array([limits[1]]))[0])
    ax.grid(axis="y", color="#dddddd", linewidth=0.8)
    ax.set_axisbelow(True)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rmsd", type=Path, required=True)
    parser.add_argument("--lddt", type=Path, required=True)
    parser.add_argument("--energy", type=Path, required=True)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    reps = sorted(
        row["representative"]
        for row in csv.DictReader((args.results / "clusters_ap.csv").open(encoding="utf-8-sig"))
        if row["representative"] not in common.EXCLUDED_INPUTS
    )
    keys = [rep.removesuffix(".inp").replace("-", "_") for rep in reps]
    rmsd = load_measurements(args.rmsd)
    lddt = load_measurements(args.lddt)
    energy = load_measurements(args.energy)
    nres = load_nres(args.energy)
    keys = [key for key in keys if key in rmsd and key in lddt and key in energy]

    rmsd_values = np.array([min(rmsd[key]) for key in keys])
    lddt_values = np.array([max(lddt[key]) for key in keys])
    energy_values = np.array([min(energy[key]) / nres[key] for key in keys])

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11, "pdf.fonttype": 42})
    fig, axes = plt.subplots(1, 3, figsize=(12, 5.4))
    fig.subplots_adjust(left=0.075, right=0.985, bottom=0.10, top=0.87, wspace=0.35)
    draw(
        axes[0], rmsd_values, color="#a9d5c7", label="RMSD (Å)",
        ticks=[2.5, 5, 7.5, 10, 12.5, 15, 17.5],
        tick_labels=["2.50", "5.00", "7.50", "10.00", "12.50", "15.00", "17.50"],
        limits=(1.0, 18.5),
    )
    draw(
        axes[1], lddt_values, color="#d8c7a1", label="lDDT",
        ticks=[0, 0.2, 0.4, 0.6, 0.8, 1.0], tick_labels=["0.00", "0.20", "0.40", "0.60", "0.80", "1.00"],
        limits=(0.0, 1.0),
    )
    draw(
        axes[2], energy_values, color="#b7cbe6", label="Energy per nucleotide",
        ticks=[-40, -20, 0, 40, 80], tick_labels=["−40", "−20", "0", "40", "80"],
        limits=(-50, 105), threshold=-20,
    )
    for ax, label in zip(axes, "ABC"):
        ax.text(-0.12, 1.04, label, transform=ax.transAxes, fontsize=14, weight="bold")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(args.output, facecolor="white")
    print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
