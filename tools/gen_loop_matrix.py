#!/usr/bin/env python3
"""
Regenerate the loop-type probability grid embedded in
G4Composer.Server/Domain/TopologyPredictor.cs (Outer / Central matrices).

It reads the deposited unimolecular G4 .inp set produced by the offline modelling pipeline
(out-quadro/inp/*.inp + manifest.csv — NOT committed; a local PDB-derived dataset) and emits
the smoothed P(type | position, length) table as both a readable matrix and a C# initializer.

Loop TYPE is recovered by inverting the .inp `path` (same algebra as silvaTopology.buildPath):
each G-tract's direction is Up if its tetrad letters ascend (A->B->C) in path order, Down if they
descend; between consecutive tracts, |delta track|==2 -> Diagonal, ==1 with a direction flip ->
Lateral, ==1 without -> Propeller. Loop LENGTH is the gap between the matching ^-runs in
`structure`. Probabilities are Dirichlet-smoothed (alpha) over the sterically-allowed types
(Propeller: any len; Lateral: len>=1; Diagonal: len>=3), so every allowed cell stays > 0.

Usage:  python tools/gen_loop_matrix.py [OUT_QUADRO_DIR]   (default: ./out-quadro)
"""
import csv, glob, os, re, sys
from collections import defaultdict

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.getcwd(), "out-quadro")
INP_DIR = os.path.join(ROOT, "inp")
ALPHA = 0.5
MAXLEN = 6

mol = {}
with open(os.path.join(ROOT, "manifest.csv"), newline="") as f:
    for row in csv.DictReader(f):
        mol[row["id"]] = (row.get("type") or "").strip().lower()

def get(txt, key):
    m = re.search(rf"^{key}\s+(.+)$", txt, re.MULTILINE)
    return m.group(1).strip() if m else ""

def path_tracts(path):
    entries = []
    for tok in path.split(";"):
        m = re.match(r"^([A-D])(\d)$", tok.strip())
        if not m: return None
        entries.append((m.group(1), int(m.group(2))))
    tracts, i = [], 0
    while i < len(entries):
        st = entries[i][1]; letters = []
        while i < len(entries) and entries[i][1] == st:
            letters.append(entries[i][0]); i += 1
        if len(letters) < 2: return None
        asc = all(letters[k] > letters[k-1] for k in range(1, len(letters)))
        desc = all(letters[k] < letters[k-1] for k in range(1, len(letters)))
        if not (asc or desc): return None
        tracts.append((st, "U" if asc else "D"))
    if len(tracts) != 4 or sorted(t[0] for t in tracts) != [1, 2, 3, 4]: return None
    return tracts

def loop_types(tracts):
    out = []
    for i in range(3):
        s0, d0 = tracts[i]; s1, d1 = tracts[i+1]
        delta = (s1 - s0) % 4
        if delta == 2: out.append("D")
        elif delta in (1, 3): out.append("L" if d0 != d1 else "P")
        else: return None
    return out

def loop_lengths(structure):
    runs = [m.span() for m in re.finditer(r"\^+", structure)]
    if len(runs) != 4: return None
    return [runs[i+1][0] - runs[i][1] for i in range(3)]

counts = defaultdict(lambda: defaultdict(int))
for pf in sorted(glob.glob(os.path.join(INP_DIR, "*.inp"))):
    sid = os.path.splitext(os.path.basename(pf))[0]
    if mol.get(sid) != "dna":
        continue
    txt = open(pf, encoding="utf-8", errors="replace").read()
    tr = path_tracts(get(txt, "path")); ln = loop_lengths(get(txt, "structure"))
    if tr is None or ln is None: continue
    ty = loop_types(tr)
    if ty is None: continue
    for pos_i, (L, T) in enumerate(zip(ln, ty), 1):
        pos = "central" if pos_i == 2 else "outer"
        counts[(pos, min(L, MAXLEN))][T] += 1

def allowed(length):
    a = ["P"]
    if length >= 1: a.append("L")
    if length >= 3: a.append("D")
    return a

def vector(pos, length):
    c = counts.get((pos, min(length, MAXLEN)), {})
    raw = {t: c.get(t, 0) + ALPHA for t in allowed(length)}
    s = sum(raw.values())
    return {t: raw[t] / s for t in raw}

def emit(pos):
    return "\n".join(
        f"        new[] {{ {v.get('P',0):.4f}, {v.get('L',0):.4f}, {v.get('D',0):.4f} }},   // {L}"
        for L in range(0, MAXLEN + 1)
        for v in [vector(pos, L)]
    )

print("    private static readonly double[][] Outer =\n    {\n" + emit("outer") + "\n    };")
print("    private static readonly double[][] Central =\n    {\n" + emit("central") + "\n    };")
