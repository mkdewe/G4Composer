#!/usr/bin/env python3
"""Draw the RMSD/energy violin figure (Fig. 2 in the manuscript).

One value per Affinity Propagation representative: the minimum-RMSD model for
panel A and the minimum-energy model for panel B.  Energy is shown on a
symmetric logarithmic scale and its density is estimated in that transformed
space, matching the manuscript caption.
"""

from __future__ import annotations

import argparse
import csv
from pathlib import Path

import numpy as np
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from scipy.stats import gaussian_kde

MANUAL_EXCLUSIONS = {"7zj4-assembly1-E.inp", "7zj5-assembly1-E.inp"}


def load_measurements(path: Path) -> dict[str, list[float]]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        cells = line.split("|")[0].split()
        out[cells[0]] = [float(value) for value in cells[1:13]]
    return out


def draw(ax, values, transform, color, label, ticks, tick_labels, limits):
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
    ax.plot([0.94, 1.06], [low_w, low_w], color="#222222", linewidth=1.2, zorder=3)
    ax.plot([0.94, 1.06], [high_w, high_w], color="#222222", linewidth=1.2, zorder=3)
    ax.plot([1], [median], marker="o", markersize=4, color="#111111", zorder=4)
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
    parser.add_argument("--energy", type=Path, required=True)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    reps = sorted(
        row["representative"]
        for row in csv.DictReader((args.results / "clusters_ap.csv").open(encoding="utf-8-sig"))
        if row["representative"] not in MANUAL_EXCLUSIONS
    )
    rmsd = load_measurements(args.rmsd)
    energy = load_measurements(args.energy)
    rmsd_values = [min(rmsd[key]) for rep in reps if (key := rep.removesuffix(".inp").replace("-", "_")) in rmsd]
    energy_values = [min(energy[key]) for rep in reps if (key := rep.removesuffix(".inp").replace("-", "_")) in energy]
    rmsd_values = np.array(rmsd_values)
    energy_values = np.array(energy_values)

    linthresh = 100.0
    transform = lambda values: np.arcsinh(np.asarray(values, dtype=float) / linthresh)

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11, "pdf.fonttype": 42})
    fig, axes = plt.subplots(1, 2, figsize=(10, 6))
    fig.subplots_adjust(left=0.09, right=0.97, bottom=0.09, top=0.86, wspace=0.35)
    draw(
        axes[0], rmsd_values, lambda v: np.asarray(v, dtype=float),
        "#a9d5c7", "RMSD (Å)",
        ticks=[2.5, 5, 7.5, 10, 12.5, 15, 17.5],
        tick_labels=["2.5", "5.0", "7.5", "10.0", "12.5", "15.0", "17.5"],
        limits=(1.0, 18.5),
    )
    draw(
        axes[1], energy_values, transform,
        "#b7cbe6", "Energy",
        ticks=[-1000, -100, 0, 100, 1000, 10000],
        tick_labels=["−1,000", "−100", "0", "100", "1,000", "10,000"],
        limits=(-2200, 25000),
    )
    axes[0].text(-0.12, 1.04, "A", transform=axes[0].transAxes, fontsize=14, weight="bold")
    axes[1].text(-0.12, 1.04, "B", transform=axes[1].transAxes, fontsize=14, weight="bold")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(args.output, facecolor="white")
    print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
