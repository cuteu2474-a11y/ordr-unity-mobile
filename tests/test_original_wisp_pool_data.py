"""Pinned JASS-extracted Wisp pools; no W3X binary needed in CI."""

import hashlib
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/original-2323C-wisp-pools.json"
EXPECTED_SHA = "2783b95e34f28ceadf76be5aee0d6d13c532bc2e27daf1920cd4569637cf5cff"


class OriginalWispPoolDataTests(unittest.TestCase):
    def test_unmodified_map_derived_reward_data(self):
        raw = SOURCE.read_bytes()
        self.assertEqual(EXPECTED_SHA, hashlib.sha256(raw).hexdigest())
        data = json.loads(raw)
        self.assertEqual("S2 2.323[C]", data["gameVersion"])
        self.assertEqual("96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d",
                         data["sourceJassSha256"])
        self.assertEqual(["e017", "e016", "e019", "e015"],
                         [x["wispRawcode"] for x in data["wisps"]])
        self.assertEqual([[13], [33], [42, 8], [60]],
                         [[len(b["entries"]) for b in w["branches"]] for w in data["wisps"]])
        self.assertEqual([[100], [100], [99, 1], [100]],
                         [[b["chancePercent"] for b in w["branches"]] for w in data["wisps"]])
        for wisp in data["wisps"]:
            for branch in wisp["branches"]:
                rawcodes = [e["rawcode"] for e in branch["entries"]]
                self.assertEqual(len(rawcodes), len(set(rawcodes)))
                self.assertTrue(all(e["weight"] == 1 and len(e["rawcode"]) == 4
                                    for e in branch["entries"]))
        rare = data["wisps"][2]["branches"]
        self.assertEqual("A0YE", rare[0]["bonusAbilityRawcode"])
        self.assertIsNone(rare[1]["bonusAbilityRawcode"])


if __name__ == "__main__":
    unittest.main()
