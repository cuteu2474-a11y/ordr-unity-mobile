using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Source-derived initial SINGLE PLAYER Nightmare building model.
    // Source: original UnitBalance.slk upgrades and original war3map.j
    // CUM:58044-58045 and CN3/BeP:52774-52776/8727-8746.
    // R012 rhpo +.58, R02K rhpo -.05, R021 rhpo -.25;
    // the sum is +.28 of original object HP. This reference calculation
    // does not assert retail-client rounding or later dynamic upgrades.
    public static class Original323StorySoloStats
    {
        public const int InitialA04QLevel = 16;
        public const int SoloHitPointPercent = 128;
        public const int PerSecondRegenFromStage = 9;
        public const int RegenPerSecond = 1000;
        public const string Source = "MODEL_ONLY: pinned 2.323[C] solo Nightmare " +
            "R012 +58%, R02K -5%, R021 -25% (BeP) / UnitBalance.slk";

        public static long InitialHitPoints(Original323StoryBuilding building)
        {
            if (building == null || building.Stage < 1 || building.Stage > 14 ||
                building.BaseHitPoints <= 0 || building.ArmorType != "fort")
                throw new ArgumentException("검증된 14종 스토리 건물 객체가 필요합니다.");
            // All 14 sourced story HP fields are multiples of 100.
            // Avoid floating-point rounding drift on large Egghead HP.
            if (building.BaseHitPoints % 100 != 0 &&
                building.Stage != 2)
                throw new FormatException("스토리 원본 HP가 감사된 객체 자료와 다릅니다.");
            long scaled = checked(building.BaseHitPoints * SoloHitPointPercent);
            if (scaled % 100 != 0)
                throw new FormatException("나눗셈 전 체력이 정수가 아닙니다.");
            return scaled / 100;
        }

        public static long RegenPerSecondFor(Original323StoryBuilding building)
        {
            if (building == null || building.Stage < 1 || building.Stage > 14)
                throw new ArgumentOutOfRangeException(nameof(building));
            return building.Stage >= PerSecondRegenFromStage ? RegenPerSecond : 0;
        }
    }
}
