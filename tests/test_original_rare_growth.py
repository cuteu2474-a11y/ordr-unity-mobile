import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / 'data'


class OriginalRareGrowthTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((DATA / 'original-2323C-rare-growth.json').read_text())
        cls.registry = {r['abilityRawcode']: r for r in json.loads(
            (DATA / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (DATA / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}

    def test_rare_recipes_match_source_conditions_and_results(self):
        self.assertEqual(41, len(self.audit['recipes']))
        self.assertEqual(41, len({r['resultCatalogId'] for r in self.audit['recipes']}))
        for row in self.audit['recipes']:
            source = self.registry[row['abilityRawcode']]
            # The compact registry groups UNIT requirements before UPUN;
            # this source audit retains the original JASS instruction order.
            self.assertCountEqual(source['conditions'], row['conditions'])
            self.assertEqual(source['results'], row['results'])
            output = self.objects[row['results'][0]['rawcode']]
            self.assertEqual('희귀함', output['grade'])
            self.assertEqual(output['catalogId'], row['resultCatalogId'])
            self.assertIn(row['nativeBaseAbility'], ('AHwe', 'Aroa'))

    def test_every_rare_recipe_has_an_actual_ability_owning_material(self):
        for row in self.audit['recipes']:
            casters = []
            for condition in row['conditions']:
                self.assertIn(condition['kind'], ('UNIT', 'UPUN'))
                if condition['kind'] == 'UPUN':
                    continue
                material = self.objects[condition['rawcode']]
                self.assertIn(material['grade'], ('흔함', '안흔함', '특별함'))
                if row['abilityRawcode'] in material['originalAbilityList'].split(','):
                    casters.append(material['catalogId'])
            self.assertTrue(casters)
            self.assertEqual(casters, row['originalCasterCatalogIds'])

    def test_special_wisp_preserves_all_33_source_outcomes_including_uncraftable_variants(self):
        pools = json.loads((DATA / 'original-2323C-wisp-pools.json').read_text())
        source = next(x for x in pools['wisps'] if x['wispRawcode'] == 'e016')['branches'][0]
        entries = self.audit['specialWisp']['entries']
        self.assertEqual(33, len(entries))
        self.assertEqual([r['rawcode'] for r in source['entries']], [r['rawcode'] for r in entries])
        self.assertEqual([1] * 33, [r['weight'] for r in entries])
        self.assertEqual({'x': -3776, 'y': 4480}, self.audit['specialWisp']['positionPlayerZero'])
        self.assertTrue({'S-14', 'S-15'}.issubset({r['catalogId'] for r in entries}))
        for row in entries:
            self.assertEqual('특별함', self.objects[row['rawcode']]['grade'])
            self.assertEqual(row['catalogId'], self.objects[row['rawcode']]['catalogId'])

    def test_base_attacks_exist_without_claiming_full_skills_or_native_cast_parity(self):
        self.assertIs(False, self.audit['retailCombatParityVerified'])
        self.assertEqual(['A1A0'], self.audit['excludedNativeCastAbilities'])
        self.assertNotIn('A1A0', {r['abilityRawcode'] for r in self.audit['recipes']})
        for row in self.audit['recipes']:
            source = self.objects[row['results'][0]['rawcode']]
            self.assertIs(False, source['combatParityVerified'])
            self.assertGreater(source['attack1']['attackCooldownSeconds'], 0)
            self.assertIsNotNone(source['attack1']['attackPointSeconds'])
            self.assertIsNotNone(source['attack1']['minimumBaseDamage'])


if __name__ == '__main__':
    unittest.main()
