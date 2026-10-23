#!/usr/bin/env python3
"""Re-extract h08A and A0BZ/A07T/A04R evidence from the pinned original W3X.

Requires mpyq only when reading a W3X. Does not invent A0BZ level-three
mana or infer Warcraft's actual AIsr damage mechanics from its isr2 field.

Usage:
    PYTHONPATH=/tmp/ordr-mpyq python3 tools/extract_original_helper_slk.py \
        --w3x '/path/to/ORDR_S2_2.323[C].w3x'
    # By default compares the re-extracted source against committed JSON.
    # --output /path/to/profile.json explicitly writes a new profile.
"""
import argparse
import hashlib
import json
from pathlib import Path

from build_original_map_base_stats import open_archive
from extract_original_unit_slk_stats import parse_sylk_bytes


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROFILE = ROOT / "data/original-2323C-helper-slk-profile.json"
MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
SLK_HASHES = {
    "AbilityData": "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda",
    "UnitBalance": "f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5",
    "UnitAbilities": "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d",
}
SOURCE_POSITIONS = [
    (-5888, 4464), (128, 4464), (-5888, -656), (128, -656)
]


def require_equal(actual, expected, field):
    if actual != expected:
        raise ValueError(f"Original source changed: {field}: {actual!r} != {expected!r}")


def as_float(row, field):
    if field not in row or row[field] in ("", "_"):
        raise ValueError("Original SLK field missing: " + field)
    return float(row[field])


