#!/usr/bin/env python3
"""Read-only CWR UNIT rawcode ↔ W3X object/catalog reconciliation.

Never treats object/name matching as approval to consume inventory, apply
special conditions, spawn a result or bypass runtime validation.
"""
import argparse
from collections import Counter
import json
from pathlib import Path


def _index_unique(rows, key, label):
    result = {}
    for row in rows:
        value = row.get(key)
        if value is None or value in result:
            raise ValueError("Duplicate or missing " + label + ": " + str(value))
        result[value] = row
    return result


def reconcile(map_stats, jass, labels, backlog, web):
    version = "S2 2.323[C]"
    if any(x.get("gameVersion") != version for x in
           (map_stats, jass, labels, backlog, web)):
        raise ValueError("2.323[C] source version mismatch")
    source_hash = map_stats.get("sourceW3xSha256")
    if not source_hash or any(s != source_hash for s in
           (jass.get("sourceSha256"), labels.get("sourceMapSha256"),
            backlog.get("sourceSha256"))):
        raise ValueError("Source W3X fingerprints disagree")
    if jass.get("conditionsInOriginalOrder") is not True:
        raise ValueError("Original CWR condition ordering not preserved")

    by_id = _index_unique(map_stats["catalogEntries"], "catalogId", "catalog ID")
    by_raw = _index_unique([r for r in map_stats["catalogEntries"]
                            if r.get("originalRawcode")], "originalRawcode", "catalog rawcode")
    object_by_raw = _index_unique(map_stats["mapObjects"], "rawcode", "W3X object rawcode")
    label_by_id = _index_unique(labels["mappings"], "catalogUnitId", "JASS label catalog ID")
    label_by_raw = _index_unique(labels["mappings"], "rawcode", "JASS label rawcode")
    backlog_by_raw = _index_unique(backlog["entries"], "rawcode", "CWR backlog rawcode")
    jass_by_ability = _index_unique(jass["registrations"], "abilityRawcode", "CWR ability")
    if set(backlog_by_raw) - set(object_by_raw):
        raise ValueError("CWR registry contains unknown source W3X objects")

    ingredient_uses = Counter()
    result_ids = set()
    for recipe in jass["registrations"]:
        if recipe["abilityRawcode"] == "TEST":
            continue
        for condition in recipe["conditions"]:
            if condition["kind"] == "UNIT":
                ingredient_uses[condition["rawcode"]] += 1
        for result in recipe["results"]:
            if result["kind"] == "UNIT":
                result_ids.add(result["rawcode"])
    if set(backlog_by_raw) != set(ingredient_uses) | result_ids:
        raise ValueError("CWR backlog rawcode set does not match registrations")
    for rawcode, item in backlog_by_raw.items():
        if item["ingredientUseCount"] != ingredient_uses[rawcode]:
            raise ValueError("CWR backlog ingredient occurrence count stale: " + rawcode)

    rows = []
    for rawcode, item in backlog_by_raw.items():
        catalog = by_raw.get(rawcode)
        label = label_by_raw.get(rawcode)
        obj = object_by_raw[rawcode]
        rows.append({
            "rawcode": rawcode,
            "ingredientUseCount": item["ingredientUseCount"],
            "resultOfAbilities": item["resultOfAbilities"],
            "catalogUnitId": catalog["catalogId"] if catalog else None,
            "objectCatalogMappingStatus": catalog["mappingStatus"] if catalog else None,
            "originalW3xObjectName": obj["mapName"],
            "jassLabelCatalogUnitId": label["catalogUnitId"] if label else None,
            "approvedForMaterialConsumption": False,
        })
    rows.sort(key=lambda row: (-row["ingredientUseCount"], row["rawcode"]))

    conflicts = []
    for unit_id, label in label_by_id.items():
        catalog = by_id.get(unit_id)
        if catalog and catalog.get("originalRawcode") and \
                catalog["originalRawcode"] != label["rawcode"]:
            conflicts.append({
                "catalogUnitId": unit_id,
                "catalogObjectRawcode": catalog["originalRawcode"],
                "jassLabelRawcode": label["rawcode"],
                "jassAbilityRawcode": label["abilityRawcode"],
                "jassLabelObjectName": object_by_raw[label["rawcode"]]["mapName"],
                "requiresManualFormOrIdentityReview": True,
            })
    conflicts.sort(key=lambda row: row["catalogUnitId"])

    web_rows = []
    for recipe in web["recipes"]:
        result = by_id.get(recipe["resultUnitId"])
        rawcode = result.get("originalRawcode") if result else None
        candidates = [entry for entry in jass_by_ability.values()
                      if any(x["kind"] == "UNIT" and x["rawcode"] == rawcode
                             and x["count"] == 1 for x in entry["results"])]
        expected = Counter()
        for part in recipe["ingredients"]:
            expected[part["unitId"]] += part["count"]
        status = "missing-or-ambiguous-result"
        material_rows = []
        non_unit = []
        jass_ability = None
        material_matches = False
        money_matches = False
        if len(candidates) == 1:
            candidate = candidates[0]
            jass_ability = candidate["abilityRawcode"]
            actual = Counter()
            for condition in candidate["conditions"]:
                if condition["kind"] != "UNIT":
                    non_unit.append(dict(condition))
                    continue
                catalog = by_raw.get(condition["rawcode"])
                unit_id = catalog["catalogId"] if catalog else None
                if unit_id:
                    actual[unit_id] += condition["count"]
                material_rows.append({"rawcode": condition["rawcode"],
                                      "catalogUnitId": unit_id,
                                      "count": condition["count"]})
            material_matches = (None not in [r["catalogUnitId"] for r in material_rows]
                                and actual == expected)
            jass_gold = sum(c["count"] for c in non_unit if c["kind"] == "GOLD")
            jass_wood = sum(c["count"] for c in non_unit if c["kind"] == "WOOD")
            money_matches = (jass_gold == recipe["extraCost"]["gold"] and
                             jass_wood == recipe["extraCost"]["lumber"] and
                             not recipe["extraCost"].get("items"))
            status = ("source-objects-material-and-listed-cost-match" if
                      material_matches and money_matches else "source-conflict-or-unmapped")
        web_rows.append({
            "resultCatalogUnitId": recipe["resultUnitId"],
            "resultOriginalRawcode": rawcode,
            "jassAbilityRawcode": jass_ability,
            "ingredientMultisetMatches": material_matches,
            "listedGoldWoodMatches": money_matches,
            "originalOrderedUnitMaterials": material_rows,
            "originalNonUnitConditionsInOrder": non_unit,
            "status": status,
            "approvedForFusionExecution": False,
        })

    summary = {
        "uniqueCwrUnitRawcodes": len(rows),
        "originalCatalogObjectMatchedRawcodes": sum(r["catalogUnitId"] is not None for r in rows),
        "previouslyUnresolvedIngredientRawcodes": sum(
            r["ingredientUseCount"] > 0 and r["jassLabelCatalogUnitId"] is None
            for r in rows),
        "newlyCrossReferencedIngredients": sum(
            r["ingredientUseCount"] > 0 and r["jassLabelCatalogUnitId"] is None
            and r["catalogUnitId"] is not None for r in rows),
        "stillWithoutCatalogIdentity": sum(
            r["ingredientUseCount"] > 0 and r["jassLabelCatalogUnitId"] is None
            and r["catalogUnitId"] is None for r in rows),
        "ingredientConditionOccurrences": sum(r["ingredientUseCount"] for r in rows),
        "catalogMatchedIngredientConditionOccurrences": sum(
            r["ingredientUseCount"] for r in rows if r["catalogUnitId"] is not None),
        "webRecipeCount": len(web_rows),
        "matchingWebRecipes": sum(r["status"] ==
            "source-objects-material-and-listed-cost-match" for r in web_rows),
        "catalogVsJassLabelConflicts": len(conflicts),
    }
    return {
        "format": "ordr-original-fusion-object-reconciliation-v1",
        "gameVersion": version,
        "sourceW3xSha256": source_hash,
        "scope": "source-identity-and-web-multiset-cross-check-only",
        "authorizedForFusionExecution": False,
        "warning": "Object rawcode links and material/cost matches do NOT prove UPUN and other side conditions, transactional consumption, world effects or gameplay parity.",
        "summary": summary,
        "conflictingCatalogJassLabels": conflicts,
        "webRecipes": web_rows,
        "entries": rows,
    }


def main():
    root = Path(__file__).resolve().parents[1] / "data"
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--verify", action="store_true", help="Validate committed JSON without writing")
    args = cli.parse_args()
    def load(name):
        return json.loads((root / name).read_text(encoding="utf-8"))
    output = reconcile(load("original-2323C-map-base-stats.json"),
                       load("original-jass-fusion-registry.json"),
                       load("original-unit-rawcode-label-links.json"),
                       load("original-fusion-rawcode-backlog.json"),
                       load("verified-mobile-fusion-recipes.json"))
    path = root / "original-fusion-object-reconciliation.json"
    if args.verify:
        if load(path.name) != output:
            raise SystemExit("Committed fusion object reconciliation is stale")
        print("Fusion evidence snapshot verified; no consumption approved")
    else:
        path.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8")
        print("Created source-only reconciliation: " + str(path))


if __name__ == "__main__":
    main()
