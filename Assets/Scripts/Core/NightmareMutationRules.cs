using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 도감의 권장 조합 수치. 실제 승리/패배 조건으로 사용하면 안 된다.
    public enum ElderCurse { None, Nusjuro, Warcury, Saturn }

    public readonly struct BuildTargets
    {
        public readonly int ArmorReduction;
        public readonly int MovementSlowPercent;
        public BuildTargets(int armorReduction, int movementSlowPercent)
        {
            ArmorReduction = armorReduction;
            MovementSlowPercent = movementSlowPercent;
        }
    }

    public static class NightmareMutationRules
    {
        public const string Difficulty = "nightmare";
        public const string Mode = "mutation";
        public const string SourceUrl = "https://ordr.kr/guides/gorosei";
        public const string SourceVersion = "2.323";

        public static BuildTargets RecommendedBuild(ElderCurse curse)
        {
            switch (curse)
            {
                case ElderCurse.None: return new BuildTargets(211, 102);
                case ElderCurse.Nusjuro: return new BuildTargets(211, 117);
                case ElderCurse.Warcury: return new BuildTargets(221, 102);
                case ElderCurse.Saturn: return new BuildTargets(211, 102);
                default: throw new ArgumentOutOfRangeException(nameof(curse));
            }
        }

        // 변이 종류, 추첨 확률, 시작 타이밍 등은 아직 확인되지 않았다.
        // 이 클래스는 오로성 선택이나 실제 효과 적용을 자동 실행하지 않는다.
    }
}
