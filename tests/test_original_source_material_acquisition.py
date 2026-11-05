"""Pinned source-material identity, acquisition gates and CWR safety checks."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"


def read(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class OriginalSourceMaterialAcquisitionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = read("original-2323C-source-material-acquisition.json")
        cls.units = {r["id"]: r for r in read("units.json")["entries"]}
        cls.original = {r["catalogId"]: r for r in
                        read("original-2323C-map-base-stats.json")["catalogEntries"]}
        cls.registry = read("original-jass-fusion-registry.json")
        cls.blocked = read("original-fusion-unconnected-review.json")

    def test_exact_source_ids_are_noncombat_materials_not_free_rewards(self):
        self.assertEqual("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                         self.audit["sourceW3xSha256"])
        # Registry is pinned by the W3X archive hash, not JASS text SHA.
        self.assertEqual(self.registry["sourceSha256"], self.audit["sourceW3xSha256"])
        self.assertEqual("96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d",
                         self.audit["sourceJassSha256"])
        self.assertEqual("nightmare-otherworld-only", self.audit["mode"])
        expected = {"KM-01": ("h04S", "초월함", "Avul,A07O", 30),
                    "LT-01": ("h06G", "재료", "A0K9,A0OV,A0JE,A080,Avul", 14)}
        for cid, (raw, grade, abilities, uses) in expected.items():
            with self.subTest(cid=cid):
                entry = self.units[cid]
                original = self.original[cid]
                self.assertEqual((entry["grade"], original["grade"]), (grade, grade))
                self.assertEqual(original["originalRawcode"], raw)
                self.assertIsNone(original["attack1"])
                self.assertFalse(original["combatParityVerified"])
                self.assertEqual(original["originalAbilityList"], abilities)
                self.assertEqual(
                    sum(int(c["count"]) for r in self.registry["registrations"]
                        for c in r["conditions"]
                        if c["kind"] == "UNIT" and c["rawcode"] == raw), uses)
        self.assertEqual(277, len(self.units))
        self.assertEqual(277, len(self.original))

    def test_story_choice_requires_observed_reward_and_user_selection(self):
        story = self.audit["story"]
        self.assertEqual(("CQB", "CSN"),
                         (story["startRewardFunction"], story["conversionFunction"]))
        self.assertEqual(("e01A", "h04S"),
                         (story["inputUnitRawcode"], story["resultUnitRawcode"]))
        self.assertEqual(1, story["count"])
        self.assertEqual((-2016, -1824, -5760, -5600),
                         (story["triggerRectBounds"]["minX"],
                          story["triggerRectBounds"]["maxX"],
                          story["triggerRectBounds"]["minY"],
                          story["triggerRectBounds"]["maxY"]))
        source = (ROOT / "Assets/Scripts/Combat/Original323SourceMaterialRewardWorld.cs").read_text()
        controller = (ROOT / "Assets/Scripts/Unity/OrdrCharacterCombatController.cs").read_text()
        panel = (ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs").read_text()
        self.assertIn('"CQB", StoryWispRawcode, 1', source)
        self.assertIn('Original323BossRoundRules.TrustedRuntimeEvidence', source)
        self.assertIn('PendingTranscendWisps < 1', source)
        self.assertIn('TryExchangeOnce(receipt,', source)
        self.assertIn('RollbackUncommittedSpawn', source)
        self.assertIn('controller.PendingAllStoryTranscendWisps > 0', panel)
        self.assertIn('RequestOriginalTranscendKumaChoice()', controller)
        self.assertEqual(["CSM", "CSN", "CSO"],
                         [x["function"] for x in story["choices"]])
        self.assertEqual([["h0BR"], ["h04S"], ["h05X", "h060"]],
                         [x["resultUnitRawcodes"] for x in story["choices"]])
        self.assertIn('Original323TranscendWispOutcome.RewardBox', source)
        self.assertIn('Original323TranscendWispOutcome.RayleighAndShip', source)
        self.assertIn('TryChooseRayleighAndShip', source)
        self.assertIn('RequestOriginalTranscendRewardBoxChoice()', controller)
        self.assertIn('RequestOriginalTranscendRayleighShipChoice()', controller)
        self.assertIn('DrawOriginalTranscendWispChoiceButtons()', panel)
        wiki = read("wiki-unit-catalog-extension.json")
        reward_box = next(e for e in wiki["entries"] if e["id"] == "TW-01")
        self.assertEqual("h0BR", reward_box["originalSourceRawcode"])
        self.assertEqual("support-object", reward_box["catalogEntryKind"])
        self.assertIn('ConfigureVerifiedOriginalSourceMaterialEvents(', controller)
        self.assertNotIn('new Original323SourceMaterialRewardWorld(', panel)
        self.assertNotIn('GameMode.NightmareOtherworld', source)

    def test_original_box_rolls_and_unique_items_replace_provisional_values(self):
        source = (ROOT / "Assets/Scripts/Combat/Original323SourceMaterialRewardWorld.cs").read_text()
        panel = (ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs").read_text()
        items = (ROOT / "Assets/Scripts/Mobile/Original323FusionSourceItemLedger.cs").read_text()
        box = self.audit["status"]["mobileDirectBoxRedemptions"]
        self.assertEqual((15000, 30000, 14, 20),
                         (box["resources"]["goldMin"], box["resources"]["goldMax"],
                          box["resources"]["woodMin"], box["resources"]["woodMax"]))
        self.assertTrue(box["resources"]["inclusiveIndependentRolls"])
        self.assertEqual(4, box["uniqueItems"]["woodBonusOnSuccessfulChoice"])
        self.assertEqual(["AI02", "AI00", "AI01", "AI03", "AI18", "AI21",
                          "AI17", "AI19", "AI16", "AI20"],
                         box["uniqueItems"]["choiceItemRawcodes"])
        self.assertEqual(10, len(box["uniqueItems"]["choiceAbilityRawcodes"]))
        self.assertIn("public const int BoxGoldMin = 15000;", source)
        self.assertIn("public const int BoxWoodMax = 20;", source)
        self.assertIn("public const int BoxItemWoodBonus = 4;", source)
        self.assertIn("TryChooseBoxItem", source)
        self.assertIn("Original323FusionSourceItemLedger.ItemShells", panel)
        self.assertIn('"AI20"', items)
        self.assertNotIn("ProvisionalBoxGold", source)
        self.assertNotIn("RandomOnlyWoodCost", source)

    def test_tokens_require_source_parent_event_not_money_gambling(self):
        source = (ROOT / "Assets/Scripts/Combat/Original323SourceMaterialRewardWorld.cs").read_text()
        tokens = self.audit["tokenSources"]
        self.assertEqual({"CJK", "CJM", "CGk", "CCk", "CKJ"},
                         {x["function"] for x in tokens})
        self.assertEqual(2000, next(x for x in tokens if x["function"] == "CJK")
                         ["sourceUnitBalanceGoldCost"])
        self.assertEqual(4, next(x for x in tokens if x["function"] == "CJK")
                         ["sourceUnitBalanceWoodCost"])
        self.assertIn('function != "CJK" && function != "CJM"', source)
        self.assertIn('function != "CCk" && function != "CKJ"', source)
        self.assertIn('confirmedTokenCount == 2 && function != "CJK"', source)
        self.assertIn('TryConfirmSettledOriginalEvent(eventId, function,', source)
        self.assertIn('TryGrantOnce(receipt, new ResourceBundle())', source)
        self.assertNotIn('case "CJB"', source)
        self.assertNotIn('case "CJC"', source)

    def test_object_linking_reduces_only_blocker_reason_not_craft_permissions(self):
        self.assertEqual(167, self.blocked["summary"]["liveExecutableRecipes"])
        self.assertEqual(98, self.blocked["summary"]["blockedRecipes"])
        self.assertEqual(3, self.blocked["summary"]["blockedByReason"]["unmapped-original-unit"])
        source_only = read("original-fusion-source-only-materials.json")
        self.assertEqual(3, source_only["summary"]["distinctSourceOnlyObjects"])
        self.assertEqual({"h05Y", "h0AN", "h0C2"},
                         {x["rawcode"] for x in source_only["entries"]})
        csharp = (ROOT / "Assets/Scripts/Mobile/Original323CommonFusionPort.cs").read_text()
        # The exact mobile-result outcomes are distinct from the old reviewed
        # native callback group. Source objects never imply free acquisition.
        reviewed = csharp.split('string[,] ReviewedRestricted = {', 1)[1].split('\n        };', 1)[0]
        self.assertNotIn('{ "TR06",', reviewed)
        self.assertIn('{ "A0QG", "h0C2", "MY-14" }', reviewed)
        self.assertIn('{ "TR06", "H09F", "TR-20" }', csharp)


if __name__ == "__main__":
    unittest.main()
