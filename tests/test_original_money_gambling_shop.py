import hashlib
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class OriginalMoneyShopTests(unittest.TestCase):
    def test_cost_stock_and_payoff_keep_original_object_and_callback_boundaries(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        self.assertEqual('h062', data['initialShopRawcode'])
        regular, high = data['entries']
        self.assertEqual(('h06F', 10, 3, 11), (regular['rawcode'], regular['goldCost'],
                                              regular['stockMaximum'], regular['stockRegenSeconds']))
        self.assertEqual((25, [3, 5], [10, 50]),
                         (regular['lowPayoutAtMost'], regular['lowReturnRange'], regular['highReturnRange']))
        self.assertEqual(('H0AZ', 500, 'R009'),
                         (high['rawcode'], high['goldCost'], high['requiredResearch']))
        self.assertEqual(35000, data['highTierGrossReturnGraduation'])
        for row in data['entries']:
            self.assertIsNone(row['stockStartOverride'])
            self.assertIsNone(row['lumberCostOverride'])
            self.assertEqual(2, row['drawsPerPurchase'])
        self.assertIs(data['retailParityVerified'], False)
        self.assertIs(data['nativeStockInitializationVerified'], False)

    def test_source_evidence_agrees_with_previous_independent_jass_extraction(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        existing = json.loads((ROOT / 'data/original-2323C-combat-gambling-script-call-evidence.json').read_text())
        self.assertEqual(existing['sourceW3xSha256'], data['sourceW3xSha256'])
        self.assertEqual(existing['sourceWar3mapJSha256'], data['sourceJassSha256'])
        for new, old in zip(data['entries'], existing['moneyGambling']):
            self.assertEqual(old['sourceFunction'], new['callback'])
            self.assertEqual(old['bodySha256'], new['callbackSha256'])

    def test_unlock_and_graduation_are_explicit_source_events(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        unlock, graduation = data['highTierUnlock'], data['graduation']
        self.assertEqual(('o01V', 'CIK', 'CIJ', 'R009'),
                         (unlock['bossRawcode'], unlock['deathCallback'],
                          unlock['researchCallback'], unlock['research']))
        self.assertEqual(('h08C', 'R01M', 5, 10000, 1),
                         (graduation['replacementShop'], graduation['research'],
                          graduation['addedWoodPurchaseRights'],
                          graduation['woodPurchaseGoldCost'], graduation['woodPerPurchase']))
        self.assertEqual(['h06F', 'H0AZ'], graduation['moneyOffersRemoved'])
        self.assertEqual({'CIK', 'CIJ', 'CJC', 'CI8', 'CI7', 'CJF', 'CJE'},
                         set(data['progressionCallbackHashes']))

    def test_graduated_trait_and_rare_shop_purchases_keep_source_costs_and_pools(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        rows = data['graduation']['verifiedOffers']
        trait, rare = rows['traitPoint'], rows['rareUnit']
        self.assertEqual(('h0AL', 'CI7', 15000, 1, 1, 3600),
                         (trait['rawcode'], trait['callback'], trait['goldCost'],
                          trait['pointAmount'], trait['stockMaximum'], trait['stockRegenSeconds']))
        self.assertEqual(('H0B0', 'CJF', 5000, 'R01N', 1, 1, 3600),
                         (rare['rawcode'], rare['callback'], rare['goldCost'],
                          rare['requiredResearch'], rare['purchaseLimit'],
                          rare['stockMaximum'], rare['stockRegenSeconds']))
        self.assertEqual((99, 43, 1, 7),
                         (rare['rareBranchPercent'], len(rare['rarePool']),
                          rare['specialBranchPercent'], len(rare['specialPool'])))
        self.assertIn('h05X', rare['rarePool'])
        self.assertNotIn('h0BG', rare['rarePool'])
        code = (ROOT / 'Assets/Scripts/Mobile/SourceModelMoneyGamblingDesk.cs').read_text()
        self.assertIn('OriginalMissionWalletRewards.TraitPointsId', code)
        for field, expected in [('TraitGoldCost', 15000), ('RareGoldCost', 5000),
                                ('GraduatedStockCapacity', 1),
                                ('GraduatedStockRefillSeconds', 3600)]:
            self.assertEqual(expected, int(re.search(r'const int ' + field + r' = (\d+);', code)[1]))

    def test_runtime_values_match_the_map_profile(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        regular = data['entries'][0]
        code = (ROOT / 'Assets/Scripts/Mobile/SourceModelRegularMoneyGambling.cs').read_text()
        for field, key in [('GoldCost', 'goldCost'), ('MaximumStock', 'stockMaximum'),
                           ('RestockSeconds', 'stockRegenSeconds')]:
            self.assertEqual(regular[key], int(re.search(r'const int ' + field + r' = (\d+);', code)[1]))
