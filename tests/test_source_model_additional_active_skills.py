"""Full 13-unit source-bound practice cast batch; no fabricated original parity."""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
COMBAT = ROOT / "Assets" / "Scripts" / "Combat"


def load(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class AdditionalPracticeActivesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.batch = load("source-model-additional-13-active-skills.json")
        cls.profiles = {x["unitId"]: x for x in load("character-skill-profiles.json")["profiles"]}
        cls.runtime = {x["catalogId"]: x for x in load("unit-runtime-status.json")["entries"]}
        cls.objects = {x["rawcode"]: x for x in load("original-2323C-unit-object-stats.json")["entries"]}
        cls.extra = (COMBAT / "SourceModelUnconnectedSkillCatalog.cs").read_text(encoding="utf-8")
        cls.next_thirty = (COMBAT / "SourceModelNextThirtySkillCatalog.cs").read_text(encoding="utf-8")
        cls.resources = (COMBAT / "SourceModelUnitResourceCatalog.cs").read_text(encoding="utf-8")

    def test_every_cost_and_original_owner_are_pinned(self):
        b = self.batch
        self.assertEqual("ordr-next-thirteen-complete-tooltip-active-model-v1", b["format"])
        self.assertEqual("TOOLTIP_MODEL_ONLY", b["execution"])
        self.assertEqual("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                         b["sourceW3xSha256"])
        rows = b["completeActivePackets"]
        self.assertEqual(13, len(rows))
        self.assertEqual(13, len({x["catalogId"] for x in rows}))
        self.assertEqual(13, len({x["originalRawcode"] for x in rows}))
        self.assertTrue(all(x["originalParityVerified"] is False for x in rows))
        for row in rows:
            uid, raw = row["catalogId"], row["originalRawcode"]
            with self.subTest(unit=uid):
                self.assertEqual(raw, self.runtime[uid]["originalRawcode"])
                self.assertTrue(self.runtime[uid]["objectBasicAttackModel"])
                profile = self.profiles[uid]
                selected = [x for x in profile["skills"] if x["name"] == row["skillName"]]
                self.assertEqual(1, len(selected))
                skill = selected[0]
                self.assertEqual(row["trigger"], skill["trigger"])
                field = "healthCost" if row["trigger"] == "activeHealth" else "manaCost"
                self.assertEqual(row["resourceCost"], skill[field])
                obj = self.objects[raw]["originalObjectFields"]["balance"]
                original_cap = float(obj["HP"] if row["trigger"] == "activeHealth"
                                     else obj["manaN"])
                self.assertEqual(row["resourceCost"], original_cap)

    def test_all_thirteen_wired_to_exact_catalog_identity_and_one_active(self):
        entries = re.findall(
            r'new SourceModelUnconnectedSkillRow\(\s*"([^"]+)",\s*"([^"]+)"',
            self.extra)
        pairs = [x for x in self.batch["completeActivePackets"] if x["catalogId"] != "I-06"]
        self.assertEqual(len(entries), len({raw for _, raw in entries}),
                         "No duplicate callbacks by source rawcode.")
        for row in pairs:
            uid, raw, name = row["catalogId"], row["originalRawcode"], row["skillName"]
            with self.subTest(unit=uid):
                self.assertIn((uid, raw), entries)
                start = self.extra.index('new SourceModelUnconnectedSkillRow("' + uid + '", "' + raw + '"')
                next_start = self.extra.find("new SourceModelUnconnectedSkillRow(", start + 1)
                section = self.extra[start:next_start if next_start >= 0 else self.extra.index("};", start)]
                self.assertIn('new[] { "' + name + '" }', section)
        self.assertIn('new SourceModelNextThirtyUnit("I-06", "h04Q",', self.next_thirty)
        self.assertIn('new[] { "대자재천신" }', self.next_thirty)
        self.assertNotIn(("I-06", "h04Q"), entries,
                         "A unit present in NextThirty must not have an unreachable parallel active.")

    def test_unsupported_effect_stays_unconnected(self):
        a = self.profiles["MY-08"]["skills"]
        unapproved = next(x for x in a if x["name"] == "로이드 포저")
        self.assertIn("movementSlow", [c["type"] for c in unapproved["components"]])
        row = self.batch["deferredComponents"][0]
        self.assertEqual(("MY-08", "로이드 포저"),
                         (row["catalogId"], row["skillName"]))
        self.assertNotIn('new[] { "로이드 포저" }', self.extra)

    def test_missing_mana_cap_does_not_abort_all_265_original_resources(self):
        obj = self.objects["H0CO"]["originalObjectFields"]["balance"]
        self.assertEqual("150", obj["HP"])
        self.assertIsNone(obj["manaN"])
        self.assertEqual(0.5, float(obj["regenMana"]))
        exception = [x for x in self.objects.values()
                     if x["originalObjectFields"]["balance"].get("manaN") is None
                     and float(x["originalObjectFields"]["balance"].get("regenMana") or 0) > 0]
        self.assertEqual(["H0CO"], [x["rawcode"] for x in exception])
        self.assertEqual(265, len(self.objects))
        self.assertIn("DeferredSourceManaRegeneration", self.resources)
        self.assertIn('OrdrJson.Value(balance, "manaN") != null', self.resources)
        self.assertIn("stats[4] = stats[3];", self.resources)
        self.assertIn("stats[3] = 0;", self.resources)


if __name__ == "__main__":
    unittest.main()
