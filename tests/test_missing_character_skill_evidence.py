"""The 71 missing tooltip profiles keep map evidence without invented skills."""

import copy
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "missing_skill_evidence", ROOT / "tools/build_missing_character_skill_evidence.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def read(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


class MissingCharacterSkillEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.units = read("units.json")
        cls.profiles = read("character-skill-profiles.json")
        cls.base = read("original-2323C-map-base-stats.json")
        cls.report = MODULE.build(cls.units, cls.profiles, cls.base)

    def test_all_seventy_one_gaps_and_rawcodes_are_sourced(self):
        summary = self.report["summary"]
        self.assertEqual(summary["catalogCharacters"], 271)
        self.assertEqual(summary["charactersWithTooltipProfiles"], 200)
        self.assertEqual(summary["charactersWithoutTooltipProfiles"], 71)
        self.assertEqual(summary["unprofiledByGrade"], {
            "흔함": 9, "안흔함": 13, "특별함": 25, "희귀함": 24})
        self.assertEqual(summary["unprofiledWithMapRawcode"], 71)
        self.assertEqual(summary["unprofiledWithAbilityCodes"], 71)
        self.assertEqual(summary["distinctAbilityRawcodesForResearch"], 181)
        self.assertEqual(len(self.report["characters"]), 71)

    def test_map_order_and_identity_are_not_inferred_from_skill_tooltips(self):
        from_original = {x["catalogId"]: x for x in self.base["catalogEntries"]}
        from_profiles = {x["unitId"] for x in self.profiles["profiles"]}
        for row in self.report["characters"]:
            with self.subTest(unit=row["catalogUnitId"]):
                self.assertNotIn(row["catalogUnitId"], from_profiles)
                original = from_original[row["catalogUnitId"]]
                self.assertEqual(row["mapUnitRawcode"], original["originalRawcode"])
                self.assertEqual(row["originalAbilityRawcodesInMapOrder"],
                                 MODULE.original_abilities(original))
                self.assertTrue(row["webDexUrl"].startswith("https://ordr.kr/character/"))
                self.assertFalse(row["originalAbilityEffectsResolved"])
                self.assertFalse(row["originalSkillJassTriggersResolved"])
                self.assertFalse(row["originalParitySkillExecutionAuthorized"])

    def test_rarity_hint_does_not_claim_ability_behavior(self):
        by_id = {x["catalogUnitId"]: x for x in self.report["characters"]}
        r04 = by_id["R-04"]
        self.assertEqual(r04["mapUnitRawcode"], "h01L")
        self.assertEqual(r04["originalAbilityRawcodesInMapOrder"],
                         ["A0K2", "A057", "A07Y", "A058", "A0B9", "A009", "Avul"])
        self.assertIn("A0K2", r04["lessFrequentlyObservedRawcodesForResearchOnly"])
        self.assertNotIn("Avul", r04["lessFrequentlyObservedRawcodesForResearchOnly"])
        frequency = self.report["linkedCharacterAbilityRawcodeOccurrenceCounts"]
        self.assertEqual(frequency["Avul"], 270)
        self.assertEqual(self.report["summary"]["verifiedSkillFormulasInThisDataset"], 0)
        self.assertEqual(self.report["summary"]["authorizedOriginalParityImplementations"], 0)

    def test_report_is_deterministic_and_no_fake_profile_is_added(self):
        self.assertEqual(self.report,
                         read("original-2323C-missing-skill-profile-evidence.json"))
        self.assertEqual(len(self.profiles["profiles"]), 200)

    def test_new_proof_shrinks_gap_without_rewriting_others(self):
        newer = copy.deepcopy(self.profiles)
        newer["profiles"].append({"unitId": "R-04", "skills": [], "passives": []})
        reduced = MODULE.build(self.units, newer, self.base)
        self.assertEqual(reduced["summary"]["charactersWithoutTooltipProfiles"], 70)
        self.assertNotIn("R-04", {r["catalogUnitId"] for r in reduced["characters"]})

    def test_wrong_version_duplicate_and_name_disagreement_fail_closed(self):
        wrong = copy.deepcopy(self.base)
        wrong["gameVersion"] = "S2 2.322"
        with self.assertRaisesRegex(ValueError, "identical ORDR"):
            MODULE.build(self.units, self.profiles, wrong)
        duplicate = copy.deepcopy(self.profiles)
        duplicate["profiles"].append(copy.deepcopy(duplicate["profiles"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            MODULE.build(self.units, duplicate, self.base)
        mismatched = copy.deepcopy(self.base)
        for row in mismatched["catalogEntries"]:
            if row["catalogId"] == "R-04":
                row["catalogName"] = "wrong name"
                break
        with self.assertRaisesRegex(ValueError, "disagrees"):
            MODULE.build(self.units, self.profiles, mismatched)


if __name__ == "__main__":
    unittest.main()
