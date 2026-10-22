#!/usr/bin/env python3
"""Calculate a *reference-engine model*, not retail-verified WC3 monster stats.

Sources: ORDR 2.323[C] war3map.j, Units/UnitBalance.slk, UpgradeData.slk,
plus Warsmash's CUpgradeEffectHitPointsPcnt / CUpgradeEffectHitPoints /
CUpgradeEffectDefenseUpgradeBonus and CPlayer.setTechToLevel implementation.

Do not wire this JSON into live wave combat before retail/actual-client parity.
"""

import argparse
import json
import math
from pathlib import Path
import struct

from build_original_map_base_stats import open_archive, sha256_file
from extract_original_monster_object_stats import MAP_SHA256
from extract_original_unit_slk_stats import parse_sylk_bytes


ROOT = Path(__file__).resolve().parents[1]
TECH_PATH = ROOT / "data/original-2323C-nightmare-monster-tech-evidence.json"
DEFAULT_OUTPUT = ROOT / "data/original-2323C-nightmare-monster-initial-model.json"

WARSMASH_COMMIT = "f9e0aeed4be372d6016519d0e97b384aa873f374"
WARSMASH_SOURCE = (
    "https://github.com/Retera/WarsmashModEngine/blob/" + WARSMASH_COMMIT
    + "/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/upgrade/"
)


def float32(value):
    """Match the Java reference implementation's float (not double) stages."""
    return struct.unpack("<f", struct.pack("<f", value))[0]


def java_round_positive(value):
    """Java StrictMath.round(float), for positive finite values within int range."""
    if not 0 <= value <= 2147483647 or not math.isfinite(value):
        raise ValueError("HP delta outside positive Java int range")
    return math.floor(value + 0.5)


def level_float(base, increment, level):
    if level < 1:
        raise ValueError("An upgrade research level must be positive")
    # Util.levelValue(float,float,int) performs float multiplication and addition.
    return float32(float32(base) + float32(float32(increment) * (level - 1)))


def apply_hp_percentage(base_hp, effect, level):
    fraction = level_float(effect["baseAtLevelOne"],
                           effect["incrementPerAdditionalLevel"], level)
    return java_round_positive(float32(float32(base_hp) * fraction))


def modeled_effects(base_hp, base_armor, armor_per_upgrade, listed, upgrades):
    """Return modeled initial stats and evidence, rejecting any unmodeled effect.

    The generic movement-speed `rmvx` effect is tracked but cannot affect HP
    or armor; attack/magic abilities and late JASS are outside this calculation.
    """
    hp_increments = []
    armor_increments = []
    for code in listed:
        entry = upgrades[code]
        level = entry["researchedLevel"]
        for effect in entry["originalUpgradeEffects"]:
            tag = effect["originalEffectTag"]
            if tag == "rhpo":
                hp_increments.append({"researchRawcode": code, "effect": tag,
                                      "modeledAdditionalHitPoints": apply_hp_percentage(base_hp, effect, level)})
            elif tag == "rarm":
                # The Warcraft upgrade defense bonus uses the *unit* defUp,
                # not UpgradeData.base1. Only preserve integer-authored defUp.
                if not float(armor_per_upgrade).is_integer():
                    raise ValueError("Non-integer Warcraft defense upgrade bonus")
                armor_increments.append({"researchRawcode": code, "effect": tag,
                                         "modeledAdditionalArmor": int(armor_per_upgrade) * level})
            elif tag != "rmvx":
                raise ValueError("Unknown or unmodeled combat research effect: " + tag)
    return {
        "modeledInitialHitPoints": base_hp + sum(x["modeledAdditionalHitPoints"] for x in hp_increments),
        "modeledInitialArmor": base_armor + sum(x["modeledAdditionalArmor"] for x in armor_increments),
        "hitPointResearchComponents": hp_increments,
        "armorResearchComponents": armor_increments,
    }


def modeled_conditional_hp(base_hp, possible_upgrades, conditional_event, techs):
    """Conditional *tech* HP delta only. Event abilities/effects not included."""
    components = []
    for research in conditional_event["nightmareResearchAtActivation"]:
        code = research["rawcode"]
        if code not in possible_upgrades:
            continue
        effect_list = techs[code]
        if len(effect_list) != 1 or effect_list[0]["originalEffectTag"] != "rhpx":
            raise ValueError("Unexpected conditional health research definition: " + code)
        effect = effect_list[0]
        level = research["researchedLevel"]
        # Warsmash CUpgradeEffectHitPoints uses integer base+mod*(level-1).
        base = effect["baseAtLevelOne"]
        mod = effect["incrementPerAdditionalLevel"]
        if not float(base).is_integer() or not float(mod).is_integer():
            raise ValueError("Conditional rhpx is not integer-valued")
        components.append({"researchRawcode": code, "modeledAdditionalHitPoints":
                           int(base) + int(mod) * (level - 1)})
    return {"event": conditional_event["event"],
            "conditionalHitPointTechComponents": components,
            "modeledAdditionalHitPointsFromConditionalResearchOnly":
                sum(x["modeledAdditionalHitPoints"] for x in components)}


