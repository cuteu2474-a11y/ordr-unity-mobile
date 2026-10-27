import json
from pathlib import Path
import unittest

DATA = Path(__file__).resolve().parents[1] / 'data'


class MobilePracticeDataTests(unittest.TestCase):
    def test_every_practice_monster_matches_the_existing_reference_model(self):
        practice = json.loads((DATA / 'mobile-practice-battle-model.json').read_text())
        original = json.loads((DATA / 'original-2323C-nightmare-monster-initial-model.json').read_text())
        lookup = {r['waveNumber']: r for r in original['waveMonsterInitialStatModels'] if r['waveNumber'] <= 20}
        self.assertEqual(20, len(practice['rounds']))
        self.assertEqual(20, practice['lastRound'])
        for row in practice['rounds']:
            expected = lookup[row['wave']]
            self.assertEqual(expected['monsterRawcode'], row['rawcode'])
            self.assertEqual(expected['modeledInitialHitPoints'], row['health'])
            self.assertEqual(expected['modeledInitialArmor'], row['armor'])
            self.assertEqual(expected['role'], row['role'])

    def test_model_stays_distinct_from_retail_verified_wave_data(self):
        practice = json.loads((DATA / 'mobile-practice-battle-model.json').read_text())
        self.assertIs(False, practice['retailParityVerified'])
        self.assertIs(False, practice['safeForVerifiedMonsterWaveRuntime'])
        self.assertEqual('ordr-mobile-practice-model-v1', practice['format'])
        self.assertEqual(70, practice['initialEnemyLimit'])
        self.assertEqual(2, practice['roundRandomWisps'])
        self.assertEqual([(-4960, 5632), (-4992, 3456), (-2560, 3456), (-2560, 5632)],
                         [(p['x'], p['y']) for p in practice['routeCenters']])


if __name__ == '__main__':
    unittest.main()
