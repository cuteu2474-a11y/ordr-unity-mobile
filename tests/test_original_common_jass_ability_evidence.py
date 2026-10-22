"""Evidence-bound common Luffy JASS callback and fusion classification."""
import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / "data"


def read(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class CommonLuffyJassEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.proof = read("original-2323C-common-ability-jass-evidence.json")
        cls.audit = read("original-2323C-common-unit-ability-audit.json")
        cls.stats = read("original-2323C-map-base-stats.json")
        cls.fusions = read("original-jass-fusion-registry.json")

    def test_pinned_original_map_and_war3map_j_fingerprint(self):
        p = self.proof
        self.assertEqual(p["sourceW3xSha256"], self.stats["sourceW3xSha256"])
        self.assertEqual(p["sourceW3xSha256"], self.fusions["sourceSha256"])
        self.assertEqual(p["sourceWar3mapJSha256"],
                         "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(p["sourceLineCount"], 63651)

    def test_all_five_abilities_have_source_callback_evidence(self):
        from_audit = [a["rawcode"] for a in self.audit["commonUnits"][0]
                      ["abilityCodesInObjectOrder"]]
        from_jass = [x["rawcode"] for x in self.proof["commonAbilities"]]
        self.assertEqual(from_audit, from_jass)
        self.assertEqual(from_jass, ["A0JE", "A09G", "A00D", "A012", "Avul"])
        self.assertEqual(self.proof["verifiedLuffyIndividualOffensiveAbilityRawcodes"], [])
        self.assertTrue(all(x["checks"] and all(
            c["line"] > 0 and c["contains"] for c in x["checks"])
            for x in self.proof["commonAbilities"]))

    def test_fusion_results_correlate_with_exact_original_registry(self):
        registry = {r["abilityRawcode"]: r for r in self.fusions["registrations"]}
        for raw, output in [("A00D", "h00K"), ("A012", "h00U")]:
            result = registry[raw]["results"]
            self.assertEqual((result[0]["kind"], result[0]["rawcode"]), ("UNIT", output))
        for item in self.proof["commonAbilities"]:
            self.assertNotIn("combat-damage", item["classification"])

    def test_jass_sales_have_exact_milestone_and_probability(self):
        rule = self.proof["saleRewardRule"]
        self.assertEqual(rule["saleAbilityRawcode"], "A09G")
        self.assertEqual(rule["requiredSaleCountPerRandomWisp"], 3)
        self.assertEqual(rule["randomWispPerMilestone"], 1)
        self.assertEqual(rule["bonusWoodProbabilityPercent"], 35)
        self.assertEqual(rule["extraWoodOnSuccess"], 1)
        self.assertFalse(rule["gameplayTransactionReady"])


if __name__ == "__main__":
    unittest.main()
