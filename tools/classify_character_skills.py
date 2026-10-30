#!/usr/bin/env python3
"""Classify collected 2.323 tooltips into reusable skill implementation queues.

This is NOT original JASS skill verification. A trigger written in the tooltip
database may be a research shorthand and must be checked against the W3X.
"""

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
VERSION = "S2 2.323[C]"

ATTACK_TRIGGERS = {
    "onAttack", "onEveryAttack", "onEachBasicAttack", "onHit", "onEveryHit",
    "everyNthAttack", "afterBasicAttacks", "onNthSuccessfulBasicHit",
    "onAttackAgainstBoss", "onAttackAgainstSpecialTarget", "onAttackWithCondition",
    "onAttackWithAbsoluteCooldown", "onBasicAttackWithAbsoluteCooldown",
    "afterBasicDamageThreshold", "summonedUnitOnAttack", "satelliteOnAttack",
}
ACTIVE_TRIGGERS = {
    "manual", "active", "activeMana", "activeHealth", "satelliteActiveMana",
    "onSkillUse", "onSkillActivated", "onSkill", "onExecute",
}
PERIODIC_TRIGGERS = {"periodic", "everyNthRound", "periodicOnSkill"}


def family(trigger):
    if trigger in ATTACK_TRIGGERS:
        return "attack-triggered"
    if trigger in ACTIVE_TRIGGERS:
        return "active-or-automatic-mobile"
    if trigger in PERIODIC_TRIGGERS:
        return "periodic"
    return "special-condition-needs-analysis"


