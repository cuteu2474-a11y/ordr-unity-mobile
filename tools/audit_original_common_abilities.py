#!/usr/bin/env python3
"""Common (C-01..09) source evidence: separate CWR recipes from combat skills.

Reads only extracted S2 2.323[C] map / JASS data. No skill absence or
runtime attack approval is inferred from a missing tooltip profile.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
VERSION = "S2 2.323[C]"


def derive(map_stats, jass, missing):
    if any(x.get("gameVersion") != VERSION for x in (map_stats, jass, missing)):
        raise ValueError("Original map version mismatch")
    source = map_stats.get("sourceW3xSha256")
    if not source or jass.get("sourceSha256") != source or \
            missing.get("sourceW3xSha256") != source:
        raise ValueError("Original W3X fingerprints mismatch")
    map_index = {x["catalogId"]: x for x in map_stats["catalogEntries"]}
    registry = {x["abilityRawcode"]: x for x in jass["registrations"]}
    if len(registry) != len(jass["registrations"]):
        raise ValueError("Duplicate CWR ability code")
    source_units = sorted((x for x in missing["characters"]
                           if x["catalogUnitId"].startswith("C-")),
                          key=lambda x: x["catalogUnitId"])
    if [x["catalogUnitId"] for x in source_units] != [
        "C-%02d" % n for n in range(1, 10)
    ]:
        raise ValueError("Expected exactly 9 source-backed common units")

    rows = []
    for unit in source_units:
        original = map_index[unit["catalogUnitId"]]
        if (original["mappingStatus"] != "verified-common-map-identity" or
                original["originalRawcode"] != unit["mapUnitRawcode"] or
                original["sourceMapName"] != unit["mapObjectName"] or
                original["attack1"] is None or
                unit["originalAbilityRawcodesInMapOrder"] !=
                original["originalAbilityList"].split(",")):
            raise ValueError("Common object or ability identity drift: " +
                             unit["catalogUnitId"])
        abilities = []
        for raw in unit["originalAbilityRawcodesInMapOrder"]:
            recipe = registry.get(raw)
            count = missing["linkedCharacterAbilityRawcodeOccurrenceCounts"].get(raw)
            if not isinstance(count, int) or count <= 0:
                raise ValueError("Missing shared ability occurrence: " + raw)
            if recipe:
                results = [x for x in recipe["results"]
                           if x["kind"] == "UNIT" and x["count"] == 1]
                if len(results) != 1:
                    raise ValueError("Common CWR ability has no unique UNIT result: " + raw)
                abilities.append({
                    "rawcode": raw,
                    "kind": "original-cwr-fusion-ability",
                    "catalogCharacterOccurrences": count,
                    "jassResultRawcode": results[0]["rawcode"],
                    "jassUnitIngredientCount": sum(
                        x["count"] for x in recipe["conditions"] if x["kind"] == "UNIT"),
                    "nativeCombatSkillVerified": False,
                })
            else:
                abilities.append({
                    "rawcode": raw,
                    "kind": "shared-or-native-ability-not-combat-classified",
                    "catalogCharacterOccurrences": count,
                    "jassResultRawcode": None,
                    "jassUnitIngredientCount": 0,
                    "nativeCombatSkillVerified": False,
                })
        rows.append({
            "catalogUnitId": unit["catalogUnitId"],
            "catalogName": unit["catalogName"],
            "originalUnitRawcode": original["originalRawcode"],
            "originalMapName": original["sourceMapName"],
            "objectAttack1": original["attack1"],
            "abilityCodesInObjectOrder": abilities,
            "individuallyConfirmedCombatSkillCount": 0,
            "allCombatSkillsProvenAbsent": False,
            "actualCombatReady": False,
        })
    return {
        "format": "ordr-2323C-common-object-and-cwr-ability-audit-v1",
        "gameVersion": VERSION,
        "sourceW3xSha256": source,
        "scope": "object-attack-and-CWR-ability-identity-only",
        "importantLimits": [
            "CWR fusion abilities are not combat procs.",
            "A0JE/A09G/Avul shared native/system behavior is not proven and cannot be assumed to be absent.",
            "Original object attack timing does not establish the first real Warcraft hit or final HP damage.",
            "Synthetic NUnit attack gates must not be shipped as real-map verification.",
        ],
        "summary": {
            "commons": len(rows),
            "cwrFusionAbilities": sum(sum(
                a["kind"] == "original-cwr-fusion-ability"
                for a in x["abilityCodesInObjectOrder"]) for x in rows),
            "uncategorizedSharedOrNativeAbilities": sum(sum(
                a["kind"] != "original-cwr-fusion-ability"
                for a in x["abilityCodesInObjectOrder"]) for x in rows),
            "actualCombatVerifiedUnits": 0,
        },
        "commonUnits": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true",
                        help="Compare with committed JSON without writing")
    args = parser.parse_args()
    def read(name):
        return json.loads((DATA / name).read_text(encoding="utf-8"))
    obj = derive(read("original-2323C-map-base-stats.json"),
                 read("original-jass-fusion-registry.json"),
                 read("original-2323C-missing-skill-profile-evidence.json"))
    out = DATA / "original-2323C-common-unit-ability-audit.json"
    if args.verify:
        if obj != read(out.name):
            raise SystemExit("Source-backed common ability audit is stale")
        print("PASS: 9 original commons / fusion abilities separated / no combat approval")
    else:
        out.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n",
                       encoding="utf-8")
        print("Created " + str(out))


if __name__ == "__main__":
    main()
