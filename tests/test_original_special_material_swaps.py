import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / 'data'


class SpecialMaterialSwapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((DATA / 'original-2323C-special-material-swaps.json').read_text())
        cls.registry = {x['abilityRawcode']: x for x in json.loads(
            (DATA / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {x['originalRawcode']: x for x in json.loads(
            (DATA / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if x.get('originalRawcode')}

    def test_every_material_and_output_matches_pinned_registry(self):
        self.assertEqual(31, len(self.audit['recipes']))
        for row in self.audit['recipes']:
            source = self.registry[row['abilityRawcode']]
            self.assertEqual(source['conditions'], row['conditions'])
            self.assertEqual(source['results'], row['results'])
            result = self.objects[row['results'][0]['rawcode']]
            self.assertEqual('특별함', result['grade'])
            self.assertEqual(result['catalogId'], row['resultCatalogId'])
            self.assertIn(row['nativeBaseAbility'], ('AHwe', 'Aroa'))
            self.assertEqual(.06, row['nativeCastSeconds'])
            self.assertEqual(0, row['nativeCooldownSeconds'])

    def test_each_recipe_has_an_actual_ability_owning_material(self):
        for row in self.audit['recipes']:
            casters = []
            for condition in row['conditions']:
                self.assertIn(condition['kind'], ('UNIT', 'UPUN'))
                if condition['kind'] != 'UNIT':
                    continue
                material = self.objects[condition['rawcode']]
                self.assertIn(material['grade'], ('흔함', '안흔함'))
                if row['abilityRawcode'] in material['originalAbilityList'].split(','):
                    casters.append(material['catalogId'])
            self.assertTrue(casters)
            self.assertEqual(casters, row['originalCasterCatalogIds'])

    def test_source_attack_stats_exist_but_retail_parity_is_not_claimed(self):
        self.assertIs(False, self.audit['retailCombatParityVerified'])
        for row in self.audit['recipes']:
            obj = self.objects[row['results'][0]['rawcode']]
            attack = obj['attack1']
            self.assertGreater(attack['attackCooldownSeconds'], 0)
            self.assertGreater(attack['attackPointSeconds'], 0)
            self.assertGreaterEqual(attack['minimumBaseDamage'], 1)
            self.assertIs(False, obj['combatParityVerified'])

    def test_unreviewed_native_variants_are_not_added(self):
        ids = {x['abilityRawcode'] for x in self.audit['recipes']}
        self.assertEqual(['A19Z', 'A1A1'], self.audit['excludedNativeCastAbilities'])
        self.assertTrue(ids.isdisjoint(self.audit['excludedNativeCastAbilities']))
        zombie = next(x for x in self.audit['recipes'] if x['abilityRawcode'] == 'A029')
        self.assertIn({'kind': 'UNIT', 'rawcode': 'h00H', 'count': 3}, zombie['conditions'])


if __name__ == '__main__':
    unittest.main()
