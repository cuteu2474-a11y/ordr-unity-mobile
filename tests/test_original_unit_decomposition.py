import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class OriginalUnitDecompositionTests(unittest.TestCase):
    def setUp(self):
        self.data = json.loads((ROOT / 'data/original-2323C-unit-decomposition.json').read_text())
        self.by_id = {r['catalogId']: r for r in self.data['entries']}

    def test_pinned_sources_and_spell_effect_dispatch_are_separate_from_combat(self):
        self.assertEqual('96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d', self.data['sourceJassSha256'])
        self.assertEqual('23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda', self.data['sourceAbilityDataSha256'])
        self.assertFalse(self.data['retailCombatParityVerified'])
        self.assertEqual('aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0', self.data['sourceW3aSha256'])
        self.assertEqual('82916514a12b7edb15252d42225cd8cc8ce61cfd', self.data['slkIntegerZeroPolicy']['sourceCommit'])
        self.assertTrue({'Bn2', 'BoK', 'BDL', 'BDK', 'BdD', 'BIY', 'BIV'} <= set(self.data['functions']))

    def test_both_casters_and_all_outputs_match_original_object_ownership(self):
        source = json.loads((ROOT / 'data/original-2323C-map-base-stats.json').read_text())
        owners = {r['catalogId']: r for r in source['catalogEntries']}
        self.assertEqual({'S-02', 'S-11'}, set(self.by_id))
        self.assertEqual(source['sourceW3xSha256'], self.data['sourceW3xSha256'])
        for row in self.by_id.values():
            self.assertEqual(owners[row['catalogId']]['originalRawcode'], row['unitRawcode'])
            self.assertIn(row['abilityRawcode'], owners[row['catalogId']]['originalAbilityList'].split(','))
            for output in row['results']:
                self.assertEqual(owners[output['catalogId']]['originalRawcode'], output['rawcode'])
            self.assertEqual((0, 0, 0, 0), (row['goldCost'], row['woodCost'], row['nativeManaCost'], row['salePointsAwarded']))

    def test_original_cast_durations_counts_and_biy_coordinates(self):
        capone, buggy = self.by_id['S-02'], self.by_id['S-11']
        self.assertEqual((5, 2), (capone['castSeconds'], buggy['castSeconds']))
        self.assertIsNone(capone['resultCountRoll'])
        self.assertEqual({'min': 2, 'max': 3}, buggy['resultCountRoll'])
        self.assertEqual([('U-13', 1, -3760, 3904), ('C-07', 1, -3232, 3232), ('C-08', 1, -2976, 3232)],
                         [(r['catalogId'], r['count'], r['x'], r['y']) for r in capone['results']])
        self.assertEqual(('C-07', None, -3232, 3232), tuple(buggy['results'][0][k] for k in ('catalogId', 'count', 'x', 'y')))

    def test_full_unit_audit_records_manual_functions_without_claiming_full_skills(self):
        status = json.loads((ROOT / 'data/unit-runtime-status.json').read_text())
        rows = {r['catalogId']: r for r in status['entries']}
        manual = {k for k, r in rows.items() if r['connectedManualFunctions']}
        self.assertEqual(set(self.by_id), manual)
        self.assertEqual(2, status['summary']['connectedManualUnitFunctions'])
        self.assertFalse(status['allUnitsComplete'])
        for key in manual:
            self.assertEqual(self.by_id[key]['castSeconds'], rows[key]['connectedManualFunctions'][0]['castSeconds'])
            self.assertFalse(rows[key]['fullUnitImplementationVerified'])


if __name__ == '__main__':
    unittest.main()
