using System;

namespace Ordr.Mobile.Core
{
    // ORDR S2 2.320~2.323 누적 패치에서 확인된 개인 미션 제한만 구현한다.
    // 출처: https://ordr.kr/patch-notes/2-320
    public enum PersonalMissionKind
    {
        OtherworldMysticFusion,
        Other
    }

    public static class OtherworldMissionRules
    {
        public const int RerollUnavailableFromRound = 10;

        public static bool CanAppear(PersonalMissionKind kind, bool otherworldEnabled)
        {
            return kind != PersonalMissionKind.OtherworldMysticFusion || otherworldEnabled;
        }

        public static bool IsRerollRoundEligible(int currentRound)
        {
            if (currentRound < 1) throw new ArgumentOutOfRangeException(nameof(currentRound));
            return currentRound < RerollUnavailableFromRound;
        }
    }
}
