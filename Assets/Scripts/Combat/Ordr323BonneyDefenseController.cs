using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    // w5(H0BX,B4C) and w5(H0BY,B4D) listen to
    // EVENT_PLAYER_UNIT_ATTACKED, not an on-hit proc.
    public enum Ordr323BonneyForm
    {
        BaseH0BX,
        TransformedH0BY
    }

    public enum Ordr323BonneyArmorEvent
    {
        AttackDeclared, // B4C/B4D: Bd2(RN,10)
        SingleProc,     // B4C 30% / B4D 50%: Bd2(RN,20)
        AreaProc        // B39/B4B from 15%/30% branch: Bd2(GetEnumUnit(),2)
    }

    // The actual form must come from a validated transformation/unit-state
    // source. Do not infer transformed state from unitId alone.
    public interface IOriginalBonneyFormEvidence
    {
        bool TryGetVerifiedForm(long instanceId, out Ordr323BonneyForm currentForm);
    }

    // Stores A04P as permanent ORIGINAL ability-level changes.
    // It does not convert them into A04M armor break or guess combined stacking.
    public sealed class Ordr323BonneyDefenseController
    {
        private readonly Dictionary<string, CombatStatusEngine> statuses;
        private readonly Dictionary<string, int> provenTotals;
        private long nextEventSequence;

        public Ordr323BonneyDefenseController(
            Dictionary<string, CombatStatusEngine> targetStatuses,
            Dictionary<string, int> verifiedAbilityIncrements)
        {
            statuses = targetStatuses ??
                throw new ArgumentNullException(nameof(targetStatuses));
            provenTotals = verifiedAbilityIncrements ??
                throw new ArgumentNullException(nameof(verifiedAbilityIncrements));
        }

        // B39/B4B use BiZ with radius 320 around the attacked target.
        // Other Bonney triggers are not restricted by this helper.
        public static bool IsWithinVerifiedAreaProc(double centerX, double centerY,
            double targetX, double targetY)
        {
            if (double.IsNaN(centerX) || double.IsInfinity(centerX) ||
                double.IsNaN(centerY) || double.IsInfinity(centerY) ||
                double.IsNaN(targetX) || double.IsInfinity(targetX) ||
                double.IsNaN(targetY) || double.IsInfinity(targetY))
                return false;
            double dx = targetX - centerX;
            double dy = targetY - centerY;
            return dx * dx + dy * dy <= 320.0 * 320.0;
        }

        public static int VerifiedRollThresholdPercent(
            Ordr323BonneyForm form, Ordr323BonneyArmorEvent eventKind)
        {
            if (!Enum.IsDefined(typeof(Ordr323BonneyForm), form) ||
                !Enum.IsDefined(typeof(Ordr323BonneyArmorEvent), eventKind))
                throw new ArgumentOutOfRangeException(nameof(form));
            if (eventKind == Ordr323BonneyArmorEvent.AttackDeclared) return 100;
            if (form == Ordr323BonneyForm.BaseH0BX)
                return eventKind == Ordr323BonneyArmorEvent.SingleProc ? 30 : 15;
            return eventKind == Ordr323BonneyArmorEvent.SingleProc ? 50 : 30;
        }

        public bool TryApply(long casterId, Ordr323BonneyForm form,
            Ordr323BonneyArmorEvent eventKind, string enemyId, out string reason)
        {
            reason = null;
            if (casterId <= 0 || string.IsNullOrWhiteSpace(enemyId) ||
                !Enum.IsDefined(typeof(Ordr323BonneyForm), form) ||
                !Enum.IsDefined(typeof(Ordr323BonneyArmorEvent), eventKind))
            {
                reason = "원본 보니 캐릭터/폼/공격 이벤트가 유효하지 않습니다";
                return false;
            }

            CombatStatusEngine target;
            int originalTotal;
            if (!statuses.TryGetValue(enemyId, out target) ||
                !provenTotals.TryGetValue(enemyId, out originalTotal) ||
                originalTotal < 0 || originalTotal > 40)
            {
                reason = "원본 A04P 대상 등록/기존 레벨 미확인";
                return false;
            }

            // Do not append to a tampered or externally supplied A04P state.
            int observedTotal = 0;
            foreach (var effect in target.Snapshot())
            {
                if (effect.Type != CombatEffectType.OriginalA04PLevelIncrease)
                    continue;
                if (!effect.SourceId.StartsWith("jass:A04P:", StringComparison.Ordinal) ||
                    effect.Origin != CombatEffectOrigin.Aura ||
                    effect.DurationSeconds != 0 ||
                    effect.Amount < 1 || effect.Amount > 40 ||
                    Math.Floor(effect.Amount) != effect.Amount)
                {
                    reason = "원본 A04P 출처와 다른 효과가 혼재";
                    return false;
                }
                observedTotal = checked(observedTotal + (int)effect.Amount);
            }
            if (observedTotal != originalTotal)
            {
                reason = "보니 A04P 원본 증가량 장부 불일치";
                return false;
            }

            int delta = eventKind == Ordr323BonneyArmorEvent.AttackDeclared ? 10 :
                        eventKind == Ordr323BonneyArmorEvent.SingleProc ? 20 : 2;
            if (originalTotal > 40 - delta)
            {
                // W3A only contains modified Idef for levels 5..41.
                // Whether Warcraft clamps SetUnitAbilityLevel above 41 is not
                // verified. Never extrapolate or silently clamp.
                reason = "A04P 원본 41레벨 초과 시 동작 미확인";
                return false;
            }
            if (nextEventSequence == long.MaxValue)
                throw new OverflowException("A04P 이벤트 시퀀스 초과");

            string callback = form == Ordr323BonneyForm.BaseH0BX ? "B4C" : "B4D";
            string source = "jass:A04P:" + callback + ":" + eventKind.ToString() +
                ":" + (++nextEventSequence).ToString(CultureInfo.InvariantCulture) +
                ":unit:" + casterId.ToString(CultureInfo.InvariantCulture);
            target.Apply(new CombatEffect(source,
                CombatEffectType.OriginalA04PLevelIncrease,
                CombatEffectOrigin.Aura, delta, 0));
            provenTotals[enemyId] = originalTotal + delta;
            return true;
        }

        public bool TryGetVerifiedAbilityLevel(string enemyId,
            out int abilityLevel)
        {
            abilityLevel = 0;
            int amount;
            if (string.IsNullOrWhiteSpace(enemyId) ||
                !provenTotals.TryGetValue(enemyId, out amount))
                return false;
            abilityLevel = amount == 0 ? 0 : 1 + amount;
            return true;
        }
    }
}
