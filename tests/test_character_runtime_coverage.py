"""Evidence-only coverage reconciled with the pinned catalog manifest."""
from copy import deepcopy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from build_character_runtime_coverage import build, load  # noqa: E402


class CharacterRuntimeCoverageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inputs = [load(filename) for filename in (
            "units.json", "wiki-unit-catalog-extension.json",
            "unit-runtime-status.json", "character-skill-profiles.json",
            "original-2323C-unit-object-stats.json",
            "original-fusion-object-reconciliation.json",
        )]
        cls.current = build(*cls.inputs)

    def test_full_registry_and_source_inherited_forms(self):
        s = self.current["summary"]
        base, extension = self.inputs[:2]
        expected_ids = {row["id"] for row in base["entries"] + extension["entries"]}
        self.assertEqual(expected_ids, {row["id"] for row in self.current["entries"]})
        self.assertEqual(extension["expectedMergedEntries"], s["catalogIdentities"])
        self.assertEqual(277, s["sourceCatalogIdentities"])
        self.assertEqual(len(extension["entries"]), s["wikiExtensionIdentities"])
        self.assertEqual(s["catalogIdentities"] - 5, s["independentRosterIdentities"])
        self.assertEqual(5, s["alternateCombatForms"])
        self.assertEqual(274, s["sourceObjectBasicAttackModels"])
        self.assertEqual(200, s["sourceTooltips"])
        self.assertEqual(5, s["formInheritedTooltips"])
        self.assertEqual(s["catalogIdentities"] - 200 - 5,
                         s["identitiesWithoutOwnOrInheritedTooltip"])
        self.assertEqual(0, s["fullyOriginalCombatVerified"])
        self.assertEqual(44, s["byGrade"]["희귀함"])
        self.assertEqual(41, s["byGrade"]["전설"])
        self.assertEqual(37, s["byGrade"]["초월함"])
        self.assertEqual(23, s["byGrade"]["히든"])
        self.assertEqual(14, s["byGrade"]["신비함"])
        self.assertEqual(2, s["byGrade"]["재료"])

    def test_original_battle_model_is_not_inflated_into_complete_skills(self):
        rows = {row["id"]: row for row in self.current["entries"]}
        hanma = rows["MY-14"]
        self.assertEqual("h0C2", hanma["sourceRawcode"])
        self.assertTrue(hanma["sourceBasicAttackModel"])
        self.assertFalse(hanma["fullyOriginalCombatVerified"])
        self.assertEqual(0, hanma["connectedSourceModelEffectCount"])
        ship = rows["PS-01"]
        self.assertEqual("히든", ship["grade"])
        self.assertEqual("h060", ship["sourceRawcode"])
        self.assertFalse(rows["R-44"]["sourceBasicAttackModel"])
        self.assertFalse(rows["SM-01"]["sourceBasicAttackModel"])
        self.assertFalse(rows["L-40"]["sourceBasicAttackModel"])
        self.assertFalse(rows["L-41"]["sourceBasicAttackModel"])
        for unit in ("TR-31", "TR-32", "TR-33", "TR-34", "TR-35"):
            self.assertFalse(rows[unit]["independentRosterIdentity"])
            self.assertTrue(rows[unit]["inheritedTooltipProfile"])
            self.assertFalse(rows[unit]["sourceBasicAttackModel"])
        self.assertTrue(all(not row["fullyOriginalCombatVerified"]
                            for row in self.current["entries"]))

    def test_committed_reproducible_snapshot(self):
        committed = json.loads((ROOT / "data" / "character-runtime-coverage.json").read_text())
        self.assertEqual(self.current, committed)
        # Changing only the report cannot invent missing original skill data.
        self.assertEqual("S2 2.323[C]", committed["gameVersion"])

    def test_reward_box_addition_has_source_identity_but_no_combat_approval(self):
        box = next(row for row in self.current["entries"] if row["id"] == "TW-01")
        self.assertEqual(("h0BR", "support-object", "초월함"),
                         (box["sourceRawcode"], box["entryKind"], box["grade"]))
        self.assertFalse(box["sourceBasicAttackModel"])
        self.assertFalse(box["ownTooltipProfile"])
        self.assertFalse(box["inheritedTooltipProfile"])
        self.assertEqual(0, box["connectedSourceModelEffectCount"])
        self.assertFalse(box["fullyOriginalCombatVerified"])
        evidence = load("original-2323C-source-material-acquisition.json")
        choice = next(row for row in evidence["story"]["choices"]
                      if row["function"] == "CSM")
        self.assertEqual(["h0BR"], choice["resultUnitRawcodes"])

    def test_duplicate_or_missing_identities_cannot_hide_behind_counts(self):
        for index, message in ((0, "Duplicate"), (2, "Duplicate")):
            inputs = deepcopy(self.inputs)
            inputs[index]["entries"].append(deepcopy(inputs[index]["entries"][0]))
            with self.subTest(input=index), self.assertRaisesRegex(ValueError, message):
                build(*inputs)
        inputs = deepcopy(self.inputs)
        inputs[1]["entries"].pop()
        with self.assertRaisesRegex(ValueError, "Missing expanded catalog"):
            build(*inputs)

    def test_cli_verify_is_read_only_and_rejects_stale_or_missing_snapshot(self):
        script = ROOT / "tools" / "build_character_runtime_coverage.py"
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "coverage.json"
            command = [sys.executable, str(script), "--output", str(target)]
            missing = subprocess.run(command + ["--verify"], capture_output=True)
            self.assertNotEqual(0, missing.returncode)
            self.assertFalse(target.exists())
            generated = subprocess.run(command, capture_output=True)
            self.assertEqual(0, generated.returncode, generated.stderr)
            before = target.read_bytes()
            self.assertNotIn(b"\r", before)
            verified = subprocess.run(command + ["--verify"], capture_output=True)
            self.assertEqual(0, verified.returncode, verified.stderr)
            self.assertEqual(before, target.read_bytes())
            stale = json.loads(before)
            stale["summary"]["catalogIdentities"] -= 1
            target.write_text(json.dumps(stale), encoding="utf-8")
            stale_bytes = target.read_bytes()
            rejected = subprocess.run(command + ["--verify"], capture_output=True)
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn(b"outdated", rejected.stderr)
            self.assertEqual(stale_bytes, target.read_bytes())


if __name__ == "__main__":
    unittest.main()
