"""Wiki extension is a view/identity layer; no speculative original gameplay."""
import json
import unittest
from collections import Counter
from pathlib import Path

DATA = Path(__file__).resolve().parents[1] / "data"


def load(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class WikiUnitExtensionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base = load("units.json")
        cls.extension = load("wiki-unit-catalog-extension.json")
        cls.reconciliation = load("original-fusion-object-reconciliation.json")

    def test_pinned_base_and_expected_grade_totals(self):
        base = {row["id"]: row for row in self.base["entries"]}
        extra = {row["id"]: row for row in self.extension["entries"]}
        self.assertEqual(277, len(base))
        self.assertEqual(9, len(extra))
        self.assertEqual(9, len(self.extension["entries"]))
        self.assertFalse(set(base) & set(extra))
        self.assertEqual(286, self.extension["expectedMergedEntries"])
        counts = Counter(row["grade"] for row in self.base["entries"])
        counts["해적선"] -= 1
        counts["히든"] += 1
        counts.update(row["grade"] for row in extra.values())
        self.assertEqual({"희귀함": 44, "전설": 41, "초월함": 36,
                          "신비함": 14, "히든": 23, "해적선": 0},
                         {key: counts[key] for key in
                          ("희귀함", "전설", "초월함", "신비함", "히든", "해적선")})
        self.assertEqual(286, sum(counts.values()))

    def test_forms_are_not_independent_fusion_results(self):
        base = {row["id"]: row for row in self.base["entries"]}
        forms = [row for row in self.extension["entries"]
                 if row["catalogEntryKind"] == "alternate-form"]
        self.assertEqual(5, len(forms))
        self.assertEqual({"TR-20", "TR-23", "TR-30"},
                         {row["variantOfUnitId"] for row in forms})
        self.assertTrue(all(base[row["variantOfUnitId"]]["grade"] == row["grade"]
                            for row in forms))
        self.assertTrue(all(row["gameRawCode"] is None and
                            row["attackPower"] is None and
                            row["gameplayStatus"] == "identity-only-no-new-execution-authority"
                            for row in self.extension["entries"]))

    def test_source_identity_never_grants_fusion_permission(self):
        items = {row["id"]: row for row in self.extension["entries"]}
        raw = {row["rawcode"]: row for row in self.reconciliation["entries"]}
        for unit_id, source in (("R-44", "h05Y"), ("MY-14", "h0C2")):
            self.assertEqual(source, items[unit_id]["originalSourceRawcode"])
            self.assertIsNone(raw[source]["catalogUnitId"])
            self.assertFalse(raw[source]["approvedForMaterialConsumption"])
        self.assertEqual("PS-01", self.extension["reclassifications"][0]["id"])
        self.assertEqual("히든", self.extension["reclassifications"][0]["grade"])
        self.assertEqual("해적선", self.extension["reclassifications"][0]["expectedGrade"])
        self.assertEqual(self.reconciliation["sourceW3xSha256"],
                         self.extension["sourceW3xSha256"])


if __name__ == "__main__":
    unittest.main()
