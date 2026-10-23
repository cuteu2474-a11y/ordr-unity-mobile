#!/usr/bin/env python3
"""Rebuild CJY alchemy refund units from the pinned W3X BjB + CWR registrations.

The map's CJY loops over Bi8/R3 fusion abilities for a consumed rawcode,
then creates ONLY BGI conditions whose kind is UNIT. The other BGI kinds
are neither items nor gifts and must not be emitted as alchemy refunds.

With --w3x, verify source map and JASS hashes and compare with committed JSON.
With --output, explicitly replace the committed JSON with re-extracted data.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive
from extract_jass_fusions import REGISTER as CWR_REGISTER, entry as cwr_entry, decode_rawcode
from extract_original_helper_slk import MAP_SHA, JASS_SHA, require_equal


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DATA = ROOT / "data/original-2323C-helper-alchemy-recipes.json"
DEFAULT_FUSIONS = ROOT / "data/original-jass-fusion-registry.json"
BI8 = re.compile(r"^call Bi8\(\$([0-9A-F]{8}),\$([0-9A-F]{8})\)$")
REQUIRED_JASS = (
    "local integer Rf=LoadInteger(R3,hP,-1)",
    "local integer R2=LoadInteger(R3,hP,Hk)",
    "local integer Rf=LoadInteger(Wb,R2,We)",
    "set hT=LoadInteger(Wq,R2,EK)",
    "set hU=LoadInteger(Wr,R2,EK)",
    "set hV=LoadInteger(Ws,R2,EK)",
    "if hT==$554E4954 then",
    "set Cm=CreateUnit(Player(JG),hU,GetPlayerStartLocationX(Player(JG)),GetPlayerStartLocationY(Player(JG)),bj_UNIT_FACING)",
    "call IssueImmediateOrder(Cm,\"holdposition\")",
    "if IsUnitType(Cm,UNIT_TYPE_UNDEAD) then",
    "call UnitAddAbility(Cm,$41303759)",
    "call BdD(GetSpellTargetUnit())",
)


def function_body(text, name):
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines)
                  if line.startswith("function " + name + " takes ")), None)
    if start is None:
        raise ValueError("Original JASS function missing: " + name)
    end = next((i for i in range(start + 1, len(lines))
                if lines[i].strip() == "endfunction"), None)
    if end is None:
        raise ValueError("Unterminated JASS function: " + name)
    return [line.strip() for line in lines[start + 1:end]]


def fourcc(hex_text):
    return bytes.fromhex(hex_text).decode("latin-1")


def verify_cwr_source(jass, fusion_registry):
    """Reparse CWR; preserve CJY's UNIT order despite old archive tag sorting."""
    rows = []
    current = None
    for row in function_body(jass, "CWR"):
        if not row.startswith(("call BGG(", "call BGH(", "call BGI(")):
            continue
        match = CWR_REGISTER.fullmatch(row)
        if not match:
            raise ValueError("Unrecognized original CWR entry: " + row)
        operation, first, second, count = match.groups()
        if operation == "BGG":
            current = {"abilityRawcode": decode_rawcode(first),
                       "results": [], "conditions": []}
            rows.append(current)
        elif current is None:
            raise ValueError("CWR ingredient without preceding BGG")
        else:
            current["results" if operation == "BGH" else "conditions"].append(
                cwr_entry(first, second, count))
    registered = fusion_registry["registrations"]
    if len(rows) != 266 or len(registered) != 266:
        raise ValueError("Original CWR BGG/BGI registrations differ from pinned archive")
    reordered = 0
    for source, archived in zip(rows, registered):
        if (source["abilityRawcode"] != archived["abilityRawcode"] or
                source["results"] != archived["results"]):
            raise ValueError("Original CWR output differs from pinned archive")
        original_unit = [x for x in source["conditions"] if x["kind"] == "UNIT"]
        archived_unit = [x for x in archived["conditions"] if x["kind"] == "UNIT"]
        if original_unit != archived_unit:
            raise ValueError("Original CWR UNIT order differs from pinned archive")
        # Original archive groups some UNIT tags before UPUN tags. Never
        # claim it preserves full BGI interleaving for normal fusion checks.
        as_key = lambda x: (x["kind"], x["rawcode"], x["count"])
        if Counter(map(as_key, source["conditions"])) != Counter(
                map(as_key, archived["conditions"])):
            raise ValueError("Original CWR BGI entries differ from pinned archive")
        if source["conditions"] != archived["conditions"]:
            reordered += 1
    return {row["abilityRawcode"]: row["conditions"] for row in rows}, reordered


