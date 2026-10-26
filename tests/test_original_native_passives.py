import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class OriginalNativePassiveEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.data = json.loads((ROOT / "data/original-2323C-native-pure-passives.json").read_text())
        self.entries = self.data["entries"]
        self.by_id = {r["catalogId"]: r for r in self.entries}

    def test_pinned_source_hashes_and_complete_effect_scope(self):
        self.assertEqual("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c", self.data["sourceW3xSha256"])
        self.assertEqual("23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda", self.data["abilityDataSha256"])
        self.assertEqual(26, len(self.entries))
        self.assertEqual(26, len({r["originalUnitRawcode"] for r in self.entries}))

    def test_low_grade_values_come_from_exact_source_fields(self):
        self.assertEqual(100000, self.by_id["U-03"]["chancePerMillion"])
        self.assertEqual((.3, .15), (self.by_id["U-03"]["normalStunSeconds"], self.by_id["U-03"]["heroStunSeconds"]))
        self.assertEqual(1.5, self.by_id["U-04"]["criticalMultiplier"])
        self.assertEqual(1.5, self.by_id["U-13"]["criticalMultiplier"])
        self.assertEqual((150000, 2), (self.by_id["S-06"]["chancePerMillion"], self.by_id["S-06"]["criticalMultiplier"]))
        self.assertEqual((700000, 1.5), (self.by_id["S-21"]["chancePerMillion"], self.by_id["S-21"]["criticalMultiplier"]))

    def test_no_guessed_bonus_or_mixed_partial_skill(self):
        for row in self.entries:
            fields = row["sourceFields"]
            self.assertEqual(1, row["level"])
            self.assertEqual(0, float(fields["DataC1"]))
            self.assertEqual(0, float(fields["DataD1"]))
            self.assertEqual(0, float(fields["Cool1"]))
            self.assertEqual(row["targetMask"], fields["targs1"])
            self.assertEqual(row["chancePerMillion"], float(fields["DataA1"]) * 10000)
            if row["kind"] == "critical":
                self.assertGreater(row["criticalMultiplier"], 1)
                self.assertEqual(0, row["normalStunSeconds"])
                self.assertEqual(0, row["heroStunSeconds"])
            else:
                self.assertEqual("stun", row["kind"])
                self.assertEqual(0, row["criticalMultiplier"])
                self.assertGreater(row["normalStunSeconds"], 0)

    def test_source_unit_ability_ownership_matches_map_catalog(self):
        base = json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text())
        owners = {r["catalogId"]: r for r in base["catalogEntries"]}
        for row in self.entries:
            owner = owners[row["catalogId"]]
            self.assertEqual(row["originalUnitRawcode"], owner["originalRawcode"])
            self.assertIn(row["abilityRawcode"], owner["originalAbilityList"].split(","))

    def test_mixed_and_multi_level_skills_are_not_silently_approximated(self):
        for excluded in ("U-05", "U-09", "U-12", "L-27", "TR-05", "TR-12", "TR-19", "E-04", "E-06"):
            self.assertNotIn(excluded, self.by_id)


if __name__ == "__main__":
    unittest.main()
