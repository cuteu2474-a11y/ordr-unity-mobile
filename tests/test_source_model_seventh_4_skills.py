"""Seven-batch exact skill approval, original resource facts and callback lifetimes."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COMBAT = ROOT / 'Assets/Scripts/Combat'
CATALOG = (COMBAT / 'SourceModelNextThirtySkillCatalog.cs').read_text()
UNCONNECTED = (COMBAT / 'SourceModelUnconnectedSkillCatalog.cs').read_text()
ATTACKS = (COMBAT / 'CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs').read_text()
SEVENTH = (COMBAT / 'CharacterCombatRuntime.SourceModelSeventhSkills.cs').read_text()
TEMPORARY = (COMBAT / 'CharacterCombatRuntime.SourceModelTemporarySkills.cs').read_text()
DISPATCH = (COMBAT / 'CharacterCombatRuntime.SourceModelAttackSkills.cs').read_text()
SIXTH = (COMBAT / 'CharacterCombatRuntime.SourceModelSixthSkills.cs').read_text()
RESOURCES = (COMBAT / 'CharacterCombatRuntime.SourceModelResources.cs').read_text()
RUNTIME = (COMBAT / 'CharacterCombatRuntime.cs').read_text()
PROFILES = {row['unitId']: row for row in json.loads(
    (ROOT / 'data/character-skill-profiles.json').read_text())['profiles']}
OBJECTS = {row['rawcode']: row for row in json.loads(
    (ROOT / 'data/original-2323C-unit-object-stats.json').read_text())['entries']}

EXPECTED = {
    'TR-11': (4, '화염용왕', 'attackWithPeriodicCooldown', 'H092'),
    'TR-16': (0, '아이스 에이지', 'onAttack', 'H097'),
    'TR-20': (0, '1080 번뇌봉', 'activeMana', 'H09F'),
    'TR-24': (2, '콤비네이션 어택', 'onAttack', 'H0CO'),
}


class SourceModelSeventhBatchTests(unittest.TestCase):
    def test_four_exact_owned_approved_skills_without_new_rawcodes(self):
        for owner, (index, name, trigger, rawcode) in EXPECTED.items():
            profile = PROFILES[owner]['skills'][index]
            self.assertEqual((name, trigger), (profile['name'], profile['trigger']))
            self.assertIn(f'case "{owner}:skill:{index}"', CATALOG)
            self.assertIn(f'skill.Name == "{name}"', CATALOG)
            self.assertIn(f'"{owner}", "{rawcode}"', UNCONNECTED)
            self.assertIn(f'"{name}"', UNCONNECTED)
            self.assertIn(rawcode, OBJECTS)
        self.assertEqual(3, sum(p[2] != 'activeMana' for p in EXPECTED.values()))
        self.assertIn('CanModelSeventhCompositeComponents', CATALOG)
        self.assertIn('ValidateExplicitCompositeShape(skill, fields)', CATALOG)

    def test_seventh_exact_values_are_pinned_to_the_imported_profiles(self):
        fire = PROFILES['TR-11']['skills'][4]
        self.assertEqual(12.5, fire['intervalSeconds'])
        self.assertEqual(15000, fire['strengthScalingPerPoint'])
        self.assertEqual([3250000, 1000000, 4250000],
                         [v['amount'] for v in fire['components'][:3]])
        ice = PROFILES['TR-16']['skills'][0]
        self.assertEqual((12.5, 50, 40, .5),
                         (ice['chancePercent'], ice['manaAtLeast'],
                          ice['manaCost'], ice['castTimeSeconds']))
        self.assertEqual(85, int(OBJECTS['H097']['originalObjectFields']['balance']['manaN']))
        zoro = PROFILES['TR-20']['skills'][0]
        self.assertEqual(145, zoro['manaCost'])
        self.assertEqual(145, int(OBJECTS['H09F']['originalObjectFields']['balance']['manaN']))
        self.assertEqual((120000, .1, 1.15),
                         (zoro['components'][1]['perTick'],
                          zoro['components'][1]['tickIntervalSeconds'],
                          zoro['components'][1]['totalDurationSeconds']))
        koby = PROFILES['TR-24']['skills'][2]['components'][0]
        self.assertEqual((5, 60, 400, 60000),
                         (koby['durationSeconds'], koby['basicAttackExplosivePercent'],
                          koby['explosiveRadius'], koby['singleMagicalDamagePerHit']))

    def test_mana_cooldown_and_delayed_damage_all_use_shared_ledger(self):
        self.assertIn('sourceModelSeventhReadyAt.TryGetValue', ATTACKS)
        self.assertIn('sourceModelSeventhReadyAt[actor.Id]', ATTACKS)
        self.assertEqual(2, RUNTIME.count('sourceModelSeventhReadyAt.Remove('))
        self.assertIn('actor.Abilities.Resources.Spend(0, skill.Number("manaCost").Value)', ATTACKS)
        self.assertIn('"TR-16:skill:0"', RESOURCES)
        self.assertIn('ScheduleSourceModelIceAge', SEVENTH)
        self.assertIn('CanFinishSourceModelDelayedAttack', SEVENTH)
        self.assertIn('SourceModelLaneRevision', SEVENTH)
        self.assertIn('ScheduleSourceModelZoroDot', SEVENTH)
        self.assertIn('!ReferenceEquals(victim, current)', SEVENTH)
        self.assertIn('ApplySourceModelSeventhTimedHit', SEVENTH)
        self.assertIn('SourceModelUnitEffects.ResolvePracticeAttackPower', SEVENTH)

    def test_clone_is_finite_combat_only_and_never_fusion_material(self):
        self.assertIn('SpawnSourceModelKobyClone', ATTACKS)
        self.assertIn('SpawnSourceTemporaryActor(actor, skill.Name', SEVENTH)
        self.assertIn('PracticeSummonsOf(actor.Id)', SEVENTH)
        self.assertIn('AttackSpec.BasicAttack.IntervalSeconds', SEVENTH)
        self.assertIn('basicAttackExplosivePercent', SEVENTH)
        self.assertIn('singleMagicalDamagePerHit', SEVENTH)
        # Source-backed summons and source-unmapped combat-only clones are
        # distinct runtime paths. Koby's timed hits remain combat-capable
        # without registering a nonexistent native object attack.
        self.assertIn('bool modeledCombat = false', TEMPORARY)
        self.assertIn('primary.X, primary.Y, true);', SEVENTH)
        self.assertRegex(
            TEMPORARY,
            r'bool\s+autonomous\s*=\s*modeledCombat\s*\|\|\s*\(nativeAutonomous')
        self.assertIn('sourceModelRules.SourceObjectAttack(sourceRawcode) != null;', TEMPORARY)
        self.assertIn('sourceModelRules.SourceObjectAttack(sourceRawcode) != null);', TEMPORARY)
        self.assertRegex(
            TEMPORARY,
            r'if\s*\(autonomous\s*&&\s*sourceRawcode\s*!=\s*null\)\s*'
            r'RegisterSourceIndependentAttack\(SummonAttackKey\(summon.Id\)')
        # The clone stays out of the fusion roster even though its scheduled
        # temporary combat hits can be reported as independent attacks.
        self.assertIn('if (!exists) return;', SEVENTH)
        self.assertIn('CanFinishSourceModelDelayedAttack(actor.Id, lane, revision)', SEVENTH)

    def test_standalone_transform_and_clone_do_not_hide_prior_sixth_skills(self):
        self.assertIn('ExecuteSourceTemporaryAttack(actor, primary)', DISPATCH)
        self.assertIn('ExecuteSourceModelUnconnectedProfileAttacks(actor, primary, unconnected)',
                      DISPATCH)
        self.assertIn('ExecuteSourceModelNextThirtyProfileAttacks(actor, primary, nextThirty)',
                      DISPATCH)
        self.assertIn('PracticeFormOf(actor.Id) == "blackMamba"', SIXTH)
        self.assertIn('return 70', SIXTH)
        self.assertIn('raw *= 1.5', SIXTH)


if __name__ == '__main__':
    unittest.main()
