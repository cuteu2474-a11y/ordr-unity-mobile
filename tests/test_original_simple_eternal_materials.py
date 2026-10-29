"""ET01 is a source-audited user-confirmed material swap, not skill parity."""
import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / 'data'


class SimpleEternalMaterialTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((DATA / 'original-2323C-eternal-material-swaps.json').read_text())
        cls.source = {r['abilityRawcode']: r for r in json.loads(
            (DATA / 'original-jass-fusion-registry.json').read_text())['registrations']}
        cls.objects = {r['originalRawcode']: r for r in json.loads(
            (DATA / 'original-2323C-map-base-stats.json').read_text())['catalogEntries']
            if r.get('originalRawcode')}

    def test_et01_cost_and_mixed_grade_materials_are_exact_source_values(self):
        receipt = self.audit['recipe']
        self.assertEqual('ET01', receipt['abilityRawcode'])
        self.assertEqual(self.source['ET01']['conditions'], receipt['conditions'])
        self.assertEqual(self.source['ET01']['results'], receipt['results'])
        self.assertEqual(receipt['resultCatalogId'], 'E-06')
        self.assertEqual(receipt['spawnAtPlayerZeroStart'], {'x': -3776, 'y': 4480})
        self.assertEqual([x for x in receipt['conditions'] if x['kind'] == 'WOOD'],
                         [{'kind': 'WOOD', 'rawcode': 'WOOD', 'count': 5}])
        self.assertEqual([x['catalogId'] for x in receipt['ingredients']],
                         ['LI-09', 'T-02', 'R-06', 'R-17', 'S-04', 'S-32'])
        for item in receipt['ingredients']:
            self.assertEqual(self.objects[item['rawcode']]['catalogId'], item['catalogId'])
        self.assertFalse(self.audit['combatParityVerified'])


if __name__ == '__main__':
    unittest.main()
