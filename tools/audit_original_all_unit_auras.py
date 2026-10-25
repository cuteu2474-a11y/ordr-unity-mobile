#!/usr/bin/env python3
"""Audit native armor/speed/attack auras across every catalog unit.

Missing inherited fields stay null. HUD utility registrations are explicitly
not promoted to live stacking rules. Requires mpyq only for W3X extraction.
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive, parse_sylk_bytes
from extract_original_helper_abilities import read_objects
from extract_original_robin_multishot import MAP_SHA, SOURCES

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "data/original-2323C-all-unit-aura-audit.json"
PARENTS = {"AHad", "AOae", "ACac", "AOac", "AEar", "Aakb"}
MISC_SHA = "131d8ca9c3b978d404a325a1f6f34ec79e97c8fc90cef491dab2608180df3223"


def value(raw):
    if raw in (None, "", "_", "-"):
        return None
    try:
        number = float(raw)
    except ValueError:
        return raw
    if number != number or abs(number) == float("inf"):
        raise ValueError("Nonfinite original field")
    return int(number) if number.is_integer() else number


def literal(raw):
    raw = raw.strip()
    if re.fullmatch(r"\$[0-9A-Fa-f]+", raw):
        return int(raw[1:], 16)
    if re.fullmatch(r"-?\d+(?:\.\d*)?", raw):
        return float(raw) if "." in raw else int(raw)
    raise ValueError("Dynamic JASS expression")


def rawcode(number):
    return int(number).to_bytes(4, "big").decode("ascii")


def utility_registrations(jass):
    result, dynamic = [], []
    for function in re.finditer(r"function (\w+) takes .*?\n(.*?)\nendfunction", jass, re.S):
        name, body = function[1], function[2]
        for call in re.finditer(r"call (BH9|BIB|BIC)\(([^)]+)\)", body):
            try:
                args = [literal(x) for x in call[2].split(",")]
                ability, buff, category = rawcode(args[0]), rawcode(args[1]) if args[1] else None, int(args[2])
            except (ValueError, OverflowError, UnicodeDecodeError):
                dynamic.append({"function": name, "call": call[0]})
                continue
            if call[1] == "BIB":
                for level in range(1, int(args[3]) + 1):
                    result.append({"abilityRawcode": ability, "buffRawcode": buff,
                                   "category": category, "level": level,
                                   "signedValue": args[4] + args[5] * (level - 1),
                                   "ownerRestriction": None, "function": name})
            else:
                result.append({"abilityRawcode": ability, "buffRawcode": buff,
                               "category": category, "level": int(args[3]),
                               "signedValue": args[4], "function": name,
                               "ownerRestriction": rawcode(args[5]) if call[1] == "BIC" and args[5] else None})
    return {"entries": result, "dynamicCallsNotExpanded": dynamic,
            "consumer": "BjL -> BIH -> BjK -> LeaderboardAddItem",
            "notACombatStackingFormula": True,
            "originalEligibilityExpression": "Mq[EK]==0 or GetUnitTypeId(Cm)==Mq[EK] and GetUnitAbilityLevel(Cm,Ml[EK])==Mp[EK]",
            "selection": "lowest registered level per ability; by category and buff-or-ability key choose smallest absolute value, except category4/B00E chooses largest"}


def derive(abilities, overrides, unit_abilities, catalog, jass, misc):
    if catalog["sourceW3xSha256"] != MAP_SHA:
        raise ValueError("Wrong catalog map fingerprint")
    objects = {}
    for code, row in abilities.items():
        if row.get("code") not in PARENTS:
            continue
        levels = []
        for level in range(1, int(row["levels"]) + 1):
            fields = {field: value(row.get(field + str(level)))
                      for field in ("DataA", "DataB", "DataC", "DataD", "Area", "Rng",
                                    "targs", "BuffID", "Dur", "HeroDur")}
            levels.append({"level": level, "fields": fields})
        objects[code] = {"abilityRawcode": code, "parentAbility": row["code"],
                         "source": "Units\\AbilityData.slk", "levels": levels}
    for table, old, new, modifications in overrides:
        code = new if new.strip("\0") else old
        parent = abilities.get(code, {}).get("code", old)
        if parent not in PARENTS and code not in objects:
            continue
        # Do not silently ignore a table overlay on an SLK-backed aura.
        if code in objects:
            objects[code]["w3aOverridesRequireMerge"] = [list(x) for x in modifications]
            continue
        counts = [int(v) for f, _, level, _, v in modifications if f == "alev" and level == 0]
        count = counts[0] if len(counts) == 1 else None
        levels = []
        for level in range(1, (count or 0) + 1):
            fields = {}
            for field in ("aare", "atar", "abuf", "Cac1", "Ear1", "Ear2", "Ear3", "Ear4", "Had1", "Had2", "Oae1", "Oae2", "adur", "ahdu", "atp1", "aub1"):
                matches = [v for f, _, lev, _, v in modifications if f == field and lev == level]
                if len(matches) > 1:
                    raise ValueError("Duplicate W3A field: " + code + "/" + field)
                fields[field] = matches[0] if matches else None
            levels.append({"level": level, "fields": fields})
        objects[code] = {"abilityRawcode": code, "parentAbility": parent,
                         "source": "war3map.w3a", "declaredLevels": count,
                         "levels": levels}
    for code, entry in objects.items():
        chain, current = [code], entry["parentAbility"]
        while current not in chain:
            chain.append(current)
            if current not in abilities:
                break
            current = abilities[current].get("code", current)
        entry["nativeParentChain"] = chain
    owners = []
    for unit, row in sorted(unit_abilities.items()):
        codes = [c for c in row.get("abilList", "").split(",") if c in objects]
        if codes:
            owners.append({"unitRawcode": unit, "abilityRawcodesInObjectOrder": codes})
    links = []
    for unit in catalog["catalogEntries"]:
        code = unit.get("originalRawcode")
        row = unit_abilities.get(code)
        if row is not None and row.get("abilList") != unit.get("originalAbilityList"):
            raise ValueError("Unit ability identity drift: " + unit["catalogId"])
        codes = None if row is None else [c for c in row.get("abilList", "").split(",") if c in objects]
        links.append({"catalogUnitId": unit["catalogId"], "name": unit["catalogName"],
                      "grade": unit["grade"], "unitRawcode": code,
                      "objectAuraCodes": codes,
                      "doesNotProveAbsenceOfJassOrRuntimeAddedEffects": True})
    groups = defaultdict(set)
    for code, entry in objects.items():
        for level in entry["levels"]:
            buff = level["fields"].get("BuffID") if entry["source"].endswith("slk") else level["fields"].get("abuf")
            if isinstance(buff, str) and len(buff) == 4:
                groups[buff].add(code)
    speed = {}
    for key in ("MinUnitSpeed", "MaxUnitSpeed"):
        match = re.search(r"^" + key + r"=(\d+)$", misc, re.M)
        if not match:
            raise ValueError("Missing explicit original movement bound: " + key)
        speed[key] = int(match[1])
    return {
        "format": "ordr-all-unit-aura-audit-v1", "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": MAP_SHA,
        "scope": "all catalog units plus all original object owners; native armor/speed/attack aura families only",
        "summary": {"catalogUnitsScanned": len(links), "catalogUnitsWithObjectAuras": sum(bool(x["objectAuraCodes"]) for x in links),
                    "unknownCatalogObjectLists": sum(x["objectAuraCodes"] is None for x in links),
                    "mapObjectAuraOwners": len(owners), "nativeAuraObjects": len(objects),
                    "byParent": dict(sorted(Counter(x["parentAbility"] for x in objects.values()).items())),
                    "byNativeRoot": dict(sorted(Counter(x["nativeParentChain"][-1] for x in objects.values()).items())),
                    "sharedBuffGroups": sum(len(v) > 1 for v in groups.values())},
        "nativeAuraObjects": [objects[k] for k in sorted(objects)],
        "catalogUnits": links, "mapObjectAuraOwners": owners,
        "sharedBuffGroups": [{"buffRawcode": k, "abilityRawcodes": sorted(v),
                              "combatStackingVerified": False} for k, v in sorted(groups.items()) if len(v) > 1],
        "originalMovementBounds": speed,
        "movementFormulaAndClampOrderVerified": False,
        "utilityRegistrations": utility_registrations(jass),
        "missingInheritedFieldsAreZero": False,
        "safeForUnconditionalLiveCombat": False,
    }


def build(path):
    if hashlib.sha256(path.read_bytes()).hexdigest() != MAP_SHA:
        raise ValueError("Wrong original W3X fingerprint")
    sources = {**SOURCES, "war3mapMisc.txt": MISC_SHA}
    with open_archive(path) as archive:
        blobs = {name: archive.read_file(name) for name in sources}
    for name, digest in sources.items():
        if blobs[name] is None or hashlib.sha256(blobs[name]).hexdigest() != digest:
            raise ValueError("Wrong original file fingerprint: " + name)
    report = derive(parse_sylk_bytes(blobs["Units\\AbilityData.slk"]),
                    read_objects(blobs["war3map.w3a"]),
                    parse_sylk_bytes(blobs["Units\\UnitAbilities.slk"]),
                    json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text()),
                    blobs["war3map.j"].decode("utf-8-sig").replace("\r\n", "\n"),
                    blobs["war3mapMisc.txt"].decode("utf-8-sig").replace("\r\n", "\n"))
    report["sourceFiles"] = [{"path": n, "sha256": s} for n, s in sources.items()]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = build(args.w3x)
    if args.output:
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    elif json.loads(OUTPUT.read_text(encoding="utf-8")) != report:
        raise ValueError("Committed all-unit aura audit differs from W3X")
    print(json.dumps(report["summary"], ensure_ascii=False))


if __name__ == "__main__":
    main()
