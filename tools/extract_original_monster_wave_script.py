#!/usr/bin/env python3
"""Index JASS wave IDs/spawn cadence without inventing mode-adjusted HP.

The original script uses ZD[round] for line enemies, uO[round] for bosses,
and separate event callbacks before and after round 50. This extractor
accepts only the pinned ORDR S2 2.323[C] map and exact reviewed callbacks.

Requires mpyq:
    python tools/extract_original_monster_wave_script.py --w3x '/path/map.w3x'
"""

import argparse
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive
from extract_original_monster_object_stats import MAP_SHA256, build as build_objects


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "data/original-2323C-monster-wave-script-evidence.json"


def function_body(jass, name):
    pattern = rf"^function {re.escape(name)} takes [^\n]*\n.*?^endfunction$"
    result = re.search(pattern, jass, re.MULTILINE | re.DOTALL)
    if result is None:
        raise ValueError(f"Missing original JASS function: {name}")
    return result.group(), jass.count("\n", 0, result.start()) + 1


def assert_fragments(text, function_name, *fragments):
    for fragment in fragments:
        if fragment not in text:
            raise ValueError(f"JASS {function_name} structure changed: {fragment}")


def original_id(hex_text):
    data = bytes.fromhex(hex_text)
    if len(data) != 4 or not all(32 <= item <= 126 for item in data):
        raise ValueError("Invalid Warcraft original rawcode " + hex_text)
    return data.decode("ascii")


def extract_arrays(init_source, init_first_line):
    result = {}
    for symbol in ("ZD", "uO"):
        entries = {}
        pattern = rf"^set {symbol}\[(\d+)\]=\$([0-9A-Fa-f]{{8}})$"
        for match in re.finditer(pattern, init_source, re.MULTILINE):
            round_number = int(match.group(1))
            if round_number in entries:
                raise ValueError(f"Duplicate JASS wave {symbol}[{round_number}]")
            entries[round_number] = {
                "rawcode": original_id(match.group(2)),
                "sourceLine": init_first_line + init_source.count("\n", 0, match.start()),
                "sourceExpression": match.group(),
            }
        result[symbol] = entries
    return result


def checked_wave_callbacks(jass):
    init, init_line = function_body(jass, "CUl")
    pieces = {name: function_body(jass, name)[0] for name in (
        "CVo", "CVp", "CVq", "CVr", "CVs", "CVt", "CVu", "CVw",
        "CVX", "CVY", "CVZ", "CVa", "CVb", "CVc", "CVd", "CVf",
    )}
    globals_expected = (
        r"^integer vJ=35$", r"^integer vH=1$", r"^constant integer uq=65$",
        r"^constant integer LG=4$",
    )
    for pattern in globals_expected:
        if not re.search(pattern, jass, re.MULTILINE):
            raise ValueError("Original wave count or ending round changed: " + pattern)
    for condition in (
        "call TriggerAddCondition(Lu,Condition(function CUl))",
        "call TriggerAddCondition(uj,Condition(function CVf))",
        "call TriggerAddCondition(uj,Condition(function CVw))",
    ):
        if condition not in jass:
            raise ValueError("Original JASS wave callbacks are not registered: " + condition)
    assert_fragments(pieces["CVo"], "CVo",
                     "CreateUnit(Player(6),ZD[X3]", "call SetUnitAbilityLevel(Cm,JD,vM)")
    assert_fragments(pieces["CVp"], "CVp",
                     "CreateUnit(Player(6),uO[X3]", "call UnitAddAbility(Cm,$41393032)")
    assert_fragments(pieces["CVX"], "CVX", "CreateUnitAtLoc(Player(6),ZD[X3]")
    assert_fragments(pieces["CVY"], "CVY", "CreateUnitAtLoc(Player(6),uO[X3]")
    assert_fragments(pieces["CVZ"], "CVZ",
                     "CreateUnitAtLoc(Player(6),ZD[X3]", "광폭화 몬스터가 소환되었습니다.")
    assert_fragments(pieces["CVq"], "CVq", "if Ji[EK] then", "call CVo(EK)")
    assert_fragments(pieces["CVr"], "CVr", "if Ji[EK] then", "call CVp(EK)")
    assert_fragments(pieces["CVs"], "CVs",
                     "if vO==0 then", "set vO=vO-1", "ModuloInteger(X3,10)==0",
                     "call CVr()", "call CVq()")
    assert_fragments(pieces["CVt"], "CVt", "set vO=vJ", "TimerStart(vN,.5,true,function CVs)")
    assert_fragments(pieces["CVu"], "CVu", "set vO=vH", "TimerStart(vN,.5,true,function CVs)")
    assert_fragments(pieces["CVw"], "CVw",
                     "if not (X3>0 and X3<=50) then", "ModuloInteger(X3,10)==0",
                     "call CVu()", "call CVt()")
    assert_fragments(pieces["CVa"], "CVa",
                     "if Ji[EK] then", "call CVX(EK)", "if vK==15 and vL<=4 then",
                     "call CVZ(EK)")
    assert_fragments(pieces["CVb"], "CVb", "if Ji[EK] then", "call CVY(EK)")
    assert_fragments(pieces["CVc"], "CVc",
                     "if vI==0 then", "set vI=vI-1",
                     "ModuloInteger(X3,5)==0 and X3>50 and vG>0",
                     "call CVb()", "set vK=vK+1", "call CVa()")
    assert_fragments(pieces["CVd"], "CVd",
                     "set vG=vH", "set vI=vJ", "TimerStart(vF,.5,true,function CVc)")
    assert_fragments(pieces["CVf"], "CVf", "if not (X3>50) then", "call CVd()")
    return extract_arrays(init, init_line), pieces


