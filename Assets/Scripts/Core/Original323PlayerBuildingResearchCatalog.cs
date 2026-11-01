using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // Read-only source inventory from ORDR S2 2.323[C]:
    // h06Q/h06R/h06S Researches, Units/UpgradeData.slk and
    // Units/CampaignUpgradeStrings.txt. This is NOT a research execution port.
    public sealed class Original323PlayerBuildingResearch
    {
        public string Rawcode { get; private set; }
        public string Name { get; private set; }
        public int MaximumLevel { get; private set; }
        // Optional verified tooltip summary. Null means the original effect
        // has not been ported to this read-only presentation catalog.
        public string EffectSummary { get; private set; }

        public Original323PlayerBuildingResearch(string rawcode, string name,
            int maximumLevel, string effectSummary = null)
        {
            if (string.IsNullOrWhiteSpace(rawcode) || string.IsNullOrWhiteSpace(name) ||
                maximumLevel < 1)
                throw new ArgumentException("원본 연구 객체 정보가 올바르지 않습니다.");
            Rawcode = rawcode;
            Name = name;
            MaximumLevel = maximumLevel;
            EffectSummary = effectSummary;
        }
    }

    public static class Original323PlayerBuildingResearchCatalog
    {
        public const string FirstUpgradeBuilding = "h06Q";
        public const string SecondUpgradeBuilding = "h06R";
        public const string ResearchBuilding = "h06S";

        private static readonly IReadOnlyList<Original323PlayerBuildingResearch> First =
            Array.AsReadOnly(new[] {
                new Original323PlayerBuildingResearch("R00F", "흔함류 업그레이드", 7),
                new Original323PlayerBuildingResearch("R008", "희귀 업그레이드", 16),
                new Original323PlayerBuildingResearch("R002", "불멸 업그레이드", 21),
                new Original323PlayerBuildingResearch("R00E", "특별 업그레이드", 15),
                new Original323PlayerBuildingResearch("R003", "초월 업그레이드", 21),
                new Original323PlayerBuildingResearch("R004", "제한 업그레이드", 21),
                new Original323PlayerBuildingResearch("R000", "전설 업그레이드", 21),
                new Original323PlayerBuildingResearch("R019", "랜덤전용 유닛 업그레이드", 21),
            });

        private static readonly IReadOnlyList<Original323PlayerBuildingResearch> Second =
            Array.AsReadOnly(new[] {
                new Original323PlayerBuildingResearch("R032", "영원 우타 업그레이드", 21),
                new Original323PlayerBuildingResearch("R02O", "영원 오뎅 업그레이드", 21),
                new Original323PlayerBuildingResearch("R015", "영원 카벤딧슈 업그레이드", 21),
                new Original323PlayerBuildingResearch("R00X", "영원 에이스 업그레이드", 21),
                new Original323PlayerBuildingResearch("R013", "영원 비비 업그레이드", 21),
                new Original323PlayerBuildingResearch("R007", "영원 미호크 업그레이드", 21),
                new Original323PlayerBuildingResearch("R014", "영원 버기 업그레이드", 21),
                new Original323PlayerBuildingResearch("R016", "영원 핸콕 업그레이드", 21),
                new Original323PlayerBuildingResearch("R02C", "영원 류마 업그레이드", 21),
                new Original323PlayerBuildingResearch("R01V", "영원 테조로 업그레이드", 21),
            });

        private static readonly IReadOnlyList<Original323PlayerBuildingResearch> Research =
            Array.AsReadOnly(new[] {
                new Original323PlayerBuildingResearch("Rhgb", "해상 디너", 1,
                    "아군 체력 재생 +0.45/초"),
                new Original323PlayerBuildingResearch("Rhra", "카리스마", 1,
                    "아군 공격력 +12%"),
                new Original323PlayerBuildingResearch("Rhpt", "식량 보급", 1,
                    "아군 마나 재생 +0.8/초"),
                new Original323PlayerBuildingResearch("Rhfc", "기후 변화", 1,
                    "적 이동 속도 -10%"),
            });

        public static IReadOnlyList<Original323PlayerBuildingResearch> ForBuilding(string rawcode)
        {
            switch (rawcode)
            {
                case FirstUpgradeBuilding: return First;
                case SecondUpgradeBuilding: return Second;
                case ResearchBuilding: return Research;
                default: throw new ArgumentOutOfRangeException(nameof(rawcode));
            }
        }
    }
}
