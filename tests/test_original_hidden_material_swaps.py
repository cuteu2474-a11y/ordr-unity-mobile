import hashlib
import json
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from audit_original_hidden_fusions import require_simple_chat_entry


class OriginalHiddenMaterialSwapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((ROOT / 'data/original-2323C-hidden-material-swaps.json').read_text())
        cls.registry = {r['abilityRawcode']: r for r in json.loads(
            (ROOT / 'data/original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (ROOT / 'data/original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}

    def test_hidden_transactions_preserve_material_order_identity_and_zero_extra_cost(self):
        self.assertEqual(13, len(self.audit['recipes']))
        for row in self.audit['recipes']:
            source = self.registry[row['abilityRawcode']]
            self.assertCountEqual(source['conditions'], row['conditions'])
            self.assertEqual([r for r in source['conditions'] if r['kind'] == 'UNIT'],
                             [r for r in row['conditions'] if r['kind'] == 'UNIT'])
            self.assertEqual(source['results'], row['results'])
            result = self.objects[row['results'][0]['rawcode']]
            self.assertEqual(row['resultCatalogId'], result['catalogId'])
            self.assertEqual('히든', result['grade'])
            for material in row['conditions']:
                self.assertIn(material['kind'], ('UNIT', 'UPUN'))
                if material['kind'] == 'UNIT':
                    self.assertIn(self.objects[material['rawcode']]['grade'],
                                  ('흔함', '안흔함', '특별함', '희귀함'))
            self.assertFalse(self.audit['retailCombatParityVerified'])

    def test_chat_entries_have_no_hidden_gameplay_mutation_or_creation_hook(self):
        functions = {k: v['source'] for k, v in self.audit['functions'].items()}
        for name, receipt in self.audit['functions'].items():
            self.assertEqual(hashlib.sha256(receipt['source'].encode()).hexdigest(), receipt['sha256LF'])
        for row in self.audit['recipes']:
            wrapper, handler = require_simple_chat_entry(row['abilityRawcode'], functions)
            self.assertEqual((row['chatWrapper'], row['chatHandler']), (wrapper, handler))
            self.assertEqual(2, len(row['commands']))
            rawcode = row['results'][0]['rawcode']
            self.assertNotIn('GetUnitTypeId(iI)==$' + rawcode.encode().hex().upper(), functions['CH8'])
        self.assertEqual({'x': -3776, 'y': 4480}, self.audit['positionPlayerZero'])

    def test_extra_state_or_different_placement_is_rejected_by_the_auditor(self):
        row = self.audit['recipes'][0]
        original = {k: v['source'] for k, v in self.audit['functions'].items()}
        for name, old, replacement in [
            (row['chatHandler'], 'call StartSound(XF)', 'set XM[JG]=false\ncall StartSound(XF)'),
            (row['chatWrapper'], 'return BI2()', 'call OtherReward()\nreturn BI2()'),
            (row['chatWrapper'], 'GetPlayerStartLocationX(GetTriggerPlayer())', 'GetUnitX(GetTriggerUnit())')
        ]:
            with self.subTest(name=name, replacement=replacement):
                changed = dict(original)
                changed[name] = changed[name].replace(old, replacement)
                with self.assertRaises(ValueError):
                    require_simple_chat_entry(row['abilityRawcode'], changed)

    def test_runtime_allowlist_matches_audited_recipes_and_excludes_unhandled_paths(self):
        source = (ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs').read_text()
        block = source.split('string[,] ReviewedHidden = {', 1)[1].split('};', 1)[0]
        actual = re.findall(r'\{ "(YY\d{2})", "(\w{4})", "(H-\d+)" \}', block)
        expected = [(r['abilityRawcode'], r['results'][0]['rawcode'], r['resultCatalogId'])
                    for r in self.audit['recipes']]
        self.assertEqual(expected, actual)
        self.assertTrue(set(self.audit['excludedHiddenAbilities']).isdisjoint(r[0] for r in actual))
        self.assertIn('YY15', self.audit['excludedHiddenAbilities'])


if __name__ == '__main__':
    unittest.main()
