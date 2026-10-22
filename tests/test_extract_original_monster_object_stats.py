"""Regression checks for original map monster *object* values, not final waves."""

import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from extract_original_monster_object_stats import (  # noqa: E402
    MAP_SHA256, classify_enemy_candidate, extract_object_stats,
)


class ClassificationTests(unittest.TestCase):
    def test_original_name_categories_without_claiming_runtime_role(self):
        cases = (
            ("o01V", "레벨 10 [보스]아론", "boss-labeled"),
            ("o00I", "레벨 1 알비다", "level-labeled"),
            ("o000", "[퀘스트] 시저", "quest-labeled"),
            ("n000", "1. 로그타운", "story-labeled"),
            ("n00X", "1단계 크립", "creep-labeled"),
            ("o002", "고런트-체력2000만", "other-o-object-unconfirmed"),
            ("n00K", "이동포탈", None),
            ("h003", "루피 - 흔함", None),
        )
        for rawcode, name, expected in cases:
            with self.subTest(rawcode=rawcode):
                self.assertEqual(classify_enemy_candidate(rawcode, name), expected)

    def test_missing_hp_or_armor_rejects_candidate(self):
        for fields in ({"HP": "0", "def": "10", "defType": "large"},
                       {"HP": "100", "defType": "large"},
                       {"HP": "100", "def": "10"}):
            with self.subTest(fields=fields):
                with self.assertRaisesRegex(ValueError, "Invalid original HP|Missing original defense"):
                    extract_object_stats({"o01V": fields},
                                         {"o01V": "레벨 10 [보스]아론"})


class OriginalMonsterDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.document = json.loads((
            ROOT / "data/original-2323C-monster-object-stats.json"
        ).read_text(encoding="utf-8"))

    def test_map_integrity_and_explicit_non_runtime_scope(self):
        doc = self.document
        self.assertEqual(doc["gameVersion"], "S2 2.323[C]")
        self.assertEqual(doc["sourceW3xSha256"], MAP_SHA256)
        self.assertEqual(doc["scope"],
                         "original-object-base-stats-not-mode-adjusted-wave-spawns")
        self.assertIs(doc["safeForVerifiedMonsterWaveRuntime"], False)
        self.assertEqual(doc["summary"]["runtimeEnemyRolesVerified"], 0)
        self.assertEqual(doc["summary"]["nightmareOtherworldFinalStatsVerified"], 0)
        self.assertEqual([r["path"] for r in doc["sourceFiles"]], [
            "Units\\UnitBalance.slk", "Units\\CampaignUnitStrings.txt"])

    def test_name_based_count_and_no_ambiguous_claims(self):
        doc = self.document
        self.assertEqual(doc["summary"]["candidateObjectCount"], 100)
        self.assertEqual(doc["summary"]["nameBasedCategoryCounts"], {
            "boss-labeled": 8, "creep-labeled": 3, "level-labeled": 60,
            "other-o-object-unconfirmed": 4, "quest-labeled": 11,
            "story-labeled": 14,
        })
        rows = doc["entries"]
        self.assertEqual(len(rows), 100)
        self.assertEqual(len({row["rawcode"] for row in rows}), 100)
        for row in rows:
            self.assertEqual(len(row["rawcode"]), 4)
            self.assertGreater(row["objectBaseHitPoints"], 0)
            self.assertIs(row["runtimeEnemyRoleVerified"], False)
            self.assertIs(row["nightmareOtherworldFinalStatsVerified"], False)

    def test_known_original_object_values(self):
        by_code = {row["rawcode"]: row for row in self.document["entries"]}
        expected = (
            ("o01V", "레벨 10 [보스]아론", 14500, 13, "large"),
            ("n000", "1. 로그타운", 70200, 10, "fort"),
            ("n009", "14. 에그헤드", 288800000, 300, "fort"),
            ("o002", "고런트-체력2000만", 20000000, 150, "normal"),
        )
        for code, name, hp, armor, defense_type in expected:
            with self.subTest(code=code):
                row = by_code[code]
                self.assertEqual(row["originalMapName"], name)
                self.assertEqual(row["objectBaseHitPoints"], hp)
                self.assertEqual(row["objectBaseArmor"], armor)
                self.assertEqual(row["warcraftArmorType"], defense_type)


if __name__ == "__main__":
    unittest.main()
