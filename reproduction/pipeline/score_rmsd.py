#!/usr/bin/env python3
"""Score every model against its experimental reference.

Protocol
--------
* hydrogens are removed from both the model and the reference;
* the reference is restricted to the author chain named by the input record
  (``auth_asym_id``, which PyMOL uses as its chain identifier), not to
  whichever chain PyMOL would prefer;
* the phosphate-oxygen aliases ``OP1``/``O1P`` and ``OP2``/``O2P`` are
  normalised on both structures;
* PyMOL ``align`` is used only to establish the reference-to-model residue
  mapping;
* the model is then superposed, and the RMSD is calculated, over all heavy
  atoms present in both mapped residues, including phosphate oxygens.
"""

from __future__ import annotations

import argparse
import csv
import sys
from pathlib import Path

import common
import pymol_rmsd


def resolve_chain(record: dict) -> list[str]:
    """Return PyMOL chain candidates for the record.

    PyMOL addresses mmCIF chains by ``auth_asym_id``, which is exactly the chain
    identifier used in the input filename, so no label mapping is needed.
    Duplicated copies carry a ``-N`` suffix (e.g. ``C-2``).
    """
    candidates = []
    for candidate in (record.get("chain_token"), record["chain"]):
        if candidate and candidate not in candidates:
            candidates.append(candidate)
    return candidates


def score_key(record: dict, args) -> list[dict]:
    key = record["key"]
    reference = common.REFERENCES / f"{record['pdb']}-assembly{record['assembly']}.cif"
    models = []
    for depth in common.DEPTHS:
        for mode in common.MODES:
            path = common.model_path(key, depth, mode, args.models_dir)
            if path.exists() and path.stat().st_size > 0:
                models.append(dict(key=key, depth=depth, mode=mode, path=str(path)))
    if not reference.exists():
        return []
    if not models:
        return []

    candidates = resolve_chain(record)
    outdir = common.WORK / "data" / "pymol"
    outdir.mkdir(parents=True, exist_ok=True)
    output = outdir / f"{key}.json"
    job = dict(reference=str(reference), candidates=candidates, models=models)
    try:
        payload = pymol_rmsd.run_jobs([job], output)
    except Exception as exc:  # noqa: BLE001
        return [
            dict(
                key=key, depth=item["depth"], mode=item["mode"],
                reference=reference.name, chain="", rmsd_heavy="", n_heavy=0,
                n_phosphate=0, status="pymol-failed", error=str(exc)[-500:],
            )
            for item in models
        ]

    rows = []
    for item in payload:
        rows.append(
            dict(
                key=item.get("key", key),
                depth=item["depth"],
                mode=item["mode"],
                reference=item.get("reference", reference.name),
                chain=item.get("chain", ""),
                rmsd_heavy=item.get("rmsd_heavy"),
                n_heavy=item.get("n_heavy", 0),
                n_phosphate=item.get("n_phosphate", 0),
                status=item.get("status", "ok"),
                error=item.get("error", ""),
            )
        )
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--keys", help="comma-separated keys (debugging)")
    parser.add_argument("--keys-file", type=Path)
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--models-dir", type=Path, default=common.MODELS)
    parser.add_argument("--output", type=Path, default=common.DATA / "scores.csv")
    args = parser.parse_args()

    records = {
        record["key"]: record
        for record in map(common.describe_input, sorted(args.inputs_dir.glob("*.inp")))
    }
    if args.keys:
        keys = [key.strip() for key in args.keys.split(",") if key.strip()]
    elif args.keys_file:
        keys = [line.strip() for line in args.keys_file.read_text(encoding="utf-8").splitlines() if line.strip()]
    else:
        keys = list(records)

    rows = []
    skipped = []
    for index, key in enumerate(keys, 1):
        result = score_key(records[key], args)
        if not result:
            skipped.append(key)
            continue
        rows.extend(result)
        ok = sum(1 for row in result if row["status"] == "ok")
        print(f"[{index}/{len(keys)}] {key}: {ok}/{len(result)} ok")

    if not rows:
        print("ERROR: no models scored", file=sys.stderr)
        return 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    failures = sum(1 for row in rows if row["status"] != "ok")
    print(f"\nwrote {len(rows)} rows to {args.output}; failures: {failures}; skipped: {len(skipped)}")
    if skipped:
        print(f"  skipped (no models): {', '.join(skipped)}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
