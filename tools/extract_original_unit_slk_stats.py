#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract original ORDR W3X unit weapon and resource object data.

Inputs are original files extracted from the same W3X:
  war3map.j
  Units/UnitWeapons.slk
  Units/UnitBalance.slk
  Units/UnitAbilities.slk

Example (Windows):
  python tools/extract_original_unit_slk_stats.py --jass war3map.j \
    --weapons UnitWeapons.slk --balance UnitBalance.slk \
    --abilities UnitAbilities.slk --output data/original-unit-weapon-stats.json

This is SOURCE DATA EXTRACTION, not a game test or a combat balancing tool.
Final Warcraft damage depends on runtime upgrades/buffs/armor and JASS.
"""
import argparse
import hashlib
import json
import pathlib
import re
from collections import OrderedDict

REGISTER = re.compile(r'call Bi8\(\$([0-9A-Fa-f]{8}),\$([0-9A-Fa-f]{8})\)')
FIELDS_WEAPON = [
    "weapsOn", "acquire", "rangeN1", "targs1", "atkType1", "weapTp1",
    "cool1", "dmgpt1", "backSw1", "dice1", "sides1", "dmgplus1",
    "Farea1", "Harea1", "Qarea1", "weapType2", "rangeN2",
    "atkType2", "weapTp2", "cool2", "dmgpt2", "dice2", "sides2", "dmgplus2",
]
FIELDS_BALANCE = ["HP", "manaN", "regenHP", "regenMana", "def", "defType", "spd"]


def rawcode(hextext):
    return bytes.fromhex(hextext).decode("latin-1")


def parse_sylk_bytes(data):
    """Parse the object-table SYLK cells directly from bytes in the W3X."""
    text = data.decode("utf-8-sig", errors="replace")
    rows = {}
    x = y = None
    for line in text.splitlines():
        if not line.startswith("C;") or ";K" not in line:
            continue
        coordinates, value = line.split(";K", 1)
        for field in coordinates.split(";")[1:]:
            if field.startswith("X"):
                x = int(field[1:])
            elif field.startswith("Y"):
                y = int(field[1:])
        if x is None or y is None:
            raise ValueError("SYLK cell has no coordinates: " + line)
        if value.startswith('"') and value.endswith('"'):
            value = value[1:-1].replace('""', '"')
        rows.setdefault(y, {})[x] = value

    header = rows.get(1)
    if not header:
        raise ValueError("Missing SYLK header")
    result = {}
    for y, cells in rows.items():
        if y == 1:
            continue
        key = cells.get(1)
        if key:
            if key in result:
                raise ValueError("Duplicate unit rawcode: " + key)
            result[key] = {
                header[k]: v for k, v in cells.items()
                if k != 1 and k in header
            }
    return result


def parse_sylk(path):
    return parse_sylk_bytes(path.read_bytes())


def extract(jass, weapons, balance, abilities):
    source = jass.read_bytes()
    registrations = OrderedDict()
    for match in REGISTER.finditer(source.decode("utf-8-sig")):
        code, ability = rawcode(match.group(1)), rawcode(match.group(2))
        registrations.setdefault(code, []).append(ability)

    weapon_rows = parse_sylk(weapons)
    balance_rows = parse_sylk(balance)
    ability_rows = parse_sylk(abilities)
    entries = []
    for code, skill_abilities in registrations.items():
        if code not in weapon_rows or code not in balance_rows:
            raise ValueError("Original object data missing for rawcode: " + code)
        w, b = weapon_rows[code], balance_rows[code]
        a = ability_rows.get(code, {})
        entries.append({
            "rawcode": code,
            "craftingAbilityRawcodes": skill_abilities,
            "weaponFields": {field: w.get(field) for field in FIELDS_WEAPON},
            "unitFields": {field: b.get(field) for field in FIELDS_BALANCE},
            "originalAbilityList": a.get("abilList"),
        })

    return {
        "gameVersion": "S2 2.323[C]",
        "scope": "source-object-fields-not-final-combat-outcomes",
        "sourceJassSha256": hashlib.sha256(source).hexdigest(),
        "sources": [
            {"name": str(weapons.name), "sha256": hashlib.sha256(weapons.read_bytes()).hexdigest()},
            {"name": str(balance.name), "sha256": hashlib.sha256(balance.read_bytes()).hexdigest()},
            {"name": str(abilities.name), "sha256": hashlib.sha256(abilities.read_bytes()).hexdigest()},
        ],
        "registrationCount": len(registrations),
        "entries": entries,
        "criticalNotes": [
            "Warcraft atkType1 is NOT equal to ORDR player-facing physical/magical damage school.",
            "dmgplus1, dice1 and sides1 are Warcraft object weapon fields, not final damage after buffs/armor.",
            "cool1 and rangeN1 are original object values and may be overridden at runtime.",
            "Original first-swing delay and full skill execution cannot be inferred from these fields.",
            "Unknown original unit names must remain unassigned without source-backed mapping.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ["jass", "weapons", "balance", "abilities", "output"]:
        parser.add_argument("--" + field, type=pathlib.Path, required=True)
    args = parser.parse_args()
    result = extract(args.jass, args.weapons, args.balance, args.abilities)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Extracted", result["registrationCount"],
          "original unit rawcode weapon records without running the game.")
    print("Wrote", args.output)


if __name__ == "__main__":
    main()
