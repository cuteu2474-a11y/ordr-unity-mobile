using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public enum CombatEffectType
    {
        ArmorReduction,
        MovementSlowPercent,
        Stun,
        MagicResistanceReduction,
        // JASS Bd5가 A04Q 능력 레벨을 영구적으로 올리는 경우만 사용.
        // 도감 퍼센트형 마방깎과는 별개.
        OriginalA04QLevelIncrease,
        ArmorBreakStack,
        AttackSpeedBuff,
        AttackPowerBuff,
        MagicDamageAmplification,
        ExplosiveDamageAmplification,
        AllDamageAmplification,
        HealthRegeneration
    }

    public enum CombatEffectOrigin
    {
        Aura,
        TriggeredSkill
    }

    public sealed class CombatEffect
    {
        public string SourceId { get; }
        public CombatEffectType Type { get; }
        public CombatEffectOrigin Origin { get; }
        public double Amount { get; }
        public double DurationSeconds { get; }

        public CombatEffect(string sourceId, CombatEffectType type,
            CombatEffectOrigin origin, double amount, double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("효과 출처가 필요합니다.");
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (origin == CombatEffectOrigin.TriggeredSkill && durationSeconds <= 0)
                throw new ArgumentException("발동형 효과에는 양수 지속시간이 필요합니다.");
            SourceId = sourceId;
            Type = type;
            Origin = origin;
            Amount = amount;
            DurationSeconds = durationSeconds;
        }
    }

    // 중첩 효과를 최종 수치로 합산하는 규칙은 명시적인 원작 정책을 요구한다.
    public interface IOriginalEffectStackingRule
    {
        double Aggregate(CombatEffectType type, IReadOnlyList<CombatEffect> effects);
    }

    // 지속효과를 시간 기반으로 관리한다. 동일 효과 중첩 공식은 임의 계산하지 않는다.
    public sealed class CombatStatusEngine
    {
        private sealed class ActiveEffect
        {
            public CombatEffect Effect;
            public double ExpiresAt;
        }

        private readonly List<ActiveEffect> effects = new List<ActiveEffect>();
        public double CurrentTime { get; private set; }
        public int ActiveCount => effects.Count;

        public void Apply(CombatEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            double expires = effect.Origin == CombatEffectOrigin.Aura
                ? double.PositiveInfinity : CurrentTime + effect.DurationSeconds;
            if (effect.Origin != CombatEffectOrigin.Aura && double.IsInfinity(expires))
                throw new OverflowException("상태효과 만료 시각이 초과되었습니다.");
            effects.Add(new ActiveEffect { Effect = effect, ExpiresAt = expires });
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double next = CurrentTime + seconds;
            if (double.IsInfinity(next))
                throw new OverflowException("전투 시간이 허용 범위를 초과했습니다.");
            CurrentTime = next;
            effects.RemoveAll(item => item.ExpiresAt <= CurrentTime);
        }

        public void RemoveSource(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException(nameof(sourceId));
            effects.RemoveAll(item => item.Effect.SourceId == sourceId);
        }

        public bool HasActiveEffect(CombatEffectType type, CombatEffectOrigin origin)
        {
            foreach (var item in effects)
                if (item.Effect.Type == type && item.Effect.Origin == origin) return true;
            return false;
        }

        public bool IsStunned()
        {
            foreach (var item in effects)
                if (item.Effect.Type == CombatEffectType.Stun) return true;
            return false;
        }

        public double Aggregate(CombatEffectType type, IOriginalEffectStackingRule originalRule)
        {
            if (originalRule == null) throw new ArgumentNullException(nameof(originalRule));
            var matching = new List<CombatEffect>();
            foreach (var active in effects)
                if (active.Effect.Type == type) matching.Add(active.Effect);
            double value = originalRule.Aggregate(type, matching.AsReadOnly());
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("원작 중첩 공식이 유효하지 않은 값을 반환했습니다.");
            return value;
        }

        // 방깎·이감 합산 방식이 검증되기 전에는 최종 보정치를 계산하지 않는다.
        public IReadOnlyList<CombatEffect> Snapshot()
        {
            var snapshot = new List<CombatEffect>(effects.Count);
            foreach (var item in effects) snapshot.Add(item.Effect);
            return snapshot.AsReadOnly();
        }
    }
}
