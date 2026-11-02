"""Evidence-only coverage of the full 286 identity catalog."""
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from build_character_runtime_coverage import build, load  # noqa: E402


class CharacterRuntimeCoverageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inputs = [load(filename) for filename in (
            "units.json", "wiki-unit-catalog-extension.json",
            "unit-runtime-status.json", "character-skill-profiles.json",
            "original-2323C-unit-object-stats.json",
            "original-fusion-object-reconciliation.json",
        )]
        cls.current = build(*cls.inputs)

    def test_full_286_registry_and_source_inherited_forms(self):
        s = self.current["summary"]
        self.assertEqual(286, s["catalogIdentities"])
        self.assertEqual(277, s["sourceCatalogIdentities"])
        self.assertEqual(9, s["wikiExtensionIdentities"])
        self.assertEqual(281, s["independentRosterIdentities"])
        self.assertEqual(5, s["alternateCombatForms"])
        self.assertEqual(274, s["sourceObjectBasicAttackModels"])
        self.assertEqual(200, s["sourceTooltips"])
        self.assertEqual(5, s["formInheritedTooltips"])
        self.assertEqual(81, s["identitiesWithoutOwnOrInheritedTooltip"])
        self.assertEqual(0, s["fullyOriginalCombatVerified"])
        self.assertEqual(44, s["byGrade"]["희귀함"])
        self.assertEqual(41, s["byGrade"]["전설"])
        self.assertEqual(36, s["byGrade"]["초월함"])
        self.assertEqual(23, s["byGrade"]["히든"])
        self.assertEqual(14, s["byGrade"]["신비함"])

    def test_original_battle_model_is_not_inflated_into_complete_skills(self):
        rows = {row["id"]: row for row in self.current["entries"]}
        hanma = rows["MY-14"]
        self.assertEqual("h0C2", hanma["sourceRawcode"])
        self.assertTrue(hanma["sourceBasicAttackModel"])
        self.assertFalse(hanma["fullyOriginalCombatVerified"])
        self.assertEqual(0, hanma["connectedSourceModelEffectCount"])
        ship = rows["PS-01"]
        self.assertEqual("히든", ship["grade"])
        self.assertEqual("h060", ship["sourceRawcode"])
        self.assertFalse(rows["R-44"]["sourceBasicAttackModel"])
        self.assertFalse(rows["L-40"]["sourceBasicAttackModel"])
        self.assertFalse(rows["L-41"]["sourceBasicAttackModel"])
        for unit in ("TR-31", "TR-32", "TR-33", "TR-34", "TR-35"):
            self.assertFalse(rows[unit]["independentRosterIdentity"])
            self.assertTrue(rows[unit]["inheritedTooltipProfile"])
            self.assertFalse(rows[unit]["sourceBasicAttackModel"])
        self.assertTrue(all(not row["fullyOriginalCombatVerified"]
                            for row in self.current["entries"]))

    def test_committed_reproducible_snapshot(self):
        committed = json.loads((ROOT / "data" / "character-runtime-coverage.json").read_text())
        self.assertEqual(self.current, committed)
        # Changing only the report cannot invent missing original skill data.
        self.assertEqual("S2 2.323[C]", committed["gameVersion"])


if __name__ == "__main__":
    unittest.main()
