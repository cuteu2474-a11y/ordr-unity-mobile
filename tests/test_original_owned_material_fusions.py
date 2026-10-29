import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from audit_original_owned_material_fusions import review


class OwnedMaterialFusionEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ROOT / 'data'
        cls.report = json.loads((cls.data / 'original-2323C-owned-material-fusions.json').read_text())
        cls.functions = {k: r['source'] for k, r in cls.report['functions'].items()}
        cls.registry = {r['abilityRawcode']: r for r in json.loads(
            (cls.data / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (cls.data / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}

    def test_tatsumaki_review_replays_without_changing_costs_or_native_approval(self):
        native, casters = review(self.functions, self.report['nativeFields'], self.registry, self.objects)
        self.assertEqual(native, self.report['nativeFields'])
        self.assertEqual(['RN-08'], casters)
        self.assertEqual(self.registry['A12C'], self.report['recipe'])
        self.assertFalse(self.report['combatParityVerified'])
        for row in self.report['functions'].values():
            self.assertEqual(hashlib.sha256(row['source'].encode()).hexdigest(), row['sha256LF'])

    def test_extra_effect_or_different_native_wrapper_is_rejected(self):
        for function, extra in [('CFm', 'set shared[JG]=true'),
                                 ('CFm', 'call GrantReward()'),
                                 ('CFl', 'call UnitAddAbility(GetSpellAbilityUnit(),$41313142)')]:
            functions = copy.deepcopy(self.functions)
            functions[function] = functions[function].replace('endfunction', extra + '\nendfunction')
            with self.subTest(function=function):
                with self.assertRaises(ValueError):
                    review(functions, self.report['nativeFields'], self.registry, self.objects)

    def test_material_group_does_not_create_or_relabel_a_unit(self):
        self.assertEqual({'rawcodes': ['H099', 'H0B2'], 'catalogIds': ['TR-08', 'TR-14']},
                         self.report['materialGroups']['H08T'])
        for raw, cid in zip(['H099', 'H0B2'], ['TR-08', 'TR-14']):
            self.assertEqual(cid, self.objects[raw]['catalogId'])
        self.assertNotIn('H08T', self.objects)
        source = (ROOT / 'Assets/Scripts/Mobile/Original323FusionMaterialGroups.cs').read_text()
        self.assertIn('rawcode == "H08T"', source)
        self.assertNotIn('SpawnCharacter', source)
        self.assertNotIn('Wallet.Grant', source)


if __name__ == '__main__':
    unittest.main()
