"""Pinned 2.323[C] helper-cow JASS and A04R W3A evidence.

Never silently approve A0BZ/A07T cast costs/range/cooldown from their
450/625 area-effect radii or interpret isr2 as an already-proven gameplay
damage formula.
"""
import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / "data"
PROOF = DATA / "original-2323C-helper-A04R-evidence.json"


class HelperA04RSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.proof = json.loads(PROOF.read_text(encoding="utf-8"))

    def test_provenance_matches_pinned_original_map(self):
        p = self.proof
        self.assertEqual(p["sourceMapSha256"],
                         "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c")
        self.assertEqual(p["sourceJassSha256"],
                         "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(p["sourceW3aSha256"],
                         "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0")
        self.assertEqual(p["abilityRawcode"], "A04R")
        self.assertEqual(p["fieldId"], "isr2")
        self.assertEqual(p["sourceFieldMeaning"], "DAMAGE_REDUCTION_ISR2")
        self.assertEqual(p["sourceTable"], "original")

    def test_all_original_float_overrides_are_pinned_without_extrapolation(self):
        levels = self.proof["sourceLevelOverrides"]
        self.assertEqual([x["level"] for x in levels], list(range(5, 27)))
        self.assertEqual([x["value"] for x in levels],
                         [round((1 - lv) * 0.01, 6) for lv in range(5, 27)])
        self.assertEqual(len(levels), 22)

    def test_jass_increases_levels_not_stun_duration(self):
        rule = self.proof["jassMechanics"]
        self.assertEqual(rule["function"], "Bd8")
        self.assertEqual(rule["onMissingAbility"],
                         "UnitAddAbility then GetUnitAbilityLevel")
        self.assertEqual(rule["abilityLevelIncreaseA07TLevel1"], 5)
        self.assertEqual(rule["abilityLevelIncreaseA07TLevel2"], 8)
        self.assertIs(rule["isTimedStun"], False)

    def test_unverified_cast_ability_fields_still_missing(self):
        p = self.proof
        self.assertIs(p["castObjectDataA0BZPresentInW3a"], False)
        self.assertIs(p["castObjectDataA07TPresentInW3a"], False)
        for field in ("originalCastRanges", "originalManaCosts",
                      "originalCooldownSeconds", "originalEnemyStartingA04RLevels"):
            self.assertIsNone(p[field], field)
        self.assertIs(p["engineDamageApplicationOfIsr2Verified"], False)


if __name__ == "__main__":
    unittest.main()
