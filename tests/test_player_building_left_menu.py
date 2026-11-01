"""CI-safe checks for direct source-player-building navigation (no facility drawer)."""
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
PANEL = ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs"
CATALOG = ROOT / "Assets/Scripts/Core/Original323PlayerBuildingResearchCatalog.cs"
THEME = ROOT / "Assets/Scripts/Unity/OrdrMobileUiTheme.cs"


class PlayerBuildingLeftMenuTests(unittest.TestCase):
    def test_all_seven_original_player_buildings_are_direct_left_buttons(self):
        source = PANEL.read_text(encoding="utf-8")
        left = source.split("private void DrawLeftTabs(", 1)[1].split(
            "private void DrawLeftTab(", 1)[0]
        entries = re.findall(r'DrawLeftTab\(Tab\.(\w+), "([^"]+)"\)', left)
        expected = [
            ("Gambling", "도박소"), ("UpgradeOne", "강화소 1"),
            ("UpgradeTwo", "강화소 2"), ("Helper", "도움소"),
            ("Missions", "미션"), ("Research", "연구소"),
            ("LogPose", "기록지침"),
        ]
        self.assertEqual(expected, [entry for entry in entries if entry in expected])
        self.assertEqual(7, len([entry for entry in entries if entry in expected]))
        self.assertNotIn("Tab.Facilities", source)
        for title in ("점수판", "위습", "유닛", "조합", "스토리", "상점"):
            self.assertIn(f'"{title}"', left)

    def test_unimplemented_research_has_original_catalog_but_no_fake_purchase(self):
        panel = PANEL.read_text(encoding="utf-8")
        catalog = CATALOG.read_text(encoding="utf-8")
        self.assertIn("case Tab.UpgradeOne: DrawPlayerBuildingResearch(", panel)
        self.assertIn("case Tab.UpgradeTwo: DrawPlayerBuildingResearch(", panel)
        self.assertIn("case Tab.Research: DrawPlayerBuildingResearch(", panel)
        page = panel.split("private void DrawPlayerBuildingResearch(", 1)[1].split(
            "private void DrawOriginalChatScoreboard(", 1)[0]
        self.assertIn("실제 연구 구매·재화 소비·유닛 능력치 적용은 아직 연결되지 않았습니다", page)
        self.assertNotIn("GUILayout.Button(", page)
        for code in ("h06Q", "h06R", "h06S"):
            self.assertIn(code, catalog)
        for upgrade in ("R00F", "R01V", "Rhfc"):
            self.assertIn(upgrade, catalog)

    def test_facility_game_shell_uses_read_only_cards_and_verified_resources(self):
        panel = PANEL.read_text(encoding="utf-8")
        catalog = CATALOG.read_text(encoding="utf-8")
        theme = THEME.read_text(encoding="utf-8")
        for name in ("LUCK / 도박소", "FORGE I / 강화소", "FORGE II / 강화소",
                     "SUPPORT / 도움소", "QUEST / 미션", "LAB / 연구소",
                     "EXPLORE / 기록지침"):
            self.assertIn(name, panel)
        self.assertIn("TryGetFacilityVisual(currentTab", panel)
        self.assertIn("DrawFacilityBanner", theme)
        self.assertIn("DrawNavigationMark", theme)
        self.assertIn("DrawResearchPlate", theme)
        self.assertIn("DrawResearchPlate(plate", panel)
        self.assertIn("DrawAccordionHeader(", panel)
        self.assertIn("expandedResearchCodes", panel)
        self.assertIn("DrawAccordionHeader", theme)
        self.assertIn("GUI.Button(area, GUIContent.none", theme)
        for effect in ("+0.45/초", "+0.8/초", "+12%", "-10%"):
            self.assertIn(effect, catalog)
        self.assertIn("구매 미연결", theme)

    def test_other_menus_share_expandable_shell_without_new_purchase_paths(self):
        panel = PANEL.read_text(encoding="utf-8")
        sections = (
            ("DrawPersonalMissions", "private static string MissionRewardText(", "TryReroll(slot.Index)"),
            ("DrawOwnedUnitIndex", "private void DrawUnitConfirmations(", "DrawUnitConfirmations(entry.InstanceId)"),
            ("DrawStory", "private void DrawFusion(", "RequestOriginalStoryBuilding(stage)"),
            ("DrawFusion", "private void DrawFusionConfirmation(", "CanExecuteFusionRecipe(offer.RecipeId)"),
            ("DrawTreasureGacha", "private void DrawAutomaticWispStatus(", "RequestMobileTreasureGacha()"),
            ("DrawHelperCow", "private void DrawAlchemyPanel(", "ChooseOriginal323Alchemy()"),
            ("DrawEconomy", "private void RefreshCurrent(", "RequestEconomyAction("),
            ("DrawAlchemyTargetDropdown", "private void DrawPager(", "alchemyTargetsExpanded"),
        )
        for name, following, preserved_action in sections:
            with self.subTest(menu=name):
                begin = "private void " + name + "("
                page = panel.split(begin, 1)[1].split(following, 1)[0]
                self.assertIn("DrawAccordionHeader(", page)
                self.assertIn("OrdrMobileUiTheme.AccordionBody", page)
                self.assertIn(preserved_action, page)
        self.assertIn("expandedMissionSlots", panel)
        self.assertIn("expandedStoryStages", panel)
        self.assertIn("expandedEconomyOffers", panel)
        self.assertIn("RequestStoryDispatch(unit.InstanceId)", panel)
        self.assertIn("RequestStoryRecall(unit.InstanceId)", panel)
        self.assertIn("RequestLowerGradeSale(", panel)



if __name__ == "__main__":
    unittest.main()