def build_model(nightmare, balances, conditional_techs):
    if nightmare["sourceW3xSha256"] != MAP_SHA256:
        raise ValueError("Nightmare tech evidence source does not match original map")
    if nightmare["safeForVerifiedMonsterWaveRuntime"] is not False:
        raise ValueError("Unexpected change in the upstream runtime guard")
    techs = {r["rawcode"]: r for r in nightmare["startingDifficultyResearch"]
             if r["playerIndex"] == nightmare["enemyOwnerPlayerIndex"]}
    rows = []
    for spawn in nightmare["waveMonsterUpgrades"]:
        code = spawn["monsterRawcode"]
        balance = balances[code]
        base_hp = int(balance["HP"])
        base_armor = float(balance["def"])
        def_up = float(balance["defUp"])
        if base_hp != spawn["objectBaseHitPoints"] or base_armor != spawn["objectBaseArmor"]:
            raise ValueError("Base object HP/armor drifted from upstream extract: " + code)
        if (spawn["sourceUnitBalanceUpgradeList"] !=
                [c for c in balance.get("upgrades", "").split(",") if c]):
            raise ValueError("UnitBalance.upgrades changed for " + code)
        matched = spawn["matchedInitialEnemyOwnerResearch"]
        if matched != [c for c in spawn["sourceUnitBalanceUpgradeList"] if c in techs]:
            raise ValueError("Mismatch between research and per-unit applicability")

        model = modeled_effects(base_hp, base_armor, def_up, matched, techs)
        conditional = [modeled_conditional_hp(base_hp, spawn["sourceUnitBalanceUpgradeList"],
                                              event, conditional_techs)
                       for event in nightmare["lateConditionalGoroseiResearch"]]
        rows.append({
            "waveNumber": spawn["waveNumber"],
            "role": spawn["role"],
            "monsterRawcode": code,
            "originalMapName": spawn["originalMapName"],
            "objectBaseHitPoints": base_hp,
            "objectBaseArmor": base_armor,
            "objectArmorPerUpgrade": def_up,
            "researchRawcodes": matched,
            **model,
            "conditionalGoroseiResearchOnly": conditional,
            "retailEffectiveStatsVerified": False,
        })
    if len(rows) != 68:
        raise ValueError("Incomplete source-backed monster wave model")
    return {
        "format": "ordr-2323C-nightmare-monster-reference-engine-model-v1",
        "gameVersion": "S2 2.323[C]",
        "sourceW3xSha256": MAP_SHA256,
        "sourceWar3mapJSha256": nightmare["sourceWar3mapJSha256"],
        "referenceImplementation": "WarsmashModEngine, not official Warcraft III executable",
        "referenceSourceCommit": WARSMASH_COMMIT,
        "referenceSourceUrls": [
            WARSMASH_SOURCE + "CUpgradeEffectHitPointsPcnt.java",
            WARSMASH_SOURCE + "CUpgradeEffectHitPoints.java",
            WARSMASH_SOURCE + "CUpgradeEffectDefenseUpgradeBonus.java",
            "https://github.com/Retera/WarsmashModEngine/blob/" + WARSMASH_COMMIT
            + "/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/players/CPlayer.java",
        ],
        "calculationScope": "base HP/armor + initial Nightmare player-6 researched upgrades",
        "conditionalScope": "HP delta from individually activated Gorosei research only, not abilities",
        "computedUsingReferenceEngineNotRetailRuntime": True,
        "includesOtherworldModeCombatMutations": False,
        "includesLateAbilitiesAndDynamicArmor": False,
        "includesRuntimeRegenerationAndOtherBuffs": False,
        "actualWarcraftClientParityVerified": False,
        "safeForVerifiedMonsterWaveRuntime": False,
        "summary": {"modeledMonsterTypes": len(rows), "rounds": 65,
                    "bossTypes": sum(row["role"] == "boss" for row in rows)},
        "waveMonsterInitialStatModels": rows,
    }


def build(w3x, nightmare_file=TECH_PATH):
    if sha256_file(w3x) != MAP_SHA256:
        raise ValueError("Original W3X hash mismatch")
    nightmare = json.loads(nightmare_file.read_text(encoding="utf-8"))
    with open_archive(w3x) as archive:
        balance = archive.read_file("Units\\UnitBalance.slk")
        upgrades = archive.read_file("Units\\UpgradeData.slk")
    if balance is None or upgrades is None:
        raise ValueError("Missing original SLK source")
    tech_rows = parse_sylk_bytes(upgrades)
    conditional = {}
    for code in ("R00S", "R01W"):
        record = tech_rows[code]
        conditional[code] = [{"originalEffectTag": record["effect1"],
                              "baseAtLevelOne": float(record["base1"]),
                              "incrementPerAdditionalLevel": float(record["mod1"])}]
    return build_model(nightmare, parse_sylk_bytes(balance), conditional)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", required=True, type=Path)
    parser.add_argument("--tech-evidence", default=TECH_PATH, type=Path)
    parser.add_argument("--output", default=DEFAULT_OUTPUT, type=Path)
    args = parser.parse_args()
    report = build(args.w3x, args.tech_evidence)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    # Keep the 68-object evidence compact to avoid bloating repo snapshots.
    args.output.write_text(json.dumps(report, ensure_ascii=False, separators=(",", ":")) + "\n",
                           encoding="utf-8")
    print(f"Modeled {len(report['waveMonsterInitialStatModels'])} original enemy types; retail parity unverified")


if __name__ == "__main__":
    main()
