"""Replay source review; reject silent gameplay effects and changed payments."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from audit_original_readonly_creation_fusions import review


class ReadOnlyCreationFusionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ROOT / 'data'
        cls.evidence = json.loads((cls.data / 'original-2323C-readonly-creation-fusions.json').read_text())
        cls.pinned = json.loads((cls.data / 'original-2323C-transcend-material-swaps.json').read_text())
        cls.functions = {n: r['source'] for n, r in cls.evidence['functions'].items()}
        cls.registry = {r['abilityRawcode']: r for r in json.loads(
            (cls.data / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (cls.data / 'original-2323C-map-base-stats.json').read_text())['catalogEntries']
                       if r.get('originalRawcode')}
        cls.port = ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs'

    def test_pinned_creation_branches_and_exact_costs_are_authorized(self):
        reviewed = review(self.functions, self.evidence['legendaryNativeFields'],
                          self.registry, self.objects, self.port, self.pinned)
        self.assertEqual(reviewed, self.evidence['reviewed'])
        self.assertEqual({'A02D', 'YY13'}, set(reviewed))
        self.assertEqual(['R-08'], reviewed['A02D']['originalCasterCatalogIds'])
        self.assertEqual([], reviewed['YY13']['originalCasterCatalogIds'])
        for name, row in self.evidence['functions'].items():
            self.assertEqual(hashlib.sha256(row['source'].encode()).hexdigest(), row['sha256LF'], name)
        self.assertEqual(3, next(c['count'] for c in reviewed['A02D']['sourceRegistration']['conditions']
                                 if c['kind'] == 'WOOD'))
        self.assertFalse(any(c['kind'] in ('ITEM', 'GOLD', 'WOOD') for c in
                             reviewed['YY13']['sourceRegistration']['conditions']))

    def test_ch8_or_hidden_callback_gain_a_gameplay_effect_is_rejected(self):
        for name, mutation in (
                ('CH8', 'call Bjp(iI)\ncall AddResource(Player(JG),100)'),
                ('BrH', 'call StartSound(XF)\ncall CreateUnit(Player(JG),0,0,0,0)'),
                ('Bjp', 'set Zn=LoadBoolean(SD,StringHash(TS),Zm)\nset bank[JG]=1')):
            with self.subTest(name=name):
                functions = copy.deepcopy(self.functions)
                target = 'call Bjp(iI)' if name == 'CH8' else (
                    'call StartSound(XF)' if name == 'BrH' else
                    'set Zn=LoadBoolean(SD,StringHash(TS),Zm)')
                functions[name] = functions[name].replace(target, mutation)
                with self.assertRaises(ValueError):
                    review(functions, self.evidence['legendaryNativeFields'],
                           self.registry, self.objects, self.port, self.pinned)

    def test_cast_and_unit_cost_mutation_cannot_keep_live_authorization(self):
        native = dict(self.evidence['legendaryNativeFields'], Cast1='1.0')
        with self.assertRaisesRegex(ValueError, 'Native legendary'):
            review(self.functions, native, self.registry, self.objects, self.port, self.pinned)
        registry = copy.deepcopy(self.registry)
        wood = next(c for c in registry['A02D']['conditions'] if c['kind'] == 'WOOD')
        wood['count'] = 4
        with self.assertRaisesRegex(ValueError, 'payment or state'):
            review(self.functions, self.evidence['legendaryNativeFields'],
                   registry, self.objects, self.port, self.pinned)


if __name__ == '__main__':
    unittest.main()
