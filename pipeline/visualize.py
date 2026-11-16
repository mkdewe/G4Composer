from pathlib import Path
import json, math, csv
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Patch
import argparse
parser = argparse.ArgumentParser(description='Plot one value per AP representative.')
parser.add_argument('--results', type=Path, required=True)
parser.add_argument('--measurements', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = args.measurements
out = args.output
out.mkdir(parents=True, exist_ok=False)
with (args.results / 'clusters_ap.csv').open(encoding='utf-8-sig') as stream:
    clusters = list(csv.DictReader(stream))
reps = sorted((c['representative'] for c in clusters))
assert len(reps) == len(set(reps))
n = len(reps)

def load(name, energy=False):
    result = {}
    for line in (root / name).read_text().splitlines()[1:]:
        if not line.strip():
            continue
        left, right = line.split('|')
        a = left.split()
        v = list(map(float, a[1:13]))
        tail = right.split()
        assert len(v) == 12 and all((math.isfinite(x) for x in v)) and (a[0] not in result)
        assert abs(min(v) - float(tail[3] if energy else tail[0])) < 1e-06
        result[a[0]] = {'min': min(v), 'threshold': float(tail[1]) if energy else None}
    return result
E = load('G16_energy_popr.txt', True)
R = load('G16_rmsd.txt')
el = ['E ≤ T', 'T < E ≤ −T', '−T < E ≤ 10,000', '10,000 < E ≤ 100,000', '100,000 < E ≤ 1 million', '1 million < E ≤ 10 million', '10 million < E ≤ 100 million', 'No data']
rl = ['RMSD ≤ 3 Å', '3 < RMSD ≤ 6 Å', '6 < RMSD ≤ 9 Å', '9 < RMSD ≤ 12 Å', '12 < RMSD ≤ 15 Å', '15 < RMSD ≤ 18 Å', '18 < RMSD ≤ 21 Å', '21 < RMSD ≤ 24 Å', '24 < RMSD ≤ 27 Å', 'No data']
cols = ['#00ac2c', '#80d311', '#d4e96b', '#fff000', '#ffbe00', '#ff1414', '#8a167d', '#111111', '#555555']
ecol = [*cols[:6], '#111111', '#b8bdc5']
rcol = cols + ['#b8bdc5']
stats = {}
for label in ['AP (unweighted)']:
    er = [0] * 8
    rr = [0] * 10
    values = []
    for rep in reps:
        key = rep.removesuffix('.inp').replace('-', '_')
        e = E.get(key)
        r = R.get(key)
        ei = next((i for i, b in enumerate([e['threshold'], -e['threshold'], 10000.0, 100000.0, 1000000.0, 10000000.0, 100000000.0]) if e['min'] <= b), None) if e else 7
        ri = next((i for i, b in enumerate([3, 6, 9, 12, 15, 18, 21, 24, 27]) if r['min'] <= b), None) if r else 9
        assert ei is not None and ri is not None
        er[ei] += 1
        rr[ri] += 1
        values.append({'representative': rep, 'energy': e, 'rmsd': r, 'energy_bin': el[ei], 'rmsd_bin': rl[ri]})
    assert sum(er) == sum(rr) == n
    stats[label] = {'n': n, 'energy_counts': er, 'rmsd_counts': rr, 'values': values}
plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 11, 'pdf.fonttype': 42})
fig = plt.figure(figsize=(12, 8), facecolor='white')
for row, (label, d) in enumerate(stats.items()):
    fig.text(0.5, 0.975 - row * 0.49, label, ha='center', va='top', fontsize=20, weight='bold')
    for col, (title, counts, labels, colors) in enumerate([('RMSD', d['rmsd_counts'], rl, rcol), ('Energy', d['energy_counts'], el, ecol)]):
        ax = fig.add_axes([0.06 + col * 0.5, 0.4 - row * 0.49, 0.38, 0.46])
        nz = [i for i, n in enumerate(counts) if n]
        ax.pie([counts[i] for i in nz], colors=[colors[i] for i in nz], startangle=90, counterclock=False, wedgeprops={'linewidth': 1, 'edgecolor': 'white'}, autopct=lambda p: f'{p:.1f}%' if p >= 4 else '', pctdistance=0.73, textprops={'fontsize': 12})
        ax.set_title(f"{title}\n{d['n']} values", fontsize=15, pad=10)
        ax.legend([Patch(facecolor=c) for c in colors], [f"{l}   |   {n} ({n / d['n'] * 100:.1f}%)" for l, n in zip(labels, counts)], loc='upper center', bbox_to_anchor=(0.5, 0.005), frameon=False, fontsize=10, labelspacing=0.4, handlelength=1.1)
