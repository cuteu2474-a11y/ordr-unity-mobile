using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Supplies only original-source-confirmed per-unit values. The character
    // catalogue has 271 unit IDs, but their existence is not proof of a
    // Warcraft base attack, attack interval, damage school or first swing time.
    public sealed class OriginalVerifiedAttackBinding
    {
        public BasicAttackSpec Stats { get; private set; }
        public double FirstAttackDelaySeconds { get; private set; }
        public string OriginalUnitRawcode { get; private set; }
        public string SourceEvidence { get; private set; }

        public OriginalVerifiedAttackBinding(BasicAttackSpec stats,
            double firstAttackDelaySeconds, string originalUnitRawcode,
            string sourceEvidence)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            if (double.IsNaN(firstAttackDelaySeconds) ||
                double.IsInfinity(firstAttackDelaySeconds) || firstAttackDelaySeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(firstAttackDelaySeconds));
            // 독립 Unity 게임에서 워크래프트 오브젝트 ID는 필수가 아니다.
            // 외부에서 확인한 공격력·주기·사거리·피해 계열과 근거가 핵심이다.
            if (!string.IsNullOrEmpty(originalUnitRawcode) &&
                originalUnitRawcode.Length != 4)
                throw new ArgumentException("원본 rawcode를 제공할 경우 4글자여야 합니다.");
            if (string.IsNullOrWhiteSpace(sourceEvidence))
                throw new ArgumentException("검증한 원본 공격 스펙 근거가 필요합니다.");
            Stats = stats;
            FirstAttackDelaySeconds = firstAttackDelaySeconds;
            OriginalUnitRawcode = originalUnitRawcode;
            SourceEvidence = sourceEvidence;
        }
    }

    public interface IOriginalVerifiedUnitAttackProvider
    {
        // Can return false when unit has no verified attack stats (including
        // the 71 catalogue-only characters). Never supply guessed values.
        bool TryResolve(CatalogUnit unit, CharacterSkillProfile detailed,
            out OriginalVerifiedAttackBinding original);
    }
}
