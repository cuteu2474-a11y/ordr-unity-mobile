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
        self.assertEqual(18, len(self.audit['recipes']))
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
                    item = self.objects[material['rawcode']]
                    if material['rawcode'] == 'h060':
                        self.assertEqual('PS-01', item['catalogId'])
                        self.assertEqual('해적선', item['grade'])
                    elif row['abilityRawcode'] == 'YY04' and material['rawcode'] == 'h035':
                        self.assertEqual(('L-17', '전설'), (item['catalogId'], item['grade']))
                    else:
                        self.assertIn(item['grade'],
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

    def test_five_pirate_ship_recipes_have_only_original_chat_bi2_side_effects(self):
        expected = {
            'YY04': ('BsB', 'BsC', 'H-02', '레드포스호'),
            'YY07': ('Brw', 'Brx', 'H-06', '모비딕호'),
            'YY08': ('BqW', 'BqX', 'H-07', '반더데켄'),
            'YY09': ('Brz', 'Br0', 'H-08', '발라티에'),
            'YY12': ('Brs', 'Brt', 'H-15', '써니호'),
        }
        rows = {x['abilityRawcode']: x for x in self.audit['recipes']}
        self.assertTrue(set(expected).issubset(rows))
        functions = {k: x['source'] for k, x in self.audit['functions'].items()}
        for ability, (wrapper, handler, catalog_id, name) in expected.items():
            with self.subTest(ability=ability):
                row = rows[ability]
                self.assertEqual((wrapper, handler), (row['chatWrapper'], row['chatHandler']))
                self.assertEqual((catalog_id, name), (row['resultCatalogId'], row['resultName']))
                self.assertEqual(1, len([x for x in row['conditions']
                                         if x['kind'] == 'UNIT' and x['rawcode'] == 'h060']))
                self.assertFalse(any(x['kind'] not in ('UNIT', 'UPUN')
                                     for x in row['conditions']))
                self.assertEqual((wrapper, handler),
                                 require_simple_chat_entry(ability, functions))
                self.assertNotIn(ability, self.audit['excludedHiddenAbilities'])

    def test_runtime_allowlist_matches_audited_recipes_and_excludes_unhandled_paths(self):
        source = (ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs').read_text()
        block = source.split('string[,] ReviewedHidden = {', 1)[1].split('};', 1)[0]
        actual = re.findall(r'\{ "(YY\d{2})", "(\w{4})", "(H-\d+)" \}', block)
        expected = [(r['abilityRawcode'], r['results'][0]['rawcode'], r['resultCatalogId'])
                    for r in self.audit['recipes']]
        additional = json.loads((ROOT / 'data/original-2323C-readonly-creation-fusions.json').read_text())
        # YY13 was deliberately excluded by the earlier no-CH8 audit; it is
        # now separately authorized after proving CH8's Bjp path read-only.
        expected.append(('YY13', 'h055', additional['reviewed']['YY13']['catalogId']))
        self.assertEqual(expected, actual)
        self.assertTrue((set(self.audit['excludedHiddenAbilities']) - {'YY13'})
                        .isdisjoint(r[0] for r in actual))
        self.assertIn('YY15', self.audit['excludedHiddenAbilities'])


if __name__ == '__main__':
    unittest.main()
