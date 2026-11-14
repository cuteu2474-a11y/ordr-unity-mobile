using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public interface ICombatProcRandom
    {
        // 반환 범위: 0 이상 exclusiveUpperBound 미만.
        int Next(int exclusiveUpperBound);
    }

    [Serializable]
    public sealed class CombatSkillCooldownState
    {
        public string UnitId;
        public string SkillId;
        public double DueAt;
        public long LastEventSequence;
    }

    [Serializable]
    public sealed class CombatSkillTriggerState
    {
        public int Version = 1;
        public double Time;
        public List<CombatSkillCooldownState> Cooldowns =
            new List<CombatSkillCooldownState>();
    }

    // 스킬의 발동 여부와 쿨타임만 관리한다.
    // 스킬 효과/반경/피해량 적용은 별도 실행기로 넘긴다.
    public sealed class CombatSkillTriggerEngine
    {
        private sealed class SkillState
        {
            public CombatSkillSpec Spec;
            public double NextReadyAt;
            public long LastEventSequence;
        }

        private readonly ICombatProcRandom random;
        private readonly Dictionary<string, Dictionary<string, SkillState>> units =
            new Dictionary<string, Dictionary<string, SkillState>>(StringComparer.Ordinal);

        public double Time { get; private set; }

        public CombatSkillTriggerState CaptureState()
        {
            var result = new CombatSkillTriggerState { Time = Time };
            foreach (var unit in units)
                foreach (var entry in unit.Value)
                    result.Cooldowns.Add(new CombatSkillCooldownState {
                        UnitId = unit.Key, SkillId = entry.Key,
                        DueAt = entry.Value.NextReadyAt,
                        LastEventSequence = entry.Value.LastEventSequence
                    });
            result.Cooldowns.Sort((a, b) => {
                int byUnit = string.CompareOrdinal(a.UnitId, b.UnitId);
                return byUnit == 0 ? string.CompareOrdinal(a.SkillId, b.SkillId)
                    : byUnit;
            });
            return result;
        }

        public static void ValidateState(CombatSkillTriggerState state)
        {
            if (state == null || state.Version != 1 ||
                state.Cooldowns == null || double.IsNaN(state.Time) ||
                double.IsInfinity(state.Time) || state.Time < 0)
                throw new FormatException("스킬 시계 형식이 잘못됐습니다.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in state.Cooldowns)
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.UnitId) ||
                    string.IsNullOrWhiteSpace(entry.SkillId) ||
                    double.IsNaN(entry.DueAt) ||
                    double.IsInfinity(entry.DueAt) || entry.DueAt < 0 ||
                    entry.LastEventSequence < 0 ||
                    !seen.Add(entry.UnitId + "\u0000" + entry.SkillId))
                    throw new FormatException("스킬 쿨타임/이벤트 순서가 잘못됐습니다.");
        }

        // Register the SAME original specs first, then set their state.
        // No skill proc or RNG call may run during restoration.
        public void ValidateRestoreMembership(CombatSkillTriggerState state)
        {
            ValidateState(state);
            int expected = 0;
            foreach (var unit in units) expected += unit.Value.Count;
            if (expected != state.Cooldowns.Count)
                throw new FormatException("스킬 개수가 원본 세션과 다릅니다.");
            foreach (var entry in state.Cooldowns)
            {
                Dictionary<string, SkillState> skills;
                if (!units.TryGetValue(entry.UnitId, out skills) ||
                    !skills.ContainsKey(entry.SkillId))
                    throw new FormatException("원본 스킬 ID가 존재하지 않습니다.");
            }
        }

        public void RestoreState(CombatSkillTriggerState state)
        {
            ValidateRestoreMembership(state);
            foreach (var entry in state.Cooldowns)
            {
                var skill = units[entry.UnitId][entry.SkillId];
                skill.NextReadyAt = entry.DueAt;
                skill.LastEventSequence = entry.LastEventSequence;
            }
            Time = state.Time;
        }

        public CombatSkillTriggerEngine(ICombatProcRandom random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public void RegisterUnit(string instanceId, UnitCombatSpec spec)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("유닛 인스턴스 ID가 필요합니다.");
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (units.ContainsKey(instanceId)) throw new InvalidOperationException("이미 등록된 유닛입니다.");
            var states = new Dictionary<string, SkillState>(StringComparer.Ordinal);
            foreach (var skill in spec.Skills)
            {
                double firstReady = Time + (skill.Trigger == SkillTriggerKind.Periodic
                    ? skill.PeriodSeconds : 0);
                if (double.IsInfinity(firstReady))
                    throw new OverflowException("첫 발동 시각이 허용 범위를 초과했습니다.");
                states.Add(skill.RawCode, new SkillState {
                    Spec = skill,
                    NextReadyAt = firstReady,
                    LastEventSequence = 0
                });
            }
            units.Add(instanceId, states);
        }

        public void RemoveUnit(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("유닛 ID가 필요합니다.");
            units.Remove(instanceId);
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double next = Time + seconds;
            if (double.IsInfinity(next)) throw new OverflowException("전투 시간이 허용 범위를 초과했습니다.");
            Time = next;
        }

        // eventSequence는 유닛별 발행자가 이벤트마다 증가시키는 고유 순번.
        // 동일 이벤트로 스킬을 재추첨하거나 중복 발동하지 않도록 한다.
        public bool TryActivate(string instanceId, string skillRawCode,
            SkillTriggerKind observedTrigger, long eventSequence)
        {
            if (eventSequence <= 0) throw new ArgumentOutOfRangeException(nameof(eventSequence));
            if (!Enum.IsDefined(typeof(SkillTriggerKind), observedTrigger))
                throw new ArgumentOutOfRangeException(nameof(observedTrigger));
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(skillRawCode))
                throw new ArgumentException("인스턴스 ID와 스킬 ID가 필요합니다.");
            Dictionary<string, SkillState> states;
            SkillState state;
            if (!units.TryGetValue(instanceId, out states) ||
                !states.TryGetValue(skillRawCode, out state))
                throw new InvalidOperationException("유닛에 등록되지 않은 스킬입니다.");
            if (eventSequence <= state.LastEventSequence) return false;
            // 실제 조건이 확인된 조건형 이벤트만 호출자가 전달한다.
            if (state.Spec.Trigger != observedTrigger ||
                state.Spec.Trigger == SkillTriggerKind.PassiveAura) return false;
            state.LastEventSequence = eventSequence;
            if (!CombatScheduler.HasReached(Time, state.NextReadyAt)) return false;

            bool success;
            if (state.Spec.ChancePerMillion == 0)
                success = false;
            else if (state.Spec.ChancePerMillion == 1000000)
                success = true;
            else
            {
                int result = random.Next(1000000);
                if (result < 0 || result >= 1000000)
                    throw new InvalidOperationException("스킬 난수 생성 결과가 범위를 벗어났습니다.");
                success = result < state.Spec.ChancePerMillion;
            }

            if (state.Spec.Trigger == SkillTriggerKind.Periodic)
                state.NextReadyAt = ReadyAt(Math.Max(state.Spec.PeriodSeconds, state.Spec.CooldownSeconds));
            else if (success)
                state.NextReadyAt = ReadyAt(state.Spec.CooldownSeconds);
            // 이벤트형 스킬의 실패는 쿨타임을 갱신하지 않는다.
            // 원본에서 실패 시에도 쿨타임이 존재하는 스킬은 별도 모델 확장이 필요하다.
            return success;
        }

        // 주기형 트리거의 예정 시각을 공통 전투 타임라인에 전달한다.
        public double? NextReadyAt(string instanceId, string skillId)
        {
            Dictionary<string, SkillState> spells;
            SkillState state;
            return instanceId != null && skillId != null &&
                units.TryGetValue(instanceId, out spells) &&
                spells.TryGetValue(skillId, out state)
                ? (double?)state.NextReadyAt : null;
        }

        public bool HasUnit(string instanceId)
        {
            return instanceId != null && units.ContainsKey(instanceId);
        }

        private double ReadyAt(double duration)
        {
            double ready = Time + duration;
            if (double.IsInfinity(ready)) throw new OverflowException("다음 스킬 발동 시각이 초과되었습니다.");
            return ready;
        }
    }
}
