"""Regression coverage for the separately audited original helper JASS."""
import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_original_helper_damage_jass import inspect_jass


class OriginalHelperDamageJassEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evidence = json.loads((ROOT / "data/original-2323C-helper-damage-jass.json")
                                  .read_text(encoding="utf-8"))

    def test_unfiltered_point_native_stays_fail_closed(self):
        point = self.evidence["A0BZ"]
        self.assertEqual(point["wrapper"], "UnitDamagePointLoc")
        self.assertEqual(point["native"], "UnitDamagePoint")
        self.assertIs(point["explicitEnemyFilter"], False)
        self.assertIs(point["mustVerifyNativeVictimCoverage"], True)
        self.assertIs(point["allNativeVictimsModeled"], False)

    def test_quake_owner_filter_and_persistent_a04r_are_distinct(self):
        quake = self.evidence["A07T"]
        self.assertIn("IsUnitEnemy", quake["targetPredicate"])
        self.assertEqual(quake["A04RLevelIncrease"], [5, 8])
        shared = self.evidence["sharedA04R"]
        self.assertEqual(shared["numberOfCallSites"], 17)
        self.assertEqual(shared["outsideHelperCallSites"], 15)
        self.assertEqual([c["levelIncrease"] for c in shared["callSites"]
                          if c["function"] in ("CJg", "CJh")], [5, 8])
        self.assertIs(shared["warcraftIsr2EffectFormulaVerified"], False)

    def test_source_damage_native_and_original_attack_flags_are_pinned(self):
        damage = self.evidence["damageInvocations"]
        self.assertEqual(damage["A0BZ"]["underlyingNative"], "UnitDamagePoint")
        self.assertEqual(damage["A07T"]["underlyingNative"], "UnitDamageTarget")
        self.assertIs(damage["A0BZ"]["wrapperSourceIncludedInW3x"], False)
        for meta in damage.values():
            self.assertIs(meta["attack"], True)
            self.assertIs(meta["ranged"], False)
            self.assertEqual(meta["attackType"], "CHAOS")
            self.assertEqual(meta["damageType"], "UNIVERSAL")
            self.assertEqual(meta["weaponType"], "WHOKNOWS")

    def test_extractor_rejects_incomplete_jass(self):
        with self.assertRaisesRegex(ValueError, "CJf"):
            inspect_jass("function CJb takes nothing returns nothing\nendfunction")


if __name__ == "__main__":
    unittest.main()
