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
                     'TrySourceModelSongSoldier',
                     'SourceModelSongSoldiers', 'ExplosiveDamageAmplification'):
            self.assertIn(name, self.damage)
        for name in ('CanModelThirdCompositeComponents',
                     'IsThirdCompositeAttack'):
            self.assertIn(name, self.approval)
        self.assertIn('SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill)',
                      self.runtime)
        # C05 consolidated expiry into the common temporary-actor lifecycle.
        # Check registration, live owner/lane counting, and retirement together;
        # resurrecting the old parallel expiry dictionary would double-count.
        native = code('CharacterCombatRuntime.SourceModelFusionNativeFollowups.cs')
        temporary = code('CharacterCombatRuntime.SourceModelTemporarySkills.cs')
        owner = code('CharacterCombatRuntime.cs')
        self.assertNotIn('sourceSongSoldierExpiry', self.damage + native + temporary + owner)
        add = self.damage.split('private void AddSourceModelUtaSongSoldier(', 1)[1].split(
            'private double SourceModelCompositeRawDamage(', 1)[0]
        self.assertIn('RegisterSourceModelUtaSongSoldier(actor, primary, lifetime,', add)
        register = native.split('private bool RegisterSourceModelUtaSongSoldier(', 1)[1].split(
            'private void RetireSourceModelUtaSongSoldierKey(', 1)[0]
        self.assertIn('SpawnSourceTemporaryActor(', register)
        self.assertIn('"h04O"', register)
        self.assertIn('virtualAttackEffect: true', register)
        self.assertNotIn('effectExecutor.Schedule', register)
        count = self.damage.split('private int SourceModelSongSoldiers(', 1)[1].split(
            'public int PracticeSongSoldierCount(', 1)[0]
        for guard in ('soldier.OwnerInstanceId == casterId',
                      'soldier.ExpiresAt.Value > Time',
                      'soldier.StoryLane == IsStoryDeployed(casterId)',
                      '!IsSharedObjectiveDeployed(casterId)',
                      '!IsOriginalPoneglyphChanneling(casterId)'):
            self.assertIn(guard, count)
        retire = temporary.split('private void EndSourceTemporaryActorAt(', 1)[1].split(
            'private bool RetireSourceTemporaryActor(', 1)[0]
        for operation in ('RetireSourceModelUtaSongSoldierKey(',
                          'UnregisterSourceIndependentAttack(',
                          'sourceTemporarySummons.RemoveAt(index)'):
            self.assertIn(operation, retire)
        self.assertIn('EndSourceTemporaryActorAt(i, validOwnerLane)', temporary)
        self.assertIn('RemoveSourceTemporaryActors(instanceId);', owner)
        self.assertIn('ForgetSourceModelFusionNativeActor(instanceId);', owner)

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

