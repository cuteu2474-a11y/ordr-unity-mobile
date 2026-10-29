"""Offline replay of pinned source review and unsafe callback regressions."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from audit_original_transcend_material_swaps import REVIEWED, review_functions


class TranscendMaterialSwapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ROOT / 'data'
        cls.port = ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs'
        cls.audit = json.loads((cls.data / 'original-2323C-transcend-material-swaps.json').read_text())
        cls.functions = {n: r['source'] for n, r in cls.audit['functions'].items()}

    def test_review_replays_against_pinned_registrations_and_live_declarations(self):
        self.assertEqual('96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d',
                         self.audit['sourceJassSha256'])
        recipes, _ = review_functions(self.functions, self.data, self.port)
        self.assertEqual(recipes, self.audit['recipes'])
        self.assertEqual(list(REVIEWED), [r['ability'] for r in recipes])
        for name, row in self.audit['functions'].items():
            self.assertEqual(row['sha256LF'], hashlib.sha256(row['source'].encode()).hexdigest(), name)
        for recipe in recipes:
            kinds = {c['kind'] for c in recipe['registration']['conditions']}
            self.assertTrue({'UNIT', 'KING', 'UBAN'}.issubset(kinds))
            self.assertEqual(recipe['ability'] == 'ET08', 'WOOD' in kinds)
        mihawk = next(r for r in recipes if r['ability'] == 'ET08')
        self.assertEqual(2, next(c['count'] for c in mihawk['ingredients'] if c['rawcode'] == 'h060'))

    def test_additional_gameplay_effects_in_chat_or_skin_queries_are_rejected(self):
        handler = self.audit['recipes'][1]['handler']
        for name, statement in ((handler, 'set globalState[JG]=true'),
                                (handler, 'call UnitAddAbility(Yu,$41313142)'),
                                (handler, 'call StartSound(UnknownEffect())'),
                                ('Bjp', 'set saved[JG]=1'),
                                ('Bjm', 'call GrantReward()')):
            with self.subTest(name=name, statement=statement):
                functions = copy.deepcopy(self.functions)
                functions[name] = functions[name].replace('endfunction', statement + '\nendfunction')
                with self.assertRaises(ValueError):
                    review_functions(functions, self.data, self.port)

    def test_source_result_creation_followup_cannot_be_ignored(self):
        functions = copy.deepcopy(self.functions)
        functions['CH8'] += '\nif GetUnitTypeId(iI)==$48303934 then'
        with self.assertRaisesRegex(ValueError, 'CH8 followup'):
            review_functions(functions, self.data, self.port)


if __name__ == '__main__':
    unittest.main()
