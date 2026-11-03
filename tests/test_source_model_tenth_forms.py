"""Batch 10 regression: source-backed transformation state and eligibility."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets" / "Scripts" / "Combat"
SOURCE = (COMBAT / "CharacterCombatRuntime.SourceModelTenthForms.cs").read_text()
ACTIVES = (COMBAT / "CharacterActiveAbilities.cs").read_text()
CAST = (COMBAT / "CharacterCombatRuntime.SourceModelTemporarySkills.cs").read_text()
ROUTER = (COMBAT / "CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text()
AUTOS = (COMBAT / "CharacterCombatRuntime.SourceModelResources.cs").read_text()
EFFECTS = (COMBAT / "CharacterCombatRuntime.SourceModelEffects.cs").read_text()
PROFILES = {
    x["unitId"]: x for x in json.loads(
        (ROOT / "data/character-skill-profiles.json").read_text())["profiles"]
}


class TenthSourceFormTests(unittest.TestCase):
    def test_exact_source_forms_and_resource_costs(self):
        doffy = PROFILES["TR-03"]["skills"][0]
        self.assertEqual("activeMana", doffy["trigger"])
        self.assertEqual(150, doffy["manaCost"])
        self.assertEqual(4.5, doffy["components"][0]["durationSeconds"])
        self.assertEqual(.03, doffy["components"][0]["attackIntervalReductionSeconds"])
        self.assertEqual(60, doffy["components"][1]["amount"])
        self.assertEqual("haki", doffy["components"][2]["mode"])
        forest = PROFILES["TR-06"]["skills"][0]
        self.assertEqual(130, forest["manaCost"])
        self.assertEqual(7, forest["components"][0]["durationSeconds"])
        self.assertEqual(400, forest["components"][0]["basicAttackSplashRadius"])
        self.assertEqual(1, forest["components"][1]["stacks"])

    def test_franky_enhancement_never_unlocked_by_default(self):
        enhanced = PROFILES["TR-28"]["enhancedChanges"]
        self.assertEqual(1, len(enhanced))
        self.assertEqual("unlockTransform", enhanced[0]["type"])
        self.assertEqual(150, enhanced[0]["manaCost"])
        self.assertEqual(6, enhanced[0]["durationSeconds"])
        self.assertEqual(4000000, enhanced[0]["onTransformSkill"]["magicalDamage"])
        self.assertIn("sourceModelFrankyEnhancedEligibility == null", SOURCE)
        self.assertIn("!sourceModelFrankyEnhancedEligibility(actor.Id)", SOURCE)
        self.assertIn("ConfigureSourceModelFrankyEnhancedEligibility", SOURCE)

    def test_form_only_attack_packets_require_real_state(self):
        self.assertIn('form == "awakening"', SOURCE)
        self.assertIn('form == "frankyGeneral"', SOURCE)
        self.assertIn('skill.Id != "TR-03:skill:4"', SOURCE)
        self.assertIn('beam.Id != "TR-28:skill:3"', SOURCE)
        self.assertIn('sword.Id != "TR-28:skill:4"', SOURCE)
        self.assertIn('sourceModelRules.NextScriptRoll(1000000) < 330000', SOURCE)
        self.assertIn('!SourceRollSkill(beam)', SOURCE)

    def test_one_identity_and_source_timer(self):
        self.assertIn("private readonly Dictionary<long, SourceModelTenthFormState>", SOURCE)
        self.assertIn("LaneRevision = SourceModelLaneRevision(actor.Id)", SOURCE)
        self.assertIn("sourceModelTenthForms.Remove(id)", SOURCE)
        self.assertIn("sourceModelForestNutrients.Remove(id)", SOURCE)
        self.assertIn('status.RemoveSource("practice-form:interval:" + id)', SOURCE)
        self.assertIn("SourceModelTenthActiveForm(id)", CAST)
        self.assertIn("ForgetSourceModelTenthForm(ownerId)", CAST)

    def test_registered_in_single_reusable_pipeline(self):
        self.assertIn("TryCastSourceModelTenthForm(caster, skillId, out result)", CAST)
        self.assertIn("SourceModelTimeAdvancing += AdvanceSourceModelTenthForms", EFFECTS)
        self.assertIn("ExecuteSourceModelTenthFormDeclaredAttack(actor, primary, rawcode)", ROUTER)
        self.assertIn("TryAutoCastSourceModelTenthForm(actor)", AUTOS)
        self.assertIn("TryCastActiveSkill(actor.Id, id, null)", SOURCE)
        self.assertIn("skill.Components.Count > 0", ACTIVES)

    def test_existing_native_and_complete_flat_forms_stay_intact(self):
        native = (COMBAT / "CharacterCombatRuntime.SourceModelNativeCharacters.cs").read_text()
        self.assertIn('sourceNativeForms[actor.Id] = "쵸파 폭주형', native)
        self.assertIn("SourceModelNativeCharacterRules.KaidoTransformRollPercent", native)
        self.assertIn('caster.Catalog.Id != "TR-18"', CAST)
        self.assertIn('"블랙 맘바 각성"', CAST)


if __name__ == "__main__":
    unittest.main()
