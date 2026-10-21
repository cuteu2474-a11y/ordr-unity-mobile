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
            if (string.IsNullOrWhiteSpace(originalUnitRawcode) ||
                originalUnitRawcode.Length != 4)
                throw new ArgumentException("공격 유닛의 원본 4글자 rawcode가 필요합니다.");
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
