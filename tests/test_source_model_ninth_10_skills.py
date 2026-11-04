"""Ninth offline tooltip batch: counters, committed hits, timed projectiles."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets/Scripts/Combat"
PROFILE = json.loads((ROOT / "data/character-skill-profiles.json").read_text())
RUNTIME = json.loads((ROOT / "data/character-runtime-coverage.json").read_text())

SKILLS = {
    "L-39": ("h0CM", ["백발백중"]),
    "L-28": ("h03G", ["해류 한판 업어치기"]),
    "I-01": ("h04C", ["기본 공격 범위 추가"]),
    "I-06": ("h04Q", ["기본 공격 추가"]),
    "MY-02": ("h0BF", ["오니", "할퀴기", "휩쓸기"]),
    "LI-06": ("h08Q", ["확정 단일 스턴"]),
    "SR-01": ("h09Y", ["피스톨 키스", "피스톨 키스 6회 발동 보너스"]),
}


class NinthTenSkillModelTests(unittest.TestCase):
    def test_exact_source_owner_and_ten_names(self):
        rows = {row["unitId"]: row for row in PROFILE["profiles"]}
        runtime = {row["id"]: row for row in RUNTIME["entries"]}
        self.assertEqual(sum(len(names) for _, names in SKILLS.values()), 10)
        source = (COMBAT / "CharacterCombatRuntime.SourceModelNinthSkills.cs").read_text()
        for uid, (rawcode, names) in SKILLS.items():
            self.assertEqual(runtime[uid]["sourceRawcode"], rawcode)
            self.assertIn('{' + f'"{rawcode}", "{uid}"' + '}', source)
            available = {x["name"] for x in rows[uid]["skills"]}
            for name in names:
                self.assertIn(name, available)
                self.assertIn('"' + name + '"', source)

    def test_hit_counter_runs_only_after_committed_positive_damage(self):
        pipeline = (COMBAT / "CombatAttackPipeline.cs").read_text()
        committed = pipeline.index("resolution.TryAttack(attackerInstanceId, targetId")
        callback = pipeline.index("practiceSuccessfulBasicHit(attackerInstanceId")
        self.assertLess(committed, callback)
        self.assertIn("if (actual > 0 && practiceSuccessfulBasicHit != null)", pipeline)
        model = (COMBAT / "CharacterCombatRuntime.SourceModelNinthSkills.cs").read_text()
        self.assertIn("SourceModelNinthIncrement(id, skill.Id, 4)", model)
        self.assertIn("!ReferenceEquals(entry.Target, current)", model)

    def test_projectile_cooldown_and_lifetime_are_shared_with_combat_clock(self):
        model = (COMBAT / "CharacterCombatRuntime.SourceModelNinthSkills.cs").read_text()
        effects = (COMBAT / "CharacterCombatRuntime.SourceModelEffects.cs").read_text()
        self.assertIn("AdvanceSourceModelNinthProjectiles", effects)
        self.assertIn('ConfigurePracticeSuccessfulBasicHit((attacker, rawcode, target, actual) => {', effects)
        self.assertIn('OnSourceModelNinthSuccessfulBasicHit(attacker, rawcode, target, actual)', effects)
        self.assertIn('OnSourceModelTwentyFifthSuccessfulBasicHit(attacker, rawcode, target, actual)', effects)
        self.assertIn('sourceModelNinthDue[key] = Time + skill.Number("absoluteCooldownSeconds").Value', model)
        self.assertIn("sourceModelNinthProjectiles.RemoveAt(i)", model)

    def test_eighth_and_older_skills_are_not_duplicate_ninth_owners(self):
        model = (COMBAT / "CharacterCombatRuntime.SourceModelNinthSkills.cs").read_text()
        self.assertNotIn('{"h02X", "L-15"}', model)  # previously approved
        self.assertNotIn('{"h058", "E-04"}', model)  # previously approved
        self.assertNotIn('{"H09F", "TR-20"}', model)
        self.assertNotIn('{"H0BD", "TR-18"}', model)
        self.assertNotIn('{"H08Y", "TR-28"}', model)  # unmet transform requirement

    def test_ninth_actor_and_enemy_cleanup_hooks(self):
        world = (COMBAT / "CharacterCombatRuntime.cs").read_text()
        model = (COMBAT / "CharacterCombatRuntime.SourceModelNinthSkills.cs").read_text()
        self.assertEqual(world.count("ForgetSourceModelNinthActor("), 2)
        self.assertEqual(world.count("ForgetSourceModelNinthEnemy("), 1)
        self.assertIn("sourceModelNinthPistolActive.Remove(id)", model)


if __name__ == "__main__":
    unittest.main()
