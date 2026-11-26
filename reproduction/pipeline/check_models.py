#!/usr/bin/env python3
"""Check that every expected archived model and energy file is present."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import common


def expected_keys() -> list[str]:
    return [record["key"] for record in common.retained_inputs()]


def missing_models(keys: list[str], models_dir: Path) -> list[str]:
    missing = []
    for key in keys:
        for depth in common.DEPTHS:
            for mode in common.MODES:
                pdb = common.model_path(key, depth, mode, models_dir)
                energy = common.energy_path(key, depth, mode, models_dir)
                if not pdb.exists() or pdb.stat().st_size == 0 or common.parse_etotal(energy) is None:
                    missing.append(f"{key}_{depth}_{mode}")
    return missing


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--models-dir", type=Path)
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    models_dir = (args.models_dir or common.MODELS).resolve()
    keys = expected_keys()
    expected = len(keys) * len(common.DEPTHS) * len(common.MODES)
    missing = missing_models(keys, models_dir)

    if missing:
        print(f"Main models: {expected - len(missing)}/{expected} complete; {len(missing)} missing", file=sys.stderr)
        for name in missing[:20]:
            print(f"  missing {name}", file=sys.stderr)
        if len(missing) > 20:
            print(f"  ... and {len(missing) - 20} more", file=sys.stderr)
        return 1
    if not args.quiet:
        print(f"Main models: {expected}/{expected} complete in {models_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