def inspect_source(jass, fusion_registry):
    """Inspect both registries; only source-exact UNIT entries are refundable."""
    if fusion_registry.get("gameVersion") != "S2 2.323[C]":
        raise ValueError("Original CWR version changed")
    require_equal(fusion_registry.get("sourceSha256"), MAP_SHA, "CWR W3X SHA")
    if fusion_registry.get("conditionsInOriginalOrder") is not True:
        raise ValueError("CWR conditions must preserve source order")
    if fusion_registry.get("registrationCount") != 266:
        raise ValueError("Original CWR registration count changed")
    source_recipes, reordered_count = verify_cwr_source(jass, fusion_registry)
    for anchor in REQUIRED_JASS:
        if anchor not in jass:
            raise ValueError("Original CJY semantics changed: " + anchor)
    if "call CJY(JG,hP,EK)" not in function_body(jass, "CJZ"):
        raise ValueError("CJZ no longer dispatches to CJY")
    recipes = source_recipes

    # Bi8 appends fusion abilities to R3[hP], keeping order. The same key
    # and order must be used in CJY; labels from Bi9 are not ingredients.
    links = {}
    for row in function_body(jass, "BjB"):
        if not row.startswith("call Bi8("):
            continue
        match = BI8.fullmatch(row)
        if match is None:
            raise ValueError("Unrecognized original Bi8 registration: " + row)
        target, ability = (fourcc(hex_text) for hex_text in match.groups())
        if ability not in recipes:
            raise ValueError("Original R3 ability absent from CWR: " + ability)
        links.setdefault(target, []).append(ability)
    if sum(map(len, links.values())) != 265 or len(links) != 265:
        raise ValueError("Original R3 target registrations changed")

    entries = []
    for target in sorted(links):
        ability_list = links[target]
        refund = []
        ignored = 0
        for ability in ability_list:
            for condition in recipes[ability]:
                if condition["kind"] == "UNIT":
                    if len(condition["rawcode"]) != 4 or not isinstance(condition["count"], int) or condition["count"] < 1:
                        raise ValueError("Malformed original alchemy UNIT condition")
                    refund.append({"rawcode": condition["rawcode"],
                                   "count": condition["count"]})
                else:
                    ignored += 1
        entries.append({"targetRawcode": target, "fusionAbilities": ability_list,
                        "refundUnits": refund, "ignoredNonUnitConditions": ignored})
    return {
        "gameVersion": "S2 2.323[C]",
        "sourceMapSha256": MAP_SHA,
        "sourceJassSha256": JASS_SHA,
        "sourceFunctions": ["BjB/Bi8", "CWR/BGG/BGI", "CJZ", "CJY"],
        "targetCount": len(entries),
        "existingFusionArchiveInterleavingMismatches": reordered_count,
        "entries": entries,
        "semantics": {
            "targetMustPassCJZOwnershipAndAbilityCheck": True,
            "onlyBgiUnitConditionsAreRefunded": True,
            "preserveR3AndBgiRegistrationOrder": True,
            "creationPosition": "player start location",
            "createdUnitOrder": "holdposition",
            "undeadCreatedUnitAbility": "A07Y",
            "missingR3RecipeConsumesTargetWithoutRefund": True,
            "includesTargetConsumptionAndManaTransaction": False,
        },
    }


def extract(w3x, registry_file=DEFAULT_FUSIONS):
    require_equal(hashlib.sha256(w3x.read_bytes()).hexdigest(), MAP_SHA,
                  "W3X SHA-256")
    with open_archive(w3x) as archive:
        raw = archive.read_file("war3map.j")
    require_equal(hashlib.sha256(raw).hexdigest(), JASS_SHA, "JASS SHA-256")
    registry = json.loads(registry_file.read_text(encoding="utf-8"))
    return inspect_source(raw.decode("utf-8-sig"), registry)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--fusion-registry", type=Path, default=DEFAULT_FUSIONS)
    parser.add_argument("--profile", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    data = extract(args.w3x, args.fusion_registry)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
        print("Extracted original alchemy refund data:", args.output)
    else:
        require_equal(data, json.loads(args.profile.read_text(encoding="utf-8")),
                      "committed original alchemy recipe data")
        print("Verified original W3X/JASS and", data["targetCount"],
              "source-ordered alchemy refund recipes.")


if __name__ == "__main__":
    main()
