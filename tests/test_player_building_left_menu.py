"""CI-safe checks for direct source-player-building navigation (no facility drawer)."""
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
PANEL = ROOT / "Assets/Scripts/Unity/OrdrMobileTouchPanel.cs"
CATALOG = ROOT / "Assets/Scripts/Core/Original323PlayerBuildingResearchCatalog.cs"


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


if __name__ == "__main__":
    unittest.main()
