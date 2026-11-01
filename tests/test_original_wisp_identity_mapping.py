"""Cross-check all automatic Wisp identity assignments against pinned map data.

A matching catalog identity is not permission to bypass the original Wisp draw.
"""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "data"


def load(filename):
    with (ROOT / filename).open(encoding="utf-8") as stream:
        return json.load(stream)


class WispIdentityMappingTests(unittest.TestCase):
    def test_all_156_reward_slots_have_unique_source_backed_identity(self):
        source = load("original-2323C-wisp-pools.json")
        assigned = load("original-2323C-wisp-character-identity-assignments.json")
        objects = load("original-2323C-unit-object-stats.json")
        reconciled = load("original-fusion-object-reconciliation.json")
        catalog = load("units.json")

        self.assertEqual(source["sourceMapSha256"], assigned["sourceMapSha256"])
        self.assertEqual(source["sourceJassSha256"], assigned["sourceJassSha256"])
        self.assertFalse(assigned["authorizesLiveSpawn"])
        self.assertFalse(assigned["authorizesWispConsumption"])

        known_objects = {row["rawcode"] for row in objects["entries"]}
        by_rawcode = {row["rawcode"]: row for row in reconciled["entries"]}
        by_catalog = {row["id"]: row for row in catalog["entries"]}
        seen_rawcodes = set()
        seen_catalog_ids = set()

        self.assertEqual(4, len(source["wisps"]))
        self.assertEqual(4, len(assigned["wisps"]))
        for original_wisp, mapped_wisp in zip(source["wisps"], assigned["wisps"]):
            self.assertEqual(original_wisp["wispRawcode"], mapped_wisp["wispRawcode"])
            self.assertEqual(len(original_wisp["branches"]), len(mapped_wisp["branches"]))
            for original_branch, mapped_branch in zip(
                    original_wisp["branches"], mapped_wisp["branches"]):
                self.assertEqual(original_branch["chancePercent"],
                                 mapped_branch["chancePercent"])
                self.assertEqual(original_branch["bonusAbilityRawcode"],
                                 mapped_branch["bonusAbilityRawcode"])
                self.assertEqual(len(original_branch["entries"]), len(mapped_branch["entries"]))
                for original_entry, mapped in zip(
                        original_branch["entries"], mapped_branch["entries"]):
                    rawcode = original_entry["rawcode"]
                    unit_id = mapped["catalogUnitId"]
                    self.assertEqual(rawcode, mapped["rawcode"])
                    self.assertNotIn(rawcode, seen_rawcodes)
                    self.assertNotIn(unit_id, seen_catalog_ids)
                    self.assertIn(rawcode, known_objects)
                    self.assertEqual(unit_id, by_rawcode[rawcode]["catalogUnitId"])
                    self.assertEqual(mapped["originalW3xObjectName"],
                                     by_rawcode[rawcode]["originalW3xObjectName"])
                    self.assertEqual(mapped["catalogUnitName"], by_catalog[unit_id]["name"])
                    self.assertEqual(mapped["catalogGrade"], by_catalog[unit_id]["grade"])
                    seen_rawcodes.add(rawcode)
                    seen_catalog_ids.add(unit_id)

        self.assertEqual(156, len(seen_rawcodes))
        self.assertEqual(156, len(seen_catalog_ids))
        self.assertEqual(0, assigned["counts"]["unresolved"])

    def test_rare_ninety_nine_percent_bonus_remains_explicit(self):
        assigned = load("original-2323C-wisp-character-identity-assignments.json")
        rare = next(row for row in assigned["wisps"] if row["wispRawcode"] == "e019")
        self.assertEqual([99, 1], [b["chancePercent"] for b in rare["branches"]])
        self.assertEqual("A0YE", rare["branches"][0]["bonusAbilityRawcode"])
        self.assertIsNone(rare["branches"][1]["bonusAbilityRawcode"])
        self.assertEqual([42, 8], [len(b["entries"]) for b in rare["branches"]])


if __name__ == "__main__":
    unittest.main()