def build_index(jass, original_objects, map_sha):
    original_jass_sha256 = hashlib.sha256(jass.encode("utf-8")).hexdigest()
    # The original map uses CRLF line endings; normalize only for scanning.
    jass = jass.replace("\r\n", "\n")
    arrays, _ = checked_wave_callbacks(jass)
    line_ids, boss_ids = arrays["ZD"], arrays["uO"]
    if (len(line_ids) != 60 or len(boss_ids) != 8
            or set(line_ids) != set(range(1, 66)) - {10, 20, 30, 40, 50}
            or set(boss_ids) != {10, 20, 30, 40, 50, 55, 60, 65}):
        raise ValueError("Original wave lookup arrays have unexpected coverage")
    objects = {entry["rawcode"]: entry for entry in original_objects["entries"]}
    if original_objects["sourceW3xSha256"] != map_sha:
        raise ValueError("JASS and object tables came from different source maps")
    used_codes = set()
    waves = []
    for wave_number in range(1, 66):
        spawns = []
        for array_name in ("ZD", "uO"):
            if wave_number not in arrays[array_name]:
                continue
            jass_entry = arrays[array_name][wave_number]
            code = jass_entry["rawcode"]
            unit = objects.get(code)
            expected_category = ("level-labeled" if array_name == "ZD"
                                 else "boss-labeled")
            if unit is None or unit["nameBasedCategory"] != expected_category:
                raise ValueError(f"JASS {array_name}[{wave_number}] not matched to the original object")
            if code in used_codes:
                raise ValueError(f"Original wave rawcode reused unexpectedly: {code}")
            used_codes.add(code)
            spawns.append({
                "role": "line" if array_name == "ZD" else "boss",
                "rawcode": code,
                "originalMapName": unit["originalMapName"],
                "sourceArray": f"{array_name}[{wave_number}]",
                "sourceJassLine": jass_entry["sourceLine"],
                "sourceJassAssignment": jass_entry["sourceExpression"],
                "objectBaseHitPoints": unit["objectBaseHitPoints"],
                "objectBaseArmor": unit["objectBaseArmor"],
                "warcraftArmorType": unit["warcraftArmorType"],
                "spawnsPerActivePlayer": 35 if array_name == "ZD" else 1,
                "cadenceSeconds": .5,
                "firstSpawnTick": 1,
            })
        if not spawns:
            raise ValueError(f"Empty JASS wave {wave_number}")
        special_berserk = (50 < wave_number < 65 and wave_number % 5 != 0)
        waves.append({
            "waveNumber": wave_number,
            "normalAndBossSpawns": spawns,
            "conditionalBerserk": ({
                "sameRawcodeAsLine": True,
                "spawnAtTick": 15,
                "maxSpawnEventsAcrossPlayers": 4,
                "condition": "vK==15 and vL<=4 and Ji[player]",
                "evidenceFunctions": ["CVc", "CVa", "CVZ"],
            } if special_berserk else None),
            "finalHealthAndArmorVerified": False,
            "exactSpawnPositionsVerified": False,
        })
    return {
        "format": "ordr-original-monster-wave-script-evidence-v1",
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": map_sha,
        "sourceWar3mapJSha256": original_jass_sha256,
        "originalObjectSource": "Units\\UnitBalance.slk",
        "jassArrayFunction": "CUl",
        "jassSpawnFunctions": ["CVo", "CVp", "CVs", "CVw", "CVX", "CVY", "CVZ", "CVa", "CVb", "CVc", "CVd", "CVf"],
        "scope": "original-script-spawn-ids-and-cadence-with-object-base-stats-only",
        "safeForVerifiedMonsterWaveRuntime": False,
        "modeAdjustedCombatStatsVerified": False,
        "summary": {
            "rounds": 65,
            "distinctLineObjectTypes": len(line_ids),
            "distinctBossObjectTypes": len(boss_ids),
            "normalRoundPerActivePlayerSpawnCount": 35,
            "bossPerActivePlayerSpawnCount": 1,
            "cadenceSeconds": .5,
            "preNewWorldBossRounds": [10, 20, 30, 40, 50],
            "newWorldBossRounds": [55, 60, 65],
            "conditionalBerserkRounds": [wave["waveNumber"] for wave in waves if wave["conditionalBerserk"]],
        },
        "waves": waves,
    }


def build(w3x):
    objects = build_objects(w3x)
    with open_archive(w3x) as archive:
        jass_bytes = archive.read_file("war3map.j")
    if jass_bytes is None:
        raise ValueError("Missing original war3map.j")
    return build_index(jass_bytes.decode("utf-8-sig"), objects, MAP_SHA256)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    report = build(args.w3x)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Extracted {len(report['waves'])} source-backed waves to {args.output}")


if __name__ == "__main__":
    main()
