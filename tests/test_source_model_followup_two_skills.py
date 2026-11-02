"""Evidence and owner/cast boundaries for the incremental two-skill batch."""

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
COMBAT = ROOT / "Assets" / "Scripts" / "Combat"


def load(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class FollowupSourceModelSkillTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evidence = load("source-model-followup-two-character-skills.json")
        cls.profiles = {x["unitId"]: x for x in load("character-skill-profiles.json")["profiles"]}
        cls.runtime = {x["catalogId"]: x for x in load("unit-runtime-status.json")["entries"]}
        cls.objects = {x["rawcode"]: x for x in load("original-2323C-unit-object-stats.json")["entries"]}
        cls.listing = (COMBAT / "SourceModelUnconnectedSkillCatalog.cs").read_text(encoding="utf-8")
        cls.parser = (COMBAT / "SourceModelNextThirtySkillCatalog.cs").read_text(encoding="utf-8")
        cls.executor = (COMBAT / "CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs").read_text(encoding="utf-8")

    def test_source_identity_packet_and_ownership(self):
        batch = self.evidence
        self.assertEqual("TOOLTIP_MODEL_ONLY", batch["execution"])
        self.assertEqual("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                         batch["sourceW3xSha256"])
        self.assertEqual(2, len(batch["additionalPackets"]))
        rawcodes = set()
        for row in batch["additionalPackets"]:
            uid, raw, name = row["catalogId"], row["originalRawcode"], row["skillName"]
            with self.subTest(unit=uid):
                self.assertNotIn(raw, rawcodes)
                rawcodes.add(raw)
                self.assertIs(False, row["originalParityVerified"])
                self.assertEqual(raw, self.runtime[uid]["originalRawcode"])
                self.assertTrue(self.runtime[uid]["objectBasicAttackModel"])
                matches = [s for s in self.profiles[uid]["skills"] if s["name"] == name]
                self.assertEqual(1, len(matches))
                skill = matches[0]
                for key in ("trigger", "target", "radius"):
                    self.assertEqual(row[key], skill[key])
                if "chancePercent" in row:
                    self.assertEqual(row["chancePercent"], skill["chancePercent"])
                if "manaCost" in row:
                    self.assertEqual(row["manaCost"], skill["manaCost"])
                    self.assertEqual(row["originalManaMaximum"],
                                     float(self.objects[raw]["originalObjectFields"]["balance"]["manaN"]))
                if uid == "LI-01":
                    self.assertEqual("casterArea", skill["target"])
                    self.assertEqual("targetCenter", row["mobileCenterPolicy"])

    def test_single_source_owner_and_separate_attack_active_allowlists(self):
        entries = re.findall(r'new SourceModelUnconnectedSkillRow\(\s*"([^"]+)",\s*"([^"]+)"',
                             self.listing)
        self.assertEqual(len(entries), len({raw for _, raw in entries}))
        self.assertIn(("I-10", "h04G"), entries)
        self.assertIn(("LI-01", "h05G"), entries)
        i10 = self.listing.split('new SourceModelUnconnectedSkillRow("I-10", "h04G",')[1].split(
            'new SourceModelUnconnectedSkillRow(', 1)[0]
        self.assertIn('Array.Empty<string>(), new[] { "마나 광역 공격" }', i10)
        li01 = self.listing.split('new SourceModelUnconnectedSkillRow("LI-01", "h05G",')[1].split(
            'new SourceModelUnconnectedSkillRow(', 1)[0]
        self.assertIn('new[] { "핏빛 범위 공격", "광역 마법 공격" }, Array.Empty<string>()', li01)
        for forbidden in ("직선 마법 공격", "차지 버스트", "매 타격 스턴", "단일 스턴"):
            self.assertNotIn('new[] { "' + forbidden + '" }', i10 + li01)

    def test_caster_area_is_explicit_and_native_duplicate_is_excluded(self):
        self.assertIn('skill.Target == "casterArea"', self.parser)
        self.assertIn("!FinitePositive(skill.Radius)", self.parser)
        self.assertIn('skill.Target == "casterArea" &&', self.executor)
        self.assertIn('SpecialGradeMobilePolicy.UsesEnemyCenteredArea(catalogId)', self.executor)
        self.assertIn("SourceScriptHitShape.AttackerCircle", self.executor)
        policy = (ROOT / "Assets" / "Scripts" / "Mobile" /
                  "SpecialGradeMobilePolicy.cs").read_text(encoding="utf-8")
        self.assertIn('public const string Redfield = "LI-01"', policy)
        self.assertIn('return unitId == Marco || unitId == Redfield;', policy)
        self.assertNotIn('new SourceModelUnconnectedSkillRow("L-32", "h030",\n                new[]',
                         self.listing)
        self.assertEqual(3, len(self.evidence["deferredRelatedSkills"]))


if __name__ == "__main__":
    unittest.main()
