#!/usr/bin/env python3
"""Inventory original unit ability rawcodes for missing tooltip profiles.

This preserves original ability ID evidence without guessing a skill effect,
trigger, chance, damage number, or unit rawcode whose identity is unresolved.
"""

import argparse
from collections import Counter
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
VERSION = "S2 2.323[C]"
SHARED_THRESHOLD = 20  # Frequency hint only; not a functional classification.
GRADE_ORDER = {"희귀함": 0, "특별함": 1, "안흔함": 2, "흔함": 3}


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def original_abilities(obj):
    value = obj.get("originalAbilityList") or ""
    return [item.strip() for item in value.split(",") if item.strip()]


def build(units, profiles, map_stats):
    if {data.get("gameVersion") for data in (units, profiles, map_stats)} != {VERSION}:
        raise ValueError("Skill evidence must use identical ORDR 2.323[C] sources")
    roster = units["entries"]
    if len(roster) != 273 or len({u["id"] for u in roster}) != len(roster):
        raise ValueError("Expected 273 distinct catalog characters")
    recorded = profiles["profiles"]
    profiled = {p["unitId"] for p in recorded}
    if len(profiled) != len(recorded):
        raise ValueError("Duplicate skill tooltip profile")
    ids = {u["id"] for u in roster}
    if not profiled <= ids:
        raise ValueError("Skill profiles include unknown characters")
    map_entries = map_stats["catalogEntries"]
    maps = {row["catalogId"]: row for row in map_entries}
    if len(maps) != 273 or set(maps) != ids:
        raise ValueError("Original map ability evidence does not cover the catalog")

    shared_counts = Counter()
    for obj in map_entries:
        if obj.get("originalRawcode"):
            shared_counts.update(set(original_abilities(obj)))

    rows = []
    for character in roster:
        uid = character["id"]
        if uid in profiled:
            continue
        obj = maps[uid]
        if (obj["catalogName"] != character["name"] or
                obj["grade"] != character["grade"]):
            raise ValueError("Map/catalog name or grade disagrees for " + uid)
        rawcodes = original_abilities(obj)
        if len(rawcodes) != len(set(rawcodes)):
            raise ValueError("Duplicate original ability rawcode for " + uid)
        prefix, serial = uid.split("-")
        rows.append({
            "catalogUnitId": uid,
            "catalogName": character["name"],
            "grade": character["grade"],
            "webDexUrl": (None if character.get("catalogEvidenceOrigin") == "original-W3X-only"
                          else "https://ordr.kr/character/" + prefix + str(int(serial))),
            "mapUnitRawcode": obj.get("originalRawcode"),
            "mapObjectIdentityStatus": obj["mappingStatus"],
            "mapObjectName": obj.get("sourceMapName"),
            "hasOriginalAttack1ObjectEvidence": obj.get("attack1") is not None,
            "originalAbilityRawcodesInMapOrder": rawcodes,
            "lessFrequentlyObservedRawcodesForResearchOnly": [
                code for code in rawcodes if shared_counts[code] < SHARED_THRESHOLD
            ],
            "originalAbilityEffectsResolved": False,
            "originalSkillJassTriggersResolved": False,
            "tooltipProfilePresent": False,
            "originalParitySkillExecutionAuthorized": False,
            "researchState": "map-ability-ids-only-needs-object-and-jass-skill-review",
        })

    rows.sort(key=lambda row: (GRADE_ORDER.get(row["grade"], 99),
                               row["catalogUnitId"]))
    by_grade = Counter(r["grade"] for r in rows)
    relevant_codes = {code for row in rows
                      for code in row["originalAbilityRawcodesInMapOrder"]}
    return {
        "format": "ordr-missing-skill-profile-map-ability-evidence-v1",
        "gameVersion": VERSION,
        "mode": "nightmare-otherworld-fixed",
        "sourceW3xSha256": map_stats["sourceW3xSha256"],
        "sourceUnitObjectFiles": map_stats["unitObjectSources"],
        "sources": ["data/units.json", "data/character-skill-profiles.json",
                    "data/original-2323C-map-base-stats.json"],
        "summary": {
            "catalogCharacters": len(roster),
            "charactersWithTooltipProfiles": len(recorded),
            "charactersWithoutTooltipProfiles": len(rows),
            "unprofiledByGrade": dict(sorted(by_grade.items())),
            "unprofiledWithMapRawcode": sum(r["mapUnitRawcode"] is not None for r in rows),
            "unprofiledWithAbilityCodes": sum(bool(r["originalAbilityRawcodesInMapOrder"])
                                              for r in rows),
            "distinctAbilityRawcodesForResearch": len(relevant_codes),
            "verifiedSkillFormulasInThisDataset": 0,
            "authorizedOriginalParityImplementations": 0,
        },
        "sharedAbilityFrequencyThresholdForResearchOnly": SHARED_THRESHOLD,
        "linkedCharacterAbilityRawcodeOccurrenceCounts": {
            code: shared_counts[code] for code in sorted(relevant_codes)
        },
        "warning": (
            "Rawcode lists are original unit-object evidence, not verified skill triggers. "
            "Shared rawcode frequency is a research hint, not an effect classification. "
            "Missing web tooltip is NOT proof a character has no skills."
        ),
        "characters": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=ROOT / "data")
    parser.add_argument("--output", type=Path,
                        default=ROOT / "data/original-2323C-missing-skill-profile-evidence.json")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    result = build(*(load(args.data_dir / name) for name in (
        "units.json", "character-skill-profiles.json", "original-2323C-map-base-stats.json")))
    content = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if not args.output.exists() or args.output.read_text(encoding="utf-8") != content:
            raise ValueError("Missing-skill evidence artifact is absent or outdated")
    else:
        args.output.write_text(content, encoding="utf-8")
    print("Missing tooltip profiles:", result["summary"]["charactersWithoutTooltipProfiles"],
          "linked original ability lists:", result["summary"]["unprofiledWithAbilityCodes"])


if __name__ == "__main__":
    main()