def build(units, profiles, object_catalog, runtime_status=None):
    for doc in (units, profiles, object_catalog):
        if doc["gameVersion"] != VERSION:
            raise ValueError("Mixed ORDR game versions in skill classification")
    unit_index = {row["id"]: row for row in units["entries"]}
    object_index = {row["catalogId"]: row for row in object_catalog["catalogEntries"]}
    if (len(unit_index) != len(units["entries"]) or
            len(object_index) != len(object_catalog["catalogEntries"]) or
            set(unit_index) != set(object_index) or
            units.get("expectedTotal") != len(unit_index)):
        raise ValueError("Character DB or map index is inconsistent")
    profile_index = {row["unitId"]: row for row in profiles["profiles"]}
    if len(profile_index) != len(profiles["profiles"]) or not set(profile_index) <= set(unit_index):
        raise ValueError("Duplicate or unknown tooltip profile")
    runtime = {}
    if runtime_status is not None:
        if runtime_status.get("sourceW3xSha256") != object_catalog.get("sourceW3xSha256"):
            raise ValueError("Runtime export and source map fingerprint differ")
        runtime = {row["catalogId"]: row for row in runtime_status["entries"]}
        if len(runtime) != len(runtime_status["entries"]) or set(runtime) != set(unit_index):
            raise ValueError("Runtime export does not cover the current catalog")
        for uid, row in runtime.items():
            if row["originalRawcode"] != object_index[uid]["originalRawcode"]:
                raise ValueError("Runtime source identity mismatch: " + uid)

    rows = []
    groups = defaultdict(list)
    kinds = Counter()
    triggers = Counter()
    linked_skills = 0
    passives = 0
    for profile in profiles["profiles"]:
        uid = profile["unitId"]
        unit = unit_index[uid]
        obj = object_index[uid]
        passives += len(profile.get("passives", []))
        for index, skill in enumerate(profile.get("skills", []), start=1):
            trigger = skill["trigger"]
            component_types = sorted({part["type"] for part in skill.get("components", [])})
            for part in skill.get("components", []):
                kinds[part["type"]] += 1
            triggers[trigger] += 1
            skill_id = "%s-S%02d" % (uid, index)
            category = family(trigger)
            template_key = (category, trigger, tuple(component_types))
            groups[template_key].append(skill_id)
            if obj["originalRawcode"]:
                linked_skills += 1
            rows.append({
                "skillId": skill_id,
                "catalogId": uid,
                "unitName": unit["name"],
                "grade": unit["grade"],
                "name": skill["name"],
                "documentedTrigger": trigger,
                "implementationFamily": category,
                "documentedEffectTypes": component_types,
                "mapUnitRawcode": obj["originalRawcode"],
                "mapObjectMappingStatus": obj["mappingStatus"],
                "originalJassSkillRawcode": None,
                "originalExecutionVerified": False,
                "implementationStatus": "requires-original-JASS-analysis",
                "source": "data/character-skill-profiles.json",
            })

    templates = []
    for (category, trigger, effects), members in groups.items():
        templates.append({
            "family": category,
            "documentedTrigger": trigger,
            "documentedEffectTypes": list(effects),
            "skillCount": len(members),
            "skills": members,
            "implementationStatus": "not-original-verified",
        })
    templates.sort(key=lambda x: (-x["skillCount"], x["family"],
                                  x["documentedTrigger"], x["documentedEffectTypes"]))
    unit_rows = []
    for uid, unit in unit_index.items():
        obj = object_index[uid]
        profile = profile_index.get(uid)
        status = runtime.get(uid)
        unit_rows.append({
            "catalogId": uid, "unitName": unit["name"], "grade": unit["grade"],
            "mapUnitRawcode": obj["originalRawcode"],
            "objectAbilityRawcodes": (obj.get("originalAbilityList") or "").split(",") if obj.get("originalAbilityList") else [],
            "tooltipProfilePresent": profile is not None,
            "documentedSkillCount": len(profile.get("skills", [])) if profile else None,
            "documentedPassiveCount": len(profile.get("passives", [])) if profile else None,
            "connectedModelEffects": status["connectedModelEffects"] if status else None,
            "connectedManualFunctions": status["connectedManualFunctions"] if status else None,
            "remainingWork": status["remainingWork"] if status else None,
        })
    return {
        "format": "ordr-skill-template-research-queue-v1",
        "gameVersion": VERSION,
        "scope": "Classified tooltip source, not implemented JASS logic",
        "caveat": "Missing basic map stats or JASS skill links cannot be filled by tooltip inference.",
        "summary": {
            "catalogCharacters": len(unit_index),
            "tooltipProfiles": len(profiles["profiles"]),
            "unitsWithoutTooltipProfile": len(unit_index) - len(profile_index),
            "unitsWithConnectedModelEffects": sum(bool(r["connectedModelEffects"]) for r in unit_rows) if runtime else None,
            "skillEntries": len(rows),
            "passiveEntries": passives,
            "skillEntriesOnMappedUnits": linked_skills,
            "skillFamilies": dict(sorted(Counter(row["implementationFamily"] for row in rows).items())),
            "documentedTriggers": dict(sorted(triggers.items())),
            "documentedEffectTypes": dict(sorted(kinds.items())),
            "reusableTemplateGroups": len(templates),
            "originalJassSkillsVerified": 0,
        },
        "templateGroups": templates,
        "skills": rows,
        "units": unit_rows,
        "unitStatusNote": "Connected effects are per-unit model exports; they cannot prove that any individual tooltip skill is implemented or original-equivalent.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=ROOT / "data")
    parser.add_argument("--output", type=Path,
                        default=ROOT / "data/original-2323C-skill-research-queue.json")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    read = lambda path: json.loads((args.data_dir / path).read_text(encoding="utf-8"))
    result = build(read("units.json"), read("character-skill-profiles.json"),
                   read("original-2323C-map-base-stats.json"),
                   read("unit-runtime-status.json"))
    content = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if not args.output.exists() or args.output.read_text(encoding="utf-8") != content:
            raise ValueError("Skill research queue is missing or out of date")
        print("PASS: skill research classification reproducible")
    else:
        args.output.write_text(content, encoding="utf-8")
        print("Wrote", args.output)
    print("Skills", result["summary"]["skillEntries"],
          "template groups", result["summary"]["reusableTemplateGroups"])


if __name__ == "__main__":
    main()
