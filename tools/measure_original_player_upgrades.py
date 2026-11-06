#!/usr/bin/env python3
"""Read original ORDR W3X MPQ tech records without modifying the archive.

This is an evidence extraction tool, not a simulation of Warcraft III's native
upgrade evaluator. Prices and numerical effect fields are retained verbatim.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import struct
import re
from pathlib import Path

import mpyq


ORIGINAL_SHA256 = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
SHOPS = {
    "h06Q": ["R00F", "R008", "R002", "R00E", "R003", "R004", "R000", "R019"],
    "h06R": ["R032", "R02O", "R015", "R00X", "R013", "R007", "R014", "R016", "R02C", "R01V"],
    "h06S": ["Rhgb", "Rhra", "Rhpt", "Rhfc"],
}


def read_archive(map_path: Path):
    contents = map_path.read_bytes()
    actual = hashlib.sha256(contents).hexdigest()
    if actual != ORIGINAL_SHA256:
        raise ValueError(f"Expected original {ORIGINAL_SHA256}, got {actual}")
    offset = contents.find(b"MPQ\x1a")
    if offset < 0:
        raise ValueError("Warcraft MPQ header not found")
    return mpyq.MPQArchive(io.BytesIO(contents[offset:]), listfile=False)


def parse_sylk(contents: bytes, key_column: str = "upgradeid") -> dict[str, dict[str, str | int | float]]:
    lines = contents.decode("utf-8-sig").splitlines()
    current_x = current_y = 0
    cells: dict[tuple[int, int], str | int | float] = {}
    for line in lines:
        if not line.startswith("C;"):
            continue
        value = None
        for piece in line.split(";")[1:]:
            if piece.startswith("X"):
                current_x = int(piece[1:])
            elif piece.startswith("Y"):
                current_y = int(piece[1:])
            elif piece.startswith("K"):
                raw = piece[1:]
                if raw.startswith('"') and raw.endswith('"'):
                    value = raw[1:-1].replace('""', '"')
                else:
                    try:
                        value = int(raw)
                    except ValueError:
                        try:
                            value = float(raw)
                        except ValueError:
                            value = raw
        if value is not None:
            cells[(current_y, current_x)] = value
    headers = {x: str(v) for (y, x), v in cells.items() if y == 1}
    result = {}
    for y in sorted({y for y, x in cells if y != 1}):
        row = {name: cells.get((y, x)) for x, name in headers.items()}
        rawcode = row.get(key_column)
        if isinstance(rawcode, str) and rawcode:
            result[rawcode] = {k: v for k, v in row.items() if v is not None}
    return result


def parse_w3a(contents: bytes) -> dict[str, dict]:
    """Read Warcraft III version-2 w3a level-data modifications."""
    offset = 0

    def integer():
        nonlocal offset
        value = struct.unpack_from("<i", contents, offset)[0]
        offset += 4
        return value

    def rawcode():
        nonlocal offset
        value = contents[offset:offset + 4].decode("ascii", errors="replace")
        offset += 4
        return value

    version = integer()
    if version != 2:
        raise ValueError(f"Unsupported w3a version {version}")
    result = {}
    for table in ("original", "custom"):
        count = integer()
        for _ in range(count):
            parent, new = rawcode(), rawcode()
            mods = []
            for _ in range(integer()):
                field = rawcode()
                kind = integer()
                level = integer()
                data_pointer = integer()
                if kind == 0:
                    value = integer()
                elif kind in (1, 2):
                    value = struct.unpack_from("<f", contents, offset)[0]
                    offset += 4
                elif kind == 3:
                    end = contents.index(b"\0", offset)
                    value = contents[offset:end].decode("utf-8", errors="replace")
                    offset = end + 1
                else:
                    raise ValueError(f"Unknown W3A modification type {kind} at {offset}")
                end_id = rawcode()
                if end_id not in (parent, new, "\x00\x00\x00\x00"):
                    raise ValueError(f"W3A {parent}/{new} mod terminator {end_id!r}")
                mods.append({"field": field, "kind": kind, "level": level,
                             "data_pointer": data_pointer, "value": value})
            result[new if new != "\x00\x00\x00\x00" else parent] = {
                "parent": parent, "new": new, "table": table, "modifications": mods}
    if offset != len(contents):
        raise ValueError(f"W3A unread trailing bytes {len(contents) - offset}")
    return result


def parse_upgrade_strings(contents: bytes) -> dict[str, dict[str, str]]:
    result: dict[str, dict[str, str]] = {}
    key = None
    for line in contents.decode("utf-8-sig").splitlines():
        line = line.strip()
        if line.startswith("[") and line.endswith("]"):
            key = line[1:-1]
            result[key] = {}
        elif key and "=" in line:
            attr, value = line.split("=", 1)
            result[key][attr] = value.strip('"')
    return result


def fourcc_from_jass_literal(literal: str) -> str:
    return int(literal, 16).to_bytes(4, "big").decode("ascii", errors="replace")


def source_step_fields(record: dict, level: int) -> dict:
    """Expose source-base+delta projections, not a validated in-game charge."""
    def calculate(base: str, delta: str):
        if record.get(base) is None:
            return None
        return record[base] + (level - 1) * record.get(delta, 0)

    return {
        "level": level,
        "gold_formula_projection": calculate("goldbase", "goldmod"),
        "lumber_formula_projection": calculate("lumberbase", "lumbermod"),
        "research_time_formula_projection": calculate("timebase", "timemod"),
        "effect1_base_plus_delta": calculate("base1", "mod1") if record.get("effect1") else None,
        "effect2_base_plus_delta": calculate("base2", "mod2") if record.get("effect2") else None,
        "effect3_base_plus_delta": calculate("base3", "mod3") if record.get("effect3") else None,
        "effect4_base_plus_delta": calculate("base4", "mod4") if record.get("effect4") else None,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("map", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--compact-out", type=Path)
    args = parser.parse_args()
    archive = read_archive(args.map)
    data = archive.read_file("Units\\UpgradeData.slk")
    strings = archive.read_file("Units\\CampaignUpgradeStrings.txt")
    if data is None or strings is None:
        raise ValueError("Original upgrade SLK/strings missing")
    rows = parse_sylk(data)
    names = parse_upgrade_strings(strings)
    ability_rows = parse_sylk(archive.read_file("Units\\AbilityData.slk"), "alias")
    ability_names = parse_upgrade_strings(archive.read_file("Units\\CampaignAbilityStrings.txt"))
    custom_abilities = parse_w3a(archive.read_file("war3map.w3a"))
    jass_bytes = archive.read_file("war3map.j")
    jass = jass_bytes.decode("utf-8")
    cuo = re.search(r"function CUo takes .*?endfunction", jass, re.S)
    cv8 = re.search(r"function CV8 takes .*?endfunction", jass, re.S)
    cv9 = re.search(r"function CV9 takes .*?endfunction", jass, re.S)
    if not all((cuo, cv8, cv9)):
        raise ValueError("Expected original JASS functions CUo/CV8/CV9 not found")
    initial_tech = {
        fourcc_from_jass_literal(hex_id): int(level)
        for hex_id, level in re.findall(
            r"SetPlayerTechResearchedSwap\(\$([0-9A-F]{8}),(\d+),Player\(EK\)\)",
            cuo.group(0))
    }
    research_effects = {
        fourcc_from_jass_literal(research): fourcc_from_jass_literal(ability)
        for research, ability in re.findall(
            r"(?:if|elseif) GetResearched\(\)==\$([0-9A-F]{8}) then\s+"
            r"call UnitAddAbility\(i4\[GetPlayerId\(Cl\)\],\$([0-9A-F]{8})\)",
            cv8.group(0))
    }
    if set(research_effects) != set(SHOPS["h06S"]):
        raise ValueError(f"Unexpected research effect mapping {research_effects}")
    if "EVENT_PLAYER_UNIT_RESEARCH_START" not in cv9.group(0):
        raise ValueError("JASS research-start hook changed")
    output = {
        "source": {"name": args.map.name, "sha256": ORIGINAL_SHA256,
                   "jass_sha256": hashlib.sha256(jass_bytes).hexdigest(),
                   "data_path": "Units/UpgradeData.slk",
                   "strings_path": "Units/CampaignUpgradeStrings.txt",
                   "ability_path": "Units/AbilityData.slk",
                   "custom_ability_path": "war3map.w3a"},
        "shops": {},
        "research_effect_ability_ids": research_effects,
        "research_abilities": {},
        "jass": {
            "startup_tech_levels": initial_tech,
            "startup_function": "CUo",
            "research_start_callback": "CV8",
            "research_start_registration": "CV9",
            "research_hook_event": "EVENT_PLAYER_UNIT_RESEARCH_START",
        },
        "formula_warning": "The level rows evaluate explicit SLK base+(level-1)*mod, "
            "NOT confirmed real purchase charges or Warcraft native effect stacking. "
            "A missing base field is null (no assertion of free cost).",
    }
    for shop, ids in SHOPS.items():
        records = []
        for rawcode in ids:
            record = rows.get(rawcode)
            if record is None:
                raise ValueError(f"Missing upgrade {shop}/{rawcode}")
            label = names.get(rawcode, {})
            records.append({"rawcode": rawcode, "name": label.get("Name"),
                            "tooltip": label.get("Ubertip"),
                            "strings": label,
                            "initial_tech_level_from_CUo": initial_tech.get(rawcode),
                            "levels": [source_step_fields(record, n) for n in range(1, int(record["maxlevel"]) + 1)],
                            "slk": record})
        output["shops"][shop] = records
    for rawcode in set(output["research_effect_ability_ids"].values()):
        output["research_abilities"][rawcode] = {
            "slk": ability_rows.get(rawcode),
            "strings": ability_names.get(rawcode),
            "w3a": custom_abilities.get(rawcode),
        }
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.compact_out:
        compact = {
            "source": output["source"],
            "evidence_status": "SOURCE_OBJECT_AND_JASS_EXTRACTED; GAMEPLAY_NOT_RUN",
            "step_columns": ["level", "gold_source_formula", "lumber_source_formula",
                             "time_source_formula", "effect1_source_formula",
                             "effect2_source_formula"],
            "formula_note": output["formula_warning"],
            "initialization": {"function": "CUo", "research_start": "CV9 -> CV8",
                               "event": output["jass"]["research_hook_event"]},
            "research_granted_auras": {},
            "shops": {},
        }
        for tech, ability_id in research_effects.items():
            abi = output["research_abilities"][ability_id]
            slk = abi["slk"] or {}
            w3a = abi["w3a"] or {}
            custom = {m["field"]: m["value"] for m in w3a.get("modifications", [])}
            compact["research_granted_auras"][tech] = {
                "ability": ability_id,
                "native_ability_code": slk.get("code") or w3a.get("parent"),
                "target": slk.get("targs1") or custom.get("atar"),
                "area": slk.get("Area1") or custom.get("aare"),
                "data_a": slk.get("DataA1") if slk else custom.get("Cac1"),
                "data_b": slk.get("DataB1") if slk else None,
                "buff": slk.get("BuffID1") or custom.get("abuf"),
                "source": "Units/AbilityData.slk" if slk else "war3map.w3a",
            }
        for shop, records in output["shops"].items():
            compact["shops"][shop] = []
            for item in records:
                slk = item["slk"]
                src = {key: slk.get(key) for key in
                       ("goldbase", "goldmod", "lumberbase", "lumbermod",
                        "timebase", "timemod", "effect1", "base1", "mod1",
                        "effect2", "base2", "mod2", "effect3", "base3", "mod3",
                        "effect4", "base4", "mod4") if key in slk}
                title = item["strings"].get("Tip") or item["name"]
                title = re.sub(r"\|c[0-9a-fA-F]{8}|\|r", "", title)
                compact["shops"][shop].append({
                    "id": item["rawcode"], "name": title,
                    "max_level": slk["maxlevel"],
                    "startup_level_set_by_jass": item["initial_tech_level_from_CUo"],
                    "source_fields": src,
                    "requires_12": item["strings"].get("Requires12"),
                    "requires_count": item["strings"].get("Requirescount"),
                    "tooltip": item["tooltip"],
                    "steps": [[r["level"], r["gold_formula_projection"],
                               r["lumber_formula_projection"],
                               r["research_time_formula_projection"],
                               r["effect1_base_plus_delta"],
                               r["effect2_base_plus_delta"]]
                              for r in item["levels"]],
                })
        args.compact_out.parent.mkdir(parents=True, exist_ok=True)
        args.compact_out.write_text(json.dumps(compact, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for shop, records in output["shops"].items():
        print(f"SHOP {shop}")
        for r in records:
            o = r["slk"]
            print(r["rawcode"], r["name"],
                  "max", o.get("maxlevel"),
                  "G", o.get("goldbase"), o.get("goldmod"),
                  "L", o.get("lumberbase"), o.get("lumbermod"),
                  "T", o.get("timebase"), o.get("timemod"),
                  "E1", o.get("effect1"), o.get("base1"), o.get("mod1"), o.get("code1"),
                  "E2", o.get("effect2"), o.get("base2"), o.get("mod2"), o.get("code2"))


if __name__ == "__main__":
    main()
