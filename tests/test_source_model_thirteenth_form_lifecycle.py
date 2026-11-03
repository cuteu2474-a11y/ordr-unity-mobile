"""Batch 13: tooltip-timed Chopper form and lane-scoped summon/root cleanup."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets/Scripts/Combat"
NATIVE = (COMBAT / "CharacterCombatRuntime.SourceModelNativeCharacters.cs").read_text()
EFFECTS = (COMBAT / "CharacterCombatRuntime.SourceModelEffects.cs").read_text()
RUNTIME = (COMBAT / "CharacterCombatRuntime.cs").read_text()
ELEVEN = (COMBAT / "CharacterCombatRuntime.SourceModelEleventhForms.cs").read_text()
TEMPORARY = (COMBAT / "CharacterCombatRuntime.SourceModelTemporarySkills.cs").read_text()
PROFILE = {entry["unitId"]: entry for entry in json.loads(
    (ROOT / "data/character-skill-profiles.json").read_text())["profiles"]}


class ThirteenthLifecycleTests(unittest.TestCase):
    def test_chopper_source_duration_and_original_object_are_gated(self):
        skill = PROFILE["TR-23"]["skills"][0]
        self.assertEqual("럼블볼 폭주", skill["name"])
        self.assertEqual(150, skill["manaCost"])
        self.assertEqual(4, skill["components"][0]["durationSeconds"])
        self.assertEqual(400, skill["components"][1]["radius"])
        self.assertIn('actor.Mapping.AttackSpec.UnitRawCode != "H091"', NATIVE)
        self.assertIn('sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "H0B6")', NATIVE)
        self.assertIn('ExpiresAt = effectExecutor.Time + 4', NATIVE)
        self.assertIn('SourceModelTimeAdvancing += AdvanceSourceChopperBursts', EFFECTS)

    def test_no_invented_mana_payment_or_permanent_full_pool_loop(self):
        self.assertIn('sourceChopperAwaitingManaReset.Add(actor.Id)', NATIVE)
        self.assertIn('if (resource.Mana < 150)', NATIVE)
        self.assertIn('sourceChopperAwaitingManaReset.Remove(actor.Id)', NATIVE)
        self.assertIn('sourceModelRules.ClearSourceForm(InstanceKey(id))', NATIVE)
        chopper = NATIVE.split('private void ExecuteChopperSourceAttack', 1)[1].split(
            'private void ExecuteVegapunkSourceAttack', 1)[0]
        self.assertNotIn('resource.Spend(', chopper)
        self.assertNotIn('resource.SetMana(0)', chopper)

    def test_lane_change_releases_native_form_roots_and_temporary_attacks(self):
        lane = RUNTIME.split('public void RefreshAfterCommittedStoryLaneChange', 1)[1].split(
            'public void ConfigureVerifiedUnitAttackProvider', 1)[0]
        for call in ('EndSourceChopperBurst(instanceId, true)',
                     'ForgetSourceModelEleventhActor(instanceId)',
                     'RefreshSourceTemporaryActors()'):
            self.assertIn(call, lane)
        self.assertIn('SourceModelTwelfthForgetOwner(id)', ELEVEN)
        self.assertIn('UnregisterSourceIndependentAttack(SummonAttackKey(summon.Id))', TEMPORARY)


if __name__ == "__main__":
    unittest.main()
