#!/usr/bin/env python3
"""Build a source-preserving rawcode reconciliation queue (data only).

The archived CWR conditions establish that a rawcode is used as a UNIT
ingredient, but do not establish its display name or catalog identity.
Unknown IDs must remain unknown until W3X object/name evidence is supplied.
This script never builds, tests or executes the game.
"""
import argparse
import collections
import json
import pathlib


def build(registry, links):
    mapped = {row["rawcode"]: row for row in links["mappings"]}
    usages = collections.defaultdict(list)
    results = collections.defaultdict(list)
    for recipe in registry["registrations"]:
        ability = recipe["abilityRawcode"]
        if ability == "TEST":
            continue
        for index, condition in enumerate(recipe["conditions"]):
            if condition["kind"] == "UNIT":
                usages[condition["rawcode"]].append({
                    "abilityRawcode": ability,
                    "conditionIndex": index,
                    "requiredCount": condition["count"],
                })
        for result in recipe["results"]:
            if result["kind"] == "UNIT":
                results[result["rawcode"]].append(ability)

    all_rawcodes = sorted(set(usages) | set(results))
    rows = []
    for rawcode in all_rawcodes:
        link = mapped.get(rawcode)
        rows.append({
            "rawcode": rawcode,
            "catalogUnitId": link["catalogUnitId"] if link else None,
            "mappingEvidence": "BjB/Bi8/Bi9-label-only" if link else "unresolved",
            "objectDataVerified": False,
            "ingredientUseCount": len(usages[rawcode]),
            "ingredientUses": usages[rawcode],
            "resultOfAbilities": results[rawcode],
        })
    rows.sort(key=lambda x: (-x["ingredientUseCount"], x["rawcode"]))
    return {
        "gameVersion": "S2 2.323[C]",
        "sourceSha256": registry["sourceSha256"],
        "source": "CWR ordered UNIT conditions and existing BjB links",
        "warning": "Unresolved rawcodes are not catalog IDs. Even label-linked IDs are not verified Warcraft object IDs; never authorize consumption from this queue.",
        "uniqueUnitRawcodes": len(rows),
        "labelLinkedRawcodes": sum(x["catalogUnitId"] is not None for x in rows),
        "unresolvedIngredientRawcodes": sum(
            x["ingredientUseCount"] > 0 and x["catalogUnitId"] is None for x in rows),
        "entries": rows,
    }


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--registry", type=pathlib.Path, default=root / "data/original-jass-fusion-registry.json")
    parser.add_argument("--links", type=pathlib.Path, default=root / "data/original-unit-rawcode-label-links.json")
    parser.add_argument("--output", type=pathlib.Path, default=root / "data/original-fusion-rawcode-backlog.json")
    args = parser.parse_args()
    result = build(json.loads(args.registry.read_text(encoding="utf-8")),
                   json.loads(args.links.read_text(encoding="utf-8")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("CWR rawcode evidence queue written: " + str(args.output))


if __name__ == "__main__":
    main()
