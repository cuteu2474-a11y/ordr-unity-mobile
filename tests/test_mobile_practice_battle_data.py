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
        tech = json.loads((DATA / 'original-2323C-nightmare-monster-tech-evidence.json').read_text())
        abilities = json.loads((DATA / 'original-2323C-monster-object-abilities.json').read_text())
        source_abilities = {row['rawcode']: set(row['objectBaseAbilities'])
                            for row in abilities['entries']}
        self.assertEqual(tech['nightmareMonsterMagicDefense']['initialA04QAbilityLevel'],
                         game['initialMagicAbilityLevel'])
        self.assertEqual(16, game['initialMagicAbilityLevel'])
        for row in game['rounds']:
            self.assertIn('A04Q', source_abilities[row['rawcode']])
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

    def test_standalone_round_lead_ins_match_fixed_jass_evidence(self):
        rules = json.loads((DATA / 'original-2323C-round-boss-rules.json').read_text())
        self.assertIs(False, rules['liveGameplayParityVerified'])
        timers = rules['roundTimersSeconds']
        self.assertEqual(21, timers['firstRoundWait'])
        self.assertEqual(70, timers['transitionAfterRound50'])
        constants = (DATA.parents[0] / 'Assets/Scripts/Combat/Original323BossRoundRules.cs').read_text()
        self.assertIn('public const int FirstRoundWaitSeconds = 21;', constants)
        self.assertIn('public const int PostRound50TransitionSeconds = 70;', constants)


if __name__ == '__main__':
    unittest.main()
