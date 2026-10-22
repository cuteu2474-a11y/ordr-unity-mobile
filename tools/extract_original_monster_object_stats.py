#!/usr/bin/env python3
"""Extract original W3X *object* stats for likely enemy units.

This is deliberately not a wave catalog: the object table does not prove
spawn order, timing, target role or nightmare/otherworld stat modifiers.

Usage (requires mpyq):

    python tools/extract_original_monster_object_stats.py \
        --w3x '/path/to/ORDR_S2_2.323[C].w3x'
"""

import argparse
from collections import Counter
import json
from pathlib import Path
import re

from build_original_map_base_stats import (
    number, open_archive, parse_names, parse_sylk_bytes, sha256_bytes, sha256_file,
)


PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = PROJECT_ROOT / "data/original-2323C-monster-object-stats.json"
MAP_SHA256 = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
SOURCE_FILES = {
    "balance": "Units\\UnitBalance.slk",
    "names": "Units\\CampaignUnitStrings.txt",
}


def classify_enemy_candidate(rawcode, map_name):
    """Return a name-based category, not a verified in-game enemy role."""
    if rawcode.startswith("o"):
        if "[보스]" in map_name:
            return "boss-labeled"
        if map_name.startswith("[퀘스트]"):
            return "quest-labeled"
        if re.search(r"레벨\s*\d+", map_name):
            return "level-labeled"
        return "other-o-object-unconfirmed"
    if rawcode.startswith("n"):
        if re.match(r"^\d+\.\s+", map_name):
            return "story-labeled"
        if re.search(r"\d+단계\s*크립", map_name):
            return "creep-labeled"
    return None


def extract_object_stats(balance, names):
    """Retain uncertain objects explicitly; do not assert they spawn."""
    result = []
    for code in sorted(balance):
        if code not in names:
            raise ValueError("Missing source unit name for " + code)
        category = classify_enemy_candidate(code, names[code])
        if category is None:
            continue
        object_balance = balance[code]
        hp = number(object_balance.get("HP"))
        armor = number(object_balance.get("def"))
        armor_type = object_balance.get("defType")
        if not isinstance(hp, int) or hp < 1:
            raise ValueError("Invalid original HP for " + code)
        if armor is None or not isinstance(armor_type, str) or not armor_type:
            raise ValueError("Missing original defense for " + code)
        result.append({
            "rawcode": code,
            "originalMapName": names[code],
            "nameBasedCategory": category,
            "objectBaseHitPoints": hp,
            "objectBaseArmor": armor,
            "warcraftArmorType": armor_type,
            "runtimeEnemyRoleVerified": False,
            "nightmareOtherworldFinalStatsVerified": False,
        })
    return result


def build(w3x):
    digest = sha256_file(w3x)
    if digest != MAP_SHA256:
        raise ValueError("Wrong original W3X SHA-256; extraction stopped")
    with open_archive(w3x) as archive:
        blobs = {key: archive.read_file(source) for key, source in SOURCE_FILES.items()}
    for key, blob in blobs.items():
        if blob is None:
            raise ValueError("Missing original map file " + SOURCE_FILES[key])
    entries = extract_object_stats(
        parse_sylk_bytes(blobs["balance"]), parse_names(blobs["names"]))
    categories = Counter(entry["nameBasedCategory"] for entry in entries)
    return {
        "format": "ordr-original-monster-object-stats-v1",
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": digest,
        "sourceFiles": [
            {"path": path, "sha256": sha256_bytes(blobs[key])}
            for key, path in SOURCE_FILES.items()
        ],
        "sourceColumns": {"HP": "HP", "armor": "def", "armorType": "defType"},
        "scope": "original-object-base-stats-not-mode-adjusted-wave-spawns",
        "safeForVerifiedMonsterWaveRuntime": False,
        "summary": {
            "candidateObjectCount": len(entries),
            "nameBasedCategoryCounts": dict(sorted(categories.items())),
            "runtimeEnemyRolesVerified": 0,
            "nightmareOtherworldFinalStatsVerified": 0,
        },
        "entries": entries,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    report = build(args.w3x)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Extracted {len(report['entries'])} name-based candidates to {args.output}")


if __name__ == "__main__":
    main()
