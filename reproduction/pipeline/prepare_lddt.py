#!/usr/bin/env python3
"""Prepare single-chain references for OpenStructure lDDT scoring.

The deposited assemblies cannot be passed directly to OpenStructure for every
input: some contain modified residues without a parent one-letter code, and
some name the author chain differently from a G4Composer model.  This script
extracts the author chain, removes non-polymer atoms and hydrogens, aligns the
reference and model sequences, and copies the model's residue name onto a
reference residue **only when the reference residue is non-standard**.  Canonical
reference bases are never renamed by residue number, which would corrupt
references whose author numbering does not start at 1.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import common

STANDARD_RESIDUES = {"A", "C", "G", "U", "DA", "DC", "DG", "DT"}


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


def model_residue_names(model: Path) -> dict[tuple[str, str], str]:
    names: dict[tuple[str, str], str] = {}
    for line in model.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith(("ATOM", "HETATM")):
            chain = line[21].strip()
            residue, name = atom_residue(line)
            names.setdefault((chain, residue), name)
    return names


def normalise_residue_names(
    reference: Path,
    model_names: dict[tuple[str, str], str],
    residue_map: dict[str, list[str]],
    key: str,
) -> list[tuple[str, str, str]]:
    """Rename only non-standard reference residues; return the changes."""
    changes: list[tuple[str, str, str]] = []
    seen_changes: set[str] = set()
    output = []
    for line in reference.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith(("ATOM", "HETATM")):
            residue, reference_name = atom_residue(line)
            if reference_name not in STANDARD_RESIDUES:
                mapped = residue_map.get(residue)
                if mapped:
                    model_name = model_names.get((mapped[0], mapped[1]))
                    if model_name and model_name != reference_name:
                        line = line[:17] + f"{model_name:>3}" + line[20:]
                        if residue not in seen_changes:
                            changes.append((key, reference_name, model_name))
                            seen_changes.add(residue)
            atom_name = line[12:16].strip()
            if atom_name == "OP1":
                line = line[:12] + " O1P" + line[16:]
            elif atom_name == "OP2":
                line = line[:12] + " O2P" + line[16:]
        output.append(line)
    reference.write_text("\n".join(output) + "\n", encoding="utf-8")
    return changes


def prepare_one(key: str, chain: str, model: Path, reference: Path, output: Path) -> tuple[str, list[tuple[str, str, str]]]:
    map_file = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False)
    map_path = map_file.name
    map_file.close()
    script = f"""
import json
from pymol import cmd

cmd.load({str(reference)!r}, "ref_obj")
cmd.remove("ref_obj and not polymer")
cmd.remove("ref_obj and hydro")
cmd.remove("ref_obj and not chain {chain!r}")
cmd.alter("ref_obj", "chain='A'")
cmd.alter("ref_obj and name OP1", 'name="O1P"')
cmd.alter("ref_obj and name OP2", 'name="O2P"')
cmd.load({str(model)!r}, "mdl_obj")
cmd.remove("mdl_obj and hydro")
cmd.alter("mdl_obj and name OP1", 'name="O1P"')
cmd.alter("mdl_obj and name OP2", 'name="O2P"')
cmd.align("mdl_obj", "ref_obj", cycles=0, object="aln", transform=0)
mapping = {{}}
for (left_name, left_index), (right_name, right_index) in cmd.get_raw_alignment("aln"):
    if left_name == "ref_obj" and right_name == "mdl_obj":
        reference_atom = cmd.get_model("ref_obj and index {{}}".format(left_index)).atom[0]
        model_atom = cmd.get_model("mdl_obj and index {{}}".format(right_index)).atom[0]
    else:
        reference_atom = cmd.get_model("ref_obj and index {{}}".format(right_index)).atom[0]
        model_atom = cmd.get_model("mdl_obj and index {{}}".format(left_index)).atom[0]
    mapping[reference_atom.resi] = [model_atom.chain, model_atom.resi]
with open({map_path!r}, "w", encoding="utf-8") as stream:
    json.dump(mapping, stream)
cmd.save({str(output)!r}, "ref_obj")
"""
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as stream:
        stream.write(script)
        script_path = stream.name
    try:
        proc = subprocess.run(["pymol", "-cq", script_path], capture_output=True, text=True)
        if proc.returncode != 0 or not output.exists():
            raise RuntimeError(
                f"PyMOL preparation failed for {key}: "
                f"{(proc.stdout or '')[-500:]} {(proc.stderr or '')[-500:]}"
            )
        residue_map = json.loads(Path(map_path).read_text(encoding="utf-8"))
        changes = normalise_residue_names(output, model_residue_names(model), residue_map, key)
        return key, changes
    finally:
        os.unlink(script_path)
        Path(map_path).unlink(missing_ok=True)


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
    changes: list[tuple[str, str, str]] = []
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {pool.submit(prepare_one, *task): task[0] for task in tasks}
        for future in as_completed(futures):
            key = futures[future]
            try:
                _, key_changes = future.result()
                changes.extend(key_changes)
                completed += 1
                if completed % 50 == 0:
                    print(f"prepared {completed}/{len(tasks)} references", flush=True)
            except Exception as exc:  # noqa: BLE001
                failures.append((key, str(exc)))
                print(f"FAIL {key}: {exc}", file=sys.stderr)

    report = common.REPORTS / "lddt_reference_changes.csv"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(
        "key,reference_residue,model_residue\n"
        + "\n".join(f"{key},{reference_name},{model_name}" for key, reference_name, model_name in changes)
        + "\n",
        encoding="utf-8",
    )
    print(f"prepared {completed}/{len(tasks)} references in {args.output_dir}")
    print(f"modified-residue renames: {len(changes)}; audit: {report}")
    if failures:
        failure_report = common.REPORTS / "lddt_reference_failures.txt"
        failure_report.write_text(
            "\n".join(f"{key}\t{error}" for key, error in failures) + "\n", encoding="utf-8"
        )
        print(f"reference failures: {len(failures)}; see {failure_report}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
