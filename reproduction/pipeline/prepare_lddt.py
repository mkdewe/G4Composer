#!/usr/bin/env python3
"""Prepare single-chain references for OpenStructure lDDT scoring.

The deposited assemblies cannot be passed directly to OpenStructure for every
input: some contain modified residues without a parent one-letter code, and
some name the author chain differently from a G4Composer model.  This script
extracts the author chain, removes non-polymer atoms and hydrogens, and copies
the model's residue names onto the reference.  Unknown atoms belonging to a
standardised residue (for example, a fluorine on a renamed guanosine) are left
for OpenStructure's normal cleanup to remove.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import common


def keys_for(inputs_dir: Path, keys_file: Path | None) -> list[str]:
    if keys_file:
        return [line.strip() for line in keys_file.read_text(encoding="utf-8").splitlines() if line.strip()]
    return [
        record["key"]
        for record in map(common.describe_input, sorted(inputs_dir.glob("*.inp")))
        if record["file"] not in common.MANUAL_EXCLUSIONS
    ]


def atom_residue(line: str) -> tuple[str, str]:
    """Return (residue sequence number + insertion code, residue name)."""
    return line[22:27].strip(), line[17:20].strip()


def normalise_residue_names(reference: Path, model: Path) -> None:
    model_residues: dict[str, str] = {}
    for line in model.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith(("ATOM", "HETATM")):
            residue, name = atom_residue(line)
            model_residues[residue] = name

    output = []
    for line in reference.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith(("ATOM", "HETATM")):
            residue, _ = atom_residue(line)
            if residue in model_residues:
                line = line[:17] + f"{model_residues[residue]:>3}" + line[20:]
            atom_name = line[12:16].strip()
            if atom_name == "OP1":
                line = line[:12] + " O1P" + line[16:]
            elif atom_name == "OP2":
                line = line[:12] + " O2P" + line[16:]
        output.append(line)
    reference.write_text("\n".join(output) + "\n", encoding="utf-8")


def prepare_one(key: str, chain: str, model: Path, reference: Path, output: Path) -> str:
    cmd = f"""
from pymol import cmd
cmd.load({str(reference)!r}, "ref_obj")
cmd.remove("ref_obj and not polymer")
cmd.remove("ref_obj and hydro")
cmd.remove("ref_obj and not chain {chain!r}")
cmd.alter("ref_obj", "chain='A'")
cmd.save({str(output)!r}, "ref_obj")
"""
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as stream:
        stream.write(cmd)
        script = stream.name
    try:
        proc = subprocess.run(["pymol", "-cq", script], capture_output=True, text=True)
    finally:
        os.unlink(script)
    if proc.returncode != 0 or not output.exists():
        raise RuntimeError(f"PyMOL preparation failed for {key}: {proc.stdout[-500:]} {proc.stderr[-500:]}")
    normalise_residue_names(output, model)
    return key


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--keys", help="comma-separated keys (debugging)")
    parser.add_argument("--keys-file", type=Path)
    parser.add_argument("--models-dir", type=Path, default=common.MODELS)
    parser.add_argument("--references", type=Path, default=common.REFERENCES)
    parser.add_argument("--output-dir", type=Path, default=common.WORK / "lddt" / "references")
    parser.add_argument("--jobs", type=int, default=8)
    args = parser.parse_args()

    records = {
        record["key"]: record
        for record in map(common.describe_input, sorted(args.inputs_dir.glob("*.inp")))
    }
    keys = [key.strip() for key in args.keys.split(",") if key.strip()] if args.keys else keys_for(args.inputs_dir, args.keys_file)
    args.output_dir.mkdir(parents=True, exist_ok=True)

    tasks = []
    failures = []
    for key in keys:
        if key in common.KNOWN_UNBUILDABLE:
            continue
        record = records[key]
        model = common.model_path(key, 30, "n", args.models_dir)
        reference = args.references / f"{record['pdb']}-assembly{record['assembly']}.cif"
        output = args.output_dir / f"{key}.pdb"
        if not reference.exists():
            failures.append((key, f"missing {reference.name}"))
            continue
        if not model.exists():
            failures.append((key, "missing model for residue-name mapping"))
            continue
        tasks.append((key, common.chain_candidates(record)[0], model, reference, output))

    if failures:
        for key, error in failures:
            print(f"  skip {key}: {error}", file=sys.stderr)

    completed = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {pool.submit(prepare_one, *task): task[0] for task in tasks}
        for future in as_completed(futures):
            key = futures[future]
            try:
                prepare_one_result = future.result()
                completed += 1
                if completed % 50 == 0:
                    print(f"prepared {completed}/{len(tasks)} references", flush=True)
            except Exception as exc:  # noqa: BLE001
                failures.append((key, str(exc)))
                print(f"FAIL {key}: {exc}", file=sys.stderr)

    print(f"prepared {completed}/{len(tasks)} references in {args.output_dir}")
    if failures:
        report = common.REPORTS / "lddt_reference_failures.txt"
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_text("\n".join(f"{key}\t{error}" for key, error in failures) + "\n", encoding="utf-8")
        print(f"reference failures: {len(failures)}; see {report}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
