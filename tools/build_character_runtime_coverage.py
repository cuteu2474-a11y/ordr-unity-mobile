#!/usr/bin/env python3
"""Reconcile ALL catalog identities with actual source-model capabilities.

An original Warcraft object basic attack is NOT a complete character skill kit.
Do not infer unknown stats or award free units from this report.
"""
from collections import Counter
import argparse
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
SOURCE_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"


def load(filename):
    return json.loads((DATA / filename).read_text(encoding="utf-8"))


def build(base, extension, status, profiles, source_stats, fusion):
    if base["gameVersion"] != "S2 2.323[C]" or extension["gameVersion"] != base["gameVersion"]:
        raise ValueError("Wrong source game version")
    if extension["sourceW3xSha256"] != SOURCE_SHA or fusion["sourceW3xSha256"] != SOURCE_SHA:
        raise ValueError("Original W3X source changed")
    original = {unit["id"]: unit for unit in base["entries"]}
    runtime = {row["catalogId"]: row for row in status["entries"]}
    if len(original) != len(base["entries"]) or len(runtime) != len(status["entries"]):
        raise ValueError("Duplicate base catalog or runtime identity")
    if len(original) != len(runtime) or set(original) != set(runtime):
        raise ValueError("Base catalog/source runtime mismatch")
    if len(original) != extension["baseCatalogEntries"]:
        raise ValueError("Unreviewed base catalog revision")
    profile_ids = {row["unitId"] for row in profiles["profiles"]}
    if not profile_ids <= set(original):
        raise ValueError("Unknown source tooltip profile")
    object_stats = {row["rawcode"]: row for row in source_stats["entries"]}
    fusion_objects = {row["rawcode"]: row for row in fusion["entries"]}
    hanma = object_stats["h0C2"]["originalObjectFields"]["weapons"]
    if not (hanma["weapsOn"] == "1" and hanma["atkType1"] == "hero" and
            int(hanma["dmgplus1"]) == 152500 and int(hanma["dice1"]) == 1 and
            int(hanma["sides1"]) == 4 and float(hanma["cool1"]) == 0.71):
        raise ValueError("Hanma original attack was changed")
    if (fusion_objects["h0C2"]["resultOfAbilities"] != ["A0QG"] or
            fusion_objects["h0C2"]["catalogUnitId"] is not None or
            fusion_objects["h05Y"]["catalogUnitId"] is not None):
        raise ValueError("Original-unmapped source identity changed")

    effective = {unit_id: dict(unit) for unit_id, unit in original.items()}
    for regrade in extension["reclassifications"]:
        unit_id = regrade["id"]
        row = effective.get(unit_id)
        if (unit_id != "PS-01" or row is None or
                row["grade"] != regrade["expectedGrade"] or regrade["grade"] != "히든"):
            raise ValueError("Unknown original reclassification")
        row["grade"] = "히든"
        row["gradeOrder"] = regrade["gradeOrder"]

    additions = {}
    for row in extension["entries"]:
        unit_id = row["id"]
        if unit_id in effective or unit_id in additions:
            raise ValueError("Duplicate additional unit")
        if row.get("attackPower") is not None or row.get("gameRawCode") is not None:
            raise ValueError("Invented extra source attack")
        additions[unit_id] = row
    if len(additions) + len(effective) != extension["expectedMergedEntries"]:
        raise ValueError("Missing expanded catalog units")
    effective.update(additions)

    rows = []
    for unit_id, unit in effective.items():
        extra = additions.get(unit_id)
        kind = extra.get("catalogEntryKind") if extra else "original-catalog"
        parent = extra.get("variantOfUnitId") if extra else None
        if parent and (parent not in original or original[parent]["grade"] != unit["grade"]):
            raise ValueError("Extra form has invalid original owner")
        source_raw = extra.get("originalSourceRawcode") if extra else runtime[unit_id]["originalRawcode"]
        own_profile = unit_id in profile_ids
        inherited_profile = bool(parent and parent in profile_ids)
        attack = bool(runtime[unit_id]["objectBasicAttackModel"]) if not extra else (
            unit_id == "MY-14" and kind == "fusion-result" and source_raw == "h0C2")
        if extra and attack and unit_id != "MY-14":
            raise ValueError("Invented extra model attack")
        original_effects = runtime[unit_id]["connectedModelEffects"] if not extra else []
        outstanding = []
        if parent:
            outstanding.append("별도 획득 금지; 원본 변신 조건·형태별 스킬 미검증")
        elif not attack:
            outstanding.append("원본 객체 기본 공격 모델 없음 또는 비전투 지원 객체")
        if not own_profile and not inherited_profile:
            outstanding.append("상세 스킬 프로필 미수집")
        outstanding.append("고유 스킬·보상/변신·원작 최종 전투 동등성 미인증")
        rows.append({
            "id": unit_id, "name": unit["name"], "grade": unit["grade"],
            "entryKind": kind, "variantOfUnitId": parent,
            "sourceRawcode": source_raw,
            "sourceBasicAttackModel": attack,
            "connectedSourceModelEffectCount": len(original_effects),
            "ownTooltipProfile": own_profile,
            "inheritedTooltipProfile": inherited_profile,
            "independentRosterIdentity": not bool(parent),
            "fullyOriginalCombatVerified": False,
            "remainingWork": outstanding,
        })
    counts = Counter(row["grade"] for row in rows)
    result = {
        "format": "ordr-character-runtime-coverage-v1",
        "gameVersion": base["gameVersion"],
        "sourceW3xSha256": SOURCE_SHA,
        "warning": "Catalog addressability, attack-only MODEL_ONLY and actual Warcraft parity are separate states; catalog identities are NOT a finished combat unit count.",
        "summary": {
            "catalogIdentities": len(rows),
            "sourceCatalogIdentities": len(original),
            "wikiExtensionIdentities": len(additions),
            "independentRosterIdentities": sum(row["independentRosterIdentity"] for row in rows),
            "alternateCombatForms": sum(not row["independentRosterIdentity"] for row in rows),
            "sourceObjectBasicAttackModels": sum(row["sourceBasicAttackModel"] for row in rows),
            "sourceTooltips": sum(row["ownTooltipProfile"] for row in rows),
            "formInheritedTooltips": sum(row["inheritedTooltipProfile"] for row in rows),
            "identitiesWithoutOwnOrInheritedTooltip": sum(
                not row["ownTooltipProfile"] and not row["inheritedTooltipProfile"] for row in rows),
            "fullyOriginalCombatVerified": 0,
            "byGrade": dict(sorted(counts.items())),
        },
        "entries": rows,
    }
    if result["summary"]["catalogIdentities"] != extension["expectedMergedEntries"]:
        raise ValueError("Stale coverage snapshot")
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DATA / "character-runtime-coverage.json")
    parser.add_argument("--verify", action="store_true",
                        help="Compare the committed snapshot without writing it")
    args = parser.parse_args(argv)
    output = build(load("units.json"), load("wiki-unit-catalog-extension.json"),
                   load("unit-runtime-status.json"), load("character-skill-profiles.json"),
                   load("original-2323C-unit-object-stats.json"),
                   load("original-fusion-object-reconciliation.json"))
    if args.verify:
        if not args.output.is_file():
            parser.exit(1, "Coverage snapshot missing; run tools/build_character_runtime_coverage.py\n")
        try:
            committed = json.loads(args.output.read_text(encoding="utf-8"))
        except (ValueError, UnicodeError):
            parser.exit(1, "Invalid coverage snapshot; regenerate and review it\n")
        if committed != output:
            parser.exit(1, "Coverage snapshot is outdated; run tools/build_character_runtime_coverage.py and review the diff\n")
        print("Coverage snapshot matches current inputs")
    else:
        args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8", newline="\n")
    print(json.dumps(output["summary"], ensure_ascii=False))


if __name__ == "__main__":
    main()
