#!/usr/bin/env python3
"""Score the bundled 3D-NuS-Qplex models with the manuscript protocol.

The fifteen models in ``qplex/`` are scored exactly like the G4Composer models:
hydrogens are removed from both the model and the experimental reference, the
reference is restricted to the author chain named in the input record, the
phosphate-oxygen aliases ``OP1``/``O1P`` and ``OP2``/``O2P`` are normalised,
and PyMOL ``align`` is run for one pass (``cycles=0``).  The resulting
all-heavy-atom RMSD values are written to ``work/data/qplex_scores.csv``.
"""

from __future__ import annotations

import argparse
import csv
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import common
import table1

PYMOL_TEMPLATE = r'''
import json
from pathlib import Path
from pymol import cmd

targets = @@TARGETS@@
output = @@OUTPUT@@

rows = []
for item in targets:
    row = dict(item)
    cmd.reinitialize()
    try:
        cmd.load(item["reference"], "ref_obj")
        cmd.remove("ref_obj and not polymer")
        chain = item["chain"]
        if cmd.count_atoms(f'ref_obj and chain "{chain}"') == 0:
            raise ValueError("reference has no chain " + repr(chain))
        cmd.remove(f'ref_obj and not chain "{chain}"')
        # The 3D-NuS-Qplex files use O1P/O2P; normalise the reference to the same names.
        cmd.alter("ref_obj and name OP1", 'name="O1P"')
        cmd.alter("ref_obj and name OP2", 'name="O2P"')
        cmd.remove("ref_obj and hydro")

        cmd.load(item["path"], "qplex_obj")
        cmd.remove("qplex_obj and hydro")
        result = cmd.align("qplex_obj", "ref_obj", cycles=0)
        row["rmsd_heavy"] = float(result[0])
        row["n_heavy"] = int(result[1])
        row["status"] = "ok"
        row["error"] = ""
        print("QPLX", item["pdb"], f"{result[0]:.6f}", result[1])
    except Exception as exc:
        row["rmsd_heavy"] = None
        row["n_heavy"] = 0
        row["status"] = "align-failed"
        row["error"] = str(exc)
        print("QPLX", item["pdb"], "ERROR", repr(exc))
    rows.append(row)

with open(output, "w", encoding="utf-8") as stream:
    for row in rows:
        row["reference"] = Path(row["reference"]).name
        row["path"] = Path(row["path"]).name
    json.dump(dict(rows=rows), stream, indent=2)
'''


def build_targets(qplex_dir: Path, references: Path) -> list[dict]:
    targets = []
    for target in table1.TARGETS:
        reference = references / f"{target['pdb']}-assembly{target['assembly']}.cif"
        path = qplex_dir / target["qplex"]
        targets.append(
            dict(
                pdb=target["pdb"],
                key=target["key"],
                file=target["qplex"],
                reference=str(reference),
                path=str(path),
                chain=target["chain"],
            )
        )
    return targets


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qplex-dir", type=Path, default=common.ROOT / "qplex")
    parser.add_argument("--references", type=Path, default=common.REFERENCES)
    parser.add_argument("--output", type=Path, default=common.DATA / "qplex_scores.csv")
    args = parser.parse_args()

    targets = build_targets(args.qplex_dir, args.references)
    missing = [item for item in targets if not Path(item["path"]).exists()]
    missing_refs = [item for item in targets if not Path(item["reference"]).exists()]
    if missing:
        for item in missing:
            print(f"missing 3D-NuS-Qplex model: {item['path']}", file=sys.stderr)
        return 1
    if missing_refs:
        for item in missing_refs:
            print(f"missing reference: {item['reference']}", file=sys.stderr)
        print("run pipeline/fetch_references.py first", file=sys.stderr)
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)
    outdir = common.WORK / "data" / "pymol"
    outdir.mkdir(parents=True, exist_ok=True)
    json_output = outdir / "qplex_scores.json"

    script = (
        PYMOL_TEMPLATE
        .replace("@@TARGETS@@", repr(targets))
        .replace("@@OUTPUT@@", repr(str(json_output)))
    )
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as stream:
        stream.write(script)
        script_path = stream.name
    try:
        proc = subprocess.run(["pymol", "-cq", script_path], capture_output=True, text=True, timeout=1800)
        if proc.returncode != 0 or not json_output.exists():
            print(proc.stderr or proc.stdout, file=sys.stderr)
            return 1
        payload = json.loads(json_output.read_text(encoding="utf-8"))
    finally:
        Path(script_path).unlink(missing_ok=True)

    rows = payload["rows"]
    for row in rows:
        row["reference"] = Path(row["reference"]).name
    with args.output.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=["pdb", "key", "file", "reference", "chain", "rmsd_heavy", "n_heavy", "status", "error"],
        )
        writer.writeheader()
        for row in rows:
            writer.writerow({name: row.get(name, "") for name in writer.fieldnames})

    failures = [row for row in rows if row["status"] != "ok"]
    print(f"\nwrote {len(rows)} rows to {args.output}; failures: {len(failures)}")
    for row in rows:
        value = "" if row["rmsd_heavy"] is None else f"{row['rmsd_heavy']:.3f}"
        print(f"  {row['pdb']:<5} chain {row['chain']:<2} RMSD {value:>7} A  n={row['n_heavy']}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
