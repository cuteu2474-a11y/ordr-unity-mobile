"""Eighth grouped tooltip model: owned skills, independent timelines, cleanup."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COMBAT = ROOT / 'Assets/Scripts/Combat'
PROFILES = {row['unitId']: row for row in json.loads(
    (ROOT / 'data/character-skill-profiles.json').read_text())['profiles']}
CATALOG = (COMBAT / 'SourceModelNextThirtySkillCatalog.cs').read_text()
UNCONNECTED = (COMBAT / 'SourceModelUnconnectedSkillCatalog.cs').read_text()
RUNTIME = (COMBAT / 'CharacterCombatRuntime.cs').read_text()
ATTACKS = (COMBAT / 'CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs').read_text()
EIGHTH = (COMBAT / 'CharacterCombatRuntime.SourceModelEighthSkills.cs').read_text()
EFFECTS = (COMBAT / 'CharacterCombatRuntime.SourceModelEffects.cs').read_text()
TEMPORARY = (COMBAT / 'CharacterCombatRuntime.SourceModelTemporarySkills.cs').read_text()
PRACTICE = (COMBAT / 'SourceModelPracticeBattle.cs').read_text()
RESOURCES = (COMBAT / 'CharacterCombatRuntime.SourceModelResources.cs').read_text()

EXPECTED = {
    'TR-20': [(1, '사자의 노래 Lv.2'), (3, '삼도류 - 극 호랑이 사냥')],
    'TR-18': [(2, '마환인'), (4, '미환인'), (5, '무시빙아')],
    'D-01': [(1, '13타 고정피해')],
    'I-07': [(1, '마법 타격')],
    'I-03': [(3, '범위 마법')],
    'H-20': [(1, '눈보라')],
    'H-14': [(1, '흉포한 검술')],
    'TR-24': [(4, '군함 백')],
    'TR-13': [(0, '카무사리'), (1, '패기 접근 효과')],
    'L-17': [(1, '패왕색 패기 접근 발동')],
    'MY-08': [(0, '독심술')],
    'LI-07': [(2, '대상 접근 시 낙뢰')],
    'E-11': [(2, '곤 인페르노')],
}


class SourceModelEighthSkillsTests(unittest.TestCase):
    def test_seventeen_unique_profiles_and_no_source_profile_edits(self):
        skills = {(owner, index, name)
                  for owner, pairs in EXPECTED.items() for index, name in pairs}
        self.assertEqual(17, len(skills))
        for owner, index, name in skills:
            self.assertEqual(PROFILES[owner]['skills'][index]['name'], name)
        self.assertEqual(250000, PROFILES['TR-13']['skills'][1]['chained'][1]
                         ['additionalAreaMagicalAmount'])
        self.assertEqual(14.25, PROFILES['TR-13']['skills'][0]['intervalSeconds'])

    def test_all_ten_new_attack_approvals_have_owner_and_exact_shape(self):
        for name in ['마환인', '미환인', '무시빙아']:
            self.assertIn(f'"{name}"', CATALOG)
        for name in ['사자의 노래 Lv.2', '삼도류 - 극 호랑이 사냥',
                     '13타 고정피해', '마법 타격', '범위 마법',
                     '눈보라', '흉포한 검술']:
            self.assertIn(f'"{name}"', UNCONNECTED)
        self.assertIn('CanModelEighthCompositeComponents(skill)', CATALOG)
        self.assertIn('ValidateExplicitCompositeShape(skill, fields)', CATALOG)
        self.assertIn('SourceModelEighthAttackCondition(actor, skill)', ATTACKS)
        self.assertIn('SourceModelEighthCommitAttackCondition(actor, skill)', ATTACKS)
        self.assertIn('SourceModelEighthRawDamageAdjustment', ATTACKS)
        self.assertIn('SourceModelEighthProcChance', ATTACKS)
        self.assertIn('sourceModelStrengthProvider', EIGHTH)

    def test_first_attack_transform_and_13th_attack_are_lifetime_scoped(self):
        self.assertIn('sourceModelYamatoOpeningReady.Add(id)', TEMPORARY)
        self.assertIn('PracticeFormOf(actor.Id) == "beast"', EIGHTH)
        self.assertIn('sourceModelYamatoOpeningReady.Remove(actor.Id)', EIGHTH)
        self.assertIn('"everyAttacks"', CATALOG)
        self.assertIn('sourceModelMariaAttackCount[actor.Id] = current', EIGHTH)
        self.assertIn('sourceModelMariaAttackCount.Remove(', RUNTIME)
        self.assertIn('ForgetSourceModelEighthActor', RUNTIME)
        self.assertIn('ForgetSourceModelEighthEnemy', RUNTIME)

    def test_proximity_and_periodic_use_shared_lane_and_hit_engine(self):
        self.assertIn('SourceModelTimeAdvancing += AdvanceSourceModelEighthTimeline', EFFECTS)
        for ident in ['MY-08:skill:0', 'TR-13:skill:1',
                      'LI-07:skill:2', 'L-17:skill:1']:
            self.assertIn(f'"{ident}"', EIGHTH)
        self.assertIn('CanAttackInThisLane(actor, enemy)', EIGHTH)
        self.assertIn('sourceModelEighthApproachRolled.Add(key)', EIGHTH)
        self.assertIn('sourceModelEighthHakiClaims[targetId] = claimant', EIGHTH)
        self.assertIn('ApplySourceModelNextThirtySkill(actor, enemy, skill, targets)', EIGHTH)
        self.assertIn('SourceModelEighthPeriodicValid', EIGHTH)
        self.assertIn('skill.PeriodSeconds != 1.5', EIGHTH)
        self.assertIn('"E-11:skill:2"', EIGHTH)

    def test_koby_five_wave_stacking_and_shanks_shared_cadence(self):
        self.assertIn('combat.OnSourceModelPracticeRoundStarted(wave)', PRACTICE)
        self.assertIn('wave % 5 != 0', EIGHTH)
        self.assertIn('previous + 1', EIGHTH)
        self.assertIn('CombatEffectType.AttackSpeedBuff', EIGHTH)
        self.assertIn('next * skill.Components[0].Number("percentPerLevel").Value', EIGHTH)
        self.assertIn('TryCastSourceModelEighthKamusari', RUNTIME)
        self.assertIn('sourceModelEighthPeriodicDue[key] = Time + skill.PeriodSeconds.Value', EIGHTH)
        self.assertIn('Resources.Spend(0, skill.Number("manaCost").Value)', EIGHTH)
        self.assertIn('"TR-13"', RESOURCES)
        self.assertIn('"H08Z"', RESOURCES)


if __name__ == '__main__':
    unittest.main()
