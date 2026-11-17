#!/usr/bin/env python3
"""Build the 12 models per input with engine 16 in the ``quadro:latest`` image.

Engine 16 is single-pass, so each (iteration depth, mode) pair is one container
run.  The alternative mode is obtained by generating the mirrored input
ourselves (``common.mirror_fields``).
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import common

MANUAL_EXCLUSIONS = {"7zj4-assembly1-E.inp", "7zj5-assembly1-E.inp"}
KNOWN_UNBUILDABLE = {
    "4ni7_assembly1_B": "ERROR 106 bledna para (mixed DNA/RNA base pair)",
    "4ni9_assembly1_B": "ERROR 106 bledna para (mixed DNA/RNA base pair)",
    "4ni9_assembly1_D": "ERROR 106 bledna para (mixed DNA/RNA base pair)",
}


def build_fields(record: dict, name: str, depth: int, mode: str) -> list[tuple[str, str]]:
    fields = common.parse_inp(Path(record["path"]))
    fields = common.replace_field(fields, "name", name)
    fields = common.remove_fields(fields, {"iteration", "iteration_steps", "test", "rm_level"})
    fields.append(("test", "n"))
    fields.append(("rm_level", "5"))
    fields.append(("iteration", str(depth)))
    if mode == "a":
        fields = common.mirror_fields(fields)
        fields = common.replace_field(fields, "name", name)
    return fields


def task_done(key: str, depth: int, mode: str, models_dir: Path) -> bool:
    pdb = common.model_path(key, depth, mode, models_dir)
    energy = common.energy_path(key, depth, mode, models_dir)
    return pdb.exists() and pdb.stat().st_size > 0 and common.parse_etotal(energy) is not None


def run_task(record: dict, depth: int, mode: str, args) -> dict:
    key = record["key"]
    name = f"{key}_{depth}_{mode}"
    tag = f"d{depth}_{mode}"
    if task_done(key, depth, mode, args.models_dir):
        return {"key": key, "tag": tag, "status": "skip"}

    run_dir = args.runs_dir / key / tag
    if run_dir.exists():
        shutil.rmtree(run_dir)
    run_dir.mkdir(parents=True)

    fields = build_fields(record, name, depth, mode)
    input_path = run_dir / f"{name}.inp"
    common.write_inp(input_path, fields)

    args.models_dir.mkdir(parents=True, exist_ok=True)
    args.logs_dir.mkdir(parents=True, exist_ok=True)

    rel_input = input_path.relative_to(common.ROOT)
    rel_models = args.models_dir.relative_to(common.ROOT)
    docker = [
        "docker", "run", "--rm",
        "--user", f"{os.getuid()}:{os.getgid()}",
        "-v", f"{common.ROOT}:/work",
        "-w", "/work",
        args.image,
        "--no-mirror",
        "--outdir", str(rel_models),
        str(rel_input),
    ]
    started = time.time()
    try:
        proc = subprocess.run(docker, capture_output=True, text=True, timeout=args.timeout)
        output = (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired as exc:
        output = f"TIMEOUT after {args.timeout}s\n{exc.stdout or ''}\n{exc.stderr or ''}"
        proc = None
    elapsed = time.time() - started

    log_path = args.logs_dir / f"{name}.log"
    log_path.write_text(output, encoding="utf-8", errors="replace")

    runlog = args.models_dir / f"{name}.runlog"
    if runlog.exists():
        shutil.move(str(runlog), str(args.logs_dir / f"{name}.runlog"))

    ok = task_done(key, depth, mode, args.models_dir)
    if ok:
        shutil.rmtree(run_dir, ignore_errors=True)
    status = "ok" if ok else "failed"
    return {"key": key, "tag": tag, "status": status, "seconds": round(elapsed, 1), "log": str(log_path)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--keys", help="comma-separated keys (debugging)")
    parser.add_argument("--keys-file", type=Path)
    parser.add_argument("--inputs-dir", type=Path, default=common.INPUTS)
    parser.add_argument("--models-dir", type=Path, default=common.MODELS)
    parser.add_argument("--runs-dir", type=Path, default=common.WORK / "runs")
    parser.add_argument("--logs-dir", type=Path, default=common.WORK / "logs")
    parser.add_argument("--image", default=os.environ.get("QUADRO_IMAGE", "quadro:latest"))
    parser.add_argument("--jobs", type=int, default=12)
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args()
    args.inputs_dir = args.inputs_dir.resolve()
    args.models_dir = args.models_dir.resolve()
    args.runs_dir = args.runs_dir.resolve()
    args.logs_dir = args.logs_dir.resolve()

    records = {
        record["key"]: record
        for record in map(common.describe_input, sorted(args.inputs_dir.glob("*.inp")))
    }
    if args.keys:
        keys = [key.strip() for key in args.keys.split(",") if key.strip()]
    elif args.keys_file:
        keys = [line.strip() for line in args.keys_file.read_text(encoding="utf-8").splitlines() if line.strip()]
    else:
        keys = [key for key, record in records.items() if record["file"] not in MANUAL_EXCLUSIONS]

    tasks = []
    skipped = []
    for key in keys:
        if key in KNOWN_UNBUILDABLE:
            skipped.append(key)
            continue
        for depth in common.DEPTHS:
            for mode in common.MODES:
                tasks.append((records[key], depth, mode))

    print(f"inputs: {len(keys)}, tasks: {len(tasks)}, skipped known-unbuildable: {len(skipped)}")
    for key in skipped:
        print(f"  skip {key}: {KNOWN_UNBUILDABLE[key]}")
    if skipped:
        report = common.REPORTS / "known_unbuildable.txt"
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_text(
            "\n".join(f"{key}\t{KNOWN_UNBUILDABLE[key]}" for key in skipped) + "\n",
            encoding="utf-8",
        )

    failures = 0
    done = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {pool.submit(run_task, record, depth, mode, args): (record["key"], depth, mode) for record, depth, mode in tasks}
        for future in as_completed(futures):
            done += 1
            result = future.result()
            label = f"[{done}/{len(tasks)}] {result['key']} {result['tag']}"
            if result["status"] == "failed":
                failures += 1
                print(f"{label} FAILED")
            elif result["status"] == "ok":
                print(f"{label} ok ({result['seconds']}s)")
            else:
                print(f"{label} skip")

    if failures:
        print(f"{failures} task(s) failed; see {args.logs_dir}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
