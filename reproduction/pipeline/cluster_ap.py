"""Run unweighted AP independently within exact four-field buckets."""
import argparse
import csv
import json
import warnings
import collections
from pathlib import Path
import numpy as np
import scipy
import sklearn
from sklearn.cluster import AffinityPropagation
from sklearn.exceptions import ConvergenceWarning
import input_helpers as ci
import common

def create_output_directory(requested):
    """Reuse the requested directory and clear only this script's old exports."""
    requested.mkdir(parents=True, exist_ok=True)
    # Remove stale exports so a failed run cannot leave old assignments behind.
    for name in ('assignments.csv', 'clusters_ap.csv', 'representatives_ap.txt',
                 'buckets.csv', 'summary.json'):
        (requested / name).unlink(missing_ok=True)
    return requested

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inputs', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('results/AP_unweighted'),
                        help='Output base path; the input count is appended automatically.')
    args = parser.parse_args()
    records = ci.load(str(args.inputs))
    # Clustering and every downstream analysis see only the retained inputs.
    excluded = sorted(r['_file'] for r in records if r['_file'] in common.EXCLUDED_INPUTS)
    if excluded:
        print('excluded:', ', '.join(excluded))
        records = [r for r in records if r['_file'] not in common.EXCLUDED_INPUTS]
    buckets = ci.bucketize(records)
    out = create_output_directory(
        args.output.with_name(f'{args.output.name}_{len(records)}')
    )
    assign = []
    clusters = []
    diagnostics = []
    for bid, (key, rows) in enumerate(buckets.items(), 1):
        rows = sorted(rows, key=lambda r: r['_file'])
        n = len(rows)
        # Features use raw rise and unscaled sine/cosine pairs.
        X = np.array([r['_rise'] + [v for t in r['_twist'] for v in (np.sin(np.radians(t)), np.cos(np.radians(t)))] for r in rows])
        # Similarity is negative squared Euclidean distance.
        S = -((X[:, None, :] - X[None, :, :]) ** 2).sum(-1)
        # Exclude self-similarities when computing the bucket median.
        pref = float(np.median(S[~np.eye(n, dtype=bool)])) if n > 1 else 0.0
        with warnings.catch_warnings(record=True) as caught:
            warnings.simplefilter('always')
            ap = AffinityPropagation(affinity='precomputed', preference=pref, damping=0.9, max_iter=2000, convergence_iter=50, random_state=0).fit(S)
        conv = [str(w.message) for w in caught if issubclass(w.category, ConvergenceWarning)]
        valid = ap.cluster_centers_indices_ is not None and len(ap.cluster_centers_indices_) > 0 and (min(ap.labels_) >= 0)
        status = 'convergence_warning' if conv else 'ok'
        if not valid:
            status = 'no_solution'
        if not valid or conv:
            diagnostics.append(dict(bucket=bid, size=n, clusters=None, preference=pref, iterations=int(ap.n_iter_), status=status, warnings=' | '.join((str(w.message) for w in caught))))
            print('FAILED', bid, n, status)
            continue
        labels = ap.labels_
        order = sorted(set(labels.tolist()), key=lambda l: int(np.flatnonzero(labels == l)[0]))
        ren = {l: i + 1 for i, l in enumerate(order)}
        ex = {ren[l]: int(ap.cluster_centers_indices_[l]) for l in order}
        lab = np.array([ren[l] for l in labels])
        for c in sorted(ex):
            ids = np.flatnonzero(lab == c)
            dr, dt = ci.spread(rows, ids)
            clusters.append(dict(bucket=bid, cluster=c, size=len(ids), representative=rows[ex[c]]['_file'], rep_rise=rows[ex[c]]['rise'], rep_twist=rows[ex[c]]['twist'], rise_spread_A=round(dr, 3), twist_spread_deg=round(dt, 2), members=' '.join((rows[i]['_file'] for i in ids))))
        for i, r in enumerate(rows):
            assign.append(dict(file=r['_file'], bucket=bid, cluster=int(lab[i]), representative=rows[ex[int(lab[i])]]['_file'], is_representative=int(i == ex[int(lab[i])]), rise=r['rise'], twist=r['twist']))
        diagnostics.append(dict(bucket=bid, size=n, clusters=len(ex), preference=pref, iterations=int(ap.n_iter_), status=status, warnings=' | '.join((str(w.message) for w in caught))))
    
    def dump(name, rows):
        with open(out / name, 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0]))
            w.writeheader()
            w.writerows(rows)
    dump('buckets.csv', diagnostics)
    failed = [d for d in diagnostics if d['status'] != 'ok']
    stats = dict(n_files=len(records), n_buckets=len(buckets), n_clusters=len(clusters), n_assigned=len(assign), failed_buckets=failed, singleton_clusters=sum((c['size'] == 1 for c in clusters)), max_cluster_size=max((c['size'] for c in clusters)), max_rise_spread_A=max((c['rise_spread_A'] for c in clusters)), max_twist_spread_deg=max((c['twist_spread_deg'] for c in clusters)), affinity='precomputed', damping=0.9, max_iter=2000, convergence_iter=50, random_state=0, library_warnings=dict(collections.Counter((d['warnings'] for d in diagnostics if d['warnings']))), two_member_buckets=[{k: d[k] for k in ['bucket', 'clusters', 'iterations']} for d in diagnostics if d['size'] == 2], versions={'numpy': np.__version__, 'scipy': scipy.__version__, 'sklearn': sklearn.__version__})
    (out / 'summary.json').write_text(json.dumps(stats, indent=2), encoding='utf-8')
    if not failed:
        assert sum((c['size'] for c in clusters)) == len(assign) == len(records)
        dump('clusters_ap.csv', clusters)
        dump('assignments.csv', assign)
        (out / 'representatives_ap.txt').write_text('\n'.join((c['representative'] for c in clusters)) + '\n', encoding='utf-8')
    for key in ('n_files', 'n_buckets', 'n_clusters'):
        print(f'{key}: {stats[key]}')
    print(f'Output: {out.resolve()}')

if __name__ == "__main__":
    main()