fig.savefig(out / 'AP_RMSD_Energy.png', dpi=220, facecolor='white')
fig.savefig(out / 'AP_RMSD_Energy.pdf', facecolor='white')
fig.savefig(out / 'AP_RMSD_Energy.svg', facecolor='white')
(out / 'chart_values.json').write_text(json.dumps({'energy_labels': el, 'rmsd_labels': rl, 'variants': stats}, indent=2, ensure_ascii=False), encoding='utf-8')
import numpy as np
from matplotlib.ticker import FixedLocator, FuncFormatter
d = stats['AP (unweighted)']
plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 11, 'pdf.fonttype': 42})
fig, axs = plt.subplots(1, 2, figsize=(10, 6))
fig.subplots_adjust(left=0.09, right=0.97, bottom=0.1, top=0.8, wspace=0.42)
fig.suptitle('AP (unweighted)', fontsize=19, weight='bold', y=0.96)
stats = {}
for ax, key, title, color in zip(axs, ['rmsd', 'energy'], ['RMSD', 'Energy'], ['#72b6a1', '#719aca']):
    v = np.array([r[key]['min'] for r in d['values'] if r[key] is not None])
    q1, median, q3 = np.quantile(v, [0.25, 0.5, 0.75])
    iqr = q3 - q1
    stats[key] = {'n': len(v), 'missing': d['n'] - len(v), 'min': float(min(v)), 'q1': float(q1), 'median': float(median), 'q3': float(q3), 'max': float(max(v)), 'whisker_low': float(min(v[v >= q1 - 1.5 * iqr])), 'whisker_high': float(max(v[v <= q3 + 1.5 * iqr])), 'outliers': int(sum((v < q1 - 1.5 * iqr) | (v > q3 + 1.5 * iqr)))}
    ax.boxplot(v, positions=[1], widths=0.42, whis=1.5, patch_artist=True, boxprops={'facecolor': color, 'edgecolor': '#334155'}, medianprops={'color': '#111827', 'linewidth': 2}, whiskerprops={'color': '#334155'}, capprops={'color': '#334155'}, flierprops={'marker': 'o', 'markersize': 4, 'markerfacecolor': 'none', 'markeredgecolor': '#475569', 'alpha': 0.75})
    ax.set_title(f'{title}\n{len(v)} values', fontsize=15, pad=14)
    ax.set_xticks([])
    ax.set_xlim(0.5, 1.5)
    ax.grid(axis='y', alpha=0.2)
    ax.set_axisbelow(True)
    ax.spines[['top', 'right']].set_visible(False)
    if key == 'rmsd':
        ax.set_ylabel('RMSD (Å)')
        ax.set_ylim(0, 19)
    else:
        ax.set_yscale('symlog', linthresh=1000, linscale=1, base=10)
        ax.set_ylim(-2300, 22000)
        ax.yaxis.set_major_locator(FixedLocator([-2000, -1000, 0, 1000, 10000, 20000]))
        ax.yaxis.set_major_formatter(FuncFormatter(lambda x, pos: f'{x:,.0f}'))
        ax.set_ylabel('Energy (symmetric log scale)')
    ax.text(0.98, 0.96, f'Median: {median:,.2f}', ha='right', va='top', transform=ax.transAxes, fontsize=10)
fig.savefig(out / 'AP_boxplot.png', dpi=240, facecolor='white')
fig.savefig(out / 'AP_boxplot.pdf', facecolor='white')
fig.savefig(out / 'AP_boxplot.svg', facecolor='white')
(out / 'boxplot_statistics.json').write_text(json.dumps(stats, indent=2), encoding='utf-8')
