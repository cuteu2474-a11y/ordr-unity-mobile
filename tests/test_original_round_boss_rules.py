"""Pinned original-script facts; no Warcraft live parity is asserted."""
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data" / "original-2323C-round-boss-rules.json"


class OriginalBossRoundScriptFacts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads(DATA.read_text(encoding="utf-8"))

    def test_pinned_map_and_non_playable_evidence(self):
        p = self.report
        self.assertEqual(p["gameVersion"], "S2 2.323[C]")
        self.assertEqual(p["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(p["sourceW3xSha256"],
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c")
        self.assertIs(p["safeForVerifiedMonsterWaveRuntime"], False)
        self.assertIs(p["liveGameplayParityVerified"], False)

    def test_original_late_boss_first_tick_and_score(self):
        p = self.report
        self.assertEqual(p["bossSpawnSecondsRelativeToSpawnTimer"],
            {"earlyBossOnly": 0.5, "lateBossWithNormal": 0.5})
        self.assertEqual(p["roundTimersSeconds"]["beforeNewWorldBoss"], 60)
        self.assertEqual(p["roundTimersSeconds"]["beforeNewWorldNormal"], 35)
        self.assertEqual(p["roundTimersSeconds"]["newWorld"], 32)
        self.assertEqual(p["lateBossScore"]["roundNumbers"], [55, 60, 65])
        self.assertEqual(p["lateBossScore"]["fastKillElapsedStrictlyLessThanSeconds"], 16)
        self.assertEqual(p["lateBossScore"]["fastKillScore"], 1000)
        self.assertEqual(p["lateBossScore"]["otherKillScore"], 500)
        self.assertEqual(p["lateBossScore"]["scoreKind"], "original-score-points-not-gold")

    def test_boss_deadline_and_gold_are_distinct(self):
        p = self.report
        self.assertEqual(p["bossDeadline"]["evaluationTrigger"], "l0")
        self.assertEqual(p["bossDeadline"]["condition"], "un[EK]!=0")
        self.assertIs(p["bossDeadline"]["automaticObservationAvailableInUnity"], False)
        self.assertEqual(p["ordinaryKillGold"]["chanceCondition"], "BFi(1,4)==3")
        self.assertIs(p["ordinaryKillGold"]["randomStateAndBuffRuntimeVerified"], False)
        sources = {x["name"]: x for x in p["sourceFunctions"]}
        self.assertIn("Bdf", sources)
        self.assertIn("CVM", sources)
        self.assertIn("CVc", sources)
        self.assertIn("CU8", sources)
        self.assertEqual(sources["Bdf"]["sourceLine"], 8484)
        for obj in sources.values():
            self.assertEqual(len(obj["normalizedBodySha256"]), 64)


if __name__ == "__main__":
    unittest.main()
