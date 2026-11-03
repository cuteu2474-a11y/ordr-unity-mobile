"""Sixth source-tooltip practice batch: exact ownership and no native duplication."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COMBAT = ROOT / 'Assets/Scripts/Combat'
CATALOG = (COMBAT / 'SourceModelNextThirtySkillCatalog.cs').read_text()
UNCONNECTED = (COMBAT / 'SourceModelUnconnectedSkillCatalog.cs').read_text()
RUNTIME = (COMBAT / 'CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs').read_text()
SIXTH = (COMBAT / 'CharacterCombatRuntime.SourceModelSixthSkills.cs').read_text()
PROFILES = {p['unitId']: p for p in json.loads(
    (ROOT / 'data/character-skill-profiles.json').read_text())['profiles']}
STATUS = {p['catalogId']: p for p in json.loads(
    (ROOT / 'data/unit-runtime-status.json').read_text())['entries']}


EXPECTED = {
    'TR-09': {0: '체력 소모 연타', 3: '평타 광역 추가',
              4: '크리티컬', 5: '단일 스턴 타격'},
    'TR-10': {1: '범위 마법타격'},
    'TR-11': {1: '불꽃 주먹', 2: '넓은 주먹'},
    'TR-12': {1: '검은다리', 3: '무통 샷'},
    'TR-13': {2: '원형 마법 스턴', 3: '자신중심 마법 스턴'},
    'TR-14': {2: '무장색 패기', 3: '컬버린'},
    'TR-15': {1: '고대병기 포세이돈'},
    'TR-19': {1: '저격', 3: '치명타'},
    'TR-24': {3: '기본 공격 특수 피해'},
    'E-12': {3: '혼합 공격', 5: '혼합 스턴 타격'},
    'I-03': {1: '기본 공격 광역 추가'},
}


class SourceModelSixthBatchTests(unittest.TestCase):
    def test_exact_twenty_profile_skills(self):
        self.assertEqual(20, sum(map(len, EXPECTED.values())))
        active = 0
        for unit, skills in EXPECTED.items():
            self.assertIn(f'"{unit}"', CATALOG + UNCONNECTED)
            self.assertIn(unit, STATUS)
            for index, name in skills.items():
                profile = PROFILES[unit]['skills'][index]
                self.assertEqual(name, profile['name'])
                self.assertIn(f'case "{unit}:skill:{index}"', CATALOG)
                self.assertIn(f'skill.Name == "{name}"', CATALOG)
                active += profile['trigger'].startswith('active')
        self.assertEqual(1, active)

    def test_preexisting_native_proc_rolls_are_not_repeated(self):
        for unit, index in [('TR-11', 3), ('TR-13', 4), ('TR-14', 4),
                            ('TR-15', 3), ('TR-22', 3)]:
            self.assertNotIn(index, EXPECTED.get(unit, {}))
        self.assertIn('skill.Id == "TR-12:skill:1" && component.Type == "stun"',
                      RUNTIME)
        self.assertIn('skill.Id == "TR-24:skill:3" && component.Type == "stun"',
                      RUNTIME)

    def test_four_core_lifecycles_are_attached(self):
        self.assertIn('CanModelSixthCompositeComponents', CATALOG)
        self.assertIn('SourceModelSixthProcChance(skill, primary)', RUNTIME)
        self.assertIn('SourceModelSixthRawDamageAdjustment', RUNTIME)
        self.assertIn('SourceModelSixthHitCount', RUNTIME)
        self.assertIn('ScheduleSourceModelPoseidon', SIXTH)
        self.assertIn('CanFinishSourceModelDelayedAttack', SIXTH)
        self.assertIn('SourceModelLaneRevision', SIXTH)
        self.assertIn('SourceModelActiveBuffCount', SIXTH)
        self.assertIn('additionalCast = false', RUNTIME)

    def test_same_attack_combo_and_profile_specific_overrides(self):
        self.assertIn('activatedThisAttack', RUNTIME)
        self.assertIn('SourceModelKindInOverride(skill, primary)', SIXTH)
        self.assertIn('sourceModelPoseidonReadyAt', SIXTH)
        self.assertIn('"TR-09:skill:5"', CATALOG)
        self.assertIn('"TR-19:skill:1"', CATALOG)
        self.assertIn('"TR-15:skill:1"', CATALOG)


if __name__ == '__main__':
    unittest.main()
