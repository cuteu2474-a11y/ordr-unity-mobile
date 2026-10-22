"""Fail-closed reconciliation checks for original Mystic JASS candidates."""

import copy
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools/reconcile_otherworld_mystic_jass_candidates.py"
SPEC = importlib.util.spec_from_file_location("mystic_jass_reconciliation", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def dataset(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


class MysticJassReconciliationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.jass = dataset("original-jass-fusion-registry.json")
        cls.links = dataset("original-unit-rawcode-label-links.json")
        cls.web = dataset("otherworld-2323C-mystic-web-recipes.json")
        cls.result = MODULE.analyze(cls.jass, cls.links, cls.web)

    def test_all_thirteen_recipes_and_original_cost_candidate_count(self):
        self.assertEqual(self.result["webRecipeCount"], 13)
        self.assertEqual(self.result["matchingGoldWoodRegistrations"], 14)
        self.assertEqual({r["resultUnitId"] for r in self.result["rows"]},
                         {f"MY-{n:02d}" for n in range(1, 14)})

    def test_eight_positive_label_correlations_not_verifications(self):
        expected = {"MY-01": "A19W", "MY-02": "A0YK",
                    "MY-03": "A0Y9", "MY-05": "A19T",
                    "MY-06": "A0XT", "MY-07": "A10V",
                    "MY-08": "A1AD", "MY-09": "A19S"}
        self.assertEqual(self.result["positiveLabelCorrelationRecipes"], 8)
        indexed = {r["resultUnitId"]: r for r in self.result["rows"]}
        for unit, ability in expected.items():
            with self.subTest(unit=unit):
                self.assertEqual(indexed[unit]["strongestLabelEvidenceCandidateAbilities"],
                                 [ability])
                self.assertIsNone(indexed[unit]["verifiedResultRawcode"])

    def test_remaining_recipes_stay_ambiguous_or_structural_only(self):
        expected = {"MY-04": {"A0Y3", "A12C"},
                    "MY-10": {"A19U", "A19V"},
                    "MY-11": {"A0Y6"},
                    "MY-12": {"A0Y3", "A12C"},
                    "MY-13": {"A19U", "A19V"}}
        indexed = {r["resultUnitId"]: r for r in self.result["rows"]}
        for unit, abilities in expected.items():
            with self.subTest(unit=unit):
                self.assertEqual(indexed[unit]["matchStatus"],
                                 "structural-only-ambiguous")
                self.assertEqual(set(indexed[unit][
                    "strongestLabelEvidenceCandidateAbilities"]), abilities)

    def test_shared_token_and_wildcard_remain_hypotheses(self):
        self.assertEqual(self.result["verifiedObjectRawcodeMappings"], 0)
        self.assertFalse(self.result["authorizedForUnitConsumption"])
        self.assertTrue(any("not verified" in x
                            for x in self.result["structuralHypotheses"]))
        for row in self.result["rows"]:
            self.assertFalse(row["authorizedForUnitConsumption"])
            self.assertTrue(row["compatibleCandidatesUnderHypotheses"])
            for candidate in row["compatibleCandidatesUnderHypotheses"]:
                cond = candidate["jassConditionsInOriginalOrder"]
                self.assertEqual(MODULE.count(cond, "UNIT", "h06G"), 1)
                self.assertEqual(MODULE.count(cond, "GOLD"), 10000)
                self.assertEqual(MODULE.count(cond, "WOOD"), 7)

    def test_generated_artifact_is_reproducible(self):
        self.assertEqual(
            self.result,
            dataset("original-jass-mystic-candidate-reconciliation.json"))

    def test_fingerprint_mismatch_rejected(self):
        links = copy.deepcopy(self.links)
        links["sourceMapSha256"] = "not-the-original-map"
        with self.assertRaisesRegex(ValueError, "different maps"):
            MODULE.analyze(self.jass, links, self.web)

    def test_version_and_mode_mismatch_rejected(self):
        web = copy.deepcopy(self.web)
        web["gameVersion"] = "S2 2.322"
        with self.assertRaisesRegex(ValueError, "Sources must all"):
            MODULE.analyze(self.jass, self.links, web)
        web = copy.deepcopy(self.web)
        web["mode"] = "normal"
        with self.assertRaisesRegex(ValueError, "otherworld mode"):
            MODULE.analyze(self.jass, self.links, web)

    def test_conflicting_label_mapping_rejected(self):
        links = copy.deepcopy(self.links)
        entry = copy.deepcopy(links["mappings"][0])
        entry["catalogUnitId"] = "D-99"
        links["mappings"].append(entry)
        with self.assertRaisesRegex(ValueError, "Conflicting catalog labels"):
            MODULE.analyze(self.jass, links, self.web)


if __name__ == "__main__":
    unittest.main()
