#!/usr/bin/env python3
"""Cross-check Nightmare difficulty research with each original wave unit's upgrade list.

This is *evidence*, not a final HP/armor formula. UnitBalance.upgrades controls
which player-6 researches an object can receive; the engine's interaction with
abilities, runtime events and exact stat rounding needs separate verification.

Requires mpyq, e.g.:
    python tools/extract_original_nightmare_monster_tech.py --w3x /path/map.w3x
"""

import argparse
from collections import Counter
from decimal import Decimal
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive, sha256_file
from extract_original_monster_object_stats import MAP_SHA256
from extract_original_monster_wave_script import function_body, original_id
from extract_original_unit_slk_stats import parse_sylk_bytes


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_WAVES = ROOT / "data/original-2323C-monster-wave-script-evidence.json"
DEFAULT_OUTPUT = ROOT / "data/original-2323C-nightmare-monster-tech-evidence.json"

# Explicitly reviewed in CUM, NOT inferred from a numeric difficulty enum.
EXPECTED_NIGHTMARE_RESEARCH = {
    ("R01A", 1, 5), ("R012", 1, 5), ("R01R", 1, 6),
    ("R024", 3, 6), ("R00J", 11, 6), ("R02M", 1, 6),
    ("R00A", 15, 6), ("R00T", 4, 6), ("R00V", 6, 6),
    ("R00U", 8, 6), ("R00W", 9, 6),
}


def nightmare_research(jass):
    """Return only the research inside the original Nightmare CUM branch."""
    function, start_line = function_body(jass.replace("\r\n", "\n"), "CUM")
    match = re.search(r"^elseif NE==NL then\n(.*?)^endif\nloop$",
                      function, re.MULTILINE | re.DOTALL)
    if match is None:
        raise ValueError("Nightmare choice branch missing from CUM")
    branch = match.group(1)
    if any(f"set LK[{i}]=16" not in branch for i in range(4)) or "set NJ=16" not in branch:
        raise ValueError("Nightmare monster A04Q levels changed")
    regex = r"^call SetPlayerTechResearchedSwap\(\$([0-9A-Fa-f]{8}),(\d+),Player\((\d+)\)\)$"
    calls = []
    for item in re.finditer(regex, branch, re.MULTILINE):
        calls.append({
            "rawcode": original_id(item.group(1)),
            "researchedLevel": int(item.group(2)),
            "playerIndex": int(item.group(3)),
            "sourceJassLine": start_line + function.count("\n", 0, match.start(1) + item.start()),
        })
    received = {(call["rawcode"], call["researchedLevel"], call["playerIndex"])
                for call in calls}
    if received != EXPECTED_NIGHTMARE_RESEARCH or len(calls) != len(received):
        raise ValueError("Nightmare starting upgrades changed or duplicate research found")
    if "call Bd4(Cm,LK[JG])" not in jass:
        raise ValueError("Original monster magic defense attachment missing")
    # This branch only enables the independent mode; it is not proof of a
    # different player-6 monster HP multiplier.
    if not all(token in function for token in (
        "if th[tn] then", "set k4=true", "call TriggerEvaluate(tu)",
        *[f"call SetPlayerTechResearched(Player({i}),$52303050,1)"
          for i in range(4)],
    )):
        raise ValueError("Independent Otherworld-mode initialization changed")
    return calls


def late_gorosei_research(jass):
    """Only conditional nightmare enemy-owner research, never a baseline buff."""
    function, _ = function_body(jass.replace("\r\n", "\n"), "BeR")
    named = [
        ("워큐리", "$6F303245", ("R00S", 10), ("R01W", 25)),
        ("새턴", "$6F303331", ("R00S", 10), ("R01W", 10)),
        ("나스쥬로", "$6F303332", ("R00S", 25), ("R01W", 10)),
    ]
    results = []
    for i, (name, unit_id, *expected_tech) in enumerate(named):
        start = function.find(f"{unit_id}==GetUnitTypeId")
        end = (function.find(f"{named[i + 1][1]}==GetUnitTypeId", start)
               if i + 1 < len(named) else function.find("call BJ3(DR,.03)", start))
        if start < 0 or end <= start:
            raise ValueError(f"Gorosei event branch missing: {name}")
        branch = function[start:end]
        if "elseif NE==NL then" not in branch:
            raise ValueError(f"Gorosei nightmare conditional missing: {name}")
        nightmare_only = branch.split("elseif NE==NL then", 1)[1]
        for code, level in expected_tech:
            literal = f"call SetPlayerTechResearchedSwap(${code.encode('ascii').hex().upper()},{level},Player(6))"
            if literal not in nightmare_only:
                raise ValueError(f"Conditional enemy research changed: {name} {code}")
        results.append({"event": name, "conditionalJassFunction": "BeR",
                        "nightmareResearchAtActivation": [
                            {"rawcode": code, "researchedLevel": level}
                            for code, level in expected_tech],
                        "alwaysAppliedAtWaveStart": False})
    reset, _ = function_body(jass.replace("\r\n", "\n"), "BeQ")
    if ("call SetPlayerTechResearched(Player(6),$52303053,0)" not in reset
            or "call SetPlayerTechResearched(Player(6),$52303157,0)" not in reset):
        raise ValueError("Conditional Gorosei upgrade reset changed")
    return results


def decimal_number(value):
    return float(Decimal(str(value)))


