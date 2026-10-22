"""Regressions for source-backed common abilities and unapproved combat."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from audit_original_common_abilities import derive  # noqa: E402


def read(filename):
    return json.loads((ROOT / "data" / filename).read_text(encoding="utf-8"))


class CommonAbilityAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = [
            read("original-2323C-map-base-stats.json"),
            read("original-jass-fusion-registry.json"),
            read("original-2323C-missing-skill-profile-evidence.json"),
        ]
        cls.report = derive(*cls.source)

    def test_committed_report_is_reproducible(self):
        self.assertEqual(self.report, read("original-2323C-common-unit-ability-audit.json"))

    def test_nine_units_have_source_damage_and_no_asserted_finished_combat(self):
        self.assertEqual(self.report["summary"]["commons"], 9)
        self.assertEqual(self.report["summary"]["actualCombatVerifiedUnits"], 0)
        for unit in self.report["commonUnits"]:
            with self.subTest(catalog=unit["catalogUnitId"]):
                self.assertTrue(unit["objectAttack1"]["attackCooldownSeconds"] > 0)
                self.assertFalse(unit["allCombatSkillsProvenAbsent"])
                self.assertFalse(unit["actualCombatReady"])
                self.assertEqual(unit["individuallyConfirmedCombatSkillCount"], 0)

    def test_all_individual_common_abilities_are_cwr_fusion_registrations(self):
        self.assertEqual(self.report["summary"]["cwrFusionAbilities"], 19)
        self.assertEqual(self.report["summary"]["uncategorizedSharedOrNativeAbilities"], 27)
        for unit in self.report["commonUnits"]:
            fusion = [x for x in unit["abilityCodesInObjectOrder"]
                      if x["kind"] == "original-cwr-fusion-ability"]
            self.assertIn(len(fusion), (2, 3))
            self.assertTrue(all(x["jassResultRawcode"] is not None for x in fusion))
            self.assertTrue(all(x["nativeCombatSkillVerified"] is False for x in fusion))
            remaining = [x for x in unit["abilityCodesInObjectOrder"]
                         if x["kind"] != "original-cwr-fusion-ability"]
            self.assertEqual([x["rawcode"] for x in remaining],
                             ["A0JE", "A09G", "Avul"])

    def test_luffy_has_two_fusion_commands_not_two_attacks(self):
        luffy = self.report["commonUnits"][0]
        self.assertEqual(luffy["catalogUnitId"], "C-01")
        self.assertEqual(luffy["originalUnitRawcode"], "h003")
        attack = luffy["objectAttack1"]
        self.assertEqual((attack["minimumBaseDamage"],
                          attack["maximumBaseDamage"],
                          attack["attackCooldownSeconds"],
                          attack["attackPointSeconds"],
                          attack["originalAttackType"]),
                         (29, 31, 1.02, 0.35, "siege"))
        abilities = luffy["abilityCodesInObjectOrder"]
        self.assertEqual([a["rawcode"] for a in abilities],
                         ["A0JE", "A09G", "A00D", "A012", "Avul"])
        fusion = {a["rawcode"]: a["jassResultRawcode"] for a in abilities
                  if a["kind"] == "original-cwr-fusion-ability"}
        self.assertEqual(fusion, {"A00D": "h00K", "A012": "h00U"})

    def test_bad_fingerprints_and_corrupted_ability_order_fail(self):
        bad = copy.deepcopy(self.source)
        bad[1]["sourceSha256"] = "not-original"
        with self.assertRaisesRegex(ValueError, "fingerprints"):
            derive(*bad)
        bad = copy.deepcopy(self.source)
        c = next(x for x in bad[2]["characters"] if x["catalogUnitId"] == "C-01")
        c["originalAbilityRawcodesInMapOrder"].reverse()
        with self.assertRaisesRegex(ValueError, "identity drift"):
            derive(*bad)
        bad = copy.deepcopy(self.source)
        c = next(x for x in bad[2]["characters"] if x["catalogUnitId"] == "C-01")
        c["originalAbilityRawcodesInMapOrder"][2] = "AAAA"
        with self.assertRaisesRegex(ValueError, "identity drift"):
            derive(*bad)

    def test_rare_and_hidden_are_not_in_common_audit(self):
        self.assertEqual([x["catalogUnitId"] for x in self.report["commonUnits"]],
                         ["C-%02d" % i for i in range(1, 10)])


if __name__ == "__main__":
    unittest.main()
