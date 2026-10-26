"""Source callsite audit for explicit money gambling and three attacks."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "data/original-2323C-combat-gambling-script-call-evidence.json"

class CombatGamblingCallsiteTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads(PATH.read_text(encoding="utf-8"))

    def test_map_pinned_with_cautious_claims(self):
        self.assertEqual(self.doc["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertIs(self.doc["fullCombatSkillAndGamblingParityVerified"], False)
        self.assertIs(self.doc["productionCostAndPurchaseWorldVerified"], False)
        self.assertIs(self.doc["nativeCritRngIsCustomScriptBfi"], False)

    def test_two_paid_gambling_branches_each_draw_twice(self):
        g = self.doc["moneyGambling"]
        self.assertEqual([x["sourceFunction"] for x in g], ["CJB","CJC"])
        self.assertEqual([x["guaranteedCustomScriptDraws"] for x in g], [2,2])
        self.assertEqual([x["highPayoutCondition"] for x in g],
            ["roll > 25","roll > 30"])
        self.assertEqual([x["highRewardRoll"] for x in g],
            ["BFi(10,50)","BFi(500,4500)"])
        self.assertEqual([x["lowRewardRoll"] for x in g],
            ["BFi(3,5)","BFi(300,400)"])
        self.assertTrue(all(not x["originalShopCostVerified"] for x in g))

    def test_only_explicit_combat_script_calls_not_native_crit(self):
        c = self.doc["combatAttackScriptCalls"]
        self.assertEqual([x["sourceFunction"] for x in c],
            ["Bnm","Bnp","Bnr"])
        self.assertTrue(all(x["roll"]=="BFi(1,100)<=10" for x in c))
        self.assertTrue(all(not x["unitOwnerAndEffectParityVerified"] for x in c))
        for x in c + self.doc["moneyGambling"]:
            self.assertEqual(len(x["bodySha256"]),64)

if __name__=="__main__":
    unittest.main()
