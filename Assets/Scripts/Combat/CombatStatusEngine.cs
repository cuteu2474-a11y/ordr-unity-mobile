using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

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
        HealthRegeneration,
        // Append to preserve numeric values of older effect types.
        // Bonney B4C/B4D Bd2(A04P) is NOT A04M ArmorBreakStack.
        OriginalA04PLevelIncrease,
        // Source-map A11L/AUfa: permanent defensive buff on Tezoro himself.
        ArmorBuff,
        // R00R/A11B AOae DataB1=.03: movement speed, NOT attack speed.
        MovementSpeedBuff,
        // S-09/ANba negative death mark; no direct damage modifier.
        // Append only, preserving all older numeric status identities.
        SourceBlackArrowDeathMark,
        AttackSpeedPenalty // Model-only copied Brulee -5% IAS
    }

    public enum CombatEffectOrigin
    {
        Aura,
        TriggeredSkill
    }

    // Shared parsing for timed combat effects. Values are inputs to the
    // status engine, not proof of a skill's native target, stacking or damage.
    public static class CommonSkillStatusEffects
    {
        public static bool IsTimedStatus(string type)
        {
            return type == "stun" || type == "armorReduction" ||
                type == "movementSlow" || type == "movementSlowPercent" ||
                type == "magicResistanceReduction" || type == "magicDamageAmplification" ||
                type == "explosiveDamageAmplification" || type == "allDamageAmplification";
        }

        public static bool TryRead(ImportedCombatEffect component,
            out CombatEffectType kind, out double amount, out double duration)
        {
            kind = CombatEffectType.Stun; amount = 0; duration = 0;
            if (component == null) return false;
            string field;
            switch (component.Type)
            {
                case "stun": kind = CombatEffectType.Stun; field = null; break;
                case "armorReduction": kind = CombatEffectType.ArmorReduction; field = "amount"; break;
                case "movementSlow":
                case "movementSlowPercent": kind = CombatEffectType.MovementSlowPercent; field = "percent"; break;
                case "magicResistanceReduction": kind = CombatEffectType.MagicResistanceReduction; field = "percent"; break;
                case "magicDamageAmplification": kind = CombatEffectType.MagicDamageAmplification; field = "percent"; break;
                case "explosiveDamageAmplification": kind = CombatEffectType.ExplosiveDamageAmplification; field = "percent"; break;
                case "allDamageAmplification": kind = CombatEffectType.AllDamageAmplification; field = "percent"; break;
                default: return false;
            }
            // Caps and extra per-effect rules require their own verified
            // handler: applying just the displayed percentage loses rules.
            foreach (string key in new[] { "maxPercent", "maxStacks", "stacks",
                "percentByTargetKind", "amountByTargetKind" })
                if (component.Get(key) != null) return false;
            double? value = field == null ? 1 : component.Number(field);
            double? seconds = component.Number("durationSeconds");
            if (!value.HasValue || !seconds.HasValue || double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) || value.Value < 0 ||
                double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value) ||
                seconds.Value <= 0) return false;
            amount = value.Value; duration = seconds.Value;
            return true;
        }

        public static bool TryReadArmorBreak(ImportedCombatEffect component,
            out int count, out int? limit)
        {
            count = 0; limit = null;
            if (component == null || component.Type != "armorBreak") return false;
            double? value = component.Number("stacks");
            double? maximum = component.Number("maxStacks");
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
                value.Value < 1 || value.Value > int.MaxValue || Math.Floor(value.Value) != value.Value ||
                (maximum.HasValue && (double.IsNaN(maximum.Value) || double.IsInfinity(maximum.Value) ||
                    maximum.Value < 0 || maximum.Value > int.MaxValue ||
                    Math.Floor(maximum.Value) != maximum.Value))) return false;
            count = (int)value.Value;
            if (maximum.HasValue) limit = (int)maximum.Value;
            return true;
        }
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

    [Serializable]
    public sealed class CombatActiveEffectState
    {
        public string SourceId;
        public CombatEffectType Type;
        public CombatEffectOrigin Origin;
        public double Amount;
        public double DurationSeconds;
        public bool NeverExpires;
        public double ExpiresAt;
    }

    [Serializable]
    public sealed class CombatStatusEngineState
    {
        public int Version = 1;
        public double Time;
        public long Revision;
        public List<CombatActiveEffectState> Effects =
            new List<CombatActiveEffectState>();
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
        // A native impact witness must not certify a state that was removed
        // and re-applied with the SAME effect object and identical numbers.
        // Increments only when the status state actually changes.
        public long Revision { get; private set; }

        public CombatStatusEngineState CaptureState()
        {
            var state = new CombatStatusEngineState {
                Time = CurrentTime, Revision = Revision
            };
            foreach (var item in effects)
                state.Effects.Add(new CombatActiveEffectState {
                    SourceId = item.Effect.SourceId,
                    Type = item.Effect.Type, Origin = item.Effect.Origin,
                    Amount = item.Effect.Amount,
                    DurationSeconds = item.Effect.DurationSeconds,
                    NeverExpires = item.Effect.Origin == CombatEffectOrigin.Aura,
                    ExpiresAt = item.Effect.Origin == CombatEffectOrigin.Aura ?
                        0 : item.ExpiresAt
                });
            return state;
        }

        public static CombatStatusEngine Restore(CombatStatusEngineState state)
        {
            if (state == null || state.Version != 1 ||
                state.Effects == null || state.Revision < 0 ||
                double.IsNaN(state.Time) || double.IsInfinity(state.Time) ||
                state.Time < 0)
                throw new FormatException("상태효과 시계/버전이 잘못됐습니다.");
            var staged = new List<ActiveEffect>();
            foreach (var item in state.Effects)
            {
                if (item == null ||
                    !Enum.IsDefined(typeof(CombatEffectType), item.Type) ||
                    !Enum.IsDefined(typeof(CombatEffectOrigin), item.Origin) ||
                    item.NeverExpires !=
                        (item.Origin == CombatEffectOrigin.Aura) ||
                    (!item.NeverExpires &&
                        (double.IsNaN(item.ExpiresAt) ||
                         double.IsInfinity(item.ExpiresAt) ||
                         item.ExpiresAt <= state.Time)) ||
                    (item.NeverExpires && item.ExpiresAt != 0))
                    throw new FormatException(
                        "상태효과 만료 시간·지속 상태가 잘못됐습니다.");
                CombatEffect effect;
                try
                {
                    effect = new CombatEffect(item.SourceId, item.Type,
                        item.Origin, item.Amount, item.DurationSeconds);
                }
                catch (ArgumentException)
                {
                    throw new FormatException("상태효과 출처/수치가 잘못됐습니다.");
                }
                staged.Add(new ActiveEffect {
                    Effect = effect, ExpiresAt = item.NeverExpires
                        ? double.PositiveInfinity : item.ExpiresAt
                });
            }
            var result = new CombatStatusEngine {
                CurrentTime = state.Time, Revision = state.Revision
            };
            result.effects.AddRange(staged);
            return result;
        }

        public void Apply(CombatEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            double expires = effect.Origin == CombatEffectOrigin.Aura
                ? double.PositiveInfinity : CurrentTime + effect.DurationSeconds;
            if (effect.Origin != CombatEffectOrigin.Aura && double.IsInfinity(expires))
                throw new OverflowException("상태효과 만료 시각이 초과되었습니다.");
            effects.Add(new ActiveEffect { Effect = effect, ExpiresAt = expires });
            Revision = checked(Revision + 1);
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double next = CurrentTime + seconds;
            if (double.IsInfinity(next))
                throw new OverflowException("전투 시간이 허용 범위를 초과했습니다.");
            CurrentTime = next;
            int expired = effects.RemoveAll(item => item.ExpiresAt <= CurrentTime);
            if (seconds > 0 || expired > 0)
                Revision = checked(Revision + 1);
        }

        public void RemoveSource(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException(nameof(sourceId));
            if (effects.RemoveAll(item => item.Effect.SourceId == sourceId) > 0)
                Revision = checked(Revision + 1);
        }

        // Dispel must remove only a beneficial *effect*, not unrelated
        // negative effects that happen to share the Warcraft source ID.
        public void RemoveSourceEffect(string sourceId, CombatEffectType type)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException(nameof(sourceId));
            if (effects.RemoveAll(item => item.Effect.SourceId == sourceId &&
                item.Effect.Type == type) > 0)
                Revision = checked(Revision + 1);
        }

        public bool HasActiveEffect(CombatEffectType type, CombatEffectOrigin origin)
        {
            foreach (var item in effects)
                if (item.Effect.Type == type && item.Effect.Origin == origin) return true;
            return false;
        }

        // For the development route integrator: account for a stun that
        // expires partway through one scheduler interval.
        public double RemainingStunSeconds
        {
            get
            {
                double remaining = 0;
                foreach (var item in effects)
                    if (item.Effect.Type == CombatEffectType.Stun)
                        remaining = Math.Max(remaining, item.ExpiresAt - CurrentTime);
                return remaining;
            }
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
