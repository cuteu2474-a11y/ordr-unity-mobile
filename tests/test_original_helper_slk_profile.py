"""CI-safe fixture checks; full W3X re-extraction is run locally with --w3x."""
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_original_helper_slk import inspect_source, MAP_SHA, SLK_HASHES


class HelperSourceProfileTests(unittest.TestCase):
    def setUp(self):
        self.profile = json.loads(
            (ROOT / "data/original-2323C-helper-slk-profile.json").read_text(
                encoding="utf-8"))

    def test_pinned_source_scope_and_unverified_mechanics(self):
        p = self.profile
        self.assertEqual(MAP_SHA, p["sourceMapSha256"])
        self.assertEqual(SLK_HASHES["AbilityData"], p["sourceAbilitySlkSha256"])
        self.assertEqual(SLK_HASHES["UnitBalance"], p["sourceUnitBalanceSlkSha256"])
        self.assertEqual(SLK_HASHES["UnitAbilities"], p["sourceUnitAbilitiesSlkSha256"])
        self.assertEqual("h08A", p["helper"]["rawcode"])
        self.assertEqual(15, p["helper"]["startingMana"])
        self.assertEqual(10000, p["helper"]["maxMana"])
        self.assertEqual(0.3, p["helper"]["manaRegenPerSecond"])
        self.assertEqual(3, p["abilities"]["A0BZ"]["declaredLevels"])
        self.assertEqual(2, len(p["abilities"]["A0BZ"]["verifiedLevels"]))
        self.assertTrue(all(p["cannotInfer"].values()))
        self.assertEqual("AIsr", p["abilities"]["A04R"]["originalBaseCode"])
        self.assertEqual(26, p["abilities"]["A04R"]["declaredLevels"])
        self.assertEqual(20, p["maximumOutputNavigation"]["availableAfterRound"])
        self.assertEqual(3, p["manaPotion"]["charges"])
        self.assertEqual(300, p["manaPotion"]["postCastManaGain"])
        self.assertTrue(p["manaPotion"]["inheritedManaCostsUnknown"])

    def test_parser_rejects_modified_original_fields(self):
        p = self.profile
        spells = {}
        for code in ("A0BZ", "A07T"):
            entry = p["abilities"][code]
            row = {"code": entry["originalBaseCode"],
                   "levels": str(entry["declaredLevels"])}
            for level in entry["verifiedLevels"]:
                i = level["level"]
                for field, key in (("Rng", "castRange"), ("Cost", "manaCost"),
                                   ("Cool", "cooldown"), ("Area", "effectRadius")):
                    row[field + str(i)] = str(level[key])
            if code == "A0BZ":
                row["Cool3"] = "150"
            spells[code] = row
        spells["A04R"] = {"code": "AIsr", "levels": "26", **{
            "DataB" + str(level["level"]): str(level["value"])
            for level in p["abilities"]["A04R"]["baseSlkIsr2Levels"]}}
        spells["A0UY"] = {"code": "AOsf", "levels": "3",
                          "Cool1": "0", "Cool2": "0", "Cool3": "0"}
        cow = {"mana0": "15", "manaN": "10000", "regenMana": "0.3"}
        abilities = {"abilList": ",".join(p["helper"]["defaultAbilities"])}
        rects = [(-5888, 5664, -5632, 5920), (128, 5664, 384, 5920),
                 (-5888, 544, -5632, 800), (128, 544, 384, 800)]
        jass = "\n".join([
            *["set Ll[%d]=B%d" % (i, i + 2) for i in range(4)],
            "set kk[EK]=CreateUnit(Player(EK),$68303841,GetUnitX(RV[EK]),GetUnitY(RV[EK])-1200,bj_UNIT_FACING)",
            "set RV[EK]=CreateUnit(Player(EK),$48304334,GetRectMinX(Ll[EK]),GetRectMinY(Ll[EK]),bj_UNIT_FACING)",
            *["set B%d=Rect(%d.,%d.,%d.,%d.)" % (i + 2, *rect)
              for i, rect in enumerate(rects)],
            "function CKX takes nothing returns nothing",
            "if X3<=20 then", "if kt[JG] then", "call CKS(K7,JG)",
            "function CKS takes integer Hk,integer JG returns nothing",
            "call SetUnitAbilityLevel(kk[JG],$4130425A,2)",
            "call SetUnitAbilityLevel(kk[JG],$41304259,2)",
            "call SetUnitAbilityLevel(kk[JG],$41303754,2)",
            "call SetUnitAbilityLevel(kk[JG],$41303758,2)",
            "call SetUnitAbilityLevel(kk[JG],$41303757,2)",
            "call UnitAddAbility(kk[JG],$41305559)",
            "function CJk takes nothing returns nothing",
            "if GetUnitAbilityLevel(GetTriggerUnit(),$41305559)==3 then",
            "call UnitRemoveAbility(GetTriggerUnit(),$41305559)",
            "call IncUnitAbilityLevel(GetTriggerUnit(),$41305559)",
            "call SetUnitState(kk[JG],UNIT_STATE_MANA,(GetUnitState(kk[JG],UNIT_STATE_MANA)+300)*1.)",
        ])
        self.assertEqual(p, inspect_source(spells, {"h08A": cow},
                         {"h08A": abilities}, jass))
        spells["A07T"]["Cost1"] = "319"
        with self.assertRaisesRegex(ValueError, "A07T"):
            inspect_source(spells, {"h08A": cow},
                           {"h08A": abilities}, jass)
        spells["A07T"]["Cost1"] = "320"
        cow["regenMana"] = "0.4"
        with self.assertRaisesRegex(ValueError, "h08A mana regeneration"):
            inspect_source(spells, {"h08A": cow},
                           {"h08A": abilities}, jass)
        cow["regenMana"] = "0.3"
        spells["A0UY"]["Cost1"] = "0"
        with self.assertRaisesRegex(ValueError, "A0UY mana cost"):
            inspect_source(spells, {"h08A": cow},
                           {"h08A": abilities}, jass)


if __name__ == "__main__":
    unittest.main()
