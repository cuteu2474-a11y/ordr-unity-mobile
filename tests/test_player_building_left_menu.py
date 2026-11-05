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

    def test_upgrade_shops_use_real_transactions_and_research_is_still_safe(self):
        panel = PANEL.read_text(encoding="utf-8")
        catalog = CATALOG.read_text(encoding="utf-8")
        self.assertIn("case Tab.UpgradeOne: DrawPlayerBuildingResearch(", panel)
        self.assertIn("case Tab.UpgradeTwo: DrawPlayerBuildingResearch(", panel)
        self.assertIn("case Tab.Research: DrawPlayerBuildingResearch(", panel)
        page = panel.split("private void DrawPlayerBuildingResearch(", 1)[1].split(
            "private void DrawOriginalChatScoreboard(", 1)[0]
        self.assertIn("PlayerBuildingUpgrades", page)
        self.assertIn("service.Purchase(upgrade.Rawcode)", page)
        self.assertIn("DrawHoldActionRow(", page)
        self.assertIn("Original323UpgradePurchaseStatus.SourceMissing", page)
        self.assertIn("if (buildingRawcode == Original323PlayerBuildingResearchCatalog.ResearchBuilding)", page)
        self.assertNotIn("구매 준비 중", page)
        for code in ("h06Q", "h06R", "h06S"):
            self.assertIn(code, catalog)
        for upgrade in ("R00F", "R01V", "Rhfc"):
            self.assertIn(upgrade, catalog)

    def test_facilities_keep_actions_and_remove_ornamental_introductions(self):
        panel = PANEL.read_text(encoding="utf-8")
        catalog = CATALOG.read_text(encoding="utf-8")
        theme = THEME.read_text(encoding="utf-8")
        # The player explicitly removed all seven decorative facility banners.
        for caption in ("LUCK / 도박소", "FORGE I / 강화소", "FORGE II / 강화소",
                        "SUPPORT / 도움소", "QUEST / 미션", "LAB / 연구소",
                        "EXPLORE / 기록지침"):
            self.assertNotIn(caption, panel)
        self.assertNotIn("TryGetFacilityVisual(currentTab", panel)
        self.assertNotIn("DrawFacilityBanner(", panel)
        # Direct controls and source-backed research still exist.
        self.assertIn("DrawLeftTab(Tab.UpgradeOne, \"강화소 1\")", panel)
        self.assertIn("DrawLeftTab(Tab.UpgradeTwo, \"강화소 2\")", panel)
        self.assertIn("DrawHoldActionRow(", panel)
        self.assertIn("DrawResearchPlate", theme)
        self.assertIn("DrawResearchPlate(plate", panel)
        self.assertIn("DrawAccordionHeader(", panel)
        self.assertIn("expandedResearchCodes", panel)
        self.assertIn("DrawAccordionHeader", theme)
        self.assertIn("GUI.Button(area, GUIContent.none", theme)
        for effect in ("+0.45/초", "+0.8/초", "+12%", "-10%"):
            self.assertIn(effect, catalog)

    def test_operating_menus_are_action_first_and_require_confirm_for_spending(self):
        panel = PANEL.read_text(encoding="utf-8")
        sections = (
            ("DrawPersonalMissions", "private static string MissionRewardText(",
             "DrawAccordionHeader(", "TryReroll(slot.Index)"),
            ("DrawOwnedUnitIndex", "private void DrawUnitConfirmations(",
             "DrawActionRow(group.Name +", "DrawUnitConfirmations(entry.InstanceId)"),
            ("DrawStory", "private void DrawFusion(",
             'DrawStatusRow("현재 목표"', "RequestOriginalStoryBuilding(stage)"),
            ("DrawFusion", "private void DrawFusionConfirmation(",
             "DrawActionRow(rowTitle", "pendingFusion = offer"),
            ("DrawTreasureGacha", "private void DrawAutomaticWispStatus(",
             "DrawActionRow(\"보물 탐색\"", "RequestMobileTreasureGacha()"),
            ("DrawHelperCow", "private void DrawAlchemyPanel(",
             "DrawStatusRow(\"도움소 마나\"", "ChooseOriginal323Alchemy()"),
            ("DrawEconomy", "private void RefreshCurrent(",
             "DrawActionRow(offer.Title", "pendingEconomy = offer"),
            ("DrawAlchemyTargetDropdown", "private void DrawOriginalJassAudit(",
             "DrawAccordionHeader(", "pendingAlchemyCastId = selected.InstanceId"),
        )
        for name, following, affordance, preserved_action in sections:
            with self.subTest(menu=name):
                begin = "private void " + name + "("
                page = panel.split(begin, 1)[1].split(following, 1)[0]
                self.assertIn(affordance, page)
                self.assertIn(preserved_action, page)

        self.assertIn("expandedMissionSlots", panel)
        self.assertNotIn("expandedStoryStages", panel)
        self.assertNotIn("expandedEconomyOffers", panel)
        self.assertIn("manual.RequestStoryDispatch(actor.InstanceId)", panel)
        self.assertIn("manual.RequestStoryRecall(actor.InstanceId)", panel)
        self.assertIn("RequestLowerGradeSale(", panel)
        self.assertIn("CanExecuteFusionRecipe(offer.RecipeId)", panel)
        # The action title now explains near-ready common-material shortages.
        # Keep checking a real clickable row and its confirmation flow.
        self.assertIn("string rowTitle = fusionListCategory", panel)
        self.assertIn("GUI.enabled = previousEnabled && offer.IsReady && executable", panel)
        self.assertIn("MobileFusionProductionAssistant.CraftRepeated(", panel)
        self.assertIn("판매 확인", panel)
        self.assertIn("분해 시작", panel)
        self.assertIn("유닛 소비 및 연금술 확정", panel)
        self.assertIn("구매 확정", panel)

    def test_shared_readable_status_rows_keep_inert_information_inert(self):
        panel = PANEL.read_text(encoding="utf-8")
        theme = THEME.read_text(encoding="utf-8")
        self.assertIn("DrawStatusRow(", theme)
        examples = (
            ("DrawStandbyDashboard", 'DrawStatusRow("라운드"'),
            ("DrawOriginalChatScoreSummary", 'DrawScoreMetric("적 처치"'),
            ("DrawPersonalMissions", 'DrawStatusRow("미션 교체"'),
            ("DrawStory", 'DrawStatusRow("현재 목표"'),
            ("DrawHelperCow", 'DrawStatusRow("도움소 마나"'),
            ("DrawTreasureGacha", 'DrawStatusRow("남은 보물"'),
        )
        for menu, marker in examples:
            body = panel.split("private void " + menu + "(", 1)[1].split(
                "\n        private ", 1
            )[0]
            self.assertIn(marker, body, menu)
        self.assertIn("if (showOriginalAudit) DrawOriginalJassAudit();", panel)
        self.assertNotIn("원본 출현 규칙·악몽 초기 수치 모델 기반", panel)
        self.assertNotIn("원본 보물 좌표·보상 월드 연결 대기", panel)
        self.assertNotIn("원본 맵 고유 능력", panel)



if __name__ == "__main__":
    unittest.main()
