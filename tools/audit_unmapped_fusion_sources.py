#!/usr/bin/env python3
"""Audit source-only fusion units without authorizing their material spending.

Source names are not executable identities. UPUN hints are not UNIT payment.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def build(data):
    inventory = json.loads((data / "original-fusion-unconnected-review.json").read_text())
    reconciliation = json.loads((data / "original-fusion-object-reconciliation.json").read_text())
    registry = json.loads((data / "original-jass-fusion-registry.json").read_text())
    source_by_ability = {r["abilityRawcode"]: r for r in registry["registrations"]}
    if (inventory["sourceW3xSha256"] != reconciliation["sourceW3xSha256"] or
            inventory["sourceW3xSha256"] != registry["sourceSha256"]):
        raise ValueError("Original map fingerprints do not match")
    objects = {r["rawcode"]: r for r in reconciliation["entries"]}
    grouped = {}
    for blocked in inventory["blocked"]:
        for rawcode in blocked["unmappedUnitRawcodes"]:
            row = grouped.setdefault(rawcode, dict(ingredient=[], result=[], abilities=[],
                                                   otherReasons=set()))
            ability = blocked["abilityRawcode"]
            source = source_by_ability[ability]
            if any(c["kind"] == "UNIT" and c["rawcode"] == rawcode
                   for c in source["conditions"]):
                row["ingredient"].append(ability)
            if any(c["kind"] == "UNIT" and c["rawcode"] == rawcode
                   for c in source["results"]):
                row["result"].append(ability)
            if ability not in row["ingredient"] and ability not in row["result"]:
                raise ValueError("Unmapped object lacks actual UNIT role: " + rawcode)
            row["abilities"].append(ability)
            row["otherReasons"].update(x for x in blocked["reasonCodes"]
                                       if x != "unmapped-original-unit")
    entries = []
    for rawcode in sorted(grouped):
        original = objects[rawcode]
        if (original.get("catalogUnitId") is not None or
                original["approvedForMaterialConsumption"] is not False):
            raise ValueError("Unverified original unit was marked craftable: " + rawcode)
        row = grouped[rawcode]
        entries.append({
            "rawcode": rawcode,
            "originalW3xObjectName": original["originalW3xObjectName"],
            "catalogUnitId": None,
            "ingredientRecipes": sorted(row["ingredient"]),
            "resultRecipes": sorted(row["result"]),
            "affectedRecipes": sorted(row["abilities"]),
            "otherBlockingReasons": sorted(row["otherReasons"]),
            "acquisitionStatus": ("source-wisp-rare-draw-retained-no-spawn"
                                  if rawcode == "h060"
                                  else "no-verified-gameplay-acquisition"),
            "requiredNextEvidence": ("verified-result-catalog-and-spawn-effects"
                                     if rawcode == "h0C2" else
                                     "verified-pirate-ship-catalog-combat-and-pending-wisp-transaction"
                                     if rawcode == "h060" else
                                     "verified-original-object-catalog-and-acquisition-before-transaction"),
        })
    return {
        "format": "ordr-source-only-fusion-materials-v1",
        "sourceW3xSha256": reconciliation["sourceW3xSha256"],
        "executionSource": inventory["executionSource"],
        "policy": "nightmare-otherworld; object names are display evidence only, never permission to consume or create units",
        "summary": {
            "blockedRecipeCount": inventory["summary"]["blockedRecipes"],
            "unmappedRecipeCount": inventory["summary"]["blockedByReason"]["unmapped-original-unit"],
            "distinctSourceOnlyObjects": len(entries),
            "sourceOnlyRoleOccurrences": sum(len(x["ingredientRecipes"]) +
                                             len(x["resultRecipes"]) for x in entries)
        },
        "entries": entries,
    }


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--verify", action="store_true")
    args = p.parse_args()
    destination = ROOT / "data/original-fusion-source-only-materials.json"
    serialized = json.dumps(build(ROOT / "data"), ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if not destination.exists() or destination.read_text() != serialized:
            raise SystemExit("Source-only fusion inventory is stale")
    else:
        destination.write_text(serialized)
