#!/usr/bin/env python3
"""Shared paths, input parsing and small helpers for the reproduction."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INPUTS = ROOT / "inputs" / "current_548"
WORK = ROOT / "work"
REFERENCES = WORK / "references"
MODELS = WORK / "models"
DATA = WORK / "data"
RESULTS = WORK / "results"
REPORTS = WORK / "reports"

DEPTHS = (30, 50, 70, 100, 150, 300)
MODES = ("n", "a")

INPUT_RE = re.compile(
    r"^(?P<pdb>[0-9][a-z0-9]{3})-assembly(?P<assembly>\d+)-(?P<chain>[A-Za-z0-9]+?)(?:-(?P<dup>\d+))?\.inp$",
    re.I,
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_inp(path: Path) -> list[tuple[str, str]]:
    fields = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, _, value = line.partition(" ")
        fields.append((key, value.strip()))
    return fields


def field_dict(fields: list[tuple[str, str]]) -> dict[str, str]:
    return dict(fields)


def write_inp(path: Path, fields: list[tuple[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(f"{key:<12}{value}" for key, value in fields) + "\n", encoding="utf-8")


def replace_field(fields: list[tuple[str, str]], key: str, value: str) -> list[tuple[str, str]]:
    out = []
    seen = False
    for name, current in fields:
        if name == key:
            if not seen:
                out.append((key, value))
                seen = True
            continue
        out.append((name, current))
    if not seen:
        out.append((key, value))
    return out


def remove_fields(fields: list[tuple[str, str]], keys: set[str]) -> list[tuple[str, str]]:
    return [(name, value) for name, value in fields if name not in keys]


def negate_numbers(value: str) -> str:
    return ";".join(f"{-float(item):g}" for item in value.split(";"))


def flip_orient(value: str) -> str:
    out = []
    for item in value.split(";"):
        if item.endswith("+"):
            out.append(item[:-1] + "-")
        elif item.endswith("-"):
            out.append(item[:-1] + "+")
        else:
            out.append(item)
    return ";".join(out)


def swap_path(value: str) -> str:
    swap = {1: "1", 2: "4", 3: "3", 4: "2"}
    out = []
    for token in value.split(";"):
        match = re.fullmatch(r"([A-Za-z]+)([1-4])", token)
        if not match:
            out.append(token)
            continue
        column, position = match.groups()
        out.append(column + swap[int(position)])
    return ";".join(out)


def mirror_fields(fields: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """Return the opposite-handed record (engine 16 builds one handedness)."""
    current = field_dict(fields)
    out = list(fields)
    out = replace_field(out, "orient", flip_orient(current["orient"]))
    out = replace_field(out, "rise", negate_numbers(current["rise"]))
    out = replace_field(out, "twist", negate_numbers(current["twist"]))
    out = replace_field(out, "path", swap_path(current["path"]))
    out = replace_field(out, "name", current["name"] + "_alt")
    return out


def key_for(path: Path) -> str:
    return path.stem.replace("-", "_")


def describe_input(path: Path) -> dict:
    match = INPUT_RE.match(path.name)
    if not match:
        raise ValueError(f"cannot parse input filename: {path.name}")
    info = match.groupdict()
    info["pdb"] = info["pdb"].upper()
    info["assembly"] = int(info["assembly"])
    info["chain_token"] = info["chain"] + (f"-{info['dup']}" if info["dup"] else "")
    info["dup"] = int(info["dup"]) if info["dup"] else None
    info["file"] = path.name
    info["path"] = str(path)
    info["key"] = key_for(path)
    fields = field_dict(parse_inp(path))
    info["name"] = fields.get("name", "")
    info["sequence"] = fields.get("sequence", "")
    info["structure"] = fields.get("structure", "")
    info["tetrads"] = (
        info["structure"].count("^") // 4
        if "^" in info["structure"]
        else sum(char.isupper() for char in info["structure"]) // 4
    )
    return info


def selected_input_paths() -> list[Path]:
    return sorted(INPUTS.glob("*.inp"))


def model_path(key: str, depth: int, mode: str, models_dir: Path | None = None) -> Path:
    base = models_dir or MODELS
    return base / f"{key}_{depth}_{mode}.pdb"


def energy_path(key: str, depth: int, mode: str, models_dir: Path | None = None) -> Path:
    base = models_dir or MODELS
    return base / f"{key}_{depth}_{mode}_energy.txt"


ETOTAL_RE = re.compile(r"Etotal\s*=\s*(-?\d+(?:\.\d+)?)")


def parse_etotal(path: Path) -> float | None:
    if not path.exists():
        return None
    match = ETOTAL_RE.search(path.read_text(encoding="utf-8", errors="replace"))
    return float(match.group(1)) if match else None


def cif_auth_chains(path: Path) -> dict[str, list[str]]:
    """Map author chain ids (what the input record names) to label chain ids."""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    start = None
    for index, line in enumerate(lines):
        if line.strip() == "loop_" and index + 1 < len(lines) and "_atom_site.group_PDB" in lines[index + 1]:
            start = index
            break
    if start is None:
        return {}
    columns: list[str] = []
    index = start + 1
    while index < len(lines) and lines[index].startswith("_atom_site."):
        columns.append(lines[index].strip())
        index += 1
    position = {name: i for i, name in enumerate(columns)}
    try:
        label = position["_atom_site.label_asym_id"]
        auth = position["_atom_site.auth_asym_id"]
    except KeyError:
        return {}
    mapping: dict[str, list[str]] = {}
    for line in lines[index:]:
        if line.startswith("#") or line.strip() == "loop_":
            break
        fields = line.split()
        if len(fields) < len(columns) or fields[0] not in ("ATOM", "HETATM"):
            continue
        mapping.setdefault(fields[auth], [])
        if fields[label] not in mapping[fields[auth]]:
            mapping[fields[auth]].append(fields[label])
    return mapping


def write_json(path: Path, payload) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
