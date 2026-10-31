"""Source extraction receipts must never become unverified game effects."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return json.loads((ROOT / 'data' / name).read_text(encoding='utf-8'))


class NextTwentyNativeSourceAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = read('original-2323C-next-20-native-source-audit.json')
        cls.inventory = read('original-2323C-next-20-skill-inventory.json')
        cls.rows = {r['catalogId']: r for r in cls.audit['units']}

    def test_exact_pinned_source_and_twenty_object_owners(self):
        self.assertEqual(self.inventory['sourceW3xSha256'],
                         self.audit['sourceW3xSha256'])
        self.assertEqual(self.inventory['pinnedJassSha256NotRereadInThisInventory'],
                         self.audit['sourceJassSha256'])
        self.assertFalse(self.audit['runtimeAuthorization'])
        self.assertEqual(20, len(self.rows))
        self.assertEqual(121, self.audit['summary']['objectAbilityEntries'])
        for row in self.inventory['units']:
            verified = self.rows[row['catalogId']]
            self.assertEqual(row['originalUnitRawcode'], verified['originalUnitRawcode'])
            self.assertEqual(row['objectAbilityRawcodesInMapOrder'],
                             [x['abilityRawcode'] for x in verified['abilities']])
            self.assertFalse(verified['originalFullSkillBehaviorVerified'])

    def test_fusion_buttons_are_never_promoted_to_combat_skills(self):
        counts = {}
        for row in self.rows.values():
            for ability in row['abilities']:
                category = ability['classification']
                counts[category] = counts.get(category, 0) + 1
                self.assertFalse(ability['confirmedFinalNativeEffect'])
        self.assertEqual(31, counts['CWR_FUSION_REGISTERED_COMBAT_USE_NOT_VERIFIED'])
        self.assertEqual(70, counts['SHARED_SYSTEM_OR_NATIVE_NOT_UNIT_SKILL_VERIFIED'])
        self.assertEqual(20, counts['NATIVE_ABILITY_NEEDS_TRIGGER_AND_EFFECT_REVIEW'])
        self.assertTrue(all(x['classification'].startswith('CWR_FUSION')
                            for x in self.rows['U-14']['abilities']
                            if x['abilityRawcode'] == 'A029'))

    def test_six_script_attack_callbacks_have_source_rolls_and_other_units_do_not(self):
        expected = {'S-08': ('Bn0', 10), 'S-12': ('Bn4', 12),
                    'S-15': ('BoC', 16), 'S-18': ('Bnr', 10),
                    'S-19': ('BoE', 14), 'S-22': ('BoI', 15)}
        self.assertEqual(set(expected), {id for id, row in self.rows.items()
                         if row['w5AttackCallbackEvidence']})
        for uid, (callback, chance) in expected.items():
            evidence = self.rows[uid]['w5AttackCallbackEvidence']
            self.assertEqual(1, len(evidence))
            self.assertEqual(callback, evidence[0]['function'])
            self.assertEqual(chance, evidence[0]['attackEventChancePercent'])
            self.assertTrue(evidence[0]['indirectSourceEffectsUnverified'])

    def test_law_tooltip_numbers_have_native_object_support_but_not_retail_parity(self):
        abilities = self.rows['S-05']['abilities']
        law = next(a for a in abilities if a['abilityRawcode'] == 'A08B')
        fields = law['originalSlkLevelOneFields']
        self.assertEqual('Auhf', law['warcraftNativeBaseAbility'])
        self.assertEqual('1.', fields['DataA1'])
        self.assertEqual('7.', fields['Dur1'])
        self.assertEqual('50.', fields['Cool1'])
        self.assertFalse(law['confirmedFinalNativeEffect'])


if __name__ == '__main__':
    unittest.main()