def researched_effect(upgrade, code, level):
    if not upgrade:
        raise ValueError(f"Missing original upgrade definition: {code}")
    maximum = int(upgrade.get("maxlevel", "0"))
    if level < 1 or level > maximum:
        raise ValueError(f"Research level outside SLK max: {code} {level}/{maximum}")
    effects = []
    for index in range(1, 5):
        tag = upgrade.get(f"effect{index}")
        if not tag:
            continue
        base = Decimal(upgrade.get(f"base{index}", "0"))
        increment = Decimal(upgrade.get(f"mod{index}", "0"))
        effects.append({
            "originalEffectTag": tag,
            "baseAtLevelOne": decimal_number(base),
            "incrementPerAdditionalLevel": decimal_number(increment),
            "rawEffectValueAtResearchedLevel": decimal_number(base + increment * (level - 1)),
        })
    return effects


def build_evidence(jass, wave_index, balances, upgrades):
    if wave_index["sourceW3xSha256"] != MAP_SHA256 or wave_index["summary"]["rounds"] != 65:
        raise ValueError("Unrecognized original monster wave source")
    researches = nightmare_research(jass)
    conditional = late_gorosei_research(jass)
    researched_by_enemy_owner = {item["rawcode"]: item for item in researches
                                 if item["playerIndex"] == 6}
    for item in researches:
        item["originalUpgradeEffects"] = researched_effect(
            upgrades.get(item["rawcode"]), item["rawcode"], item["researchedLevel"])

    units = []
    for wave in wave_index["waves"]:
        for spawn in wave["normalAndBossSpawns"]:
            code = spawn["rawcode"]
            balance = balances.get(code)
            if not balance:
                raise ValueError(f"Missing original UnitBalance record for {code}")
            possible = [part for part in balance.get("upgrades", "").split(",") if part]
            if len(possible) != len(set(possible)):
                raise ValueError(f"Duplicate upgrade attachment on {code}")
            matched = [part for part in possible if part in researched_by_enemy_owner]
            units.append({
                "waveNumber": wave["waveNumber"],
                "role": spawn["role"],
                "monsterRawcode": code,
                "originalMapName": spawn["originalMapName"],
                "objectBaseHitPoints": spawn["objectBaseHitPoints"],
                "objectBaseArmor": spawn["objectBaseArmor"],
                "sourceUnitBalanceUpgradeList": possible,
                "matchedInitialEnemyOwnerResearch": matched,
                "unmatchedInitialEnemyOwnerResearch": sorted(set(researched_by_enemy_owner) - set(matched)),
                "effectiveHitPointsVerified": False,
                "effectiveArmorVerified": False,
            })

    counts = Counter(tuple(unit["matchedInitialEnemyOwnerResearch"]) for unit in units)
    if len(units) != 68 or len({u["monsterRawcode"] for u in units}) != 68:
        raise ValueError("Unexpected wave monster attachment coverage")
    return {
        "format": "ordr-original-2323C-nightmare-monster-tech-evidence-v1",
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": MAP_SHA256,
        "sourceWar3mapJSha256": wave_index["sourceWar3mapJSha256"],
        "sources": ["war3map.j:CUM", "Units\\UnitBalance.slk:upgrades",
                    "Units\\UpgradeData.slk:effect/base/mod"],
        "difficulty": "악몽",
        "otherworldMode": {
            "independentChoiceFlag": "th[tn]",
            "playerZeroThroughThreeResearch": "R00P",
            "doesNotSetAnAdditionalEnemyResearchInThisBranch": True,
            "modeWideEffectiveMonsterStatsVerified": False,
        },
        "nightmareMonsterMagicDefense": {
            "sourceJassField": "LK[0..3]",
            "initialA04QAbilityLevel": 16,
            "affectedByOtherGameplayEvents": True,
        },
        "startingDifficultyResearch": researches,
        "lateConditionalGoroseiResearch": conditional,
        "enemyOwnerPlayerIndex": 6,
        "scope": "initial-researched-tech-intersected-with-each-wave-unit-upgrade-list",
        "lateConditionalGoroseiTechIdentifiedNotApplied": True,
        "otherRuntimeTechAndAbilityEventsExhaustivelyVerified": False,
        "engineUpgradeApplicationAndRoundingVerified": False,
        "safeForVerifiedMonsterWaveRuntime": False,
        "summary": {
            "monsterTypesFromJassSpawnArrays": len(units),
            "differentMatchingUpgradeLists": len(counts),
            "matchedUpgradeListCounts": [
                {"researchRawcodes": list(codes), "monsterTypes": count}
                for codes, count in sorted(counts.items())
            ],
        },
        "waveMonsterUpgrades": units,
    }


def build(w3x, waves_path=DEFAULT_WAVES):
    if sha256_file(w3x) != MAP_SHA256:
        raise ValueError("Original W3X SHA-256 mismatch")
    index = json.loads(waves_path.read_text(encoding="utf-8"))
    with open_archive(w3x) as archive:
        blobs = {name: archive.read_file(name) for name in (
            "war3map.j", "Units\\UnitBalance.slk", "Units\\UpgradeData.slk")}
    if any(blob is None for blob in blobs.values()):
        raise ValueError("Missing original JASS or SLK upgrade data")
    return build_evidence(blobs["war3map.j"].decode("utf-8-sig"), index,
                          parse_sylk_bytes(blobs["Units\\UnitBalance.slk"]),
                          parse_sylk_bytes(blobs["Units\\UpgradeData.slk"]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", required=True, type=Path)
    parser.add_argument("--waves", default=DEFAULT_WAVES, type=Path)
    parser.add_argument("--output", default=DEFAULT_OUTPUT, type=Path)
    args = parser.parse_args()
    result = build(args.w3x, args.waves)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Source-checked Nightmare upgrade eligibility for {len(result['waveMonsterUpgrades'])} monsters")


if __name__ == "__main__":
    main()
