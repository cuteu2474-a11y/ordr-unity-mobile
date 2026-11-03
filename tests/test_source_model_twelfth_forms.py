"""Batch 12: follow-up form packets must keep source conditions intact."""
import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "Assets/Scripts/Combat"
TWELVE = (COMBAT / "CharacterCombatRuntime.SourceModelTwelfthForms.cs").read_text()
ELEVEN = (COMBAT / "CharacterCombatRuntime.SourceModelEleventhForms.cs").read_text()
NATIVE = (COMBAT / "CharacterCombatRuntime.SourceModelOriginalSpecials.cs").read_text()
DISPLAY = (ROOT / "Assets/Scripts/Unity/OrdrUnitWorldPresenter.cs").read_text()
SKILLS = {entry["unitId"]: entry["skills"] for entry in json.loads(
    (ROOT / "data/character-skill-profiles.json").read_text())["profiles"]}


class TwelfthFormTests(unittest.TestCase):
    def test_bonney_source_threshold_exact_and_target_bound(self):
        bonney = SKILLS["TR-21"]
        self.assertEqual(40, bonney[5]["requiredStacks"])
        self.assertEqual(40, bonney[6]["requiredStacks"])
        self.assertEqual((500000, 8), (bonney[5]["components"][0]["amount"],
                                       bonney[5]["components"][0]["plusTargetCurrentHealthPercent"]))
        self.assertEqual((150000, 12), (bonney[6]["components"][0]["amount"],
                                        bonney[6]["components"][0]["plusTargetCurrentHealthPercent"]))
        self.assertIn("sourceModelBonneyAgeTriggered.Add(key)", TWELVE)
        self.assertIn("SourceModelTwelfthBonneyAgeAdded(actor, victim, existing,", ELEVEN)
        self.assertIn("sourceModelBonneyRootOwner.ContainsKey(enemyId)", TWELVE)
        self.assertIn("SourceModelTwelfthBonneyRooted(enemyId)", NATIVE)
        self.assertIn("combat.PracticeBonneyScaleReduced(entry.Id)", DISPLAY)

    def test_bonney_area_stun_cooldown_source_not_invented(self):
        source = SKILLS["TR-21"][4]
        self.assertEqual((12, 15, 600),
                         (source["chancePercent"], source["absoluteCooldownSeconds"], source["radius"]))
        self.assertIn('sourceModelBonneyStunReadyAt[actor.Id] = effectExecutor.Time + 15', TWELVE)
        self.assertIn('SourceModelTwelfthBonneyStun(actor, primary)', ELEVEN)

    def test_king_fire_slow_ticks_share_damage_and_lanes(self):
        fire, slow = SKILLS["LI-10"][3:5]
        self.assertEqual((14.25, 42500, .27, 2.7),
                         (fire["chancePercent"], fire["components"][0]["damagePerTick"],
                          fire["components"][0]["intervalSeconds"],
                          fire["components"][0]["durationSeconds"]))
        self.assertEqual((300000, 3, 45),
                         (slow["components"][0]["perSecond"],
                          slow["components"][0]["durationSeconds"],
                          slow["components"][1]["percent"]))
        self.assertIn("SourceModelTwelfthKingSlow(actor, primary)", ELEVEN)
        self.assertIn("SourceModelTwelfthKingFire(actor, primary)", ELEVEN)
        self.assertIn("CanFinishSourceModelDelayedAttack(actor.Id, lane, revision)", TWELVE)
        self.assertIn("latest != revision", TWELVE)
        self.assertIn("for (int tick = 1; tick <= 10; tick++)", TWELVE)

    def test_original_unmapped_and_native_transform_guards_preserved(self):
        self.assertNotIn('SourceModelTenthFormProfile(actor, "E-02", "H0BK")', ELEVEN)
        native_forms = (COMBAT / "CharacterCombatRuntime.SourceModelNativeCharacters.cs").read_text()
        self.assertIn('"H0B6"', native_forms)
        self.assertIn('"h0AD"', native_forms)


if __name__ == "__main__":
    unittest.main()
