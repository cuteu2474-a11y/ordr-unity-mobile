"""Batch-three source identities, complete approval shapes and damage wiring."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def load(name):
    return json.loads((ROOT / 'data' / name).read_text(encoding='utf-8'))


def code(name):
    return (ROOT / 'Assets/Scripts/Combat' / name).read_text(encoding='utf-8')


class ThirdTwelveAttackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = load('source-model-third-12-composite-attacks.json')
        cls.profiles = {p['unitId']: p for p in load('character-skill-profiles.json')['profiles']}
        cls.units = {p['catalogId']: p for p in load('unit-runtime-status.json')['entries']}
        cls.catalog = code('SourceModelUnconnectedSkillCatalog.cs')
        cls.approval = code('SourceModelNextThirtySkillCatalog.cs')
        cls.damage = code('CharacterCombatRuntime.SourceModelCompositeEffects.cs')
        cls.runtime = code('CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs')

    def test_all_twelve_unique_source_effect_packets_are_approved_once(self):
        rows = self.batch['skills']
        self.assertEqual(12, len(rows))
        self.assertEqual(12, len({p['skillId'] for p in rows}))
        self.assertEqual(7, len({p['unitId'] for p in rows}))
        for row in rows:
            with self.subTest(skill=row['skillId']):
                self.assertEqual(row['rawcode'], self.units[row['unitId']]['originalRawcode'])
                skill = next(s for s in self.profiles[row['unitId']]['skills']
                             if s['name'] == row['name'])
                self.assertEqual(row['chancePercent'], skill['chancePercent'])
                self.assertEqual('onAttack', skill['trigger'])
                self.assertEqual(row['components'], [e['type'] for e in skill['components']])
                self.assertIn('case "' + row['skillId'] + '"', self.approval)
                self.assertIn('"' + row['name'] + '"', self.catalog)

    def test_generic_damage_scaling_and_soldier_lifetime_use_one_engine(self):
        for name in ('basicAttackPercent', 'basicAttackMultiplier',
                     'skillDamagePercent', 'bonusPercent',
                     'percentPerSoldier', 'amountPerSoldier',
                     'targetCurrentHealth', 'plusMissingHealthPercent',
                     'sourceSongSoldierExpiry', 'TrySourceModelSongSoldier',
                     'SourceModelSongSoldiers', 'ExplosiveDamageAmplification'):
            self.assertIn(name, self.damage)
        for name in ('CanModelThirdCompositeComponents',
                     'IsThirdCompositeAttack'):
            self.assertIn(name, self.approval)
        self.assertIn('SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill)',
                      self.runtime)
        self.assertIn('sourceSongSoldierExpiry.Remove(instanceId)',
                      code('CharacterCombatRuntime.cs'))

    def test_no_fabricated_hero_strength_or_soldier_rawcode(self):
        policies = self.batch['mobilePolicies']
        self.assertIn('deferred', policies['strength'])
        self.assertIn('no source-backed combat rawcode', policies['soldier'])
        self.assertIn('4.25x', policies['critical'])
        self.assertIn('strengthScalingPerPoint', self.approval)
        self.assertIn('ConfigureSourceModelHeroStrengthProvider', self.damage)
        self.assertIn('not a claim', policies['parity'])


if __name__ == '__main__':
    unittest.main()
