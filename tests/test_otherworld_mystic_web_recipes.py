import json
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


class OtherworldMysticWebRecipesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(
            (REPO / "data/otherworld-2323C-mystic-web-recipes.json").read_text(encoding="utf-8")
        )
        units = json.loads((REPO / "data/units.json").read_text(encoding="utf-8"))
        cls.units = {unit["id"]: unit for unit in units["entries"]}

    def test_all_thirteen_web_mystic_recipes_have_unique_matching_results(self):
        self.assertEqual("S2 2.323[C]", self.data["gameVersion"])
        self.assertEqual("nightmare-otherworld", self.data["mode"])
        recipes = self.data["recipes"]
        self.assertEqual(13, len(recipes))
        self.assertEqual(len(recipes), self.data["recipeCount"])
        self.assertEqual({f"MY-{i:02}" for i in range(1, 14)},
                         {recipe["resultUnitId"] for recipe in recipes})
        for recipe in recipes:
            result = self.units[recipe["resultUnitId"]]
            self.assertEqual(result["name"], recipe["resultName"])
            self.assertEqual("신비함", result["grade"])
            index = int(recipe["resultUnitId"].split("-")[1])
            self.assertEqual(f"https://ordr.kr/character/MY{index}", recipe["sourceUrl"])

    def test_materials_refer_to_real_units_and_wildcards_are_not_guessed(self):
        wildcard_count = 0
        for recipe in self.data["recipes"]:
            seen = set()
            random_fixed_units = 0
            for material in recipe["ingredients"]:
                self.assertEqual(1, material["count"])
                if material["kind"] == "any-random":
                    self.assertNotIn("unitId", material)
                    wildcard_count += 1
                    continue
                self.assertEqual("unit", material["kind"])
                unit_id = material["unitId"]
                self.assertIn(unit_id, self.units)
                self.assertNotIn(unit_id, seen)
                seen.add(unit_id)
                random_fixed_units += self.units[unit_id]["grade"] == "랜덤"
            self.assertEqual(1, random_fixed_units)
        self.assertEqual(5, wildcard_count)

    def test_original_transaction_remains_disabled_despite_web_costs(self):
        self.assertFalse(self.data["authorizedForConsumption"])
        self.assertFalse(self.data["originalJassConditionsVerified"])
        for recipe in self.data["recipes"]:
            self.assertEqual(10000, recipe["gold"])
            self.assertEqual(7, recipe["lumber"])
            self.assertIn("행운의토큰", recipe["specialItems"])
            expected = ["해적선", "행운의토큰"] if recipe["resultUnitId"] == "MY-01" \
                else ["행운의토큰"]
            self.assertEqual(expected, recipe["specialItems"])


if __name__ == "__main__":
    unittest.main()
