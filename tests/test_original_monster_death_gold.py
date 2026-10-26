"""Regression checks for pinned Warcraft gold-death source facts."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "data/original-2323C-monster-death-gold-evidence.json"

class OriginalMonsterGoldFacts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads(PATH.read_text(encoding="utf-8"))

    def test_w3x_pin_and_no_live_rng_claim(self):
        p = self.doc
        self.assertEqual(p["gameVersion"], "S2 2.323[C]")
        self.assertEqual(p["sourceWar3mapJSha256"],
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d")
        self.assertEqual(p["sourceW3xSha256"],
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c")
        self.assertIs(p["safeForLiveOriginalRandomParity"], False)
        for key in ("requiresObservedPlayerSlot",
                    "requiresObservedGlobalRandomRoll",
                    "requiresObservedDeathTimeRound",
                    "requiresObservedGoldBonus",
                    "requiresObservedClientGold"):
            self.assertIs(p[key], True)

    def test_source_random_and_round_basis(self):
        p = self.doc
        self.assertEqual(p["rollCondition"], "BFi(1,4)==3")
        self.assertEqual(p["roundBasis"], "X3 at death (not monster birth)")
        self.assertEqual(p["eligibleSpawns"], ["normal","boss","enraged"])
        self.assertEqual(p["nativeRealToInteger"], "R2I truncate toward zero")
        self.assertEqual(p["grant"],
            "Bcf(Player(playerSlot),goldAmount) -> PLAYER_STATE_RESOURCE_GOLD")

    def test_original_functions_have_fixed_hashes_and_source_locations(self):
        functions = {x["name"]: x for x in self.doc["sourceFunctions"]}
        for name in ["CVM", "BFi", "BFh", "Bcf", "CVo", "CVp", "CVX", "CVY", "CVZ"]:
            self.assertIn(name, functions)
            self.assertEqual(len(functions[name]["normalizedBodySha256"]), 64)
        self.assertEqual(functions["CVM"]["sourceLine"], 59398)
        self.assertEqual(functions["BFi"]["sourceLine"], 3596)
        self.assertEqual(functions["Bcf"]["sourceLine"], 8236)

if __name__ == "__main__":
    unittest.main()
