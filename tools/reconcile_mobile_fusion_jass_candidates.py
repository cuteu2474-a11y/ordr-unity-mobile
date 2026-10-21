#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Cross-reference 2.323 web recipes and original ordered CWR registrations.

Produces UNORDERED rawcode candidate groups, never a rawcode->catalogId mapping.
In particular, recipe ingredient presentation order is NOT evidence of the
order of Warcraft CWR UNIT registrations. Does not run or test the game.
"""
import argparse
import json
import pathlib
from collections import Counter


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def group(rows, key):
    result = Counter()
    for row in rows:
        result[row[key]] += row["count"]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=pathlib.Path,
                        default=pathlib.Path("data/original-jass-fusion-registry.json"))
    parser.add_argument("--links", type=pathlib.Path,
                        default=pathlib.Path("data/original-unit-rawcode-label-links.json"))
    parser.add_argument("--recipes", type=pathlib.Path,
                        default=pathlib.Path("data/verified-mobile-fusion-recipes.json"))
    parser.add_argument("--output", type=pathlib.Path,
                        default=pathlib.Path("data/original-jass-web-recipe-candidate-groups.json"))
    args = parser.parse_args()
    jass, links, web = (load(args.jass), load(args.links), load(args.recipes))
    versions = {x["gameVersion"] for x in (jass, links, web)}
    if versions != {"S2 2.323[C]"}:
        raise ValueError("Recipe source versions do not match S2 2.323[C]")
    if jass["sourceSha256"] != links["sourceMapSha256"]:
        raise ValueError("JASS map fingerprints disagree")

    by_unit = {x["catalogUnitId"]: x for x in links["mappings"]}
    by_ability = {x["abilityRawcode"]: x for x in jass["registrations"]}
    rows = []
    for recipe in web["recipes"]:
        link = by_unit.get(recipe["resultUnitId"])
        entry = by_ability.get(link["abilityRawcode"]) if link else None
        row = {
            "resultCatalogUnitId": recipe["resultUnitId"],
            "resultName": recipe["name"],
            "dexSource": recipe["sourceUrl"],
            "jassAbilityRawcode": link["abilityRawcode"] if link else None,
            "jassResultRawcode": link["rawcode"] if link else None,
            "status": "result-id-unlinked",
            "jassMaterials": [],
            "dexMaterials": [{"catalogUnitId": i["unitId"], "count": i["count"]}
                             for i in recipe["ingredients"]],
            "nonUnitConditions": [],
        }
        if entry:
            raw = group((x for x in entry["conditions"] if x["kind"] == "UNIT"),
                        "rawcode")
            recipe_units = group(recipe["ingredients"], "unitId")
            special = [x for x in entry["conditions"] if x["kind"] != "UNIT"]
            costs = {
                kind: sum(x["count"] for x in special if x["kind"] == kind)
                for kind in ("GOLD", "WOOD")
            }
            result_is_same = (
                len(entry["results"]) == 1 and
                entry["results"][0]["kind"] == "UNIT" and
                entry["results"][0]["count"] == 1 and
                entry["results"][0]["rawcode"] == link["rawcode"]
            )
            row["jassMaterials"] = [
                {"rawcode": rawcode, "count": count} for rawcode, count in raw.items()
            ]
            row["nonUnitConditions"] = [
                x["kind"] + "/" + x["rawcode"] + "/" + str(x["count"])
                for x in special
            ]
            row["status"] = (
                "unordered-ingredient-candidate-group"
                if result_is_same and sum(raw.values()) == sum(recipe_units.values())
                and costs["GOLD"] == recipe["extraCost"]["gold"]
                and costs["WOOD"] == recipe["extraCost"]["lumber"]
                and not recipe["extraCost"].get("items")
                else "source-recipe-inconsistent"
            )
        rows.append(row)

    candidate = [x for x in rows if x["status"] == "unordered-ingredient-candidate-group"]
    payload = {
        "gameVersion": "S2 2.323[C]",
        "sourceJassSha256": jass["sourceSha256"],
        "scope": "unordered-web-dex-jass-candidate-groups",
        "webRecipes": len(rows),
        "candidateGroups": len(candidate),
        "ambiguousRawcodes": len({
            item["rawcode"] for row in candidate for item in row["jassMaterials"]
        }),
        "objectRawcodeMappingsVerified": 0,
        "authorizedForUnitConsumption": False,
        "notice": "Ingredients are unordered sets. No 1-to-1 mapping is inferred from index.",
        "rows": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Candidate groups:", len(candidate), "verified object mappings: 0")


if __name__ == "__main__":
    main()
