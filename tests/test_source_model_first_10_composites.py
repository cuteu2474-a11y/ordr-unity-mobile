"""Regression audit for the ten explicitly approved mobile composite actives."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def load(filename):
    return json.loads((ROOT / 'data' / filename).read_text())


class FirstTenCompositeActiveTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = load('source-model-first-10-composite-actives.json')
        cls.profiles = {p['unitId']: p for p in load('character-skill-profiles.json')['profiles']}
        cls.status = {p['catalogId']: p for p in load('unit-runtime-status.json')['entries']}
        cls.objects = {p['rawcode']: p for p in load('original-2323C-unit-object-stats.json')['entries']}
        cls.basic_catalog = (ROOT / 'Assets/Scripts/Combat/SourceModelNextThirtySkillCatalog.cs').read_text()
        cls.extra_catalog = (ROOT / 'Assets/Scripts/Combat/SourceModelUnconnectedSkillCatalog.cs').read_text()
        cls.executor = (ROOT / 'Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs').read_text()
        cls.effects = (ROOT / 'Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelCompositeEffects.cs').read_text()

    def test_all_ten_are_unique_and_have_source_cost_and_modelled_components(self):
        packets = self.batch['activePackets']
        self.assertEqual(10, len(packets))
        self.assertEqual(10, len({p['catalogId'] for p in packets}))
        self.assertEqual(10, len({p['rawcode'] for p in packets}))
        for row in packets:
            with self.subTest(unit=row['catalogId']):
                unit = row['catalogId']
                self.assertEqual(row['rawcode'], self.status[unit]['originalRawcode'])
                skill = next(s for s in self.profiles[unit]['skills']
                             if s['name'] == row['skillName'])
                self.assertEqual('activeHealth' if row['resource'] == 'health' else
                                 'activeMana', skill['trigger'])
                self.assertEqual(row['cost'], skill['healthCost' if row['resource'] == 'health'
                                                           else 'manaCost'])
                self.assertEqual('area', skill['target'])
                self.assertGreater(skill['radius'], 0)
                self.assertGreaterEqual(len(skill['components']), 2)
                maximum = self.objects[row['rawcode']]['originalObjectFields']['balance']
                self.assertEqual(row['cost'], float(maximum['HP' if row['resource'] == 'health'
                                                       else 'manaN']))
                self.assertIn('case "' + unit + ':skill:', self.basic_catalog)
                source = self.basic_catalog if unit in {'I-04', 'H-04', 'TR-10', 'L-25'} else self.extra_catalog
                self.assertIn('"' + row['skillName'] + '"', source)

    def test_shared_statuses_damage_and_defense_are_executed_not_just_listed(self):
        for token in ('ApplySourceModelCompositeStatus',
                      'ApplySourceModelCompositeArmorBreak',
                      'SourceModelCompositeRawDamage',
                      'SourceModelCompositeMagicFactor',
                      'ResolvePracticeArmorReduction',
                      'ArmorBreakStacksOf(target.Id)'):
            self.assertIn(token, self.executor)
        for token in ('CommonSkillStatusEffects.TryRead',
                      'CombatEffectOrigin.TriggeredSkill',
                      'CombatEffectOrigin.Aura',
                      'targetKindMultiplier',
                      'requiredArmorBreakStacks',
                      'IsStoryDeployed',
                      'MagicDamageAmplification',
                      'MagicResistanceReduction'):
            self.assertIn(token, self.effects)
        movement = (ROOT / 'Assets/Scripts/Combat/SourceModelUnitEffects.cs').read_text()
        self.assertIn('practice-tooltip:', movement)
        practice = (ROOT / 'Assets/Scripts/Combat/SourceModelPracticeBattle.cs').read_text()
        self.assertIn('ResolvePracticeMovementSlow', practice)

    def test_existing_auto_active_priority_and_exclusive_caster_gate_are_preserved(self):
        auto = (ROOT / 'Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelResources.cs').read_text()
        self.assertIn('foreach (string approvedName in activeNames)', auto)
        self.assertIn('if (actorCast) break;', auto)
        self.assertIn('new[] { "요르 포저", "로이드 포저" }', self.extra_catalog)
        self.assertIn('if (caster.Profile.UnitId != caster.Catalog.Id ||', self.executor)
        self.assertIn('CanCastApprovedActive(', self.executor)

    def test_effect_omissions_are_explicitly_documented(self):
        self.assertIn('until target dies', self.batch['mobileEffectPolicies']['missingStatusDuration'])
        self.assertTrue(any('oneTimeConditionBypass' in x for x in self.batch['deferred']))
        self.assertIn('no invented numeric duration',
                      self.batch['mobileEffectPolicies']['missingStatusDuration'])


if __name__ == '__main__':
    unittest.main()
