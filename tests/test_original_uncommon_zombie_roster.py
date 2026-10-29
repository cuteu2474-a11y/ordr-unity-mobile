"""Original S2 2.323[C] Zombie is map-only uncommon, not a fabricated web unit."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


class MapOnlyUncommonRosterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.units = read("units.json")
        cls.source = read("original-2323C-map-base-stats.json")
        cls.index = read("character-master-database.json")
        cls.audit = read("character-database-audit.json")
        cls.uncommon = read("characters/by-grade/U.json")
        cls.missing = read("original-2323C-missing-skill-profile-evidence.json")
        cls.matrix = read("unit-implementation-matrix.json")
        cls.fusion = read("original-fusion-object-reconciliation.json")

    def test_all_source_catalogs_register_exactly_fourteen_uncommons(self):
        uncommon = [u for u in self.units["entries"] if u["grade"] == "안흔함"]
        self.assertEqual(self.units["expectedTotal"], 277)
        self.assertEqual(len(self.units["entries"]), 277)
        self.assertEqual(len(uncommon), 14)
        self.assertEqual([u["id"] for u in uncommon],
                         ["U-%02d" % i for i in range(1, 15)])
        self.assertEqual(uncommon[-1]["name"], "좀비")
        self.assertEqual(uncommon[-1]["catalogEvidenceOrigin"], "original-W3X-only")
        self.assertIsNone(uncommon[-1]["detailUrl"])
        self.assertIsNone(uncommon[-1]["gameRawCode"])
        self.assertEqual(self.uncommon["catalogCount"], 14)
        self.assertEqual(len(self.uncommon["characters"]), 14)
        self.assertEqual(self.index["summary"]["catalogCharacters"], 272)
        self.assertEqual(self.index["gradeFiles"][1]["catalogCount"], 14)
        self.assertEqual(self.audit["byGrade"]["안흔함"]["catalog"], 14)
        self.assertEqual(self.matrix["byGrade"]["안흔함"]["units"], 14)
        self.assertEqual(len(self.matrix["entries"]), 277)

    def test_zombie_identity_weapon_stats_and_ability_codes_are_from_w3x(self):
        source = next(x for x in self.source["mapObjects"]
                      if x["rawcode"] == "h00H")
        entry = next(x for x in self.source["catalogEntries"]
                     if x["catalogId"] == "U-14")
        self.assertEqual(source["mapName"], "좀비 - 안흔함")
        self.assertEqual(entry["sourceMapName"], source["mapName"])
        self.assertEqual(entry["originalRawcode"], "h00H")
        self.assertEqual(entry["mappingStatus"], "unique-map-name-and-grade")
        self.assertEqual(entry["attack1"], source["attack1"])
        self.assertEqual(entry["baseStats"], source["baseStats"])
        self.assertEqual(entry["originalAbilityList"], "A029,Avul")
        self.assertEqual(source["attack1"]["baseDamagePlus"], 120)
        self.assertEqual((source["attack1"]["diceCount"],
                          source["attack1"]["diceSides"]), (2, 4))
        self.assertEqual((source["attack1"]["minimumBaseDamage"],
                          source["attack1"]["maximumBaseDamage"]), (122, 128))
        self.assertEqual(source["attack1"]["attackCooldownSeconds"], 0.61)
        self.assertEqual(source["attack1"]["originalRange"], 1050)
        self.assertEqual(source["attack1"]["originalAttackType"], "pierce")
        self.assertEqual(source["attack1"]["originalWeaponType"], "normal")
        self.assertIs(entry["combatParityVerified"], False)

    def test_a029_is_original_cwr_recipe_and_material_consumption_is_unapproved(self):
        registry = read("original-jass-fusion-registry.json")
        recipe = next(x for x in registry["registrations"]
                      if x["abilityRawcode"] == "A029")
        self.assertEqual(recipe["conditions"], [
            {"kind": "UNIT", "rawcode": "h00H", "count": 3},
            {"kind": "UNIT", "rawcode": "h001", "count": 1},
            {"kind": "UPUN", "rawcode": "h05K", "count": 1}])
        self.assertEqual(recipe["results"], [
            {"kind": "UNIT", "rawcode": "h010", "count": 1}])
        referenced = next(x for x in self.fusion["entries"]
                          if x["rawcode"] == "h00H")
        self.assertEqual(referenced["catalogUnitId"], "U-14")
        self.assertEqual(referenced["objectCatalogMappingStatus"],
                         "unique-map-name-and-grade")
        self.assertFalse(referenced["approvedForMaterialConsumption"])
        self.assertFalse(self.fusion["authorizedForFusionExecution"])

    def test_unknown_original_combat_and_nonexistent_web_page_stay_unknown(self):
        zombie = next(x for x in self.missing["characters"]
                      if x["catalogUnitId"] == "U-14")
        self.assertEqual(self.missing["summary"]["charactersWithoutTooltipProfiles"], 77)
        self.assertEqual(zombie["mapUnitRawcode"], "h00H")
        self.assertIsNone(zombie["webDexUrl"])
        self.assertEqual(zombie["originalAbilityRawcodesInMapOrder"],
                         ["A029", "Avul"])
        self.assertFalse(zombie["originalAbilityEffectsResolved"])
        self.assertFalse(zombie["originalSkillJassTriggersResolved"])
        self.assertFalse(zombie["originalParitySkillExecutionAuthorized"])
        unit = self.uncommon["characters"][-1]
        self.assertIsNone(unit["profile"])
        self.assertFalse(unit["completeness"]["allowedForParityCombat"])
        self.assertFalse(unit["coreCombat"]["numericValuesFromPatchAreNotCombatVerified"]
                         is False)
        self.assertEqual(self.matrix["entries"][22]["id"], "U-14")


if __name__ == "__main__":
    unittest.main()
