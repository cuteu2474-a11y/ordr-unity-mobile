"""Pinned Warcraft object fields for three script-attacked dummy spells."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/original-2323C-dummy-native-object-abilities.json"

class OriginalDummySpellObjectDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.facts=json.loads(SOURCE.read_text(encoding="utf-8"))

    def test_map_and_ability_object_hashes(self):
        self.assertEqual(self.facts["sourceJassSha256"],
          "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(self.facts["abilityDataSlkSha256"],
          "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda")
        self.assertEqual(self.facts["unitAbilitySlkSha256"],
          "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d")
        self.assertIs(self.facts["nativeCastHitGeometryAndImmunityVerifiedInUnity"],False)
        self.assertIs(self.facts["rawStatsAreNotFinalHpDamage"],True)

    def test_dummy_has_both_original_order_abilities(self):
        self.assertIn("A0VS",self.facts["dummyAbilities"])
        self.assertIn("A086",self.facts["dummyAbilities"])
        by={row["ability"]:row for row in self.facts["abilities"]}
        swarm=by["A0VS"]; lightning=by["A086"]
        self.assertEqual(swarm["owners"],["h00M","h00A"])
        self.assertEqual(swarm["parentAbility"],"AUcs")
        self.assertEqual(swarm["nativeOrder"],"carrionswarm")
        self.assertEqual((swarm["area1"],swarm["range1"],swarm["dataA1"],
                          swarm["dataB1"],swarm["dataC1"],swarm["dataD1"]),
                         (200,1700,150,70000000,800,250))
        self.assertIsNone(swarm["maxNativeTargets"])
        self.assertEqual(lightning["parentAbility"],"ANfl")
        self.assertEqual(lightning["nativeOrder"],"forkedlightning")
        self.assertEqual(lightning["owners"],["h00Q"])
        self.assertEqual(lightning["maxNativeTargets"],4)
        self.assertEqual((lightning["area1"],lightning["range1"],
                          lightning["dataA1"],lightning["dataB1"],
                          lightning["dataC1"],lightning["dataD1"]),
                         (125,1750,1500,4,1700,500))
if __name__=="__main__":unittest.main()
