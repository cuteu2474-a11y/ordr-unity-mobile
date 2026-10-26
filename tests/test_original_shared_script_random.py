"""Validate fixed W3X source facts for shared scripted RNG."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "data/original-2323C-shared-script-random.json"

class OriginalSharedScriptRandomEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads(PATH.read_text(encoding="utf-8"))

    def test_original_rng_identity_is_pinned_and_runtime_not_approved(self):
        doc = self.doc
        self.assertEqual(doc["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertIs(doc["fullNativeRandomStateParityVerified"], False)
        self.assertIs(doc["allScriptCallOrderingVerified"], False)
        self.assertEqual(doc["BFh"]["multiplier"], 69069)
        self.assertEqual(doc["BFh"]["divisor"], 2147483647)
        self.assertEqual(doc["BFh"]["seedIfZero"], "GetRandomInt(1,1000000)")

    def test_native_pool_and_scripted_random_are_separate(self):
        doc = self.doc
        self.assertEqual(len(doc["supportedSeparateLanes"]), 2)
        a, b = doc["supportedSeparateLanes"]
        self.assertIs(a["sharedState"], True)
        self.assertIs(b["sharedStateWithBFi"], False)
        self.assertIn("PlaceRandomUnit reward selection", b["roles"])

    def test_rare_wisp_uses_script_draw_but_regular_pool_does_not(self):
        by_name = {c["source"]: c for c in self.doc["noteworthyCalls"]}
        self.assertEqual(by_name["CSI"]["call"], "BFi(1,100)<=99")
        self.assertEqual(by_name["CSK"]["call"], "BFi(1,100)>33")
        self.assertEqual(by_name["CVM"]["call"], "BFi(1,4)==3")
        for name in ["CSF", "CSC", "CR6"]:
            self.assertEqual(by_name[name]["customBfiCalls"], 0)
        signatures = {f["name"]: f for f in self.doc["functions"]}
        self.assertEqual(signatures["BFh"]["sourceLine"], 3589)
        self.assertEqual(signatures["BFi"]["sourceLine"], 3596)
        for entry in signatures.values():
            self.assertEqual(len(entry["bodySha256"]), 64)

if __name__ == "__main__":
    unittest.main()
