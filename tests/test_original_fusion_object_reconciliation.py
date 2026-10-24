"""Source-only W3X object/JASS reconciliation: no gameplay authorization."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from reconcile_original_fusion_objects import reconcile  # noqa: E402


def load(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


class OriginalFusionObjectReconciliationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inputs = (
            load("original-2323C-map-base-stats.json"),
            load("original-jass-fusion-registry.json"),
            load("original-unit-rawcode-label-links.json"),
            load("original-fusion-rawcode-backlog.json"),
            load("verified-mobile-fusion-recipes.json"),
        )
        cls.result = reconcile(*cls.inputs)

    def test_snapshot_is_reproducible(self):
        self.assertEqual(self.result, load("original-fusion-object-reconciliation.json"))
        self.assertFalse(self.result["authorizedForFusionExecution"])

    def test_all_source_identities_are_not_approvals(self):
        s = self.result["summary"]
        self.assertEqual(s["uniqueCwrUnitRawcodes"], 280)
        self.assertEqual(s["originalCatalogObjectMatchedRawcodes"], 271)
        self.assertEqual(s["previouslyUnresolvedIngredientRawcodes"], 163)
        self.assertEqual(s["newlyCrossReferencedIngredients"], 156)
        self.assertEqual(s["stillWithoutCatalogIdentity"], 7)
        self.assertEqual(s["ingredientConditionOccurrences"], 775)
        self.assertEqual(s["catalogMatchedIngredientConditionOccurrences"], 712)
        self.assertTrue(all(not r["approvedForMaterialConsumption"]
                            for r in self.result["entries"]))

    def test_ten_web_recipes_match_but_keep_original_nonunit_conditions(self):
        s = self.result["summary"]
        self.assertEqual(s["webRecipeCount"], 10)
        self.assertEqual(s["matchingWebRecipes"], 10)
        for row in self.result["webRecipes"]:
            with self.subTest(unit=row["resultCatalogUnitId"]):
                self.assertTrue(row["ingredientMultisetMatches"])
                self.assertTrue(row["listedGoldWoodMatches"])
                self.assertTrue(row["originalNonUnitConditionsInOrder"])
                self.assertFalse(row["approvedForFusionExecution"])

    def test_original_map_zombie_is_mapped_but_unverified_material_consumption(self):
        unknown = {r["rawcode"]: r for r in self.result["entries"]
                   if r["ingredientUseCount"] and r["catalogUnitId"] is None}
        self.assertEqual(set(unknown),
            {"h04S","h06G","h060","h05X","h05Y","H08T","h0AN"})
        zombie = next(r for r in self.result["entries"] if r["rawcode"] == "h00H")
        self.assertEqual(zombie["catalogUnitId"], "U-14")
        self.assertEqual(zombie["originalW3xObjectName"], "좀비 - 안흔함")
        self.assertFalse(zombie["approvedForMaterialConsumption"])

    def test_et04_ryuma_object_and_jass_result_agree(self):
        self.assertEqual(self.result["summary"]["catalogVsJassLabelConflicts"], 0)
        self.assertEqual(self.result["conflictingCatalogJassLabels"], [])
        row = next(r for r in self.result["entries"] if r["rawcode"] == "h0CJ")
        self.assertEqual(row["catalogUnitId"], "E-03")
        self.assertEqual(row["jassLabelCatalogUnitId"], "E-03")
        self.assertFalse(row["approvedForMaterialConsumption"])

    def test_reject_wrong_version_hash_and_stale_backlog(self):
        inputs = copy.deepcopy(self.inputs)
        inputs[0]["sourceW3xSha256"] = "other-map"
        with self.assertRaisesRegex(ValueError, "fingerprints disagree"):
            reconcile(*inputs)
        inputs = copy.deepcopy(self.inputs)
        inputs[1]["conditionsInOriginalOrder"] = False
        with self.assertRaisesRegex(ValueError, "ordering"):
            reconcile(*inputs)
        inputs = copy.deepcopy(self.inputs)
        inputs[4]["gameVersion"] = "old"
        with self.assertRaisesRegex(ValueError, "version mismatch"):
            reconcile(*inputs)
        inputs = copy.deepcopy(self.inputs)
        inputs[3]["entries"][0]["ingredientUseCount"] += 1
        with self.assertRaisesRegex(ValueError, "stale"):
            reconcile(*inputs)

    def test_reject_duplicate_catalog_identity(self):
        inputs = copy.deepcopy(self.inputs)
        inputs[0]["catalogEntries"].append(copy.deepcopy(inputs[0]["catalogEntries"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            reconcile(*inputs)


if __name__ == "__main__":
    unittest.main()
