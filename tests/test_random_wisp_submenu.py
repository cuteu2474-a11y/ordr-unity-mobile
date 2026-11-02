"""CI-safe guard for the mobile random-wisp submenu and existing cow wiring."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
PANEL = ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs"
CONTROLLER = ROOT / "Assets/Scripts/Unity/OrdrCharacterCombatController.cs"
MANUAL = ROOT / "Assets/Scripts/Mobile/MobileManualInteraction.cs"
THEME = ROOT / "Assets/Scripts/Unity/OrdrMobileUiTheme.cs"


class RandomWispSubmenuTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.panel = PANEL.read_text(encoding="utf-8")
        cls.controller = CONTROLLER.read_text(encoding="utf-8")
        cls.manual = MANUAL.read_text(encoding="utf-8")

    def test_all_three_random_wisp_rows_execute_directly_on_single_press(self):
        self.assertIn("private bool showRandomWispActions = true;", self.panel)
        self.assertIn("DrawRandomWispActions();", self.panel)
        submenu = self.panel.split("private void DrawRandomWispActions()", 1)[1].split(
            "private void DrawHelperCow()", 1)[0]
        self.assertIn("showRandomWispActions = !showRandomWispActions", submenu)
        self.assertEqual(1, submenu.count("DrawAccordionHeader("))
        self.assertEqual(3, submenu.count("DrawActionRow("))
        self.assertNotIn("expandedRandomWispDetail", submenu)
        self.assertNotIn("GUILayout.Label(", submenu)
        names = ["1. 흔함 유닛 소환", "2. 골드·목재 추첨", "3. 도움소 마나 충전"]
        self.assertEqual(sorted(submenu.index(name) for name in names),
                         [submenu.index(name) for name in names])
        self.assertIn("RequestCommonWispBatch(1)", submenu)
        self.assertIn("VerifiedCommonWispRecruitmentCost", submenu)
        self.assertIn("CanRecruitVerifiedCommonWisp", submenu)
        self.assertIn("OriginalRandomWispResourceExchange.OfferId", submenu)
        self.assertIn("RequestEconomyAction(", submenu)
        self.assertIn("OriginalHelperCowRules.OfferId", submenu)
        self.assertIn("GUI.enabled = wasEnabled && canRecharge;", submenu)
        self.assertNotIn("RepeatExplicitResourceUse", submenu)
        self.assertNotIn("foreach (int count in new[] { 1, 5, 10 })", submenu)

    def test_automatic_wisp_rows_retry_existing_awards_without_details(self):
        automatic = self.panel.split("private void DrawAutomaticWispStatus(", 1)[1].split(
            "private void DrawWisp()", 1)[0]
        self.assertIn("DrawActionRow(", automatic)
        self.assertNotIn("DrawAccordionHeader(", automatic)
        self.assertNotIn("expandedAutomaticWispIds", automatic)
        self.assertIn("CanAutomaticallyResolve(id)", automatic)
        self.assertIn("balance > 0", automatic)
        self.assertIn("CanProcessAutomaticWisps", automatic)
        self.assertIn("RequestAutomaticWispRetry()", automatic)
        self.assertIn("AutomaticFailure(id)", automatic)
        self.assertNotIn("RequestEconomyAction(", automatic)
        display = self.panel.split("private void DrawWisp()", 1)[1].split(
            "private void RunWispDirect(", 1)[0]
        self.assertLess(display.index("DrawRandomWispActions();"),
                        display.index("DrawAutomaticWispStatus("))
        self.assertNotIn("DrawChoiceWispMerge();", display)
        self.assertNotIn("빈 캐릭터 우선:", display)

    def test_remaining_wisp_offers_are_direct_actions_but_other_offers_confirm(self):
        economy = self.panel.split("private void DrawEconomy(", 1)[1].split(
            "private void RefreshCurrent(", 1)[0]
        self.assertIn("category == ManualEconomyCategory.Wisp", economy)
        self.assertIn("DrawActionRow(offer.Title", economy)
        self.assertIn("RunWispDirect(offer.Title", economy)
        self.assertIn("pendingEconomy = offer", economy)
        self.assertIn("OrdrMobileUiTheme.AccordionBody", economy)

    def test_evolution_selection_is_in_units_not_wisp_and_counts_are_prominent(self):
        units = self.panel.split("private void DrawUnits()", 1)[1].split(
            "private void DrawUnitDetails(", 1)[0]
        wisp = self.panel.split("private void DrawWisp()", 1)[1].split(
            "private void RunWispDirect(", 1)[0]
        evo = self.panel.split("private void DrawStartingEvolutionSelection()", 1)[1].split(
            "private void DrawUnitDetails(", 1)[0]
        self.assertIn("DrawStartingEvolutionSelection();", units)
        self.assertIn("if (!browsingFullCatalog)", units)
        self.assertIn("StartingEvolutionActorId", evo)
        self.assertIn("SharedEconomy.Units.Select(id)", evo)
        self.assertNotIn("StartingEvolutionActorId", wisp)
        self.assertNotIn("빈 캐릭터 우선", wisp)
        self.assertNotIn("원본 위습 자동 소환", wisp)

        random = self.panel.split("private void DrawRandomWispActions()", 1)[1].split(
            "private void DrawHelperCow()", 1)[0]
        automatic = self.panel.split("private void DrawAutomaticWispStatus(", 1)[1].split(
            "private void DrawWisp()", 1)[0]
        self.assertIn("OrdrMobileUiTheme.Gold, 80f, true", random)
        self.assertIn("OrdrMobileUiTheme.Aqua, 76f, true", automatic)
        theme = THEME.read_text(encoding="utf-8")
        self.assertIn("DrawWispCountBadge(", theme)
        self.assertIn('"남은 수량"', theme)
        self.assertIn("wispCountValue.fontSize = 27;", theme)
        self.assertIn("GUI.enabled = wasEnabled;", theme)

    def test_helper_panel_has_no_second_mana_recharge_transaction(self):
        helper = self.panel.split("private void DrawHelperCow()", 1)[1].split(
            "private void DrawAlchemyPanel()", 1)[0]
        self.assertNotIn("OriginalHelperCowRules.OfferId", helper)
        self.assertIn("TryGetHelperCowMana(out mana)", helper)
        self.assertNotIn("마나 충전은 위습", helper)

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
