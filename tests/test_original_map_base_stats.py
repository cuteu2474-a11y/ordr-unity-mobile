"""Non-Unity regression checks for S2 2.323[C] map-only base stats."""

import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from build_original_map_base_stats import (  # noqa: E402
    COMMON_RAWCODES, attack_stats, clean_name, normalize_name,
)
from classify_character_skills import family  # noqa: E402


class WarcraftObjectAttackTests(unittest.TestCase):
    def test_luffy_damage_dice_are_not_tooltip_damage(self):
        data = attack_stats({"cool1": "1.02", "dmgplus1": "28",
                             "dice1": "1", "sides1": "3"}, 1, True)
        self.assertEqual(data["minimumBaseDamage"], 29)
        self.assertEqual(data["maximumBaseDamage"], 31)
        self.assertEqual(data["attackCooldownSeconds"], 1.02)

    def test_unavailable_attack_is_none(self):
        self.assertIsNone(attack_stats({}, 1, False))
        missing = attack_stats({"cool1": "1.1"}, 1, True)
        self.assertIsNone(missing["minimumBaseDamage"])
        self.assertIsNone(missing["maximumBaseDamage"])

    def test_map_label_normalization(self):
        self.assertEqual(clean_name("|CFFFF0000몽키.D.루피|r"), "몽키.D.루피")
        self.assertEqual(normalize_name("바질 호킨스"), normalize_name("바질호킨스"))


class GeneratedDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text(
            encoding="utf-8"))
        cls.skills = json.loads((ROOT / "data/original-2323C-skill-research-queue.json").read_text(
            encoding="utf-8"))

    def test_full_source_index_and_partial_verified_join(self):
        report = self.source["summary"]
        self.assertEqual(report["originalNamedObjects"], 1493)
        self.assertEqual(report["catalogCharacters"], 271)
        self.assertEqual(report["linkedCatalogCharacters"], 162)
        self.assertEqual(report["unresolvedCatalogCharacters"], 109)
        self.assertEqual(report["gameplayParityVerified"], 0)
        rows = self.source["catalogEntries"]
        self.assertEqual(len({r["catalogId"] for r in rows}), 271)
        linked = [r for r in rows if r["originalRawcode"]]
        self.assertEqual(len({r["originalRawcode"] for r in linked}), len(linked))
        self.assertTrue(all(r["combatParityVerified"] is False for r in rows))
        for row in rows:
            if not row["originalRawcode"]:
                self.assertIsNone(row["attack1"])
                self.assertIsNone(row["baseStats"])
                self.assertIsNone(row["sourceMapName"])

    def test_common_nine_match_original_map_damage_and_interval(self):
        report = {x["catalogId"]: x for x in self.source["catalogEntries"]}
        # These are *map object* damage ranges, not actual damage against an enemy.
        expected = {
            "C-01": (29, 31, 1.02), "C-02": (31, 33, 1.03),
            "C-03": (26, 30, 0.98), "C-04": (25, 27, 0.84),
            "C-05": (23, 25, 0.77), "C-06": (22, 24, 0.57),
            "C-07": (29, 31, 0.99), "C-08": (30, 32, 0.96),
            "C-09": (28, 30, 0.94),
        }
        self.assertEqual(len(COMMON_RAWCODES), 9)
        for uid, (min_dmg, max_dmg, interval) in expected.items():
            unit = report[uid]
            self.assertEqual(unit["mappingStatus"], "verified-common-map-identity")
            self.assertEqual(unit["originalRawcode"], COMMON_RAWCODES[uid][0])
            attack = unit["attack1"]
            self.assertEqual(attack["minimumBaseDamage"], min_dmg)
            self.assertEqual(attack["maximumBaseDamage"], max_dmg)
            self.assertAlmostEqual(attack["attackCooldownSeconds"], interval)

    def test_common_nine_attack_preview_has_source_timing_and_is_not_verified_damage(self):
        entries = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        for uid in COMMON_RAWCODES:
            row = entries[uid]
            attack = row["attack1"]
            self.assertIsNotNone(attack["attackPointSeconds"])
            self.assertGreaterEqual(attack["attackPointSeconds"], 0)
            self.assertLess(attack["attackPointSeconds"], attack["attackCooldownSeconds"])
            self.assertFalse(row["combatParityVerified"])

    def test_common_native_object_dice_and_attack_type_are_preserved(self):
        rows = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        expected_types = {
            "C-01": "siege", "C-02": "pierce", "C-03": "normal",
            "C-04": "normal", "C-05": "siege", "C-06": "normal",
            "C-07": "pierce", "C-08": "siege", "C-09": "pierce",
        }
        for catalog_id, expected_type in expected_types.items():
            attack = rows[catalog_id]["attack1"]
            self.assertEqual(attack["originalAttackType"], expected_type)
            self.assertGreater(attack["diceCount"], 0)
            self.assertGreater(attack["diceSides"], 0)
            self.assertEqual(attack["minimumBaseDamage"],
                             attack["baseDamagePlus"] + attack["diceCount"])
            self.assertEqual(attack["maximumBaseDamage"],
                             attack["baseDamagePlus"] + attack["diceCount"] * attack["diceSides"])

    def test_skill_queue_preserves_uncertainty(self):
        s = self.skills["summary"]
        self.assertEqual(s["tooltipProfiles"], 200)
        self.assertEqual(s["skillEntries"], 554)
        self.assertEqual(s["passiveEntries"], 295)
        self.assertEqual(s["originalJassSkillsVerified"], 0)
        self.assertEqual(len(self.skills["skills"]), 554)
        self.assertEqual(sum(x["skillCount"] for x in self.skills["templateGroups"]), 554)
        self.assertEqual(len(set(x["skillId"] for x in self.skills["skills"])), 554)
        self.assertTrue(all(x["originalJassSkillRawcode"] is None for x in self.skills["skills"]))

    def test_trigger_families_do_not_invent_special_behavior(self):
        self.assertEqual(family("onAttack"), "attack-triggered")
        self.assertEqual(family("manual"), "active-or-automatic-mobile")
        self.assertEqual(family("periodic"), "periodic")
        self.assertEqual(family("onSkillOrAttack"), "special-condition-needs-analysis")


if __name__ == "__main__":
    unittest.main()
