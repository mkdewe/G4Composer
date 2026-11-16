#!/usr/bin/env python3
"""Generate the Table 1 default-parameter inputs.

The default-parameter condition replaces the experimental rise and twist by the
nominal 3.4 A and 29 deg, keeping the experimental topology, path, chi and sugar
annotations.  This is the condition that best reproduces the archived default
run.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import common
import table1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=common.WORK / "inputs" / "table1")
    args = parser.parse_args()

    records = {record["key"]: record for record in map(common.describe_input, common.selected_input_paths())}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    keys = []
    for key in table1.keys():
        record = records[key]
        fields = common.parse_inp(Path(record["path"]))
        steps = max(1, record["tetrads"] - 1)
        fields = common.replace_field(fields, "rise", ";".join(["3.4"] * steps))
        fields = common.replace_field(fields, "twist", ";".join(["29"] * steps))
        common.write_inp(args.output_dir / record["file"], fields)
        keys.append(key)
    keys_file = common.WORK / "inputs" / "table1_keys.txt"
    keys_file.parent.mkdir(parents=True, exist_ok=True)
    keys_file.write_text("\n".join(keys) + "\n", encoding="utf-8")
    print(f"wrote {len(keys)} default-parameter inputs -> {args.output_dir}")
    print(f"keys -> {keys_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
