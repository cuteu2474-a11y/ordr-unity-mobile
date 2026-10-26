"""Pinned original Warcraft script set-vs-stack evidence."""
import json
import pathlib
import unittest

PATH = pathlib.Path(__file__).resolve().parents[1] / "data" / "original-2323C-a04q-native-mutations.json"

class A04QMutationSourceEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evidence = json.loads(PATH.read_text(encoding="utf-8"))

    def test_exact_source_and_different_operations(self):
        e= self.evidence
        self.assertEqual(e["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(e["sourceSetExpression"],"SetUnitAbilityLevel(Cm,JV,Hk)")
        self.assertEqual(e["sourceIncreaseExpression"],
            "SetUnitAbilityLevel(Cm,JV,GetUnitAbilityLevel(Cm,JV)+Hk)")
        self.assertEqual(e["sourceFunctionSetDefinitionLine"],8554)
        self.assertEqual(e["sourceFunctionIncreaseDefinitionLine"],8560)

    def test_all_eight_verified_set_sites(self):
        entries=self.evidence["sourceSetCalls"]
        self.assertEqual([x["caller"] for x in entries],
            ["Bvi","CKz","CNI","CVX","CVY","CVZ","CVo","CVp"])
        self.assertEqual([x["line"] for x in entries],
            [20698,50860,52131,59504,59518,59552,59681,59694])
        self.assertEqual([x["operand"] for x in entries],
            ["LK[JG]","LK[JG]","NJ","LK[JG]","LK[JG]","LK[JG]","LK[JG]","LK[JG]"])
        self.assertTrue(self.evidence["independentActualUnitCallbackObservationRequired"])
        self.assertFalse(self.evidence["nativeDamageAmplificationStackingFormulaFullyVerified"])
        self.assertEqual((self.evidence["allowedVerifiedObjectLevelMin"],
            self.evidence["allowedVerifiedObjectLevelMax"]),(1,41))

if __name__=="__main__": unittest.main()
