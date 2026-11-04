"""Batch 14: satellite source mana, tooltip actives and variant identity gates."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
COMBAT = ROOT / "Assets/Scripts/Combat"
AUDIT = json.loads((DATA / "source-model-fourteenth-satellite-and-variant-audit.json").read_text())
SOURCE = json.loads((DATA / "original-2323C-map-base-stats.json").read_text())
SKILLS = {p["unitId"]: p for p in
          json.loads((DATA / "character-skill-profiles.json").read_text())["profiles"]}
WIKI = {p["id"]: p for p in
        json.loads((DATA / "wiki-unit-catalog-extension.json").read_text())["entries"]}
OBJECTS = {o["rawcode"]: o for o in SOURCE["mapObjects"]}
INDEPENDENT = (COMBAT / "CharacterCombatRuntime.SourceModelIndependentAttacks.cs").read_text()
NATIVE = (COMBAT / "CharacterCombatRuntime.SourceModelNativeCharacters.cs").read_text()
EFFECTS = (COMBAT / "CharacterCombatRuntime.SourceModelEffects.cs").read_text()
MAP = (ROOT / "Assets/Scripts/Data/OriginalMapBaseStatCatalog.cs").read_text()


class FourteenthSatelliteTests(unittest.TestCase):
    def test_map_pools_and_tooltip_costs_match_audit(self):
        self.assertEqual(SOURCE["sourceW3xSha256"], AUDIT["sourceW3xSha256"])
        skills = {s["name"]: s for s in SKILLS["TR-30"]["skills"]}
        for row in AUDIT["satelliteMana"]:
            with self.subTest(rawcode=row["rawcode"]):
                base = OBJECTS[row["rawcode"]]["baseStats"]
                self.assertEqual(row["maximumMana"], base["mana"])
                self.assertEqual(row["nativeManaRegeneration"], base["manaRegeneration"])
                if row["activeSkill"] is None:
                    self.assertFalse(any(s.get("satellite") == row["name"] and
                                         s["trigger"] == "satelliteActiveMana"
                                         for s in skills.values()))
                    continue
                spell = skills[row["activeSkill"]]
                self.assertEqual("satelliteActiveMana", spell["trigger"])
                self.assertEqual(row["name"], spell["satellite"])
                self.assertEqual(row["manaCost"], spell["manaCost"])
                self.assertEqual(row["radius"], spell["radius"])
                self.assertEqual(row["flatDamage"], spell["components"][0]["amount"])
                self.assertEqual(row["damageSchool"], spell["components"][0]["type"])

    def test_actual_mana_bonuses_match_source_and_are_bounded(self):
        spell = next(s for s in SKILLS["TR-30"]["skills"]
                     if s["name"] == "베가펑크 마나 공격")
        bonus = next(e for e in spell["components"]
                     if e["type"] == "satelliteManaRegeneration")
        self.assertEqual(AUDIT["vegapunkMainCast"]["manaCost"], spell["manaCost"])
        self.assertEqual(25, bonus["perSecond"])
        self.assertEqual(3, bonus["durationSeconds"])
        self.assertIn("StartSourceSatelliteManaRegeneration(actor.Id, 25, 3)", NATIVE)
        self.assertIn("Math.Min(actor.MaximumMana, actor.Mana + added)", INDEPENDENT)
        self.assertIn("SourceModelTimeAdvancing += AdvanceSourceIndependentMana", EFFECTS)
        self.assertIn("objects.FindMapObjectResources(rawcode)",
                      (COMBAT / "SourceModelBasicCombatRules.cs").read_text())
        self.assertIn("OrdrJson.Number(stats, \"mana\")", MAP)
        self.assertIn("actor.Mana -= cost", INDEPENDENT)
        self.assertIn("actor.MaximumMana != cost", INDEPENDENT)
        self.assertIn('actor.Rawcode != "H0AE"', INDEPENDENT)
        self.assertIn('TrySourceModelSatelliteAttribute(actor,', INDEPENDENT)
        self.assertIn('"strength", out strength)', INDEPENDENT)
        self.assertIn('ApplySourceModelSatelliteFixedArea(actor, primary,', INDEPENDENT)
        self.assertIn('target.Enemy.MaxHealth * (.001 * strength)', INDEPENDENT)
        self.assertIn("actual", INDEPENDENT)  # Real HP ledger, not cosmetic event only.

    def test_five_variants_remain_distinct_from_independent_seraphim(self):
        expected = {"TR-31": "TR-20", "TR-32": "TR-23",
                    "TR-33": "TR-30", "TR-34": "TR-30", "TR-35": "TR-30"}
        rows = AUDIT["alternateForms"]
        self.assertEqual(set(expected), {r["catalogId"] for r in rows})
        for row in rows:
            with self.subTest(catalog=row["catalogId"]):
                source = WIKI[row["catalogId"]]
                self.assertEqual("alternate-form", source["catalogEntryKind"])
                self.assertEqual(expected[row["catalogId"]], source["variantOfUnitId"])
                self.assertEqual(source["originalSourceRawcode"], row["originalFormRawcode"])
                self.assertEqual("identity-only-no-new-execution-authority",
                                 source["gameplayStatus"])
                self.assertIn(row["candidateMapRawcode"], OBJECTS)
                self.assertIn(row["result"], ("IDENTITY_ONLY", "EXISTING_PARENT_FORM"))
        self.assertEqual("H0B6", WIKI["TR-32"]["originalSourceRawcode"])
        self.assertEqual("H0B6", AUDIT["alternateForms"][1]["originalFormRawcode"])
        self.assertIn('sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "H0B6")', NATIVE)


if __name__ == "__main__":
    unittest.main()
