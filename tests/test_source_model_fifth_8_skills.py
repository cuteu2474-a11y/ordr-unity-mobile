"""Fifth skill group: exact owner/identity, strict triggers and reusable effects."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def data(name):
    return json.loads((ROOT / 'data' / name).read_text(encoding='utf-8'))


def source(name):
    return (ROOT / 'Assets' / 'Scripts' / 'Combat' / name).read_text(encoding='utf-8')


class FifthSkillTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = data('source-model-fifth-8-composite-skills.json')
        cls.profiles = {r['unitId']: r for r in data('character-skill-profiles.json')['profiles']}
        cls.approval = source('SourceModelNextThirtySkillCatalog.cs')
        cls.fifth = source('CharacterCombatRuntime.SourceModelFifthSkills.cs')

    def test_eight_exact_skills_and_passive_are_present(self):
        rows = self.batch['skills']
        self.assertEqual(8, len(rows))
        self.assertEqual(8, len({r['skillId'] for r in rows}))
        self.assertEqual(7, self.batch['newOnAttackPackets'])
        self.assertEqual(1, self.batch['newAutomaticActivePackets'])
        for row in rows:
            with self.subTest(name=row['name']):
                profile = self.profiles[row['unitId']]
                index = int(row['skillId'].split(':')[-1])
                skill = profile['skills'][index]
                self.assertEqual(row['name'], skill['name'])
                self.assertEqual(row['components'], [c['type'] for c in skill['components']])
                self.assertIn('case "' + row['skillId'] + '"', self.approval)
                if 'chancePercent' in row:
                    self.assertEqual(row['chancePercent'], skill['chancePercent'])
                if 'manaCost' in row:
                    self.assertEqual(row['manaCost'], skill['manaCost'])
        self.assertEqual('onSkillActivated', self.profiles['E-07']['skills'][1]['trigger'])

    def test_route_and_owner_lifecycle_share_common_runtime(self):
        for symbol in ('ConfigureSourceModelRouteCorners', 'SourceModelRankyakuTargets',
                       'CanAttackInThisLane', 'SourceModelAceDelayedUltimate',
                       'CanFinishSourceModelDelayedAttack', 'SourceModelRobinMultishotTargets',
                       'SourceModelAceFlameOnSkill'):
            self.assertIn(symbol, self.fifth)
        runtime = source('CharacterCombatRuntime.cs')
        self.assertIn('RemoveSourceModelAceFlames(instanceId)', runtime)
        self.assertIn('RemoveSourceModelAceFlames(id)', runtime)
        self.assertIn('sourceModelRouteCorners', self.fifth)
        self.assertIn('ConfigureSourceModelRouteCorners(catalog.RouteCorners)',
                      source('SourceModelPracticeBattle.cs'))

    def test_no_guessed_agility_and_clear_count(self):
        self.assertIn('sourceModelAccumulatedClearCount?.Invoke()', self.fifth)
        self.assertIn('Math.Min(300', self.fifth)
        self.assertIn('ConfigureSourceModelHeroAgilityProvider',
                      source('CharacterCombatRuntime.SourceModelCompositeEffects.cs'))
        self.assertIn('actor.Abilities.Resources.Spend(0, skill.Number("manaCost").Value)',
                      source('CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs'))
        self.assertEqual(5.25, self.profiles['TR-07']['skills'][3]['chancePercent'])


if __name__ == '__main__':
    unittest.main()
