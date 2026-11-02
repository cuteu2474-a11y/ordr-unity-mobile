"""Pinned ORDR JASS/W3A cross-checks without shipping the private source map."""
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_mpq_war3map_j import extract_member, checksum
from extract_original_helper_abilities import read_objects


class SourceCharacterCallbackEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((ROOT / "data/original-2323C-source-character-callback-evidence.json")
                               .read_text(encoding="utf-8"))
        cls.map = json.loads((ROOT / "data/original-2323C-map-base-stats.json")
                             .read_text(encoding="utf-8"))
        cls.code = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelNativeCharacters.cs")\
            .read_text(encoding="utf-8")

    def test_kaido_ability_gate_and_branch_counter_match_original_object_list(self):
        a = self.audit["kaido"]
        units = {r["catalogId"]: r for r in self.map["catalogEntries"]}
        objects = {r["rawcode"]: r for r in self.map["mapObjects"]}
        self.assertEqual("h07M", units["I-11"]["originalRawcode"])
        self.assertNotIn("A0PS", units["I-11"]["originalAbilityList"].split(","))
        self.assertIn("A0PS", objects["h0AD"]["originalAbilityList"].split(","))
        self.assertFalse(a["humanObjectHasA0PS"])
        self.assertTrue(a["dragonObjectHasA0PS"])
        self.assertEqual((8, 15), (a["firstBranch"]["atMost"],
                                    a["secondElseIfBranch"]["atMost"]))
        self.assertEqual((2, 2, 1), (a["firstBranch"]["userDataIncrease"],
                                        a["secondElseIfBranch"]["userDataIncrease"],
                                        a["otherwiseUserDataIncrease"]))
        self.assertEqual(11, 1 + a["life100RepeatedPackets"])
        self.assertIn("ExecuteKaidoSecondSpecialModelPacket", self.code)
        self.assertIn("ExecuteKaidoOrdinaryModelPacket", self.code)
        self.assertIn("KaidoCanRollAutoMorph", self.code)
        self.assertIn("KaidoUserDataIncrease(branch)", self.code)

    def test_sugar_and_shinobu_spawn_location_is_not_primary_center(self):
        sugar = self.audit["sugar"]
        shinobu = self.audit["shinobu"]
        objects = {r["rawcode"]: r for r in self.map["mapObjects"]}
        self.assertEqual(["Avul", "A07D"],
                         objects["h06P"]["originalAbilityList"].split(","))
        self.assertEqual(sugar["suicideRawcode"], "h06P")
        self.assertEqual(shinobu["correctCloneRawcode"], "h085")
        self.assertEqual(shinobu["otherAttackerCloneRawcode"], "h0A9")
        self.assertEqual(shinobu["cloneOffsetFromTarget"], 119)
        special = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelOriginalSpecials.cs")\
            .read_text(encoding="utf-8")
        temporary = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelTemporarySkills.cs")\
            .read_text(encoding="utf-8")
        self.assertIn("roster.Get(actor.Id)", special)
        self.assertIn("casterPosition.X, casterPosition.Y", special)
        self.assertIn("119 * Math.Cos(angle)", temporary)
        self.assertIn("119 * Math.Sin(angle)", temporary)
        self.assertFalse(self.audit["warcraftNativeParityVerified"])

    def test_mpq_member_hashes_and_original_callbacks_when_owner_map_exists(self):
        # Local 133MB W3X stays outside Git; CI tests the committed audit only.
        local = ROOT.parent / "source_reference/ORDR_S2_2.323[C].w3x"
        if not local.exists():
            self.skipTest("Original owned W3X is intentionally absent from Git")
        self.assertEqual(self.audit["sourceW3xSha256"], checksum(local))
        script = extract_member(local, "war3map.j")
        abilities = extract_member(local, "war3map.w3a")
        self.assertEqual(self.audit["sourceJassSha256"], hashlib.sha256(script).hexdigest())
        self.assertEqual(self.audit["sourceW3aSha256"], hashlib.sha256(abilities).hexdigest())
        jass = script.decode("utf-8-sig")

        def fn(name):
            start = jass.index("function " + name + " takes nothing returns nothing")
            return jass[start:jass.index("endfunction", start)]

        sugar = fn("Btq")
        self.assertIn("local location Id=GetUnitLoc(JL)", sugar)
        self.assertIn("CreateUnitAtLoc(GetOwningPlayer(JL),$68303650,Id", sugar)
        self.assertIn('IssueTargetOrder(JI,"selfdestruct",RN)', sugar)
        shinobu = fn("Bt2")
        self.assertIn("PolarProjectionBJ(Ie,119.,GetRandomDirectionDeg())", shinobu)
        self.assertIn("GetUnitTypeId(JL)==$68303432", shinobu)
        self.assertIn("$68303835,V7", shinobu)
        self.assertIn("$68304139,V7", shinobu)
        human, dragon = fn("CBL"), fn("CBM")
        self.assertIn("GetUnitAbilityLevel(JL,$41305053)>0", human)
        for callback in (human, dragon):
            self.assertIn("BFi(1,100)<=8", callback)
            self.assertIn("elseif BFi(1,100)<=15", callback)
            self.assertIn("GetUnitUserData(JL)>=10", callback)
        self.assertIn("B_g(LoadUnitHandle(E2,0,DR*EM+0),2)", fn("B_x"))
        self.assertIn("B_g(LoadUnitHandle(E2,0,DR*EM+0),1)", fn("CBE"))
        ids = {base for _, base, custom, _ in read_objects(abilities)} | {
            custom for _, base, custom, _ in read_objects(abilities)}
        self.assertFalse(set(self.audit["w3aOverridesNotPresent"]) & ids)


if __name__ == "__main__":
    unittest.main()
