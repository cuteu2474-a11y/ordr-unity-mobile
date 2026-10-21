#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Rebuild the per-character game implementation worklist.

This is a source-data transformation, not a Unity/NUnit gameplay test.
The output distinguishes a catalog/spawnable unit from a fully working
original-combat unit. It must never fill an unknown value with a guess.
"""
import argparse
import json
import pathlib
from collections import OrderedDict


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def confirmed_base_attack(unit, profile):
    if profile:
        attack = profile.get("basicAttack", {})
        if (profile.get("basicAttackVerificationStatus") == "original-confirmed"
                and all(attack.get(k) is not None for k in (
                    "nominalAttackPower", "intervalSeconds", "range", "damageSchool"))):
            return True
    return (unit.get("attackVerificationStatus") == "original-confirmed"
            and bool(unit.get("gameRawCode"))
            and all(unit.get(k) is not None for k in (
                "attackPower", "attackIntervalSeconds", "attackRange",
                "attackDamageSchool", "firstAttackDelaySeconds")))


def build(unit_doc, profile_doc, links_doc, patch_doc):
    version = "S2 2.323[C]"
    for name, doc in [
        ("units", unit_doc), ("skills", profile_doc),
        ("rawcode labels", links_doc), ("stat evidence", patch_doc)
    ]:
        if doc.get("gameVersion") != version:
            raise ValueError(name + " source version does not match " + version)

    units = unit_doc["entries"]
    profiles = {p["unitId"]: p for p in profile_doc["profiles"]}
    labels = {p["catalogUnitId"]: p for p in links_doc["mappings"]}
    evidence = {p["unitId"]: p for p in patch_doc["verifiedFromPages"]}
    if len(profiles) != len(profile_doc["profiles"]):
        raise ValueError("Duplicate skill profile")
    if len(labels) != len(links_doc["mappings"]):
        raise ValueError("Duplicate catalog unit mapping")
    if len(set(u["id"] for u in units)) != len(units):
        raise ValueError("Duplicate unit ID in catalog")
    if not set(profiles).issubset({u["id"] for u in units}):
        raise ValueError("Skill profile references a missing unit")
    if not set(labels).issubset({u["id"] for u in units}):
        raise ValueError("JASS label references a missing unit")

    rows = []
    by_grade = OrderedDict()
    for unit in units:
        detail = profiles.get(unit["id"])
        label = labels.get(unit["id"])
        patch = evidence.get(unit["id"])
        skill_entries = detail.get("skills", []) if detail else []
        passives = detail.get("passives", []) if detail else []
        attack_ready = confirmed_base_attack(unit, detail)
        row = {
            "id": unit["id"],
            "name": unit["name"],
            "grade": unit["grade"],
            "rosterDefinition": "implemented",
            "skillProfile": "partial" if detail else "not-collected",
            "skillEntries": len(skill_entries),
            "passiveEntries": len(passives),
            "triggerKinds": sorted({skill["trigger"] for skill in skill_entries}),
            "effectKinds": sorted({
                effect["type"] for skill in skill_entries
                for effect in skill.get("components", [])
            }),
            "passiveKinds": sorted({effect["type"] for effect in passives}),
            "jassLabelRawcode": label["rawcode"] if label else None,
            "jassAbilityRawcode": label["abilityRawcode"] if label else None,
            "rawcodeEvidence": "Bi8/Bi9-label" if label else "unresolved",
            "patchStatEvidenceFields": [
                item["stat"] for item in patch["observations"]
            ] if patch else [],
            "verifiedBasicAttack": attack_ready,
            "originalCombatParity": "not-verified",
        }
        rows.append(row)
        group = by_grade.setdefault(unit["grade"], {
            "units": 0, "profiles": 0, "rawcodeLinks": 0
        })
        group["units"] += 1
        group["profiles"] += int(detail is not None)
        group["rawcodeLinks"] += int(label is not None)

    return {
        "gameVersion": version,
        "mode": "nightmare-otherworld",
        "generatedOn": "2026-10-21",
        "purpose": "All-unit runtime registration and remaining original-combat implementation worklist; no invented stats.",
        "scopeWarning": "271/271 are catalog-addressable roster units, not 271 fully implemented fighting units. 200 profiles are partially transcribed. Stats/formulas and full native behaviors remain unverified.",
        "summary": {
            "units": len(rows),
            "catalogRosterEligible": len(rows),
            "partialSkillProfiles": sum(row["skillProfile"] == "partial" for row in rows),
            "uncollectedProfiles": sum(row["skillProfile"] == "not-collected" for row in rows),
            "skillEntries": sum(row["skillEntries"] for row in rows),
            "passiveEntries": sum(row["passiveEntries"] for row in rows),
            "linkedJassRawcodes": sum(bool(row["jassLabelRawcode"]) for row in rows),
            "allStatsVerified": sum(row["verifiedBasicAttack"] for row in rows),
        },
        "byGrade": by_grade,
        "entries": rows,
    }


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-directory", type=pathlib.Path, default=root / "data")
    parser.add_argument("--output", type=pathlib.Path,
                        default=root / "data/unit-implementation-matrix.json")
    args = parser.parse_args()
    d = args.source_directory
    result = build(
        load(d / "units.json"),
        load(d / "character-skill-profiles.json"),
        load(d / "original-unit-rawcode-label-links.json"),
        load(d / "original-stat-evidence.json"),
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Unit catalog records:", result["summary"]["units"])
    print("Partial skill profiles:", result["summary"]["partialSkillProfiles"])
    print("Original attack stats entirely verified:",
          result["summary"]["allStatsVerified"])
    print("No game execution or tests were run.")


if __name__ == "__main__":
    main()
