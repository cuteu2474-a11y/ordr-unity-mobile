"""Regression checks for exact source upgrade eligibility, not simulated HP."""

from collections import Counter
import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from extract_original_monster_object_stats import MAP_SHA256  # noqa: E402
from extract_original_nightmare_monster_tech import (  # noqa: E402
    build_evidence, decimal_number, researched_effect,
)


class NightmareResearchUnitTests(unittest.TestCase):
    def test_research_effect_uses_slk_base_plus_level_increment(self):
        ability = {"maxlevel": "20", "effect1": "rhpo", "base1": "0.1", "mod1": "0.1"}
        effect = researched_effect(ability, "R00A", 15)
        self.assertEqual(effect[0]["rawEffectValueAtResearchedLevel"], 1.5)
        self.assertEqual(decimal_number("0.81"), 0.81)

    def test_fails_closed_on_missing_or_out_of_range_upgrade(self):
        with self.assertRaisesRegex(ValueError, "Missing original upgrade"):
            researched_effect(None, "R00A", 15)
        with self.assertRaisesRegex(ValueError, "outside SLK max"):
            researched_effect({"maxlevel": "2"}, "R00A", 15)

    def test_rejects_wrong_jass_source(self):
        waves = {"sourceW3xSha256": MAP_SHA256, "summary": {"rounds": 65}}
        with self.assertRaisesRegex(ValueError, "JASS function"):
            build_evidence("", waves, {}, {})


class SourceNightmareWaveIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tech = json.loads((ROOT / "data/original-2323C-nightmare-monster-tech-evidence.json")
                              .read_text(encoding="utf-8"))
        cls.waves = json.loads((ROOT / "data/original-2323C-monster-wave-script-evidence.json")
                               .read_text(encoding="utf-8"))

    def test_source_identity_and_runtime_guard(self):
        self.assertEqual(self.tech["sourceW3xSha256"], MAP_SHA256)
        self.assertEqual(self.tech["sourceWar3mapJSha256"], self.waves["sourceWar3mapJSha256"])
        self.assertFalse(self.tech["safeForVerifiedMonsterWaveRuntime"])
        self.assertFalse(self.tech["engineUpgradeApplicationAndRoundingVerified"])
        self.assertFalse(self.tech["otherworldMode"]["modeWideEffectiveMonsterStatsVerified"])
        self.assertFalse(self.tech["otherRuntimeTechAndAbilityEventsExhaustivelyVerified"])
        self.assertEqual(self.tech["nightmareMonsterMagicDefense"]["initialA04QAbilityLevel"], 16)

    def test_exact_nightmare_enemy_research_values(self):
        researched = {(r["rawcode"], r["researchedLevel"], r["playerIndex"]): r
                      for r in self.tech["startingDifficultyResearch"]}
        self.assertEqual(len(researched), 11)
        self.assertEqual(researched[("R00A", 15, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 1.5)
        self.assertEqual(researched[("R00T", 4, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 1.08)
        self.assertEqual(researched[("R00V", 6, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 5.76)
        self.assertEqual(researched[("R00U", 8, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 8.0)
        self.assertEqual(researched[("R00W", 9, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 7.48)
        self.assertEqual(researched[("R02M", 1, 6)]["originalUpgradeEffects"][0]
                         ["rawEffectValueAtResearchedLevel"], 8.5)
        self.assertEqual(researched[("R024", 3, 6)]["originalUpgradeEffects"][0]
                         ["originalEffectTag"], "rmvx")
        self.assertEqual(researched[("R00J", 11, 6)]["originalUpgradeEffects"][0]
                         ["originalEffectTag"], "rarm")
        self.assertEqual(researched[("R01A", 1, 5)]["playerIndex"], 5)

    def test_each_original_spawn_has_its_own_eligible_tech_not_every_upgrade(self):
        exact = {(w["waveNumber"], unit["role"]): unit
                 for w in self.waves["waves"] for unit in w["normalAndBossSpawns"]}
        actual = {(u["waveNumber"], u["role"]): u
                  for u in self.tech["waveMonsterUpgrades"]}
        self.assertEqual(set(exact), set(actual))
        self.assertEqual(len(actual), 68)
        for key, unit in actual.items():
            self.assertEqual(unit["monsterRawcode"], exact[key]["rawcode"])
            self.assertEqual(unit["objectBaseHitPoints"], exact[key]["objectBaseHitPoints"])
            self.assertEqual(unit["objectBaseArmor"], exact[key]["objectBaseArmor"])
            self.assertTrue(set(unit["matchedInitialEnemyOwnerResearch"]).issubset(
                unit["sourceUnitBalanceUpgradeList"]))
            self.assertFalse(unit["effectiveHitPointsVerified"])
            self.assertFalse(unit["effectiveArmorVerified"])

        self.assertEqual(actual[(10, "boss")]["matchedInitialEnemyOwnerResearch"], ["R02M"])
        self.assertEqual(actual[(1, "line")]["matchedInitialEnemyOwnerResearch"],
                         ["R024", "R00J", "R00A"])
        self.assertEqual(actual[(39, "line")]["matchedInitialEnemyOwnerResearch"], ["R024"])
        self.assertEqual(actual[(65, "boss")]["matchedInitialEnemyOwnerResearch"], ["R02M"])
        self.assertEqual(Counter(tuple(u["matchedInitialEnemyOwnerResearch"])
                                 for u in actual.values()).most_common()[-1][1], 1)

    def test_gorosei_is_conditional_and_separate(self):
        events = {record["event"]: record for record in self.tech["lateConditionalGoroseiResearch"]}
        self.assertEqual(set(events), {"워큐리", "새턴", "나스쥬로"})
        self.assertEqual(events["워큐리"]["nightmareResearchAtActivation"], [
            {"rawcode": "R00S", "researchedLevel": 10},
            {"rawcode": "R01W", "researchedLevel": 25},
        ])
        self.assertEqual(events["나스쥬로"]["nightmareResearchAtActivation"], [
            {"rawcode": "R00S", "researchedLevel": 25},
            {"rawcode": "R01W", "researchedLevel": 10},
        ])
        self.assertTrue(all(not event["alwaysAppliedAtWaveStart"] for event in events.values()))
        self.assertTrue(self.tech["lateConditionalGoroseiTechIdentifiedNotApplied"])


if __name__ == "__main__":
    unittest.main()
