import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'data'


def read(name):
    return json.loads((DATA / name).read_text())


class UnitRuntimeStatusTests(unittest.TestCase):
    def setUp(self):
        self.status = read('unit-runtime-status.json')
        self.rows = {r['catalogId']: r for r in self.status['entries']}

    def test_covers_every_source_catalog_identity_without_promoting_verification(self):
        source = read('original-2323C-map-base-stats.json')
        original = {r['catalogId']: r for r in source['catalogEntries']}
        self.assertEqual(277, len(self.rows))
        self.assertEqual(set(original), set(self.rows))
        self.assertEqual(source['sourceW3xSha256'], self.status['sourceW3xSha256'])
        self.assertFalse(self.status['allUnitsComplete'])
        for key, row in self.rows.items():
            self.assertEqual(original[key]['originalRawcode'], row['originalRawcode'])
            self.assertFalse(row['fullUnitImplementationVerified'])
            self.assertTrue(row['remainingWork'])

    def test_attack_support_requires_a_mapped_source_attack_object(self):
        source = read('original-2323C-map-base-stats.json')
        for row in source['catalogEntries']:
            self.assertEqual(bool(row.get('originalRawcode') and row.get('attack1')),
                             self.rows[row['catalogId']]['objectBasicAttackModel'])
        self.assertFalse(self.rows['E-02']['objectBasicAttackModel'])
        self.assertFalse(self.rows['X-05']['objectBasicAttackModel'])

    def test_tooltip_inventory_stays_distinct_from_connected_native_effects(self):
        profiles = read('character-skill-profiles.json')['profiles']
        self.assertEqual(sum(len(p.get('skills', [])) for p in profiles),
                         self.status['summary']['documentedSkillEntries'])
        self.assertEqual(sum(len(p.get('passives', [])) for p in profiles),
                         self.status['summary']['documentedPassiveEntries'])
        for passive in read('original-2323C-native-pure-passives.json')['entries']:
            effects = self.rows[passive['catalogId']]['connectedModelEffects']
            self.assertTrue(any('객체 확률' in effect for effect in effects))
        for passive in read('original-2323C-native-pure-passives.json')['practiceFirstLevelEntries']:
            effects = self.rows[passive['catalogId']]['connectedModelEffects']
            self.assertTrue(any('연습 1단계' in effect for effect in effects))
        for passive in read('original-2323C-native-pure-passives.json')['practiceBonusEntries']:
            effects = self.rows[passive['catalogId']]['connectedModelEffects']
            self.assertTrue(any('객체 추가 피해' in effect for effect in effects))
        self.assertEqual(194, sum(bool(r['connectedModelEffects']) for r in self.rows.values()))
        self.assertEqual(269, sum(len(r['connectedModelEffects']) for r in self.rows.values()))
        attack_audit = read('original-2323C-attack-skill-batch.json')
        self.assertEqual(20, sum(any('공격 선언 기술 · ' in effect for effect in
            self.rows[row['catalogId']]['connectedModelEffects'])
            for row in attack_audit['units']))
        next_audit = read('original-2323C-next-20-native-source-audit.json')
        source_callbacks = [r for r in next_audit['units'] if r['w5AttackCallbacksRegistered']]
        self.assertEqual(6, len(source_callbacks))
        for row in source_callbacks:
            effects = self.rows[row['catalogId']]['connectedModelEffects']
            self.assertTrue(any('공격 선언 기술 · ' in e for e in effects), row['catalogId'])
        self.assertTrue(any('A08B 언홀리' in e for e in self.rows['S-05']['connectedModelEffects']))
        self.assertTrue(any('A087 블랙' in e for e in self.rows['S-09']['connectedModelEffects']))
        self.assertTrue(any('A0EG 리퀴드' in e for e in self.rows['S-20']['connectedModelEffects']))
        self.assertTrue(any('A0EA 피닉스' in e for e in self.rows['S-28']['connectedModelEffects']))
        self.assertEqual(0, self.status['summary']['fullyVerifiedUnits'])

    def test_inherited_mode_is_pinned_to_base_source_and_model_only(self):
        inherited = read('practice-native-inherited-defaults.json')
        self.assertEqual('ddc360800784f808219bbc2518b16b0f12413082aada2755cc00944adcc31cec',
                         inherited['sourceBaseAbilityDataSha256'])
        self.assertEqual(self.status['sourceW3xSha256'], inherited['sourceW3xSha256'])
        self.assertEqual(0, inherited['AHadFlatArmorMode'])
        self.assertFalse(inherited['safeForRetailVerifiedRuntime'])

    def test_audit_rejects_incomplete_or_duplicate_runtime_exports(self):
        spec = importlib.util.spec_from_file_location('unit_audit', ROOT / 'tools/build_unit_runtime_status.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as tmp:
            export = Path(tmp) / 'runtime.tsv'
            for text in ('C-01\t1\t\n', 'C-01\t1\t\nC-01\t1\t\n', 'C-01\ttrue\t\n'):
                export.write_text(text)
                with self.assertRaises(ValueError):
                    module.build(DATA, export)


if __name__ == '__main__':
    unittest.main()

