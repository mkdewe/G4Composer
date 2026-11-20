#!/usr/bin/env python3
"""Shared PyMOL all-heavy-atom RMSD helper.

PyMOL ``align`` is used only to establish the reference-to-model residue
mapping.  The superposition and the reported RMSD are then computed over every
heavy atom present in both mapped residues, including phosphate oxygens.  This
avoids PyMOL's nucleic-acid alignment subset, which omits O1P/O2P from the
fitted atom set.
"""

from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path

PYMOL_TEMPLATE = r'''
import json
from pathlib import Path

import numpy as np
from pymol import cmd

jobs = @@JOBS@@
output = @@OUTPUT@@


def residue_atoms(object_name):
    atoms = {}
    for atom in cmd.get_model(object_name).atom:
        atoms.setdefault((atom.chain, atom.resi), {})[atom.name] = np.asarray(
            atom.coord, dtype=float
        )
    return atoms


def common_heavy_atoms(model_name, reference_name, residue_map):
    model = residue_atoms(model_name)
    reference = residue_atoms(reference_name)
    mobile = []
    target = []
    phosphate = 0
    for model_key, atoms in model.items():
        reference_key = residue_map.get(model_key)
        if reference_key is None:
            continue
        reference_atoms = reference.get(reference_key, {})
        for atom_name, coordinate in atoms.items():
            if atom_name not in reference_atoms:
                continue
            mobile.append(coordinate)
            target.append(reference_atoms[atom_name])
            if atom_name in ("O1P", "O2P"):
                phosphate += 1
    return np.asarray(mobile), np.asarray(target), phosphate


def kabsch_rmsd(mobile, target):
    mobile_centered = mobile - mobile.mean(axis=0)
    target_centered = target - target.mean(axis=0)
    left, _, right = np.linalg.svd(mobile_centered.T @ target_centered)
    sign = np.sign(np.linalg.det(right.T @ left.T))
    rotation = right.T @ np.diag([1.0, 1.0, sign]) @ left.T
    difference = (mobile_centered @ rotation.T) - target_centered
    return float(np.sqrt((difference ** 2).sum() / len(mobile)))


rows = []
for job in jobs:
    cmd.reinitialize()
    reference_name = Path(job["reference"]).name
    try:
        cmd.load(job["reference"], "ref_obj")
        cmd.remove("ref_obj and not polymer")
        cmd.remove("ref_obj and hydro")
        chain = None
        for candidate in job["candidates"]:
            if candidate and cmd.count_atoms('ref_obj and chain "{}"'.format(candidate)) > 0:
                chain = candidate
                break
        if chain is None:
            raise SystemExit("no reference chain matched: " + repr(job["candidates"]))
        cmd.remove('ref_obj and not chain "{}"'.format(chain))
        cmd.alter("ref_obj and name OP1", 'name="O1P"')
        cmd.alter("ref_obj and name OP2", 'name="O2P"')
        cmd.remove("ref_obj and hydro")

        for item in job["models"]:
            row = dict(item)
            row["reference"] = reference_name
            row["chain"] = chain
            try:
                cmd.load(item["path"], "mdl_obj")
                cmd.remove("mdl_obj and hydro")
                cmd.alter("mdl_obj and name OP1", 'name="O1P"')
                cmd.alter("mdl_obj and name OP2", 'name="O2P"')
                cmd.align("mdl_obj", "ref_obj", cycles=0, object="aln", transform=0)

                residue_map = {}
                for (left_name, left_index), (right_name, right_index) in cmd.get_raw_alignment("aln"):
                    if left_name == "ref_obj" and right_name == "mdl_obj":
                        reference_atom = cmd.get_model(
                            "ref_obj and index {}".format(left_index)
                        ).atom[0]
                        model_atom = cmd.get_model(
                            "mdl_obj and index {}".format(right_index)
                        ).atom[0]
                    else:
                        reference_atom = cmd.get_model(
                            "ref_obj and index {}".format(right_index)
                        ).atom[0]
                        model_atom = cmd.get_model(
                            "mdl_obj and index {}".format(left_index)
                        ).atom[0]
                    residue_map[(model_atom.chain, model_atom.resi)] = (
                        reference_atom.chain,
                        reference_atom.resi,
                    )

                mobile, target, phosphate = common_heavy_atoms(
                    "mdl_obj", "ref_obj", residue_map
                )
                if len(mobile) == 0:
                    raise ValueError("no common heavy atoms")
                row["rmsd_heavy"] = kabsch_rmsd(mobile, target)
                row["n_heavy"] = int(len(mobile))
                row["n_phosphate"] = int(phosphate)
                row["status"] = "ok"
                row["error"] = ""
                cmd.delete("mdl_obj")
            except Exception as exc:  # noqa: BLE001
                row["rmsd_heavy"] = None
                row["n_heavy"] = 0
                row["n_phosphate"] = 0
                row["status"] = "align-failed"
                row["error"] = str(exc)
                cmd.delete("mdl_obj")
            rows.append(row)
    except Exception as exc:  # noqa: BLE001
        for item in job["models"]:
            row = dict(item)
            row["reference"] = reference_name
            row["chain"] = ""
            row["rmsd_heavy"] = None
            row["n_heavy"] = 0
            row["n_phosphate"] = 0
            row["status"] = "reference-failed"
            row["error"] = str(exc)
            rows.append(row)

with open(output, "w", encoding="utf-8") as stream:
    json.dump(dict(rows=rows), stream, indent=2)
'''


def run_jobs(jobs: list[dict], output: Path, timeout: int = 1800) -> list[dict]:
    """Run one or more reference/model jobs in a fresh PyMOL process."""
    output.parent.mkdir(parents=True, exist_ok=True)
    script = PYMOL_TEMPLATE.replace("@@JOBS@@", repr(jobs)).replace(
        "@@OUTPUT@@", repr(str(output))
    )
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as stream:
        stream.write(script)
        script_path = stream.name
    try:
        proc = subprocess.run(
            ["pymol", "-cq", script_path], capture_output=True, text=True, timeout=timeout
        )
    finally:
        Path(script_path).unlink(missing_ok=True)
    if proc.returncode != 0 or not output.exists():
        raise RuntimeError(
            "PyMOL RMSD calculation failed: "
            + (proc.stderr or proc.stdout or "no output")[-2000:]
        )
    payload = json.loads(output.read_text(encoding="utf-8"))
    return payload["rows"]