def inspect_source(ability, balance, unit_abilities, jass):
    """Check every published profile value against independently read rows."""
    spell_rows = {key: ability[key] for key in ("A0BZ", "A07T", "A04R", "A0UY")}
    h08a = balance["h08A"]
    h08a_abilities = unit_abilities["h08A"]["abilList"].split(",")
    require_equal(spell_rows["A0BZ"]["code"], "ANcs", "A0BZ base code")
    require_equal(spell_rows["A07T"]["code"], "AOeq", "A07T base code")
    require_equal(spell_rows["A04R"]["code"], "AIsr", "A04R base code")
    require_equal(int(spell_rows["A0BZ"]["levels"]), 3, "A0BZ levels")
    require_equal(int(spell_rows["A07T"]["levels"]), 2, "A07T levels")
    require_equal(int(spell_rows["A04R"]["levels"]), 26, "A04R levels")
    potion = spell_rows["A0UY"]
    require_equal(potion["code"], "AOsf", "A0UY inherited base ability")
    require_equal(int(potion["levels"]), 3, "A0UY charges / levels")
    for level in (1, 2, 3):
        require_equal(as_float(potion, f"Cool{level}"), 0.0,
                      f"A0UY cooldown level {level}")
        # Missing fields in imported custom SLK are *not* proof of a zero
        # mana cost: AOsf may inherit it from the Warcraft base table.
        if potion.get(f"Cost{level}") not in (None, "", "_"):
            raise ValueError(f"A0UY mana cost level {level} is no longer inherited")

    spells = {}
    for code, requested_levels in (("A0BZ", 2), ("A07T", 2)):
        row = spell_rows[code]
        levels = []
        for level in range(1, requested_levels + 1):
            levels.append({
                "level": level,
                "castRange": int(as_float(row, f"Rng{level}")),
                "manaCost": int(as_float(row, f"Cost{level}")),
                "cooldown": int(as_float(row, f"Cool{level}")),
                "effectRadius": int(as_float(row, f"Area{level}")),
            })
        spells[code] = {
            "originalBaseCode": row["code"],
            "declaredLevels": int(row["levels"]),
            "verifiedLevels": levels,
        }
    require_equal(spells["A0BZ"]["verifiedLevels"],
                  [{"level": 1, "castRange": 99999, "manaCost": 500,
                    "cooldown": 100, "effectRadius": 450},
                   {"level": 2, "castRange": 99999, "manaCost": 333,
                    "cooldown": 66, "effectRadius": 450}], "A0BZ")
    require_equal(spells["A07T"]["verifiedLevels"],
                  [{"level": 1, "castRange": 99999, "manaCost": 320,
                    "cooldown": 150, "effectRadius": 625},
                   {"level": 2, "castRange": 99999, "manaCost": 320,
                    "cooldown": 100, "effectRadius": 625}], "A07T")
    if "Cost3" in spell_rows["A0BZ"] and spell_rows["A0BZ"]["Cost3"] not in ("", "_"):
        raise ValueError("A0BZ level-three cost is no longer missing")
    require_equal(int(as_float(spell_rows["A0BZ"], "Cool3")), 150,
                  "A0BZ unimplemented level-three cooldown")
    spells["A0BZ"]["unverifiedLevel3ManaCost"] = True

    lower_levels = [
        {"level": level, "value": round(as_float(spell_rows["A04R"], f"DataB{level}"), 6)}
        for level in range(1, 5)
    ]
    require_equal(lower_levels,
                  [{"level": i, "value": round((1 - i) / 100, 6)}
                   for i in range(1, 5)], "A04R lower levels")
    spells["A04R"] = {
        "originalBaseCode": "AIsr",
        "declaredLevels": 26,
        "baseSlkIsr2Levels": lower_levels,
        "higherOverridesData": "original-2323C-helper-A04R-evidence.json",
    }

    require_equal(int(as_float(h08a, "mana0")), 15, "h08A starting mana")
    require_equal(int(as_float(h08a, "manaN")), 10000, "h08A maximum mana")
    require_equal(as_float(h08a, "regenMana"), 0.3, "h08A mana regeneration")
    require_equal(h08a_abilities,
                  ["A07W", "A07T", "A07X", "A0BZ", "A082", "A07Z",
                   "A07U", "A07V", "A0BY", "A081", "Avul"],
                  "h08A original abilities")
    for anchor in (
        "set Ll[0]=B2", "set Ll[1]=B3", "set Ll[2]=B4", "set Ll[3]=B5",
        "set kk[EK]=CreateUnit(Player(EK),$68303841,GetUnitX(RV[EK]),GetUnitY(RV[EK])-1200,bj_UNIT_FACING)",
        "set RV[EK]=CreateUnit(Player(EK),$48304334,GetRectMinX(Ll[EK]),GetRectMinY(Ll[EK]),bj_UNIT_FACING)",
    ):
        if anchor not in jass:
            raise ValueError("Missing original spawn evidence: " + anchor)
    for anchor in (
        "function CKX takes nothing returns nothing",
        "if X3<=20 then",
        "if kt[JG] then",
        "call CKS(K7,JG)",
        "function CKS takes integer Hk,integer JG returns nothing",
        "call SetUnitAbilityLevel(kk[JG],$4130425A,2)",  # A0BZ
        "call SetUnitAbilityLevel(kk[JG],$41304259,2)",  # A0BY
        "call SetUnitAbilityLevel(kk[JG],$41303754,2)",  # A07T
        "call SetUnitAbilityLevel(kk[JG],$41303758,2)",  # A07X
        "call SetUnitAbilityLevel(kk[JG],$41303757,2)",  # A07W
        "call UnitAddAbility(kk[JG],$41305559)",       # A0UY
        "function CJk takes nothing returns nothing",
        "if GetUnitAbilityLevel(GetTriggerUnit(),$41305559)==3 then",
        "call UnitRemoveAbility(GetTriggerUnit(),$41305559)",
        "call IncUnitAbilityLevel(GetTriggerUnit(),$41305559)",
        "call SetUnitState(kk[JG],UNIT_STATE_MANA,(GetUnitState(kk[JG],UNIT_STATE_MANA)+300)*1.)",
    ):
        if anchor not in jass:
            raise ValueError("Missing original navigation/potion JASS: " + anchor)
    base_rects = [(-5888, 5664), (128, 5664), (-5888, 544), (128, 544)]
    for i, (x, y) in enumerate(base_rects):
        if f"set B{i + 2}=Rect({float(x):.0f}.,{float(y):.0f}.," not in jass:
            raise ValueError("Original helper spawning rectangle changed: B" + str(i + 2))
        require_equal((x, y - 1200), SOURCE_POSITIONS[i], "h08A spawn")

    return {
        "gameVersion": "S2 2.323[C]",
        "sourceMapSha256": MAP_SHA,
        "sourceJassSha256": JASS_SHA,
        "sourceAbilitySlkSha256": SLK_HASHES["AbilityData"],
        "sourceUnitBalanceSlkSha256": SLK_HASHES["UnitBalance"],
        "sourceUnitAbilitiesSlkSha256": SLK_HASHES["UnitAbilities"],
        "abilities": spells,
        "maximumOutputNavigation": {
            "choiceJass": "CKX -> CKS(1, player)",
            "availableAfterRound": 20,
            "oncePerPlayer": True,
            "upgradedAbilities": ["A0BZ", "A0BY", "A07T", "A07X", "A07W"],
            "newAbility": "A0UY",
        },
        "manaPotion": {
            "rawcode": "A0UY",
            "baseAbility": "AOsf",
            "jassFunction": "CJk",
            "charges": 3,
            "postCastManaGain": 300,
            "originalCooldownSeconds": [0, 0, 0],
            "inheritedManaCostsUnknown": True,
        },
        "helper": {
            "rawcode": "h08A",
            "startingMana": 15,
            "maxMana": 10000,
            "manaRegenPerSecond": 0.3,
            "defaultAbilities": h08a_abilities,
            "playerSpawnPositions": [
                {"player": i, "x": x, "y": y}
                for i, (x, y) in enumerate(SOURCE_POSITIONS)
            ],
            "sourceJassFunction": "CUo",
            "sourceRectArray": "Ll[0..3]=B2..B5",
            "positionExpression":
                "GetRectMinX(Ll[player]), GetRectMinY(Ll[player]) - 1200",
        },
        "cannotInfer": {
            "a0bzLevel3ManaCost": True,
            "originalTargetImmunity": True,
            "originalDamageAfterDefense": True,
            "a04rEngineFormula": True,
            "manaPotionInheritedManaCosts": True,
        },
    }


