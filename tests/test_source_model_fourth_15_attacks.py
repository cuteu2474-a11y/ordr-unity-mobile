"""Batch-four strict tooltip skill identities and common model wiring."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read_json(filename):
    return json.loads((ROOT / 'data' / filename).read_text(encoding='utf-8'))


def read_combat(filename):
    return (ROOT / 'Assets' / 'Scripts' / 'Combat' / filename).read_text(encoding='utf-8')


class FourthFifteenAttackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = read_json('source-model-fourth-15-composite-attacks.json')
        cls.profiles = {p['unitId']: p for p in read_json('character-skill-profiles.json')['profiles']}
        cls.units = {row['catalogId']: row for row in read_json('unit-runtime-status.json')['entries']}
        cls.approval = read_combat('SourceModelNextThirtySkillCatalog.cs')
        cls.unconnected = read_combat('SourceModelUnconnectedSkillCatalog.cs')
        cls.damage = read_combat('CharacterCombatRuntime.SourceModelCompositeEffects.cs')
        cls.attack = read_combat('CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs')

    def test_fifteen_source_packets_have_unique_owner_and_exact_component_order(self):
        rows = self.batch['skills']
        self.assertEqual(15, len(rows))
        self.assertEqual(15, len({x['skillId'] for x in rows}))
        self.assertEqual(11, len({x['unitId'] for x in rows}))
        for row in rows:
            with self.subTest(row=row['skillId']):
                self.assertEqual(row['rawcode'], self.units[row['unitId']]['originalRawcode'])
                skill = next(s for s in self.profiles[row['unitId']]['skills']
                             if s['name'] == row['name'])
                self.assertEqual('onAttack', skill['trigger'])
                self.assertEqual(row['chancePercent'], skill['chancePercent'])
                self.assertEqual(row['components'], [e['type'] for e in skill['components']])
                self.assertIn('case "' + row['skillId'] + '"', self.approval)
                self.assertIn('"' + row['name'] + '"',
                              self.approval + self.unconnected)

    def test_shared_scaling_and_armor_status_use_complete_skill_gate(self):
        for marker in ('IsFourthCompositeAttack', 'CanModelFourthCompositeComponents',
                       'ValidateExplicitCompositeShape', 'targetKindMultiplier',
                       'agilityScalingPerPoint', 'singleExtraPercent',
                       'extraSingleAmount', 'maxArmorReduction'):
            self.assertIn(marker, self.approval + self.damage + self.attack)
        self.assertIn('ApplySourceModelCompositeArmorBreak', self.attack)
        self.assertIn('ApplySourceModelCompositeStatus', self.attack)
        self.assertIn('ConfigureSourceModelHeroAgilityProvider', self.damage)
        self.assertIn('sourceModelFireFistReadyAt.Remove(instanceId)',
                      read_combat('CharacterCombatRuntime.cs'))

    def test_four_exceptional_skills_do_not_infer_missing_metadata(self):
        profiles = self.profiles
        self.assertNotIn('target', profiles['D-03']['skills'][2])
        self.assertEqual(500, profiles['D-03']['skills'][2]['radius'])
        self.assertEqual('line', profiles['TR-26']['skills'][4]['target'])
        self.assertEqual(4.25, profiles['E-07']['skills'][3]['absoluteCooldownSeconds'])
        self.assertEqual(3500, profiles['TR-26']['skills'][2]['components'][0]
                         ['agilityScalingPerPoint'])
        self.assertIn('skill.Id == "D-03:skill:2"', self.approval)
        self.assertIn('skill.Id == "TR-26:skill:4"', self.attack)
        self.assertIn('skill.Id == "E-07:skill:3"', self.attack)
        self.assertIn('ProjectileLine', self.attack)


if __name__ == '__main__':
    unittest.main()
