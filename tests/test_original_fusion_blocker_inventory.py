"""Batch-check the entire source registry against the live executable gate."""
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from classify_original_fusion_blockers import build


class FusionBlockerInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ROOT / 'data'
        cls.port = ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs'
        cls.inventory = json.loads((cls.data / 'original-fusion-unconnected-review.json').read_text())
        cls.source = {r['abilityRawcode']: r for r in json.loads(
            (cls.data / 'original-jass-fusion-registry.json').read_text())['registrations']}

    def test_all_265_source_recipes_are_recounted_from_real_csharp_arrays(self):
        self.assertEqual(build(self.data, self.port), self.inventory)
        summary = self.inventory['summary']
        self.assertEqual((265, 132, 133), (summary['sourceRecipesExcludingTest'],
                                         summary['liveExecutableRecipes'],
                                         summary['blockedRecipes']))
        self.assertEqual({'Reviewed': 13, 'ReviewedSpecial': 31, 'ReviewedRare': 41,
                          'ReviewedLegendary': 33, 'ReviewedHidden': 13,
                          'ReviewedEternal': 1}, summary['liveExecutableByGrade'])
        self.assertEqual(133, len({r['abilityRawcode'] for r in self.inventory['blocked']}))

    def test_grouped_reasons_preserve_source_costs_and_unknown_materials(self):
        for row in self.inventory['blocked']:
            source = self.source[row['abilityRawcode']]['conditions']
            self.assertEqual(row['sourceGoldCost'],
                             sum(x['count'] for x in source if x['kind'] == 'GOLD'))
            self.assertEqual(row['sourceWoodCost'],
                             sum(x['count'] for x in source if x['kind'] == 'WOOD'))
            self.assertTrue(row['reasonCodes'])
        blocked = {r['abilityRawcode']: r for r in self.inventory['blocked']}
        self.assertEqual(['h060'], blocked['YY04']['unmappedUnitRawcodes'])
        self.assertIn('navigation-ban-state', blocked['TR01']['reasonCodes'])
        self.assertIn('item-payment', blocked['ET02']['reasonCodes'])
        self.assertIn('native-cast-AOsf', blocked['A19Z']['reasonCodes'])
        self.assertIn('chat-player-state-effect', blocked['YY15']['reasonCodes'])
        self.assertIn('no-unit-payment-in-registration', blocked['UU01']['reasonCodes'])
        self.assertIn('no-unit-payment-in-registration', blocked['YY26']['reasonCodes'])
        self.assertIn('creation-followup-CH8', blocked['YY20']['reasonCodes'])
        self.assertNotIn('A01S', blocked)
        self.assertNotIn('A113', blocked)
        self.assertNotIn('YY18', blocked)
        self.assertNotIn('YY25', blocked)
        self.assertNotIn('ET01', blocked)

    def test_unverified_entries_do_not_turn_into_free_unit_rewards(self):
        for ability in ('UU01', 'YY26'):
            self.assertFalse(any(x['kind'] == 'UNIT' for x in self.source[ability]['conditions']))
        self.assertFalse(self.inventory['summary']['liveExecutableByGrade'].get('ReviewedImmortal'))


if __name__ == '__main__':
    unittest.main()
