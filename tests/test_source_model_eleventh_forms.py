"""Batch 11: sourced forms are same-owner states, with conservative gates."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets/Scripts/Combat"
FORMS = (COMBAT / "CharacterCombatRuntime.SourceModelEleventhForms.cs").read_text()
TEN = (COMBAT / "CharacterCombatRuntime.SourceModelTenthForms.cs").read_text()
ATTACK = (COMBAT / "CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text()
EFFECTS = (COMBAT / "CharacterCombatRuntime.SourceModelEffects.cs").read_text()
OBJECTS = (COMBAT / "SourceModelBasicCombatRules.cs").read_text()
PROFILES = {p["unitId"]: p for p in json.loads(
    (ROOT / "data/character-skill-profiles.json").read_text())['profiles']}


class EleventhSourceFormTests(unittest.TestCase):
    def test_nika_cost_duration_and_unmapped_alternate_are_not_merged(self):
        nika = PROFILES["E-01"]["skills"][0]
        alternate = PROFILES["E-02"]["skills"][0]
        self.assertEqual((115, 4.25),
                         (nika["healthCost"], nika["components"][0]["durationSeconds"]))
        self.assertEqual((115, 4.25),
                         (alternate["healthCost"], alternate["components"][0]["durationSeconds"]))
        self.assertIn('SourceModelTenthFormProfile(actor, "E-01", "H0BK")', FORMS)
        self.assertNotIn('SourceModelTenthFormProfile(actor, "E-02", "H0BK")', FORMS)
        self.assertIn('"giant", 4.25', FORMS)

    def test_bonney_base_and_nika_are_distinct_original_attack_objects(self):
        source = PROFILES["TR-21"]["skills"][0]
        self.assertEqual(150, source["manaCost"])
        self.assertTrue(source["components"][0]["untilManaExhausted"])
        self.assertIn('"H0BY"', OBJECTS)
        self.assertIn('SetSourceForm(InstanceKey(actor.Id), "H0BY")', FORMS)
        self.assertIn('resource.SetSourceModelFormManaPool(100, 100)', FORMS)
        self.assertIn('previous.Form == "nika"', TEN)
        self.assertIn('ClearSourceForm(InstanceKey(id))', TEN)
        self.assertIn('string form = rules.SourceFormRawcodeOf(attacker)', EFFECTS)
        self.assertIn('form == "H0BY" || form == "h0AF" ? form : rawcode', EFFECTS)
        self.assertIn('rawcode == "H0BX"', ATTACK)

    def test_king_requires_actual_stacks_and_spends_exact_mana(self):
        skill = PROFILES["LI-10"]["skills"][0]
        sword = PROFILES["LI-10"]["skills"][2]
        self.assertEqual((50, 35, 33),
                         (skill["requiredTargetArmorBreak"],
                          skill["manaAtLeast"], skill["manaCost"]))
        self.assertEqual((10, 4),
                         (sword["chancePercent"], sword["components"][1]["stacks"]))
        self.assertIn('ArmorBreakStacksOf(victim.Id) >= 50', FORMS)
        self.assertIn('resource.Spend(0, 33)', FORMS)
        self.assertIn('"executioner", 1.25', FORMS)
        self.assertIn('FindSourceSkill(actor, "잔혹한 검술")', FORMS)
        self.assertIn('sourceModelKingExecutionUsed.Add(actor.Id)', FORMS)

    def test_marco_requires_external_witness_no_fabricated_toggle(self):
        phoenix, human = PROFILES["LI-03"]["skills"][:2]
        self.assertEqual((18, 10, 15),
                         (phoenix["chancePercent"], phoenix["healthCost"], phoenix["minHealth"]))
        self.assertEqual((15, 7, 15),
                         (human["chancePercent"], human["healthCost"], human["minHealth"]))
        self.assertIn('ConfigureSourceModelMarcoFormEvidence', FORMS)
        self.assertIn('sourceModelMarcoVerifiedForm == null', FORMS)
        self.assertIn('!SourceModelTenthFormProfile(actor, "LI-03", "h08O")', FORMS)
        self.assertIn('IsOriginalPoneglyphChanneling(id)', FORMS)
        self.assertIn('resource.Health < 15', FORMS)
        self.assertNotIn('resource.Health - cost < 15', FORMS)
        self.assertIn('skill.Components[1].Number("bonusDamagePercent") != 40', FORMS)
        self.assertIn('SourceModelSafeFixedAmount(firstHpLoss * .40', FORMS)
        self.assertIn('skill.Components[1].Number("targetCurrentHealthPercent") != 30', FORMS)
        self.assertIn('skill.Components[2].Number("durationSeconds") != 2', FORMS)

    def test_one_pipeline_and_previous_native_paths_remain(self):
        self.assertIn('TryCastSourceModelEleventhForm(actor, skillId, out result)', TEN)
        self.assertIn('ExecuteSourceModelEleventhFormDeclaredAttack(actor, primary, rawcode)', TEN)
        self.assertIn('AdvanceSourceModelEleventhBonney', EFFECTS)
        native = (COMBAT / "CharacterCombatRuntime.SourceModelNativeCharacters.cs").read_text()
        self.assertIn('"H0B6"', native)
        self.assertIn('"h0AD"', native)


if __name__ == "__main__":
    unittest.main()
