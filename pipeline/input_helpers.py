import os, glob, sys
from collections import OrderedDict, defaultdict
import numpy as np
KEY_FIELDS = ("sequence", "structure", "orient", "path")

def parse_inp(path: str) -> dict:
    """Read one input file and parse rise and twist arrays."""
    d: dict[str, str] = {}
    with open(path, encoding='utf-8', errors='replace') as fh:
        for line in fh:
            parts = line.split(None, 1)
            if len(parts) == 2:
                d[parts[0]] = parts[1].strip()
    missing = [f for f in (*KEY_FIELDS, 'rise', 'twist') if f not in d]
    if missing:
        raise ValueError(f'{path}: missing fields {missing}')
    d['_rise'] = [float(x) for x in d['rise'].split(';') if x.strip()]
    d['_twist'] = [float(x) for x in d['twist'].split(';') if x.strip()]
    if len(d['_rise']) != len(d['_twist']):
        raise ValueError(f"{path}: rise length is {len(d['_rise'])} and twist length is {len(d['_twist'])}")
    d['_file'] = os.path.basename(path)
    d['_pdb'] = d['_file'].split('-')[0]
    return d

def load(indir: str) -> list[dict]:
    """Read input files in filename order."""
    files = sorted(glob.glob(os.path.join(indir, '*.inp')))
    if not files:
        sys.exit(f'no .inp files in {indir}')
    return [parse_inp(f) for f in files]

def bucketize(records: list[dict]) -> 'OrderedDict[tuple, list[dict]]':
    """Group by exact sequence, structure, orientation and path."""
    buckets: dict[tuple, list[dict]] = defaultdict(list)
    for r in records:
        buckets[tuple((r[f] for f in KEY_FIELDS))].append(r)
    ordered = sorted(buckets.items(), key=lambda kv: (-len(kv[1]), kv[0]))
    return OrderedDict(ordered)

def spread(rows: list[dict], idx: np.ndarray) -> tuple[float, float]:
    """Return maximum rise range and minimum covering twist arc across positions."""
    rise = np.array([rows[i]['_rise'] for i in idx], dtype=float)
    tw = np.array([rows[i]['_twist'] for i in idx], dtype=float)
    dr = float((rise.max(axis=0) - rise.min(axis=0)).max())
    dt = 0.0
    for k in range(tw.shape[1]):
        a = np.radians(tw[:, k])
        s = np.sort(np.degrees(np.arctan2(np.sin(a), np.cos(a))) % 360.0)
        if len(s) == 1:
            arc = 0.0
        else:
            gaps = np.diff(np.concatenate([s, s[:1] + 360.0]))
            arc = 360.0 - gaps.max()
        dt = max(dt, arc)
    return (dr, dt)
