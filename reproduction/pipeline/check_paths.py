#!/usr/bin/env python3
"""Fail if archived artifacts contain host-specific absolute paths."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import common

PATTERNS = (b"/home/", b"/tmp/", b"/work/", b"/opt/", str(common.ROOT).encode())
TEXT_SUFFIXES = {".pdb", ".txt", ".csv", ".json", ".md", ".sha256"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "directories",
        nargs="*",
        type=Path,
        help="directories to scan (default: models, qplex, reports, figures)",
    )
    args = parser.parse_args()

    directories = args.directories or [
        common.MODELS,
        common.ROOT / "qplex",
        common.ROOT / "reports",
        common.ROOT / "figures",
        common.ROOT.parent / "examples" / "reference",
    ]
    findings = []
    for directory in directories:
        directory = directory.resolve()
        if not directory.exists():
            continue
        for path in directory.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
                continue
            data = path.read_bytes()
            for pattern in PATTERNS:
                if pattern in data:
                    findings.append((path, pattern.decode("utf-8", "replace")))

    if findings:
        for path, pattern in findings[:50]:
            print(f"{path}: contains {pattern!r}", file=sys.stderr)
        print(f"{len(findings)} file(s) contain absolute paths", file=sys.stderr)
        return 1
    print("no absolute paths in archived artifacts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
