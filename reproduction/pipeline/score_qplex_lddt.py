#!/usr/bin/env python3
"""Compute all-atom lDDT for the bundled 3D-NuS-Qplex models.

3D-NuS-Qplex uses blank chain IDs and residue names ADE/GUA/THY/CYT.  They are
normalised to chain A and standard DNA residue names before OpenStructure is
run against the same prepared reference used for the G4Composer models.
"""

from __future__ import annotations

import argparse
import csv
import os
import subprocess
import sys
from pathlib import Path

import common
import table1

RESIDUES = {"GUA": "DG", "ADE": "DA", "THY": "DT", "CYT": "DC"}


def standardise(source: Path, destination: Path) -> None:
    lines = []
    for line in source.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line.startswith(("ATOM", "HETATM")):
            continue
        element = line[76:78].strip() if len(line) >= 78 else ""
        atom_name = line[12:16].strip()
        if element in {"H", "D"} or (not element and atom_name.startswith(("H", "1H", "2H", "3H"))):
            continue
        residue = RESIDUES.get(line[17:20].strip(), line[17:20].strip())
        line = line[:17] + f"{residue:>3}" + line[20:21] + "A" + line[22:]
        lines.append(line)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qplex-dir", type=Path, default=common.ROOT / "qplex")
    parser.add_argument("--keys", help="comma-separated Table 1 keys (debugging)")
    parser.add_argument("--references-dir", type=Path, default=common.WORK / "lddt" / "references")
    parser.add_argument("--output-dir", type=Path, default=common.WORK / "lddt")
    parser.add_argument("--output", type=Path, default=common.DATA / "qplex_lddt.csv")
    parser.add_argument("--image", default=os.environ.get("OPENSTRUCTURE_IMAGE", "registry.scicore.unibas.ch/schwede/openstructure:2.12.0"))
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    json_dir = args.output_dir / "qplex_json"
    model_dir = args.output_dir / "qplex_models"
    json_dir.mkdir(exist_ok=True)
    model_dir.mkdir(exist_ok=True)
    args.json_dir = json_dir

    selected = set(args.keys.split(",")) if args.keys else None
    rows = []
    failures = 0
    for target in table1.TARGETS:
        if selected is not None and target["key"] not in selected:
            continue
        key = target["key"]
        pdb = target["pdb"]
        reference = args.references_dir / f"{key}.pdb"
        if not reference.exists():
            print(f"missing prepared reference for {key}; run prepare_lddt.py first", file=sys.stderr)
            failures += 1
            continue
        model = model_dir / target["qplex"]
        standardise(args.qplex_dir / target["qplex"], model)
        output = json_dir / f"{pdb}.json"
        command = [
            "docker", "run", "--rm", "-u", f"{os.getuid()}:{os.getgid()}",
            "-v", f"{model_dir}:/model:ro",
            "-v", f"{reference.parent}:/reference:ro",
            "-v", f"{json_dir}:/out",
            args.image,
            "compare-structures",
            "-m", f"/model/{model.name}",
            "-r", f"/reference/{reference.name}",
            "-c", "A:A",
            "--lddt",
            "--fault-tolerant",
            "--min-pep-length", "4",
            "--min-nuc-length", "4",
            "-o", f"/out/{output.name}",
        ]
        subprocess.run(command, capture_output=True, text=True, timeout=args.timeout)
        lddt = None
        status = "ok"
        error = ""
        if output.exists():
            payload = output.read_text(encoding="utf-8")
            import json
            data = json.loads(payload)
            lddt = data.get("lddt")
            if lddt is None:
                status = "failed"
                error = data.get("status", "no lddt")
        else:
            status = "failed"
            error = "no JSON output"
        if lddt is None:
            failures += 1
        rows.append(dict(pdb=pdb, key=key, qplex_model=model.name, reference=reference.name, lddt=lddt, status=status, error=error))
        print(f"{pdb}: lDDT={lddt if lddt is not None else 'FAILED'}", flush=True)

    with args.output.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=["pdb", "key", "qplex_model", "reference", "lddt", "status", "error"])
        writer.writeheader()
        for row in rows:
            row["lddt"] = "" if row["lddt"] is None else f"{row['lddt']:.6f}"
            writer.writerow(row)
    print(f"wrote {args.output}; failures {failures}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
