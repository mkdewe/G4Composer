#!/usr/bin/env python3
"""Download the experimental references used for RMSD scoring.

Every key in ``references/manifest.csv`` pins the RCSB URL and the SHA256 of the
decompressed mmCIF.  A cached file is reused only if its hash still matches, so
an upstream change cannot silently alter the manuscript numbers.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import sys
import time
import urllib.request
from pathlib import Path

import common

UA = "g4composer-repro/1.0 (academic use)"


def fetch(url: str, retries: int = 3) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": UA})
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read()
            return gzip.decompress(data) if url.endswith(".gz") else data
        except Exception as exc:  # network errors are expected
            last = exc
            time.sleep(1.5 * (attempt + 1))
    raise RuntimeError(f"could not download {url}: {last}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true", help="re-download every reference")
    parser.add_argument("--keys", help="comma-separated keys; default all")
    args = parser.parse_args()

    manifest = list(csv.DictReader((common.ROOT / "references" / "manifest.csv").open(encoding="utf-8")))
    wanted = {key.strip() for key in args.keys.split(",")} if args.keys else None
    outdir = common.REFERENCES
    outdir.mkdir(parents=True, exist_ok=True)

    failures = 0
    for index, row in enumerate(manifest, 1):
        if wanted and row["key"] not in wanted:
            continue
        target = outdir / row["reference"]
        if not args.force and target.exists() and common.sha256(target) == row["sha256"]:
            continue
        print(f"[{index}/{len(manifest)}] {row['key']} <- {row['url'].rsplit('/', 1)[-1]}")
        try:
            data = fetch(row["url"])
        except RuntimeError as exc:
            print(f"  FAILED: {exc}", file=sys.stderr)
            failures += 1
            continue
        digest = hashlib.sha256(data).hexdigest()
        if digest != row["sha256"]:
            print(
                f"  FAILED: SHA256 mismatch for {row['key']}: got {digest[:12]}, "
                f"expected {row['sha256'][:12]}",
                file=sys.stderr,
            )
            failures += 1
            continue
        target.write_bytes(data)

    cached = sum(1 for row in manifest if (outdir / row["reference"]).exists())
    print(f"references present: {cached}/{len(manifest)}; failures: {failures}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
