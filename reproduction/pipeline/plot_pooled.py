#!/usr/bin/env python3
"""Draw whole-dataset supplementary figures without model selection.

S1: pooled RMSD/lDDT distributions and their joint distribution.
S2: energy per nucleotide, RMSD/lDDT by -20*N threshold group, and both
    energy–accuracy correlations.
S3: median RMSD and lDDT by CYANA depth and standard/alternative mode.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import common
import matplotlib
import numpy as np

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from scipy.stats import gaussian_kde, spearmanr

ORDER = [(depth, mode) for depth in common.DEPTHS for mode in common.MODES]


def load_table(path: Path) -> dict[str, np.ndarray]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        cells = line.split("|")[0].split()
        if len(cells) >= 13:
            out[cells[0]] = np.array([float(x) for x in cells[1:13]], dtype=float)
    return out


def load_nres(path: Path) -> dict[str, int]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        if "|" not in line:
            continue
        left, right = line.split("|")
        out[left.split()[0]] = int(right.split()[0])
    return out


def violin(ax, groups, labels, *, color, ylabel, transform=lambda v: np.asarray(v, dtype=float), ticks=None, tick_labels=None, threshold=None, ylim=None):
    for pos, values in enumerate(groups, start=1):
        transformed = transform(np.asarray(values, dtype=float))
        low, high = transformed.min(), transformed.max()
        pad = 0.08 * (high - low) if high > low else 0.5
        grid = np.linspace(low - pad, high + pad, 400)
        density = gaussian_kde(transformed, bw_method=0.35)(grid)
        width = density / density.max() * 0.40
        ax.fill_betweenx(grid, pos - width, pos + width, color=color, alpha=0.9, linewidth=0)
        q1, median, q3 = np.quantile(transformed, [0.25, 0.5, 0.75])
        iqr = q3 - q1
        lo = transformed[transformed >= q1 - 1.5 * iqr].min()
        hi = transformed[transformed <= q3 + 1.5 * iqr].max()
        ax.plot([pos, pos], [lo, hi], color="#222222", linewidth=1.0, zorder=3)
        for endpoint in (lo, hi):
            ax.plot([pos - 0.05, pos + 0.05], [endpoint, endpoint], color="#222222", linewidth=1.0, zorder=3)
        ax.plot([pos], [median], marker="o", markersize=3.5, color="#111111", zorder=4)
    if threshold is not None:
        ax.axhline(float(transform(np.array([threshold]))[0]), color="#b7413a", linestyle="--", linewidth=1.1)
    ax.set_xticks(range(1, len(groups) + 1))
    ax.set_xticklabels(labels)
    ax.set_ylabel(ylabel)
    if ticks is None:
        ax.ticklabel_format(axis="y", style="plain")
    else:
        ax.set_yticks(transform(np.asarray(ticks, dtype=float)))
        ax.set_yticklabels(tick_labels)
    if ylim is not None:
        ax.set_ylim(transform(np.asarray(ylim, dtype=float)))
    ax.grid(axis="y", color="#dddddd", linewidth=0.8)
    ax.set_axisbelow(True)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)


def group_name(energy: float, threshold: float) -> str:
    if energy < 1.1 * threshold:
        return "below"
    if energy > 0.9 * threshold:
        return "above"
    return "around"


def hexplot(ax, x, y, xlabel, ylabel, rho, *, x_transform=None, x_ticks=None, x_tick_labels=None, vline=None, ylim=None):
    x = np.asarray(x, dtype=float)
    y = np.asarray(y, dtype=float)
    if x_transform is not None:
        x = x_transform(x)
    ax.hexbin(x, y, gridsize=70, cmap="Blues", mincnt=1, bins="log")
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.text(0.03, 0.96, f"$\\rho = {rho:.2f}$\n$N = {len(x)}$", transform=ax.transAxes, va="top")
    if vline is not None:
        ax.axvline(vline, color="#b7413a", linestyle="--", linewidth=1.1)
    if x_ticks is not None:
        ax.set_xticks(x_ticks)
        ax.set_xticklabels(x_tick_labels)
    if ylim is not None:
        ax.set_ylim(ylim)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rmsd", type=Path, default=common.DATA / "G16_rmsd.txt")
    parser.add_argument("--lddt", type=Path, default=common.DATA / "G16_lddt.txt")
    parser.add_argument("--energy", type=Path, default=common.DATA / "G16_energy_popr.txt")
    parser.add_argument("--output-dir", type=Path, default=common.FIGURES)
    args = parser.parse_args()
    rmsd = load_table(args.rmsd)
    lddt = load_table(args.lddt)
    energy = load_table(args.energy)
    nres = load_nres(args.energy)
    excluded_keys = {common.key_for(Path(name)) for name in common.EXCLUDED_INPUTS}
    keys = sorted(k for k in rmsd if k in lddt and k in energy and k not in excluded_keys)
    args.output_dir.mkdir(parents=True, exist_ok=True)

    R = np.concatenate([rmsd[k] for k in keys])
    L = np.concatenate([lddt[k] for k in keys])
    E = np.concatenate([energy[k] for k in keys])
    N = np.concatenate([np.full(12, nres[k]) for k in keys])
    groups = {label: {"rmsd": [], "lddt": []} for label in ("below", "around", "above")}
    for key in keys:
        threshold = -20.0 * nres[key]
        for e, r, l in zip(energy[key], rmsd[key], lddt[key]):
            label = group_name(e, threshold)
            groups[label]["rmsd"].append(r)
            groups[label]["lddt"].append(l)

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9, "pdf.fonttype": 42})

    # S1: whole-dataset accuracy and the relationship between metrics.
    fig, axes = plt.subplots(1, 3, figsize=(10.5, 3.5))
    fig.subplots_adjust(left=0.075, right=0.985, bottom=0.16, top=0.86, wspace=0.34)
    violin(axes[0], [R], ["All 6,504 models"], color="#a9d5c7", ylabel="RMSD (Å)")
    violin(axes[1], [L], ["All 6,504 models"], color="#d8c7a1", ylabel="lDDT", ylim=(0.0, 1.0))
    hexplot(axes[2], R, L, "RMSD (Å)", "lDDT", float(spearmanr(L, R)[0]), ylim=(0.0, 1.0))
    for ax, label in zip(axes, "ABC"):
        ax.text(-0.14, 1.04, label, transform=ax.transAxes, fontsize=13, weight="bold")
    fig.savefig(args.output_dir / "S1_pooled_accuracy.pdf", facecolor="white")
    plt.close(fig)

    # S2: threshold analysis and energy correlations.
    fig = plt.figure(figsize=(15, 8))
    grid = fig.add_gridspec(2, 6, left=0.065, right=0.98, bottom=0.10, top=0.92, wspace=1.1, hspace=0.55)
    axA = fig.add_subplot(grid[0, 0:2])
    axB = fig.add_subplot(grid[0, 2:4])
    axC = fig.add_subplot(grid[0, 4:6])
    axD = fig.add_subplot(grid[1, 0:3])
    axE = fig.add_subplot(grid[1, 3:6])
    transform = lambda v: np.arcsinh(np.asarray(v, dtype=float) / 100.0)
    violin(axA, [E / N], ["All 6,504 models"], color="#b7cbe6", ylabel="Energy per nucleotide", transform=transform,
           ticks=[-20, 100, 1000, 10000], tick_labels=["−20", "100", "1,000", "10,000"],
           threshold=-20, ylim=(-60, 40000))
    violin(axB, [groups[label]["rmsd"] for label in ("below", "around", "above")], ["Below", "Around", "Above"], color="#a9d5c7", ylabel="RMSD (Å)")
    violin(axC, [groups[label]["lddt"] for label in ("below", "around", "above")], ["Below", "Around", "Above"], color="#d8c7a1", ylabel="lDDT", ylim=(0.0, 1.0))
    axA.set_title("Energy distribution", fontsize=10)
    axB.set_title("Accuracy by energy-threshold group", fontsize=10)
    axC.set_title("Local accuracy by energy-threshold group", fontsize=10)
    # Correlate the displayed total energies, not a differently normalized axis.
    # The -20*N threshold depends on input length and is therefore shown only
    # for the normalized distribution in panel A.
    transformed_energy = lambda v: np.arcsinh(np.asarray(v, dtype=float) / 1000.0)
    ticks = transformed_energy(np.array([-1000, 0, 1000, 10000, 100000]))
    labels = ["−1,000", "0", "1,000", "10,000", "100,000"]
    hexplot(axD, E, R, "Total Xplor-NIH energy", "RMSD (Å)", float(spearmanr(E, R)[0]), x_transform=transformed_energy, x_ticks=ticks, x_tick_labels=labels)
    hexplot(axE, E, L, "Total Xplor-NIH energy", "lDDT", float(spearmanr(E, L)[0]), x_transform=transformed_energy, x_ticks=ticks, x_tick_labels=labels, ylim=(0.0, 1.0))
    for ax, label in zip((axA, axB, axC, axD, axE), "ABCDE"):
        ax.text(-0.14, 1.04, label, transform=ax.transAxes, fontsize=13, weight="bold")
    fig.savefig(args.output_dir / "S2_energy_threshold.pdf", facecolor="white")
    plt.close(fig)

    # S3: sensitivity to CYANA depth in each mode.
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5))
    fig.subplots_adjust(left=0.09, right=0.97, bottom=0.14, top=0.86, wspace=0.25)
    colors = {"n": "#2b7a6b", "a": "#b05a48"}
    for mode in common.MODES:
        idx = common.MODES.index(mode)
        med_r = np.array([np.median([rmsd[k][2*d+idx] for k in keys]) for d in range(6)])
        q1_r = np.array([np.percentile([rmsd[k][2*d+idx] for k in keys], 25) for d in range(6)])
        q3_r = np.array([np.percentile([rmsd[k][2*d+idx] for k in keys], 75) for d in range(6)])
        med_l = np.array([np.median([lddt[k][2*d+idx] for k in keys]) for d in range(6)])
        q1_l = np.array([np.percentile([lddt[k][2*d+idx] for k in keys], 25) for d in range(6)])
        q3_l = np.array([np.percentile([lddt[k][2*d+idx] for k in keys], 75) for d in range(6)])
        label = "standard" if mode == "n" else "alternative"
        axes[0].plot(common.DEPTHS, med_r, "o-", color=colors[mode], label=label)
        axes[0].fill_between(common.DEPTHS, q1_r, q3_r, color=colors[mode], alpha=0.15)
        axes[1].plot(common.DEPTHS, med_l, "o-", color=colors[mode], label=label)
        axes[1].fill_between(common.DEPTHS, q1_l, q3_l, color=colors[mode], alpha=0.15)
    for ax, ylabel in zip(axes, ["Median RMSD (Å), IQR band", "Median lDDT, IQR band"]):
        ax.set_xticks(common.DEPTHS)
        ax.set_xlabel("CYANA iterations per build step")
        ax.set_ylabel(ylabel)
        ax.legend(frameon=False)
        ax.grid(color="#dddddd", linewidth=0.8)
        ax.set_axisbelow(True)
        for side in ("top", "right"):
            ax.spines[side].set_visible(False)
    for ax, label in zip(axes, "AB"):
        ax.text(-0.14, 1.04, label, transform=ax.transAxes, fontsize=13, weight="bold")
    fig.savefig(args.output_dir / "S3_iteration_depth.pdf", facecolor="white")
    plt.close(fig)

    print(f"wrote supplementary figures in {args.output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
