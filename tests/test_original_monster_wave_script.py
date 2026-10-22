"""Non-Unity regression checks for original monster wave JASS evidence."""

import json
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from extract_original_monster_wave_script import (  # noqa: E402
    extract_arrays, original_id,
)
from extract_original_monster_object_stats import MAP_SHA256  # noqa: E402


class RawcodeSourceTests(unittest.TestCase):
    def test_decodes_jass_integer_literal_to_original_rawcode(self):
        self.assertEqual(original_id("6F303156"), "o01V")
        self.assertEqual(original_id("6F303049"), "o00I")
        with self.assertRaisesRegex(ValueError, "Invalid Warcraft"):
            original_id("00000000")

    def test_rejects_duplicate_wave_array_assignment(self):
        init = "set ZD[1]=$6F303049\nset ZD[1]=$6F303034\n"
        with self.assertRaisesRegex(ValueError, "Duplicate JASS wave"):
            extract_arrays(init, 100)


class OriginalWaveEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads((
            ROOT / "data/original-2323C-monster-wave-script-evidence.json"
        ).read_text(encoding="utf-8"))
        cls.objects = {entry["rawcode"]: entry for entry in json.loads((
            ROOT / "data/original-2323C-monster-object-stats.json"
        ).read_text(encoding="utf-8"))["entries"]}

    def test_original_sources_and_fail_closed_runtime(self):
        doc = self.report
        self.assertEqual(doc["sourceW3xSha256"], MAP_SHA256)
        self.assertEqual(doc["sourceWar3mapJSha256"],
                         "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertIs(doc["safeForVerifiedMonsterWaveRuntime"], False)
        self.assertIs(doc["modeAdjustedCombatStatsVerified"], False)
        self.assertEqual(doc["jassArrayFunction"], "CUl")
        self.assertEqual(doc["summary"]["rounds"], 65)
        self.assertEqual([w["waveNumber"] for w in doc["waves"]], list(range(1, 66)))

    def test_every_source_array_and_source_object_agree(self):
        seen = set()
        for wave in self.report["waves"]:
            wave_number = wave["waveNumber"]
            for spawn in wave["normalAndBossSpawns"]:
                code = spawn["rawcode"]
                self.assertNotIn(code, seen)
                seen.add(code)
                source = self.objects[code]
                for field, object_field in (
                    ("originalMapName", "originalMapName"),
                    ("objectBaseHitPoints", "objectBaseHitPoints"),
                    ("objectBaseArmor", "objectBaseArmor"),
                    ("warcraftArmorType", "warcraftArmorType"),
                ):
                    self.assertEqual(spawn[field], source[object_field])
                symbol = "ZD" if spawn["role"] == "line" else "uO"
                self.assertEqual(spawn["sourceArray"], f"{symbol}[{wave_number}]")
                self.assertEqual(spawn["sourceJassAssignment"],
                                 f"set {symbol}[{wave_number}]=${code.encode('ascii').hex().upper()}")
                self.assertGreater(spawn["sourceJassLine"], 58000)
                self.assertEqual(spawn["cadenceSeconds"], 0.5)
                self.assertEqual(spawn["firstSpawnTick"], 1)
                self.assertIs(wave["finalHealthAndArmorVerified"], False)
                self.assertIs(wave["exactSpawnPositionsVerified"], False)
        self.assertEqual(len(seen), 68)

    def test_round_composition_and_conditional_extra(self):
        for wave in self.report["waves"]:
            number = wave["waveNumber"]
            plan = [(s["role"], s["spawnsPerActivePlayer"])
                    for s in wave["normalAndBossSpawns"]]
            if number <= 50 and number % 10 == 0:
                self.assertEqual(plan, [("boss", 1)])
            elif number > 50 and number % 5 == 0:
                self.assertEqual(plan, [("line", 35), ("boss", 1)])
            else:
                self.assertEqual(plan, [("line", 35)])
            if 50 < number < 65 and number % 5 != 0:
                extra = wave["conditionalBerserk"]
                self.assertIsNotNone(extra)
                self.assertEqual(extra["spawnAtTick"], 15)
                self.assertEqual(extra["maxSpawnEventsAcrossPlayers"], 4)
            else:
                self.assertIsNone(wave["conditionalBerserk"])

    def test_key_boss_and_normal_identifiers(self):
        waves = {w["waveNumber"]: w for w in self.report["waves"]}
        self.assertEqual(waves[1]["normalAndBossSpawns"][0]["rawcode"], "o00I")
        self.assertEqual(waves[10]["normalAndBossSpawns"][0]["rawcode"], "o01V")
        self.assertEqual(waves[55]["normalAndBossSpawns"][1]["rawcode"], "o001")
        self.assertEqual(waves[65]["normalAndBossSpawns"][1]["rawcode"], "o02B")


if __name__ == "__main__":
    unittest.main()
