"""Source-backed approval and shared execution paths for composite batch two."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


class SecondElevenCompositeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = json.loads(read('data/source-model-second-11-composite-skills.json'))
        cls.profiles = {p['unitId']: p for p in
                        json.loads(read('data/character-skill-profiles.json'))['profiles']}
        cls.roster = {p['catalogId']: p for p in
                      json.loads(read('data/unit-runtime-status.json'))['entries']}
        cls.skills = read('Assets/Scripts/Combat/SourceModelNextThirtySkillCatalog.cs')
        cls.extra = read('Assets/Scripts/Combat/SourceModelUnconnectedSkillCatalog.cs')
        cls.attack = read('Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelAttackSkills.cs')
        cls.runtime = read('Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs')
        cls.effects = read('Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelCompositeEffects.cs')

    def test_eleven_names_match_source_and_identity_allow_list(self):
        rows = self.batch['skills']
        self.assertEqual(11, len(rows))
        self.assertEqual(11, len({(r['unitId'], r['name']) for r in rows}))
        self.assertEqual(8, sum(r['trigger'].startswith('active') for r in rows))
        self.assertEqual(3, sum(r['trigger'] == 'onAttack' for r in rows))
        for row in rows:
            with self.subTest(skill=row['name']):
                self.assertEqual(row['rawcode'],
                                 self.roster[row['unitId']]['originalRawcode'])
                skill = next(x for x in self.profiles[row['unitId']]['skills']
                             if x['name'] == row['name'])
                self.assertEqual(row['trigger'], skill['trigger'])
                self.assertIn('case "' + row['unitId'] + ':skill:', self.skills)
                if row['trigger'].startswith('active'):
                    key = 'healthCost' if row['trigger'] == 'activeHealth' else 'manaCost'
                    self.assertEqual(row['cost'], skill[key])
                else:
                    self.assertEqual(row['chancePercent'], skill['chancePercent'])

    def test_new_skill_packets_are_approved_but_sentinel_remains_native_only(self):
        for row in self.batch['skills']:
            if row['unitId'] == 'RN-04':
                self.assertNotIn('new SourceModelUnconnectedSkillRow("RN-04"', self.extra)
                self.assertIn('script.CatalogId == "RN-04"', self.attack)
                self.assertIn('ApplySourceModelSentinelMagicAmplification', self.attack)
            else:
                self.assertIn('"' + row['name'] + '"', self.extra)
        self.assertIn('CanModelSecondCompositeComponents(skill)', self.skills)
        self.assertIn('IsSecondCompositeSkill(skill)', self.runtime)

    def test_shared_hp_status_defense_damage_and_ally_power_execution(self):
        for token in ('ApplySourceModelCompositeStatus',
                      'ApplySourceModelCompositeArmorBreak',
                      'ApplySourceModelCompositeAllyPower',
                      'ApplySourceModelSecondMissingHealthPhysical',
                      'singleTargetMagicalDamageMultiplier',
                      'plusLegacyLevelDamage',
                      'selfHeal'):
            self.assertIn(token, self.runtime)
        for token in ('CombatEffectType.ExplosiveDamageAmplification',
                      'CombatEffectType.MovementSlowPercent',
                      'requiredArmorBreakStacks',
                      'SourceModelCompositeRawDamage',
                      'practice-tooltip:allypower:'):
            self.assertIn(token, self.effects)
        rules = read('Assets/Scripts/Combat/SourceModelBasicCombatRules.cs')
        self.assertIn('ResolvePracticeAttackPower', rules)
        self.assertIn('practice-tooltip:allypower:', rules)
        mobile = read('Assets/Scripts/Mobile/SpecialGradeMobilePolicy.cs')
        self.assertIn('unitId == "LI-02"', mobile)

    def test_no_unsupported_parity_or_hidden_duration_assumption(self):
        policies = self.batch['mobilePolicies']
        self.assertIn('No invented duration', policies['durationAbsent'])
        self.assertIn('deferred', policies['explosive'])
        self.assertIn('logical outcome approximation', policies['parity'])


if __name__ == '__main__':
    unittest.main()
