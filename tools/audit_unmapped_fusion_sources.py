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
    if (inventory["sourceW3xSha256"] != reconciliation["sourceW3xSha256"] or
            inventory["sourceW3xSha256"] != registry["sourceSha256"]):
        raise ValueError("Original map fingerprints do not match")
    objects = {r["rawcode"]: r for r in reconciliation["entries"]}
    # H08T is an abstract source UNIT bucket with a separately audited group
    # of real alternatives, not an independently spawned map object.
    material_groups = json.loads((data / "original-2323C-owned-material-fusions.json").read_text())
    grouped_rawcodes = set(material_groups["materialGroups"])
    # Audit every real source-only UNIT role, including a later executable
    # outcome. Deriving this solely from *blocked* recipes hides h0C2 after
    # A0QG's mobile-result permission was added.
    grouped = {}
    for source in registry["registrations"]:
        if source["abilityRawcode"] == "TEST":
            continue
        for role, instructions in (("ingredient", source["conditions"]),
                                   ("result", source["results"])):
            for instruction in instructions:
                if instruction["kind"] != "UNIT":
                    continue
                rawcode = instruction["rawcode"]
                if (rawcode not in objects or rawcode in grouped_rawcodes or
                        objects[rawcode].get("catalogUnitId") is not None):
                    continue
                row = grouped.setdefault(rawcode, dict(ingredient=[], result=[], abilities=[],
                                                       otherReasons=set()))
                ability = source["abilityRawcode"]
                row[role].append(ability)
                if ability not in row["abilities"]:
                    row["abilities"].append(ability)
    blocked_by_ability = {row["abilityRawcode"]: row for row in inventory["blocked"]}
    for rawcode, row in grouped.items():
        for ability in row["abilities"]:
            blocked = blocked_by_ability.get(ability)
            if blocked is not None:
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
            "acquisitionStatus": ("source-CWR-mobile-result-enabled-native-callback-unverified"
                                  if rawcode == "h0C2" else
                                  "source-wisp-rare-draw-retained-no-spawn"
                                  if rawcode == "h060"
                                  else "no-verified-gameplay-acquisition"),
            "requiredNextEvidence": ("source-native-CWR-result-callback-and-gameplay-parity"
                                     if rawcode == "h0C2" else
                                     "verified-pirate-ship-catalog-combat-and-pending-wisp-transaction"
                                     if rawcode == "h060" else
                                     "verified-original-object-catalog-and-acquisition-before-transaction"),
        })
    return {
        "format": "ordr-source-only-fusion-materials-v1",
        "sourceW3xSha256": reconciliation["sourceW3xSha256"],
        "executionSource": inventory["executionSource"],
        "policy": "nightmare-otherworld; W3X object names alone never grant crafting or acquisition; executable mobile CWR recipes require independent source-transaction approval",
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
