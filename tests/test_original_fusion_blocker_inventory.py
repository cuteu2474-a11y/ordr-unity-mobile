"""Batch-check the entire source registry against the live executable gate."""
import json
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from classify_original_fusion_blockers import build


class FusionBlockerInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ROOT / 'data'
        cls.port = ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs'
        cls.inventory = json.loads((cls.data / 'original-fusion-unconnected-review.json').read_text())
        cls.source = {r['abilityRawcode']: r for r in json.loads(
            (cls.data / 'original-jass-fusion-registry.json').read_text())['registrations']}

    def test_all_265_source_recipes_are_recounted_from_real_csharp_arrays(self):
        self.assertEqual(build(self.data, self.port), self.inventory)
        summary = self.inventory['summary']
        self.assertEqual((265, 167, 98), (summary['sourceRecipesExcludingTest'],
                                         summary['liveExecutableRecipes'],
                                         summary['blockedRecipes']))
        self.assertEqual({'Reviewed': 13, 'ReviewedSpecial': 31, 'ReviewedRare': 41,
                          'ReviewedLegendary': 34, 'ReviewedHidden': 19,
                          'ReviewedEternal': 1, 'ReviewedRestricted': 28},
                         summary['liveExecutableByGrade'])
        self.assertEqual(98, len({r['abilityRawcode'] for r in self.inventory['blocked']}))

    def test_grouped_reasons_preserve_source_costs_and_unknown_materials(self):
        for row in self.inventory['blocked']:
            source = self.source[row['abilityRawcode']]['conditions']
            self.assertEqual(row['sourceGoldCost'],
                             sum(x['count'] for x in source if x['kind'] == 'GOLD'))
            self.assertEqual(row['sourceWoodCost'],
                             sum(x['count'] for x in source if x['kind'] == 'WOOD'))
            self.assertTrue(row['reasonCodes'])
        blocked = {r['abilityRawcode']: r for r in self.inventory['blocked']}
        for ability in ('YY04', 'YY07', 'YY08', 'YY09', 'YY12'):
            self.assertNotIn(ability, blocked, "Pinned JASS chat-only ship fusions are executable")
        self.assertIn('top-tier-route-or-callback-audit', blocked['TR06']['reasonCodes'])
        self.assertNotIn('navigation-ban-state', blocked['TR06']['reasonCodes'])
        self.assertIn('item-payment', blocked['ET02']['reasonCodes'])
        self.assertIn('native-cast-AOsf', blocked['A19Z']['reasonCodes'])
        self.assertIn('chat-player-state-effect', blocked['YY15']['reasonCodes'])
        self.assertIn('no-unit-payment-in-registration', blocked['UU01']['reasonCodes'])
        self.assertIn('no-unit-payment-in-registration', blocked['YY26']['reasonCodes'])
        self.assertIn('creation-followup-CH8', blocked['YY20']['reasonCodes'])
        self.assertNotIn('A01S', blocked)
        self.assertNotIn('A113', blocked)
        self.assertNotIn('YY18', blocked)
        self.assertNotIn('YY25', blocked)
        self.assertNotIn('YY13', blocked)
        self.assertNotIn('A02D', blocked)
        self.assertNotIn('ET01', blocked)
        for ability in ('A0RP', 'IM02', 'IM03', 'IM10', 'ET09', 'ET11'):
            self.assertNotIn(ability, blocked)
        for ability in ('A0Y3', 'A0Y9', 'A0XT', 'A1AD', 'A0Y6', 'A0YK', 'A10V',
                        'A19W', 'A19U', 'A19S', 'A19V', 'A19T', 'A0QG'):
            self.assertNotIn(ability, blocked)

    def test_unverified_entries_do_not_turn_into_free_unit_rewards(self):
        for ability in ('UU01', 'YY26'):
            self.assertFalse(any(x['kind'] == 'UNIT' for x in self.source[ability]['conditions']))
        self.assertFalse(self.inventory['summary']['liveExecutableByGrade'].get('ReviewedImmortal'))

    def test_source_conditional_batch_is_audited_and_guarded_by_shared_state(self):
        evidence = json.loads((self.data / 'original-2323C-conditional-fusion-audit.json').read_text())
        registry = json.loads((self.data / 'original-jass-fusion-registry.json').read_text())
        self.assertEqual(registry['sourceSha256'], evidence['sourceMapSha256'])
        self.assertEqual(64, len(evidence['sourceJassSha256']))
        code = self.port.read_text()
        restricted = re.search(r'string\[,] ReviewedRestricted = \{(.*?)\n        \};', code, re.S)
        self.assertIsNotNone(restricted)
        declared = re.findall(r'\{ "([A-Z0-9]{4})", "(\w{4})", "([A-Z]+-\d+)" \}',
                              restricted.group(1))
        expected = [(r['ability'], r['result'], r['catalogId'])
                    for r in evidence['reviewedExecutable']]
        self.assertEqual(expected, declared[:len(expected)],
                         'Native callback-reviewed recipes stay pinned in their original order')
        expanded = declared[len(expected):]
        self.assertEqual([
            ('A0Y3', 'h09W', 'MY-04'), ('A0Y9', 'h0A2', 'MY-03'),
            ('A0XT', 'h09N', 'MY-06'), ('A1AD', 'h08M', 'MY-08'),
            ('A0Y6', 'h09R', 'MY-11'), ('A0YK', 'h0BF', 'MY-02'),
            ('A10V', 'h083', 'MY-07'), ('A19W', 'h09M', 'MY-01'),
            ('A19U', 'h0AC', 'MY-13'), ('A19S', 'h0A6', 'MY-09'),
            ('A19V', 'h0CC', 'MY-10'), ('A19T', 'h08I', 'MY-05'),
            ('A0QG', 'h0C2', 'MY-14')], expanded,
            'The 13 mobile-result Mystic recipes require separate source/result gates')
        objects = {r['originalRawcode']: r for r in json.loads((self.data /
            'original-2323C-map-base-stats.json').read_text())['catalogEntries']
                   if r.get('originalRawcode')}
        for ability, rawcode, catalog_id in expected:
            self.assertEqual(objects[rawcode]['catalogId'], catalog_id)
            registration = self.source[ability]
            self.assertEqual(registration['results'], [dict(kind='UNIT', rawcode=rawcode, count=1)])
            kinds = {c['kind'] for c in registration['conditions']}
            self.assertTrue({'UNIT', 'KING'}.issubset(kinds))
            self.assertEqual(not ability.startswith('TR'), 'WOOD' in kinds)
            self.assertTrue(kinds.issubset({'UNIT', 'WOOD', 'GOLD', 'KING', 'UBAN',
                                           'UPUN', 'RMAX'}))
            for kind in ('KING', 'UBAN'):
                if kind not in kinds:
                    self.assertEqual('A12C', ability)
                    continue
                self.assertEqual([rawcode], [c['rawcode'] for c in registration['conditions']
                                             if c['kind'] == kind])
        mystic_evidence = json.loads((self.data / 'original-2323C-mystic-pick-batch35.json').read_text())
        self.assertEqual('MOBILE_RESULT_MATCHED_NOT_NATIVE_CALLBACK_VERIFIED',
                         mystic_evidence['executionClassification'])
        documented_pick = {row['abilityRawcode'] for row in mystic_evidence['implementedResults']}
        self.assertTrue({'A19W', 'A19U', 'A19S', 'A19V', 'A19T'} <= documented_pick)
        for ability, rawcode, catalog_id in expanded:
            reg = self.source[ability]
            self.assertEqual(reg['results'], [dict(kind='UNIT', rawcode=rawcode, count=1)])
            kinds = {condition['kind'] for condition in reg['conditions']}
            self.assertTrue({'UNIT', 'GOLD', 'WOOD', 'KING'} <= kinds)
            self.assertTrue(kinds <= {'UNIT', 'GOLD', 'WOOD', 'KING', 'PICK'})
            self.assertEqual(10000, sum(c['count'] for c in reg['conditions']
                                        if c['kind'] == 'GOLD'))
            self.assertEqual(7, sum(c['count'] for c in reg['conditions']
                                    if c['kind'] == 'WOOD'))
            if ability in documented_pick or ability == 'A0QG':
                self.assertEqual([('A800', 1)], [(c['rawcode'], c['count'])
                                                 for c in reg['conditions'] if c['kind'] == 'PICK'])
            else:
                self.assertNotIn('PICK', kinds)
            if rawcode in objects:
                self.assertEqual(objects[rawcode]['catalogId'], catalog_id)
            else:
                self.assertEqual(('A0QG', 'h0C2', 'MY-14'),
                                 (ability, rawcode, catalog_id))
        self.assertIn('verifiedConditions.CanSatisfy', code)
        self.assertIn('sourceRestrictions.ConfirmObservedOriginalRound', code)
        self.assertIn('BeginOriginalFusionUnitSwap', code)

    def test_otherworld_mode_never_draws_mutated_mode_bans(self):
        raw = (ROOT / 'Assets/Scripts/Mobile/Original323FusionVerifiedRestrictions.cs').read_text()
        evidence = json.loads((self.data / 'original-2323C-conditional-fusion-audit.json').read_text())
        self.assertIn('th[tl]', evidence['verifiedBanState']['sourceModeSelection'])
        self.assertIn('th[tn]', evidence['verifiedBanState']['sourceModeSelection'])
        self.assertIn('nightmare-otherworld', evidence['verifiedBanState']['mobileMode'])
        self.assertIn('GameSession.ModeId != "nightmare-otherworld"', raw)
        self.assertIn('public int BannedUnitCount => 0', raw)
        self.assertIn('isBanned = false;', raw)
        self.assertNotIn('SourceBanPools', raw)
        self.assertNotIn('new Random()', raw)


if __name__ == '__main__':
    unittest.main()
