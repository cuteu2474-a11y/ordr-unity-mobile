#!/usr/bin/env python3
"""Reconcile Mystic web recipes with original JASS registration *candidates*.

All matches are conditional on explicitly stated structural hypotheses.  This
is a research aid, NOT a rawcode mapping or permission to consume game units.
"""

import argparse
from collections import Counter
import json
from pathlib import Path


VERSION = "S2 2.323[C]"
SHARED_UNIT = "h06G"
WILDCARD_PICK = "A800"


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def count(rows, kind, rawcode=None):
    return sum(row["count"] for row in rows if row["kind"] == kind
               and (rawcode is None or row["rawcode"] == rawcode))


def analyze(jass, links, web):
    if {jass.get("gameVersion"), links.get("gameVersion"),
        web.get("gameVersion")} != {VERSION}:
        raise ValueError("Sources must all be from S2 2.323[C]")
    if jass.get("sourceSha256") != links.get("sourceMapSha256"):
        raise ValueError("JASS and unit-label evidence refer to different maps")
    if web.get("mode") != "nightmare-otherworld":
        raise ValueError("Mystic recipes must use otherworld mode")

    raw_to_catalog = {}
    for mapping in links["mappings"]:
        raw, catalog = mapping["rawcode"], mapping["catalogUnitId"]
        if raw in raw_to_catalog and raw_to_catalog[raw] != catalog:
            raise ValueError("Conflicting catalog labels for original rawcode " + raw)
        raw_to_catalog[raw] = catalog

    registrations = []
    for entry in jass["registrations"]:
        conditions = entry["conditions"]
        results = entry["results"]
        if (count(conditions, "GOLD") != 10000
                or count(conditions, "WOOD") != 7
                or count(conditions, "UNIT", SHARED_UNIT) != 1
                or len(results) != 1 or results[0]["kind"] != "UNIT"
                or results[0]["count"] != 1
                or count(conditions, "KING", results[0]["rawcode"]) != 1):
            continue
        registrations.append(entry)

    rows = []
    for recipe in web["recipes"]:
        if recipe["gold"] != 10000 or recipe["lumber"] != 7:
            raise ValueError("Unexpected Mystic web cost: " + recipe["resultUnitId"])
        fixed = Counter()
        wildcard_count = 0
        for ingredient in recipe["ingredients"]:
            if ingredient["kind"] == "unit":
                fixed[ingredient["unitId"]] += ingredient["count"]
            elif ingredient["kind"] == "any-random":
                wildcard_count += ingredient["count"]
            else:
                raise ValueError("Unknown web ingredient kind")
        if "행운의토큰" not in recipe["specialItems"]:
            raise ValueError("Missing shared special item in web recipe")
        # This subtraction is a *hypothesis*: h06G may represent the shared
        # token and a second special item may add one unlabelled UNIT entry.
        special_unit_slots = len(recipe["specialItems"]) - 1
        candidates = []
        for entry in registrations:
            cond = entry["conditions"]
            picks = count(cond, "PICK", WILDCARD_PICK)
            if picks != wildcard_count:
                continue
            if count(cond, "PICK") != picks:
                continue
            unit_conditions = [c for c in cond if c["kind"] == "UNIT"
                               and c["rawcode"] != SHARED_UNIT]
            if sum(x["count"] for x in unit_conditions) != (
                    sum(fixed.values()) + special_unit_slots):
                continue
            observed_labels = Counter()
            for item in unit_conditions:
                label = raw_to_catalog.get(item["rawcode"])
                if label:
                    observed_labels[label] += item["count"]
            # A known JASS label contradicting the web fixed-material list
            # excludes the candidate under the label-correlation hypothesis.
            if any(n > fixed[label] for label, n in observed_labels.items()):
                continue
            candidates.append({
                "abilityRawcode": entry["abilityRawcode"],
                "resultRawcode": entry["results"][0]["rawcode"],
                "labelMatchedIngredients": [
                    {"catalogUnitId": key, "count": value}
                    for key, value in sorted(observed_labels.items())
                ],
                "unidentifiedMaterialRawcodes": [
                    {"rawcode": c["rawcode"], "count": c["count"]}
                    for c in unit_conditions if c["rawcode"] not in raw_to_catalog
                ],
                "jassConditionsInOriginalOrder": cond,
            })
        candidates.sort(key=lambda c: (-sum(x["count"] for x in
                                            c["labelMatchedIngredients"]),
                                       c["abilityRawcode"]))
        max_matches = max((sum(i["count"] for i in c["labelMatchedIngredients"])
                           for c in candidates), default=0)
        strongest = [c["abilityRawcode"] for c in candidates
                     if sum(x["count"] for x in c["labelMatchedIngredients"])
                     == max_matches]
        rows.append({
            "resultUnitId": recipe["resultUnitId"],
            "resultName": recipe["resultName"],
            "webSourceUrl": recipe["sourceUrl"],
            "webFixedIngredients": [
                {"catalogUnitId": k, "count": n} for k, n in sorted(fixed.items())
            ],
            "webWildcardCount": wildcard_count,
            "webSpecialItems": recipe["specialItems"],
            "compatibleCandidatesUnderHypotheses": candidates,
            "strongestLabelEvidenceCandidateAbilities": strongest,
            "strongestLabelEvidenceCount": max_matches,
            "matchStatus": ("positive-label-correlation" if max_matches
                            else "structural-only-ambiguous"),
            "verifiedResultRawcode": None,
            "authorizedForUnitConsumption": False,
        })

    return {
        "gameVersion": VERSION,
        "mode": "악몽 난이도 + 이세계모드 고정",
        "sourceJassSha256": jass["sourceSha256"],
        "webRecipeCount": len(rows),
        "matchingGoldWoodRegistrations": len(registrations),
        "positiveLabelCorrelationRecipes": sum(
            row["strongestLabelEvidenceCount"] > 0 for row in rows),
        "structuralHypotheses": [
            "h06G (shared UNIT condition) may be the web lucky token; not verified",
            "PICK:A800 may encode a web any-random ingredient; not verified",
            "Each web special item after the common token may occupy an extra UNIT slot; not verified",
            "JASS Bi8/Bi9 catalog labels are grade/name-correlated, not object-verified",
        ],
        "verifiedObjectRawcodeMappings": 0,
        "authorizedForUnitConsumption": False,
        "warning": "Research-only alternatives, not a 1:1 mapping. Never select the first candidate for gameplay.",
        "rows": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=Path,
                        default=Path("data/original-jass-fusion-registry.json"))
    parser.add_argument("--links", type=Path,
                        default=Path("data/original-unit-rawcode-label-links.json"))
    parser.add_argument("--mystic", type=Path,
                        default=Path("data/otherworld-2323C-mystic-web-recipes.json"))
    parser.add_argument("--output", type=Path,
                        default=Path("data/original-jass-mystic-candidate-reconciliation.json"))
    args = parser.parse_args()
    result = analyze(load(args.jass), load(args.links), load(args.mystic))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Mystic recipes:", result["webRecipeCount"],
          "JASS cost matches:", result["matchingGoldWoodRegistrations"],
          "positive label correlations:", result["positiveLabelCorrelationRecipes"],
          "verified object mappings: 0")


if __name__ == "__main__":
    main()
