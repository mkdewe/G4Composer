#!/usr/bin/env python3
"""Compute global all-atom lDDT for archived G4Composer models.

OpenStructure 2.12.0 is run in its official Docker image.  The reference is
the single-chain PDB produced by ``prepare_lddt.py`` and is mapped to model
chain A.  Only the numeric lDDT score is retained; the JSON output stays in
the git-ignored work area.
"""

from __future__ import annotations

import argparse
import csv
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import common

ORDER = [(depth, mode) for depth in common.DEPTHS for mode in common.MODES]
HEADER = "key " + " ".join(f"{depth}{mode}" for depth, mode in ORDER)


def run_one(key: str, chain: str, depth: int, mode: str, args) -> dict:
    model = common.model_path(key, depth, mode, args.models_dir)
    reference = args.references_dir / f"{key}.pdb"
    output = args.json_dir / f"{key}_{depth}_{mode}.json"
    if not args.force:
        previous = args.previous.get((key, depth, mode))
        if previous is not None:
            return {
                "key": key, "depth": depth, "mode": mode, "lddt": previous,
                "status": "reused", "model": model.name,
                "reference": reference.name, "chain": chain, "error": "",
            }
    if args.force and output.exists():
        output.unlink()

    command = [
        "docker", "run", "--rm", "-u", f"{os.getuid()}:{os.getgid()}",
        "-v", f"{common.ROOT}:/repo:ro",
        "-v", f"{args.json_dir}:/out",
    ]
    if common.ROOT in reference.parents:
        model_argument = f"/repo/{model.relative_to(common.ROOT)}"
        reference_argument = f"/repo/{reference.relative_to(common.ROOT)}"
    else:
        command[7:7] = ["-v", f"{reference.parent}:/reference:ro"]
        model_argument = f"/repo/{model.relative_to(common.ROOT)}"
        reference_argument = f"/reference/{reference.name}"
    command += [
        args.image,
        "compare-structures",
        "-m", model_argument,
        "-r", reference_argument,
        "-c", "A:A",
        "--lddt",
        "--fault-tolerant",
        "--min-pep-length", "4",
        "--min-nuc-length", "4",
        "-o", f"/out/{output.name}",
    ]
    proc = subprocess.run(command, capture_output=True, text=True, timeout=args.timeout)
    score = None
    status = "ok"
    error = ""
    if output.exists():
        try:
            import json
            payload = json.loads(output.read_text(encoding="utf-8"))
            score = payload.get("lddt")
            if score is None:
                status = "failed"
                error = payload.get("status", "no lddt")
        except Exception as exc:  # noqa: BLE001
            status = "failed"
            error = str(exc)
    else:
        status = "failed"
        error = (proc.stderr or proc.stdout)[-1000:]
    return {
        "key": key,
        "depth": depth,
        "mode": mode,
        "lddt": score,
        "status": status,
        "error": error,
        "model": model.name,
        "reference": reference.name,
        "chain": chain,
    }


