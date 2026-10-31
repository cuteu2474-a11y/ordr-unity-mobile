import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets/Scripts/Combat"
SOURCE_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"


class NextThirtySourceBatchTests(unittest.TestCase):
    def test_source_identity_and_no_duplicate_dispatch(self):
        batch = (COMBAT / "SourceModelBatchAttackCatalog.cs").read_text(encoding="utf-8")
        previous = [
            (COMBAT / name).read_text(encoding="utf-8")
            for name in ("SourceModelThirdAttackCatalog.cs",
                         "SourceModelFourthAttackCatalog.cs",
                         "SourceModelFifthAttackCatalog.cs")
        ]
        all_ids = []
        raws = []
        for code in previous + [batch]:
            matches = re.findall(
                r'new SourceScriptAttackSpec\("([^"]+)",\s*"([^"]+)"',
                code)
            if code == batch:
                matches = re.findall(
                    r'new SourceModelBatchAttackSpec\("([^"]+)",\s*"([^"]+)"',
                    code)
            all_ids.extend(i for i, _ in matches)
            raws.extend(r for _, r in matches)
        self.assertEqual(len(all_ids), 18)
        self.assertEqual(len(all_ids), len(set(all_ids)))
        self.assertEqual(len(raws), len(set(raws)))
        self.assertEqual(len([x for x in raws if len(x) == 4]), 18)
        base = (COMBAT / "SourceModelScriptAttackCatalog.cs").read_text(encoding="utf-8")
        self.assertIn(SOURCE_SHA, base)
        runtime = (COMBAT / "CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text(encoding="utf-8")
        self.assertIn("SourceModelBatchAttackCatalog.TryGet(rawcode, out batch)", runtime)
        self.assertIn("ExecuteSourceModelBatchAttack(actor, primary, batch)", runtime)

    def test_independent_source_probabilities_and_delayed_zoro(self):
        code = (COMBAT / "SourceModelBatchAttackCatalog.cs").read_text(encoding="utf-8")
        self.assertIn('Stage("Buo:45/1000", 1000, 45,', code)
        self.assertIn('Stage("Buo:125/1000", 1000, 125,', code)
        self.assertNotIn('Stage("Bsk:unconditional Bsj",', code)
        self.assertIn('Stage("Bsk:10/100 fallback Bsj", 100, 10,', code)
        self.assertIn('Circle(385, 150000)', code)
        self.assertIn('Direct(900000, 1, 1.5)', code)
        self.assertIn('null, .04)', code)
        self.assertIn('Stage("B19:17/100 bl", 100, 17,', code)
        self.assertIn('Stage("B19:10/100 bm", 100, 10,', code)
        self.assertIn('Stage("B19:25/100 Bcb from IG proxy", 100, 25,', code)
        executor = (COMBAT / "CharacterCombatRuntime.SourceModelBatchAttack.cs").read_text(encoding="utf-8")
        self.assertIn("stage.RollSides == 0", executor)
        self.assertIn("stage.RollSides > 0 && roll > stage.RollSuccess", executor)
        self.assertIn("SchedulePracticeSourceAttack", executor)
        self.assertIn("sourceBatchDelayedHitSequence", executor)
        self.assertIn("ApplySourceScriptHits", executor)
        self.assertIn("EffectDeferred", executor)
        self.assertIn('spec.CatalogId == "L-27"', executor)
        self.assertIn("primary.Enemy.Health <= primary.Enemy.MaxHealth * .15", executor)
        self.assertIn("Bsk:health-gate", executor)


    def test_attack_type_symbols_match_actual_runtime_enum(self):
        # JASS ATTACK_TYPE_NORMAL is represented by Spells (index 0), not
        # an invented Ordr323AttackType.Normal member. Prevent the same
        # CS0117 compiler error anywhere in our combat source catalogs.
        enum_source = (COMBAT / "Ordr323MapDamageMath.cs").read_text(encoding="utf-8")
        declared = re.search(
            r'public enum Ordr323AttackType\s*\{([^}]+)\}',
            enum_source, re.DOTALL)
        self.assertIsNotNone(declared)
        names = set()
        for line in declared.group(1).splitlines():
            line = line.split("//", 1)[0].strip()
            for member in line.split(","):
                name = member.split("=", 1)[0].strip()
                if name:
                    self.assertRegex(name, r"^[A-Za-z_][A-Za-z0-9_]*$")
                    names.add(name)
        self.assertIn("Spells", names)
        self.assertNotIn("Normal", names)
        mismatches = {}
        for file in (ROOT / "Assets/Scripts").rglob("*.cs"):
            contents = file.read_text(encoding="utf-8")
            references = set(re.findall(
                r'\bOrdr323AttackType\.([A-Za-z_][A-Za-z0-9_]*)',
                contents))
            if references - names:
                mismatches[str(file.relative_to(ROOT))] = sorted(references - names)
        self.assertFalse(mismatches, f"Undefined Ordr323AttackType references: {mismatches}")
        for filename in ("SourceModelBatchAttackCatalog.cs",
                         "SourceModelFourthAttackCatalog.cs",
                         "SourceModelFifthAttackCatalog.cs"):
            contents = (COMBAT / filename).read_text(encoding="utf-8")
            self.assertIn("Ordr323AttackType.Spells", contents)
            self.assertNotIn("Ordr323AttackType.Normal", contents)

    def test_coverage_report_never_claims_whole_unit_skill_complete(self):
        report = json.loads((ROOT / "data/original-2323C-next-30-runtime-coverage.json").read_text(encoding="utf-8"))
        self.assertEqual(report["sourceJassSha256"], SOURCE_SHA)
        self.assertEqual(len(report["units"]), 30)
        self.assertEqual(len({u["id"] for u in report["units"]}), 30)
        self.assertEqual(sum(u["damagePacketsModeled"] for u in report["units"]), 18)
        self.assertTrue(all(u["allOriginalSkillsComplete"] is False for u in report["units"]))
        self.assertEqual(report["apkStatus"], "requested-not-built")


if __name__ == "__main__":
    unittest.main()
