#!/usr/bin/env python3
"""Audit executable lower-grade management against the pinned original files.

Original W3X/JASS/SLK inputs remain outside the repository. No game numbers
are inferred from web names, grades, test fixtures or review-only mappings.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from extract_jass_fusions import extract
from extract_original_unit_slk_stats import parse_sylk_bytes

JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
REVIEWED = {
    "A006": ("h00O", "U-01"), "A00E": ("h00C", "U-02"),
    "A00W": ("h00D", "U-03"), "A0N5": ("h00M", "U-04"),
    "A00D": ("h00K", "U-05"), "A00P": ("h00I", "U-06"),
    "A00V": ("h00N", "U-07"), "A02I": ("h00G", "U-08"),
    "A01R": ("h00E", "U-09"), "A00L": ("h00J", "U-10"),
    "A01H": ("h00L", "U-11"), "A01L": ("h00F", "U-12"),
    "A0N7": ("h00A", "U-13"),
}


def audit(jass, ability_slk, map_path):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError("Wrong original JASS")
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    funcs = {}
    for name in ("CH5", "CHi", "CHu", "CHv", "CHw", "CHx", "CHy", "CH8",
                 "CIB", "CT1", "CT2", "CRO", "CRP", "CRQ", "CRR", "CRX", "CSE", "CSF"):
        match = re.search(r"^function " + name + r" takes .*?^endfunction$", text, re.M | re.S)
        if not match:
            raise ValueError("Missing function " + name)
        body = match.group(0)
        funcs[name] = {"line": text[:match.start()].count("\n") + 1,
                       "sha256LF": hashlib.sha256(body.encode()).hexdigest()}
        if name in ("CH5", "CHi") and "$5550554E" in body:
            raise ValueError("UPUN now has a registered check/payment callback")
    abilities = parse_sylk_bytes(ability_slk.read_bytes())
    originals = json.loads(map_path.read_text())
    if originals["sourceW3xSha256"] != MAP_SHA:
        raise ValueError("Wrong original objects")
    objects = {r["originalRawcode"]: r for r in originals["catalogEntries"] if r.get("originalRawcode")}
    registry = {r["abilityRawcode"]: r for r in extract(jass)["registrations"]}
    recipes = []
    for ability, (rawcode, unit) in REVIEWED.items():
        recipe = registry[ability]
        if recipe["results"] != [{"kind": "UNIT", "rawcode": rawcode, "count": 1}]:
            raise ValueError("Changed output " + ability)
        if objects[rawcode]["catalogId"] != unit:
            raise ValueError("Changed object identity " + rawcode)
        casters = []
        for condition in recipe["conditions"]:
            if condition["kind"] == "UPUN":
                continue
            if condition["kind"] != "UNIT" or objects[condition["rawcode"]]["grade"] != "흔함":
                raise ValueError("Unsupported material/cost condition " + ability)
            original = objects[condition["rawcode"]]
            if ability in original["originalAbilityList"].split(","):
                casters.append(original["catalogId"])
        native = abilities[ability]
        if not casters or native.get("code") not in ("AHwe", "Aroa") or native.get("targs1") != "_":
            raise ValueError("Unverified original no-target cast " + ability)
        recipes.append({**recipe, "resultCatalogId": unit,
                        "originalCasterCatalogIds": casters,
                        "nativeBaseAbility": native["code"], "nativeTargets": native["targs1"],
                        "nativeCastSeconds": float(native["Cast1"]),
                        "nativeCooldownSeconds": float(native["Cool1"])})
    if "set C=Rect(-4032.,3776.,-3488.,4032.)" not in text or "set Lq[0]=C" not in text:
        raise ValueError("Changed player-zero uncommon Wisp placement")
    return {"gameVersion": "S2 2.323[C]", "sourceW3xSha256": MAP_SHA,
            "sourceJassSha256": JASS_SHA,
            "sourceAbilitySlkSha256": hashlib.sha256(ability_slk.read_bytes()).hexdigest(),
            "scope": "13 common-to-uncommon fusions; CRO/CRP/CRQ/CRR sales; CSE/CSF uncommon Wisp",
            "functions": funcs, "recipes": recipes,
            "uncommonWispPositionPlayerZero": {"x": -3760, "y": 3904},
            "limitations": ["Not a full combat/stat verification.",
                            "Mobile RNG preserves distributions, not Warcraft global replay equivalence.",
                            "Preparation has no battle clock or invented enemies.",
                            "Fusion uses an atomic mobile confirmation; native 0.06s cast animation is not simulated."]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--jass", required=True, type=Path)
    parser.add_argument("--ability-slk", required=True, type=Path)
    parser.add_argument("--map-stats", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = audit(args.jass, args.ability_slk, args.map_stats)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Audited {len(result['recipes'])} original lower-grade fusion recipes")


if __name__ == "__main__":
    main()
