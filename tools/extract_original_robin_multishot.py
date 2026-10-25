#!/usr/bin/env python3
"""Re-extract Robin's native Aroc evidence; never infer final damage/timing.

Requires mpyq for --w3x. Without --output, compare against committed evidence.
The source W3X and extracted Warcraft files must remain outside the repository.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive, parse_sylk_bytes
from extract_original_helper_abilities import read_objects

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROFILE = ROOT / "data/original-2323C-robin-A07M-evidence.json"
MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
SOURCES = {
    "Units\\AbilityData.slk": "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda",
    "Units\\UnitWeapons.slk": "b5be254f57b34f56dd991043e8c2a4936d7bd1f54fce213b201316971ac65ca5",
    "Units\\UnitAbilities.slk": "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d",
    "Units\\CampaignAbilityStrings.txt": "1e6d9f5ccd22f67ad0edf5264bce77aa6b944032881b81ab7051f6bebf97b979",
    "war3map.j": "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d",
    "war3map.w3a": "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0",
}

# Literal source fields. In particular, DataC1=1 is NOT the target count 3.
ABILITY = {
    "code": "Aroc", "levels": "1", "targs1": "air,enemies,ground",
    "Cast1": "0.0", "Dur1": "0.01", "HeroDur1": "0.01",
    "Cool1": "0.", "Area1": "600.", "Rng1": "0.0",
    "DataA1": "0.", "DataB1": "99999.", "DataC1": "1",
}
WEAPON = {
    "weapsOn": "1", "targs1": "structure,air,item,ward,debris,neutral,ground",
    "rangeN1": "600", "atkType1": "normal", "weapTp1": "missile",
    "cool1": "1.2", "dice1": "2", "sides1": "3", "dmgplus1": "60",
    "dmgpt1": "0.25", "targCount1": "3", "damageLoss1": "0.0",
}
UNIT_ABILITIES = {"abilList": "A07Y,A0JE,A0B8,A07M,A03E,Avul"}
STRINGS = {
    "Missileart": "Abilities\\Spells\\Items\\AIso\\BIsvTarget.mdl",
    "MissileHoming": "1", "Missilespeed": "1600",
    "Name": "!0012_꽃꽃열매", "Tip": "|cff1e90ff꽃꽃 열매 :: Lv.1|r",
    "Ubertip": "|cffFAED7D꽃꽃 열매의 능력으로 여러명의 적에게 공격합니다.|r|n|n|cffffdead◎기본효과|r : 3명의 적을 동시에 공격|r",
}


def section(text, rawcode):
    match = re.search(r"^\[" + re.escape(rawcode) + r"\]\s*\n(.*?)(?=^\[|\Z)",
                      text.replace("\r\n", "\n"), re.M | re.S)
    if not match:
        raise ValueError("Missing original ability strings: " + rawcode)
    return dict(line.split("=", 1) for line in match[1].splitlines() if "=" in line)


def inspect_source(ability, weapon, unit_abilities, strings, jass, overrides):
    rows = {}
    for label, row, expected in (
        ("ability", ability, ABILITY), ("weapon", weapon, WEAPON),
        ("unitAbilities", unit_abilities, UNIT_ABILITIES),
        ("strings", strings, STRINGS),
    ):
        rows[label] = {key: row.get(key) for key in expected}
        if rows[label] != expected:
            raise ValueError("Original Robin source changed: " + label)
    if any(old in ("A07M", "Aroc") or new in ("A07M", "Aroc")
           for _, old, new, _ in overrides):
        raise ValueError("Unexpected Robin native ability W3A override")
    # Literal search only; absence does not rule out indirect calls or hooks.
    decimal_rawcode = int.from_bytes(b"A07M", "big")
    references = re.findall(r"A07M|\$4130374[dD]|\b" + str(decimal_rawcode) + r"\b", jass)
    if references:
        raise ValueError("New direct A07M JASS reference requires review")
    return {
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": MAP_SHA,
        "sourceFiles": [{"path": name, "sha256": sha} for name, sha in SOURCES.items()],
        "catalogUnitId": "U-01", "unitRawcode": "h00O", "abilityRawcode": "A07M",
        "sourceRows": rows,
        "targetCountSource": "Units\\UnitWeapons.slk:h00O:targCount1",
        "totalNativeTargetCount": int(weapon["targCount1"]),
        "maximumAdditionalTargets": int(weapon["targCount1"]) - 1,
        "directJassReferenceCount": 0,
        "w3aOverrideCount": 0,
        "nativeAbilityChanceFieldPresent": False,
        "safeForUnconditionalLiveCombat": False,
        "unverified": [
            "native-target-enumeration-and-mask-semantics",
            "damage-dice-sharing-or-independent-rolls",
            "missile-launch-and-impact-timing",
            "DataA1-DataB1-DataC1-engine-application",
            "primary-zero-damage-and-secondary-shot-behavior",
            "target-immunity-and-final-hp-quantization",
            "indirect-jass-damage-hooks-and-runtime-upgrades",
        ],
    }


def build(w3x):
    if hashlib.sha256(w3x.read_bytes()).hexdigest() != MAP_SHA:
        raise ValueError("Wrong original W3X fingerprint")
    with open_archive(w3x) as archive:
        blobs = {name: archive.read_file(name) for name in SOURCES}
    for name, digest in SOURCES.items():
        if blobs[name] is None or hashlib.sha256(blobs[name]).hexdigest() != digest:
            raise ValueError("Wrong original source fingerprint: " + name)
    return inspect_source(
        parse_sylk_bytes(blobs["Units\\AbilityData.slk"])["A07M"],
        parse_sylk_bytes(blobs["Units\\UnitWeapons.slk"])["h00O"],
        parse_sylk_bytes(blobs["Units\\UnitAbilities.slk"])["h00O"],
        section(blobs["Units\\CampaignAbilityStrings.txt"].decode("utf-8-sig"), "A07M"),
        blobs["war3map.j"].decode("utf-8-sig"),
        read_objects(blobs["war3map.w3a"]),
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = build(args.w3x)
    if args.output:
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
        print("Wrote source-only Robin evidence:", args.output)
    else:
        if json.loads(DEFAULT_PROFILE.read_text(encoding="utf-8")) != report:
            raise ValueError("Committed Robin evidence differs from original W3X")
        print("Robin A07M evidence matches the pinned original W3X; live parity remains unverified.")


if __name__ == "__main__":
    main()
