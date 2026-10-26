"""Original attack-time dummy spell scripts vs pinned source evidence."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "data/original-2323C-attacked-dummy-spells.json"

class AttackedDummySourceFacts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads(PATH.read_text(encoding="utf-8"))

    def test_source_pin_and_uncertainty(self):
        p= self.report
        self.assertEqual(p["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(p["sourceContext"]["trigger"], "EVENT_PLAYER_UNIT_ATTACKED")
        self.assertEqual(p["sourceContext"]["victimOwnerPlayers"],[5,6])
        self.assertIs(p["runtimeSpellDamageAndCollisionParityVerified"],False)
        self.assertIs(p["actualNativeSpellExecutorConnected"],False)

    def test_exact_rawcode_names_order_and_targets(self):
        p=self.report
        rows=p["skills"]
        self.assertEqual([x["ownerKoreanName"] for x in rows],
            ["블루노","후쿠로","에넬"])
        self.assertEqual([x["ownerUnitRawcode"] for x in rows],
            ["h00M","h00A","h00Q"])
        self.assertEqual([x["jassFunction"] for x in rows],["Bnm","Bnp","Bnr"])
        self.assertEqual([x["spellOrder"] for x in rows],
            ["carrionswarm","carrionswarm","forkedlightning"])
        self.assertEqual([x["chance"] for x in rows],["BFi(1,100)<=10"]*3)
        self.assertEqual([x["target"] for x in rows],
            ["attacker position","attacker position","attacked enemy unit"])
        self.assertTrue(all(len(x["bodySha256"])==64 for x in rows))
        self.assertEqual(p["dummyUnit"]["rawcode"],"e0KD")

if __name__=="__main__":
    unittest.main()
