"""Pinned source proof for original Bd5 add-to-level magic defense events."""
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/"data/original-2323C-magic-defense-additive-stack-calls.json"

class SourceMagicDefenseStackEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.e=json.loads(SOURCE.read_text(encoding="utf-8"))

    def test_pinned_source_and_nonpercent_formula(self):
        e=self.e
        self.assertEqual(e["sourceWar3mapJSha256"],
          "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(e["sourceFunction"],"Bd5")
        self.assertEqual(e["sourceFunctionLine"],8560)
        self.assertEqual(e["sourceFunctionSha256"],
          "f3f7fcb6e3261416fb9b936e5c4f70e9c82232baf811d31bd3cc6c4e51db703d")
        self.assertEqual(e["sourceFormula"],
          "SetUnitAbilityLevel(target, JV, GetUnitAbilityLevel(target, JV) + Hk)")
        self.assertIs(e["nativeBehaviorBeyondSourceVerifiedLevel41"],False)

    def test_every_direct_original_callsite_is_pinned(self):
        e=self.e["sourceCalls"]
        self.assertEqual(len(e),9)
        self.assertEqual([v["callback"] for v in e],
          ["BoM","Br6","Bur","BvG","B46","B7I","CEa","CFI","CGm"])
        self.assertEqual([v["increase"] for v in e],
          [10,10,1,18,5,10,1,3,10])
        self.assertEqual([v["sourceLine"] for v in e],
          [15139,17604,19761,20134,32897,35379,43508,44588,46939])
        self.assertTrue(all(len(v["callbackBodySha256"])==64 for v in e))

if __name__=="__main__":
    unittest.main()
