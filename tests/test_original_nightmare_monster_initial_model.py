"""Reference-engine simulation math for source-backed Nightmare wave objects."""

import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from extract_original_monster_object_stats import MAP_SHA256  # noqa: E402
from model_original_nightmare_monster_initial_stats import (  # noqa: E402
    apply_hp_percentage, build_model, float32, modeled_effects,
)


class ModeledResearchMathTests(unittest.TestCase):
    def test_percentage_delta_uses_java_half_up_not_python_bankers_round(self):
        effect = {"baseAtLevelOne": 1.5, "incrementPerAdditionalLevel": 0.0}
        self.assertEqual(apply_hp_percentage(135, effect, 1), 203)

    def test_float32_intermediates_do_not_imply_exact_decimal_integer(self):
        effect = {"baseAtLevelOne": 8.5, "incrementPerAdditionalLevel": 0.0}
        self.assertEqual(apply_hp_percentage(13_595_000, effect, 1), 115_557_504)
        self.assertNotEqual(float32(115_557_500), 115_557_500)

    def test_armor_uses_unit_defup_times_research_level_not_upgrade_base(self):
        tech = {"R00J": {"researchedLevel": 11, "originalUpgradeEffects": [
            {"originalEffectTag": "rarm", "baseAtLevelOne": 0.1,
             "incrementPerAdditionalLevel": 0.0}]}}
        stats = modeled_effects(1000, 81, 10, ["R00J"], tech)
        self.assertEqual(stats["modeledInitialArmor"], 191)
        self.assertEqual(stats["modeledInitialHitPoints"], 1000)

    def test_unknown_effect_is_rejected_not_assumed_harmless(self):
        tech = {"R00A": {"researchedLevel": 1, "originalUpgradeEffects": [
            {"originalEffectTag": "unverified"}]}}
        with self.assertRaisesRegex(ValueError, "Unknown or unmodeled"):
            modeled_effects(100, 0, 0, ["R00A"], tech)

    def test_upstream_runtime_guard_must_be_off(self):
        with self.assertRaisesRegex(ValueError, "upstream runtime guard"):
            build_model({"sourceW3xSha256": MAP_SHA256,
                         "safeForVerifiedMonsterWaveRuntime": True}, {}, {})


class OriginalModeledWaveDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads((ROOT / "data/original-2323C-nightmare-monster-initial-model.json")
                                .read_text(encoding="utf-8"))
        cls.tech = json.loads((ROOT / "data/original-2323C-nightmare-monster-tech-evidence.json")
                              .read_text(encoding="utf-8"))
        cls.monsters = {(r["waveNumber"], r["role"]): r
                        for r in cls.report["waveMonsterInitialStatModels"]}

    def test_retains_source_evidence_and_is_not_runtime_ready(self):
        self.assertEqual(self.report["sourceW3xSha256"], MAP_SHA256)
        self.assertEqual(self.report["sourceWar3mapJSha256"], self.tech["sourceWar3mapJSha256"])
        self.assertEqual(self.report["summary"],
                         {"modeledMonsterTypes": 68, "rounds": 65, "bossTypes": 8})
        self.assertTrue(self.report["computedUsingReferenceEngineNotRetailRuntime"])
        self.assertFalse(self.report["safeForVerifiedMonsterWaveRuntime"])
        self.assertFalse(self.report["actualWarcraftClientParityVerified"])
        self.assertFalse(self.report["includesOtherworldModeCombatMutations"])
        self.assertFalse(self.report["includesLateAbilitiesAndDynamicArmor"])
        self.assertTrue(all(not m["retailEffectiveStatsVerified"] for m in self.monsters.values()))

    def test_each_unit_keeps_unmodified_source_values_and_components_sum(self):
        self.assertEqual(len(self.monsters), 68)
        for record in self.monsters.values():
            self.assertEqual(record["modeledInitialHitPoints"], record["objectBaseHitPoints"]
                             + sum(x["modeledAdditionalHitPoints"]
                                   for x in record["hitPointResearchComponents"]))
            self.assertEqual(record["modeledInitialArmor"], record["objectBaseArmor"]
                             + sum(x["modeledAdditionalArmor"]
                                   for x in record["armorResearchComponents"]))
            self.assertEqual(len(record["conditionalGoroseiResearchOnly"]), 3)

    def test_checked_line_boss_and_special_waves(self):
        self.assertEqual(self.monsters[(1, "line")]["modeledInitialHitPoints"], 338)
        self.assertEqual(self.monsters[(10, "boss")]["modeledInitialHitPoints"], 137750)
        self.assertEqual(self.monsters[(15, "line")]["modeledInitialHitPoints"], 9559)
        self.assertEqual(self.monsters[(39, "line")]["modeledInitialHitPoints"], 1)
        self.assertEqual(self.monsters[(39, "line")]["modeledInitialArmor"], 39)
        self.assertEqual(self.monsters[(50, "boss")]["modeledInitialHitPoints"], 197600000)
        self.assertEqual(self.monsters[(65, "line")]["modeledInitialArmor"], 191)
        self.assertEqual(self.monsters[(65, "boss")]["modeledInitialHitPoints"], 129152504)

    def test_gorosei_research_hp_is_applied_only_to_eligible_unit_lists(self):
        def events(wave, role):
            return {e["event"]: e["modeledAdditionalHitPointsFromConditionalResearchOnly"]
                    for e in self.monsters[(wave, role)]["conditionalGoroseiResearchOnly"]}
        self.assertEqual(events(1, "line"), {"워큐리": 10_000_000, "새턴": 10_000_000,
                                              "나스쥬로": 25_000_000})
        self.assertEqual(events(10, "boss"), {"워큐리": 25_000_000, "새턴": 10_000_000,
                                               "나스쥬로": 10_000_000})
        self.assertEqual(events(39, "line"), {"워큐리": 0, "새턴": 0, "나스쥬로": 0})


if __name__ == "__main__":
    unittest.main()
