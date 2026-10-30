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

    def test_standalone_enemy_data_covers_all_65_original_jass_waves_and_roles(self):
        game = json.loads((DATA / 'mobile-standalone-enemy-model.json').read_text())
        original = json.loads((DATA / 'original-2323C-nightmare-monster-initial-model.json').read_text())
        jass = json.loads((DATA / 'original-2323C-monster-wave-script-evidence.json').read_text())
        stat_rows = {(r['waveNumber'], r['role']): r for r in original['waveMonsterInitialStatModels']}
        spawn_ids = {(w['waveNumber'], s['role']): s['rawcode']
                     for w in jass['waves'] for s in w['normalAndBossSpawns']}
        self.assertEqual('ordr-mobile-standalone-source-model-v1', game['format'])
        self.assertEqual(65, game['lastRound'])
        self.assertEqual(68, len(game['rounds']))
        self.assertEqual(set(spawn_ids), {(r['wave'], r['role']) for r in game['rounds']})
        self.assertEqual(jass['sourceW3xSha256'], game['sourceW3xSha256'])
        self.assertEqual(jass['sourceWar3mapJSha256'], game['sourceJassSha256'])
        self.assertIs(False, game['retailParityVerified'])
        self.assertIs(False, game['safeForVerifiedMonsterWaveRuntime'])
        self.assertEqual(70, game['initialEnemyLimit'])
        self.assertEqual(2, game['roundRandomWisps'])
        for row in game['rounds']:
            key = (row['wave'], row['role'])
            stat = stat_rows[key]
            self.assertEqual(spawn_ids[key], row['rawcode'])
            self.assertEqual(stat['monsterRawcode'], row['rawcode'])
            self.assertEqual(stat['modeledInitialHitPoints'], row['health'])
            self.assertEqual(stat['modeledInitialArmor'], row['armor'])
            self.assertGreater(row['objectMovementSpeed'], 0)
        for wave in (55, 60, 65):
            self.assertEqual({'line', 'boss'},
                             {r['role'] for r in game['rounds'] if r['wave'] == wave})


if __name__ == '__main__':
    unittest.main()
