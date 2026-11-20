#!/usr/bin/env python3
"""Score the bundled 3D-NuS-Qplex models with the manuscript protocol.

The fifteen models in ``qplex/`` are scored exactly like the G4Composer models:
hydrogens are removed from both the model and the experimental reference, the
reference is restricted to the author chain named in the input record, the
phosphate-oxygen aliases ``OP1``/``O1P`` and ``OP2``/``O2P`` are normalised, and
PyMOL ``align`` is used only to establish the residue mapping.  The resulting
superposition and RMSD use every heavy atom present in both mapped residues,
including phosphate oxygens.
"""

from __future__ import annotations

import argparse
import csv
import sys
from pathlib import Path

import common
import pymol_rmsd
import table1


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

    jobs = []
    for target in targets:
        jobs.append(
            dict(
                reference=target["reference"],
                candidates=[target["chain"]],
                models=[
                    dict(
                        pdb=target["pdb"],
                        key=target["key"],
                        file=target["file"],
                        path=target["path"],
                    )
                ],
            )
        )
    try:
        payload = pymol_rmsd.run_jobs(jobs, json_output)
    except Exception as exc:  # noqa: BLE001
        print(f"PyMOL RMSD calculation failed: {exc}", file=sys.stderr)
        return 1

    rows = []
    for item in payload:
        rows.append(
            dict(
                pdb=item.get("pdb", ""),
                key=item.get("key", ""),
                file=item.get("file", ""),
                reference=item.get("reference", ""),
                chain=item.get("chain", ""),
                rmsd_heavy=item.get("rmsd_heavy"),
                n_heavy=item.get("n_heavy", 0),
                n_phosphate=item.get("n_phosphate", 0),
                status=item.get("status", "ok"),
                error=item.get("error", ""),
            )
        )

    with args.output.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=[
                "pdb", "key", "file", "reference", "chain", "rmsd_heavy",
                "n_heavy", "n_phosphate", "status", "error",
            ],
        )
        writer.writeheader()
        writer.writerows(rows)

    failures = [row for row in rows if row["status"] != "ok"]
    print(f"\nwrote {len(rows)} rows to {args.output}; failures: {len(failures)}")
    for row in rows:
        value = "" if row["rmsd_heavy"] is None else f"{row['rmsd_heavy']:.3f}"
        print(f"  {row['pdb']:<5} chain {row['chain']:<2} RMSD {value:>7} A  n={row['n_heavy']}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
