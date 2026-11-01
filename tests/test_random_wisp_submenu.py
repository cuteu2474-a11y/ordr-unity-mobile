"""CI-safe guard for the mobile random-wisp submenu and existing cow wiring."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
PANEL = ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs"
CONTROLLER = ROOT / "Assets/Scripts/Unity/OrdrCharacterCombatController.cs"
MANUAL = ROOT / "Assets/Scripts/Mobile/MobileManualInteraction.cs"


class RandomWispSubmenuTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.panel = PANEL.read_text(encoding="utf-8")
        cls.controller = CONTROLLER.read_text(encoding="utf-8")
        cls.manual = MANUAL.read_text(encoding="utf-8")

    def test_all_three_actions_live_under_one_expandable_random_wisp_section(self):
        self.assertIn("private bool showRandomWispActions = true;", self.panel)
        self.assertIn("DrawRandomWispActions();", self.panel)
        submenu = self.panel.split("private void DrawRandomWispActions()", 1)[1].split(
            "private void DrawHelperCow()", 1)[0]
        self.assertIn("showRandomWispActions = !showRandomWispActions", submenu)
        self.assertIn("expandedRandomWispDetail", submenu)
        self.assertEqual(4, submenu.count("DrawAccordionHeader("))
        names = ["1. 흔함 유닛 소환", "2. 골드·목재 추첨", "3. 도움소 마나 충전"]
        self.assertEqual(sorted(submenu.index(name) for name in names),
                         [submenu.index(name) for name in names])
        self.assertIn("RequestCommonWispBatch(1)", submenu)
        self.assertIn("foreach (int count in new[] { 1, 5, 10 })", submenu)
        self.assertIn("OriginalHelperCowRules.OfferId", submenu)
        self.assertIn("GUI.enabled = wasEnabled && canRecharge;", submenu)
        self.assertIn("TryGetHelperCowMana(out mana)", submenu)

    def test_automatic_wisp_sections_are_foldable_without_new_transactions(self):
        automatic = self.panel.split("private void DrawAutomaticWispStatus(", 1)[1].split(
            "private void DrawWisp()", 1)[0]
        self.assertIn("expandedAutomaticWispIds", automatic)
        self.assertIn("DrawAccordionHeader(", automatic)
        self.assertIn("CanAutomaticallyResolve(id)", automatic)
        self.assertIn("AutomaticFailure(id)", automatic)
        self.assertNotIn("RequestEconomyAction(", automatic)
        display = self.panel.split("private void DrawWisp()", 1)[1].split(
            "private void RunWispDirect(", 1)[0]
        self.assertLess(display.index("DrawRandomWispActions();"),
                        display.index("DrawAutomaticWispStatus("))
        self.assertNotIn("DrawChoiceWispMerge();", display)

    def test_helper_panel_has_no_second_mana_recharge_transaction(self):
        helper = self.panel.split("private void DrawHelperCow()", 1)[1].split(
            "private void DrawAlchemyPanel()", 1)[0]
        self.assertNotIn("OriginalHelperCowRules.OfferId", helper)
        self.assertIn("위습 → 랜덤위습 → 도움소 마나 충전", helper)

    def test_original_solo_cow_is_connected_to_existing_transaction(self):
        begin = self.controller.split("public void BeginSession()", 1)[1].split(
            "private void OnCommittedCommonSale", 1)[0]
        self.assertIn("AttachVerifiedSoloHelperCow();", begin)
        self.assertIn("new OriginalHelperCowManaWorld(", self.controller)
        self.assertIn("ManualActions.ConfigureHelperCowRecharge(HelperCowRecharge)",
                      self.controller)
        self.assertIn("helperCowRecharge.SpendOne()", self.manual)


if __name__ == "__main__":
    unittest.main()
