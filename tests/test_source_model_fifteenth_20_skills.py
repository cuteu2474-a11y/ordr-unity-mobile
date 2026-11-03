"""Batch 15: twenty new, source-owned, actually executable tooltip packets."""
import json
import pathlib
import re
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
CODE = ROOT / "Assets/Scripts/Combat"
AUDIT = json.loads((DATA / "source-model-fifteenth-20-skills.json").read_text())
PROFILES = {x["unitId"]: x for x in
            json.loads((DATA / "character-skill-profiles.json").read_text())["profiles"]}
MAP = json.loads((DATA / "original-2323C-map-base-stats.json").read_text())
RAW = {x["catalogId"]: x["originalRawcode"] for x in MAP["catalogEntries"]}
SOURCE_CALLBACKS = {x["catalogId"] for x in
                    json.loads((DATA / "original-2323C-attack-skill-batch.json").read_text())["units"]}
ALLOW = (CODE / "SourceModelUnconnectedSkillCatalog.cs").read_text()
GATE = (CODE / "SourceModelNextThirtySkillCatalog.cs").read_text()
EXACT = (CODE / "SourceModelFifteenthSkillCatalog.cs").read_text()
RUNTIME = (CODE / "CharacterCombatRuntime.SourceModelNextThirtyAttacks.cs").read_text()
SPECIAL = (CODE / "CharacterCombatRuntime.SourceModelFifteenthSkills.cs").read_text()


class FifteenthTwentySkillTests(unittest.TestCase):
    def test_all_twenty_are_distinct_mapped_source_packets(self):
        self.assertEqual(MAP["sourceW3xSha256"], AUDIT["sourceW3xSha256"])
        entries = AUDIT["skills"]
        self.assertEqual(20, len(entries))
        self.assertEqual(20, len({x["catalogId"] for x in entries}))
        self.assertEqual(16, sum(x["trigger"] in ("onAttack", "onEveryAttack")
                                 for x in entries))
        self.assertEqual(4, sum(x["trigger"] in ("activeMana", "activeHealth")
                                for x in entries))
        for row in entries:
            with self.subTest(unit=row["catalogId"]):
                self.assertEqual(row["originalRawcode"], RAW[row["catalogId"]])
                profile = PROFILES[row["catalogId"]]
                skill = profile["skills"][row["skillIndex"]]
                self.assertEqual(row["name"], skill["name"])
                self.assertEqual(row["trigger"], skill["trigger"])
                self.assertIn('"' + row["catalogId"] + '"', ALLOW)
                self.assertIn('"' + row["name"] + '"', ALLOW)
                self.assertNotIn(row["catalogId"], SOURCE_CALLBACKS,
                                 "JASS native source-owned callback would double-count damage")

    def test_complete_packet_gate_for_specials_and_shared_runtime(self):
        special = [x for x in AUDIT["skills"] if x["execution"] == "exact-special"]
        generic = [x for x in AUDIT["skills"] if x["execution"] == "generic"]
        self.assertEqual(11, len(special))
        self.assertEqual(9, len(generic))
        for row in special:
            with self.subTest(special=row["catalogId"]):
                self.assertIn('case "' + row["catalogId"] +
                              ':skill:' + str(row["skillIndex"]) + '"', EXACT)
        self.assertIn("CanModelExactSpecial(skill)", GATE)
        self.assertIn("SourceModelFifteenthProcChance", RUNTIME)
        self.assertIn("SourceModelFifteenthDamageAdjustment", RUNTIME)
        self.assertIn("SourceModelFifteenthComponentTargets", RUNTIME)
        self.assertIn("ApplySourceModelFifteenthPrimaryFollowup", RUNTIME)
        self.assertIn("ApplySourceModelAreaPacket", SPECIAL)
        self.assertIn("sourceModelStrengthProvider", SPECIAL)
        self.assertIn("target.Enemy.MaxHealth", SPECIAL)

    def test_unverified_scaling_cannot_generate_hero_attributes(self):
        self.assertIn("sourceModelStrengthProvider == null) return 0", SPECIAL)
        self.assertIn("return recorded.HasValue && recorded.Value >= 0", SPECIAL)
        self.assertIn("CanModelExactSpecial(ImportedCombatSkill skill)", EXACT)
        self.assertIn("skill.OriginalFields.Count == keys", EXACT)
        self.assertIn("effect.Fields.Count != pairs.Length / 2 + 1", EXACT)
        self.assertIn("SameValue", EXACT)
        self.assertNotIn("Grant(", SPECIAL)


if __name__ == "__main__":
    unittest.main()