def extract(w3x):
    digest = hashlib.sha256(w3x.read_bytes()).hexdigest()
    require_equal(digest, MAP_SHA, "W3X SHA-256")
    with open_archive(w3x) as archive:
        slks = {}
        for name, expected_hash in SLK_HASHES.items():
            raw = archive.read_file("Units\\" + name + ".slk")
            if not raw:
                raise ValueError("Missing W3X imported SLK: " + name)
            require_equal(hashlib.sha256(raw).hexdigest(), expected_hash, name)
            slks[name] = parse_sylk_bytes(raw)
        jass_bytes = archive.read_file("war3map.j")
        require_equal(hashlib.sha256(jass_bytes).hexdigest(), JASS_SHA, "JASS SHA-256")
    return inspect_source(slks["AbilityData"], slks["UnitBalance"],
                          slks["UnitAbilities"],
                          jass_bytes.decode("utf-8-sig").replace("\r\n", "\n"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--profile", type=Path, default=DEFAULT_PROFILE)
    parser.add_argument("--output", type=Path,
                        help="Explicitly write extracted JSON instead of checking profile")
    args = parser.parse_args()
    profile = extract(args.w3x)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(profile, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        print("Extracted original helper SLK profile to", args.output)
    else:
        require_equal(profile, json.loads(args.profile.read_text(encoding="utf-8")),
                      "committed helper SLK profile")
        print("Verified original W3X, JASS, 3 SLKs and helper profile.")


if __name__ == "__main__":
    main()
