#!/usr/bin/env python3
"""Extract source-only monster innate ability IDs from ORDR S2 2.323[C].

Requires mpyq and the exact pinned W3X. Never infers live immunity or
ability levels from a unit's object-editor starting ability list.

    python tools/extract_original_monster_object_abilities.py --w3x /path/ORDR.w3x
"""

import argparse
from collections import Counter
import json
from pathlib import Path

from build_original_map_base_stats import (
    open_archive, parse_names, parse_sylk_bytes, sha256_bytes, sha256_file,
)

PROJECT_ROOT = Path(__file__).resolve().parent.parent
OUTPUT = PROJECT_ROOT / "data/original-2323C-monster-object-abilities.json"
OBJECT_STATS = PROJECT_ROOT / "data/original-2323C-monster-object-stats.json"
WAVES = PROJECT_ROOT / "data/original-2323C-monster-wave-script-evidence.json"
MAP_SHA256 = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA256 = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
FILES = (
    ("abilities", r"Units\UnitAbilities.slk"),
    ("abilityData", r"Units\AbilityData.slk"),
    ("names", r"Units\CampaignAbilityStrings.txt"),
)


def build(w3x):
    digest = sha256_file(w3x)
    if digest != MAP_SHA256:
        raise ValueError("Wrong original W3X SHA-256")

    objects = json.loads(OBJECT_STATS.read_text(encoding="utf-8"))
    waves = json.loads(WAVES.read_text(encoding="utf-8"))
    if objects["sourceW3xSha256"] != digest or \
            waves["sourceW3xSha256"] != digest or \
            waves["sourceWar3mapJSha256"] != JASS_SHA256:
        raise ValueError("Original object/wave reference does not match this map")

    with open_archive(w3x) as archive:
        blobs = {key: archive.read_file(path) for key, path in FILES}
    if any(blob is None for blob in blobs.values()):
        raise ValueError("Source map lacks required UnitAbilities/AbilityData/Strings")
    abilities = parse_sylk_bytes(blobs["abilities"])
    ability_data = parse_sylk_bytes(blobs["abilityData"])
    ability_names = parse_names(blobs["names"])

    roles = {}
    for wave in waves["waves"]:
        for spawn in wave["normalAndBossSpawns"]:
            rawcode, role = spawn["rawcode"], spawn["role"]
            if rawcode in roles and roles[rawcode] != role:
                raise ValueError("Wave role changes for " + rawcode)
            roles[rawcode] = role

    entries = []
    referenced = set()
    for obj in objects["entries"]:
        rawcode = obj["rawcode"]
        if rawcode not in abilities:
            raise ValueError("Missing original UnitAbilities object: " + rawcode)
        row = abilities[rawcode]
        base = [value for value in row.get("abilList", "").split(",") if value]
        hero = [value for value in row.get("heroAbilList", "").split(",") if value]
        if len(base) != len(set(base)) or not base or hero:
            raise ValueError("Unexpected ability list: " + rawcode)
        referenced.update(base)
        entries.append({
            "rawcode": rawcode,
            "originalMapName": obj["originalMapName"],
            "nameBasedCategory": obj["nameBasedCategory"],
            "scriptWaveRole": roles.get(rawcode),
            "objectBaseAbilities": base,
            "heroSkillList": hero,
            "objectUnitType": None,  # filled from UnitBalance below
            "objectTargetType": None,  # filled from UnitData below
        })

    with open_archive(w3x) as archive:
        balance = parse_sylk_bytes(archive.read_file(r"Units\UnitBalance.slk"))
        data = parse_sylk_bytes(archive.read_file(r"Units\UnitData.slk"))
    for entry in entries:
        rawcode = entry["rawcode"]
        entry["objectUnitType"] = balance[rawcode].get("type")
        entry["objectTargetType"] = data[rawcode].get("targType")

    definitions = []
    for rawcode in sorted(referenced):
        ability = ability_data.get(rawcode)
        if not ability or rawcode not in ability_names:
            raise ValueError("Ability definition missing: " + rawcode)
        definitions.append({
            "rawcode": rawcode,
            "nativeCode": ability["code"],
            "originalAbilityName": ability_names[rawcode],
            "objectDataLevels": int(ability["levels"]),
        })

    categories = Counter(entry["scriptWaveRole"] for entry in entries)
    summary = {
        "candidateObjectCount": len(entries),
        "scriptWaveObjectCount": len(roles),
        "lineWaveObjectCount": categories["line"],
        "bossWaveObjectCount": categories["boss"],
        "storyBaseStunResistanceCount": sum(
            e["nameBasedCategory"] == "story-labeled" and
            "A08Q" in e["objectBaseAbilities"] for e in entries),
        "creepBaseMagicImmuneCount": sum(
            "Amim" in e["objectBaseAbilities"] for e in entries),
        "initialObjectAbilitiesOnly": True,
    }
    if (summary["candidateObjectCount"], summary["scriptWaveObjectCount"],
            summary["lineWaveObjectCount"], summary["bossWaveObjectCount"]) != \
            (100, 68, 60, 8):
        raise ValueError("Unexpected monster object/wave coverage")
    if not set(roles).issubset({entry["rawcode"] for entry in entries}):
        raise ValueError("Wave rawcode missing from object table")
    return {
        "format": "ordr-original-monster-object-abilities-v1",
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": digest,
        "sourceWar3mapJSha256": JASS_SHA256,
        "sourceFiles": [
            {"path": path, "sha256": sha256_bytes(blobs[key])}
            for key, path in FILES
        ],
        "scope":
            "object-starting-ability-identities-and-source-native-codes-not-current-target-immunity-or-live-ability-levels",
        "safeForVerifiedMonsterWaveRuntime": False,
        "liveImmunityFlagsVerified": False,
        "summary": summary,
        "abilityDefinitions": definitions,
        "entries": entries,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    result = build(args.w3x)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Extracted object base abilities for", len(result["entries"]), "monsters")


if __name__ == "__main__":
    main()
