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

    def test_runtime_values_match_the_map_profile(self):
        data = json.loads((ROOT / 'data/original-2323C-money-gambling-shop.json').read_text())
        regular = data['entries'][0]
        code = (ROOT / 'Assets/Scripts/Mobile/SourceModelRegularMoneyGambling.cs').read_text()
        for field, key in [('GoldCost', 'goldCost'), ('MaximumStock', 'stockMaximum'),
                           ('RestockSeconds', 'stockRegenSeconds')]:
            self.assertEqual(regular[key], int(re.search(r'const int ' + field + r' = (\d+);', code)[1]))