def load_previous(path: Path) -> dict[tuple[str, int, str], float]:
    previous = {}
    if not path.exists():
        return previous
    with path.open(encoding="utf-8") as stream:
        for row in csv.DictReader(stream):
            if row.get("lddt"):
                previous[(row["key"], int(row["depth"]), row["mode"])] = float(row["lddt"])
    return previous


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--keys-file", type=Path)
    parser.add_argument("--keys", help="comma-separated keys (debugging)")
    parser.add_argument("--models-dir", type=Path, default=common.MODELS)
    parser.add_argument("--references-dir", type=Path, default=common.WORK / "lddt" / "references")
    parser.add_argument("--json-dir", type=Path, default=common.WORK / "lddt" / "json")
    parser.add_argument("--output", type=Path, default=common.DATA / "lddt_scores.csv")
    parser.add_argument("--table", type=Path, default=common.DATA / "G16_lddt.txt")
    parser.add_argument("--jobs", type=int, default=12)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--image", default=os.environ.get("OPENSTRUCTURE_IMAGE", "registry.scicore.unibas.ch/schwede/openstructure:2.12.0"))
    args = parser.parse_args()
    args.previous = load_previous(args.output)

    records = {
        record["key"]: record
        for record in map(common.describe_input, sorted(args.inputs_dir.glob("*.inp")))
    }
    if args.keys:
        keys = [key.strip() for key in args.keys.split(",") if key.strip()]
    elif args.keys_file:
        keys = [line.strip() for line in args.keys_file.read_text(encoding="utf-8").splitlines() if line.strip()]
    else:
        keys = [
            key for key, record in records.items()
            if record["file"] not in common.MANUAL_EXCLUSIONS and key not in common.KNOWN_UNBUILDABLE
        ]

    missing = [key for key in keys if not (args.references_dir / f"{key}.pdb").exists()]
    if missing:
        print(f"missing {len(missing)} prepared references; run prepare_lddt.py first", file=sys.stderr)
        print("  " + "\n  ".join(missing[:10]), file=sys.stderr)
        return 1

    args.json_dir.mkdir(parents=True, exist_ok=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.table.parent.mkdir(parents=True, exist_ok=True)

    tasks = [(key, depth, mode) for key in keys for depth, mode in ORDER]
    results = []
    # Partial runs update only the named keys but preserve other scored rows.
    selected = set(keys)
    for (key, depth, mode), score in args.previous.items():
        if key not in selected:
            results.append({
                "key": key,
                "depth": depth,
                "mode": mode,
                "lddt": score,
                "status": "reused",
                "model": common.model_path(key, depth, mode, args.models_dir).name,
                "reference": f"{key}.pdb",
                "chain": records[key]["chain"],
            })
    failures = 0
    done = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {
            pool.submit(run_one, key, common.chain_candidates(records[key])[0], depth, mode, args): (key, depth, mode)
            for key, depth, mode in tasks
        }
        for future in as_completed(futures):
            result = future.result()
            results.append(result)
            done += 1
            if result["status"] == "failed":
                failures += 1
                print(f"FAIL {result['key']} {result['depth']}{result['mode']}: {result.get('error', '')[:160]}", file=sys.stderr)
            if done % 250 == 0:
                print(f"scored {done}/{len(tasks)} models; failures {failures}", flush=True)

    all_keys = sorted({*keys, *(key for key, _, _ in args.previous)})
    by_key = {key: {} for key in all_keys}
    for result in results:
        by_key[result["key"]][(result["depth"], result["mode"])] = result
    with args.output.open("w", newline="", encoding="utf-8") as stream:
        fieldnames = ["key", "pdb", "assembly", "chain", "depth", "mode", "model", "reference", "lddt", "status", "error"]
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        for result in sorted(results, key=lambda item: (item["key"], ORDER.index((item["depth"], item["mode"])))):
            record = records[result["key"]]
            writer.writerow({
                "key": result["key"],
                "pdb": record["pdb"],
                "assembly": record["assembly"],
                "chain": result["chain"],
                "depth": result["depth"],
                "mode": result["mode"],
                "model": result.get("model", common.model_path(result["key"], result["depth"], result["mode"], args.models_dir).name),
                "reference": result.get("reference", f"{result['key']}.pdb"),
                "lddt": "" if result["lddt"] is None else f"{result['lddt']:.6f}",
                "status": result["status"],
                "error": result.get("error", ""),
            })

    lines = [HEADER]
    complete = 0
    for key in all_keys:
        values = [by_key[key][(depth, mode)]["lddt"] for depth, mode in ORDER]
        if any(value is None for value in values):
            continue
        complete += 1
        lines.append(f"{key:<24} " + " ".join(f"{value:8.3f}" for value in values) + f" | {min(values):8.3f} {max(values):8.3f}")
    args.table.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"wrote {args.output} and {args.table}")
    print(f"complete inputs: {complete}/{len(all_keys)}; failed models: {failures}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
