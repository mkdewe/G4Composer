#!/usr/bin/env python3
"""Remove host-specific paths from archived PDB and energy files.

PDB files lose the run-specific ``REMARK FILENAME`` and ``REMARK DATE`` lines;
coordinates and atom records are untouched.  Energy files are reduced to their
``Etotal`` summary line, which is all the analysis code reads.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

import common

ETOTAL = re.compile(r"Etotal\s*=\s*-?\d")


def sanitize_pdb(path: Path) -> bool:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    kept = [
        line
        for line in lines
        if not line.startswith("REMARK FILENAME=") and not line.startswith("REMARK DATE:")
    ]
    if kept != lines:
        path.write_text("\n".join(kept) + "\n", encoding="utf-8")
        return True
    return False


def sanitize_energy(path: Path) -> bool:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    summary = next((line for line in lines if ETOTAL.search(line)), None)
    if summary is None:
        raise SystemExit(f"no Etotal line in {path}")
    text = "# G4Composer 1.0 / Xplor-NIH 2.39 energy summary\n" + summary.rstrip() + "\n"
    if text != path.read_text(encoding="utf-8", errors="replace"):
        path.write_text(text, encoding="utf-8")
        return True
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "directories",
        nargs="*",
        type=Path,
        help="directories to sanitize (default: models and examples/reference)",
    )
    args = parser.parse_args()

    directories = args.directories or [
        common.MODELS,
        common.ROOT.parent / "examples" / "reference",
    ]
    changed_pdb = changed_energy = 0
    for directory in directories:
        directory = directory.resolve()
        if not directory.exists():
            continue
        for pdb in directory.glob("*.pdb"):
            changed_pdb += int(sanitize_pdb(pdb))
        for energy in directory.glob("*_energy.txt"):
            changed_energy += int(sanitize_energy(energy))
    print(f"sanitized {changed_pdb} PDB and {changed_energy} energy file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
