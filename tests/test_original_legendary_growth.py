import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / 'data'


class OriginalLegendaryGrowthTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((DATA / 'original-2323C-legendary-growth.json').read_text())
        cls.registry = {r['abilityRawcode']: r for r in json.loads(
            (DATA / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (DATA / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}

    def test_legendary_materials_and_exact_wood_cost_match_source(self):
        self.assertEqual(31, len(self.audit['recipes']))
        self.assertEqual(31, len({r['resultCatalogId'] for r in self.audit['recipes']}))
        for row in self.audit['recipes']:
            source = self.registry[row['abilityRawcode']]
            self.assertCountEqual(source['conditions'], row['conditions'])
            self.assertEqual(source['results'], row['results'])
            self.assertEqual([{'kind': 'WOOD', 'rawcode': 'WOOD', 'count': 3}],
                             [r for r in row['conditions'] if r['kind'] == 'WOOD'])
            self.assertEqual(3, row['woodCost'])
            self.assertEqual('전설', self.objects[row['results'][0]['rawcode']]['grade'])

    def test_legendary_casters_are_actual_material_actors(self):
        for row in self.audit['recipes']:
            casters = []
            for material in row['conditions']:
                self.assertIn(material['kind'], ('UNIT', 'UPUN', 'WOOD'))
                if material['kind'] != 'UNIT':
                    continue
                obj = self.objects[material['rawcode']]
                self.assertIn(obj['grade'], ('흔함', '안흔함', '특별함', '희귀함'))
                if row['abilityRawcode'] in obj['originalAbilityList'].split(','):
                    casters.append(obj['catalogId'])
            self.assertTrue(casters)
            self.assertEqual(casters, row['originalCasterCatalogIds'])

    def test_additional_creation_and_unmapped_variant_recipes_are_excluded(self):
        excluded = {'A00O', 'A02D', 'A025', 'A0UG', 'A01S', 'A02Q', 'A02G', 'A113'}
        self.assertEqual(excluded, set(self.audit['excludedAbilities']))
        self.assertTrue(excluded.isdisjoint({r['abilityRawcode'] for r in self.audit['recipes']}))

    def test_object_attacks_are_available_without_claiming_retail_parity(self):
        self.assertIs(False, self.audit['retailCombatParityVerified'])
        for row in self.audit['recipes']:
            source = self.objects[row['results'][0]['rawcode']]
            self.assertIs(False, source['combatParityVerified'])
            self.assertIsNotNone(source['attack1']['minimumBaseDamage'])
            self.assertGreater(source['attack1']['attackCooldownSeconds'], 0)
            self.assertIsNotNone(source['attack1']['attackPointSeconds'])
            self.assertEqual(source['catalogId'], row['resultCatalogId'])


if __name__ == '__main__':
    unittest.main()
