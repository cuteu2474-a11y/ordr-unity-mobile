using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class CombatTimedEffectState
    {
        public long Sequence;
        public string CasterId;
        public string TargetId;
        public double At;
        public string Kind;
        public string Payload;
    }

    [Serializable]
    public sealed class CombatTimedEffectQueueState
    {
        public int Version = 1;
        public double Time;
        public long NextSequence;
        public List<CombatTimedEffectState> Pending =
            new List<CombatTimedEffectState>();
    }

    // 도감에 없는 연속 타격 간격을 고정 프레임이나 임의 초 단위로 추정하지 않는다.
    // 원작에 맞는 시간 오프셋(시전 시작을 0으로 한 초 단위)을 외부 정책이 제공한다.
    public interface IOriginalSkillHitTimingProvider
    {
        bool TryGetHitOffsets(CharacterSkillProfile caster,
            ImportedCombatSkill skill, ImportedCombatEffect effect,
            out IReadOnlyList<double> hitOffsetsSeconds);
    }

    // 시전 후 발생하는 다단히트/도트의 시간을 한 시계로 관리한다.
    // 같은 시각에는 등록 순서대로 실행하고, 사망/퇴장한 목표의 예약 피해는 취소한다.
    public sealed class CombatTimedEffectQueue
    {
        private sealed class Item
        {
            public long Sequence;
            public string CasterId;
            public string TargetId;
            public double At;
            public Action Execute;
            public string Kind;
            public string Payload;
        }

        private readonly List<Item> pending = new List<Item>();
        private long nextSequence;
        private bool advancing;
        public double Time { get; private set; }
        public int PendingCount { get { return pending.Count; } }

        public CombatTimedEffectQueueState CaptureState()
        {
            if (advancing)
                throw new InvalidOperationException("전투 예약 처리 도중 저장할 수 없습니다.");
            var state = new CombatTimedEffectQueueState {
                Time = Time, NextSequence = nextSequence
            };
            foreach (var item in pending)
            {
                // An arbitrary Action closure may capture live units, random
                // state or status references. Serializing its time alone
                // would silently erase a future attack on resume.
                if (string.IsNullOrWhiteSpace(item.Kind))
                    throw new InvalidOperationException(
                        "복원 종류가 없는 예약 타격은 저장할 수 없습니다.");
                state.Pending.Add(new CombatTimedEffectState {
                    Sequence = item.Sequence, CasterId = item.CasterId,
                    TargetId = item.TargetId, At = item.At,
                    Kind = item.Kind, Payload = item.Payload
                });
            }
            state.Pending.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            return state;
        }

        public static void ValidateState(CombatTimedEffectQueueState state)
        {
            if (state == null || state.Version != 1 ||
                state.Pending == null || state.NextSequence < 0 ||
                double.IsNaN(state.Time) || double.IsInfinity(state.Time) ||
                state.Time < 0)
                throw new FormatException("예약 전투 시계 형식이 잘못됐습니다.");
            var seen = new HashSet<long>();
            foreach (var item in state.Pending)
                if (item == null || item.Sequence <= 0 ||
                    item.Sequence > state.NextSequence ||
                    !seen.Add(item.Sequence) ||
                    string.IsNullOrWhiteSpace(item.CasterId) ||
                    string.IsNullOrWhiteSpace(item.TargetId) ||
                    string.IsNullOrWhiteSpace(item.Kind) ||
                    item.Payload == null ||
                    double.IsNaN(item.At) || double.IsInfinity(item.At) ||
                    item.At < state.Time)
                    throw new FormatException("예약된 공격/효과가 잘못됐습니다.");
        }

        public void RestoreState(CombatTimedEffectQueueState state,
            Func<string, string, string, string, Action> recreate)
        {
            ValidateState(state);
            if (advancing || Time != 0 || pending.Count != 0 || nextSequence != 0)
                throw new InvalidOperationException(
                    "새 전투 예약 엔진에서만 복원할 수 있습니다.");
            if (state.Pending.Count > 0 && recreate == null)
                throw new ArgumentNullException(nameof(recreate));
            var staged = new List<Item>();
            foreach (var item in state.Pending)
            {
                var callback = recreate(item.Kind, item.Payload,
                    item.CasterId, item.TargetId);
                if (callback == null)
                    throw new FormatException(
                        "예약 스킬 효과를 다시 연결할 수 없습니다.");
                staged.Add(new Item {
                    Sequence = item.Sequence, CasterId = item.CasterId,
                    TargetId = item.TargetId, At = item.At,
                    Kind = item.Kind, Payload = item.Payload,
                    Execute = callback
                });
            }
            // No executable callback is invoked during restore.
            pending.AddRange(staged);
            nextSequence = state.NextSequence;
            Time = state.Time;
        }

        public void ScheduleRestorable(string casterId, string targetId,
            double offsetSeconds, string kind, string payload, Action callback)
        {
            if (string.IsNullOrWhiteSpace(kind) || payload == null)
                throw new ArgumentException(
                    "예약 기술은 복원 종류와 데이터가 필요합니다.");
            Schedule(casterId, targetId, offsetSeconds, callback);
            var item = pending[pending.Count - 1];
            item.Kind = kind;
            item.Payload = payload;
        }

        public static bool AreValidOffsets(ImportedCombatEffect effect,
            IReadOnlyList<double> offsets)
        {
            if (effect == null || offsets == null || offsets.Count == 0 ||
                offsets.Count > 4096) return false;
            double last = -1;
            foreach (double at in offsets)
            {
                if (double.IsNaN(at) || double.IsInfinity(at) ||
                    at < 0 || at < last) return false;
                last = at;
            }
            var hits = effect.Number("hits");
            if (hits.HasValue && (hits.Value < 1 ||
                Math.Floor(hits.Value) != hits.Value ||
                hits.Value != offsets.Count)) return false;
            return true;
        }

        public void Schedule(string casterId, string targetId,
            double offsetSeconds, Action callback)
        {
            if (string.IsNullOrWhiteSpace(casterId) ||
                string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("예약 피해의 공격자·대상이 필요합니다.");
            // A positive duration smaller than binary64 precision at this
            // battle time cannot create a future event. Reject it so a
            // self-rescheduling effect cannot fire forever at one instant.
            double dueAt = Time + offsetSeconds;
            if (double.IsNaN(offsetSeconds) || double.IsInfinity(offsetSeconds) ||
                offsetSeconds <= 0 || double.IsInfinity(dueAt) ||
                dueAt <= Time)
                throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (nextSequence == long.MaxValue)
                throw new OverflowException("예약 타격 순번이 초과되었습니다.");
            pending.Add(new Item {
                CasterId = casterId, TargetId = targetId,
                At = dueAt,
                Sequence = ++nextSequence, Execute = callback
            });
        }

        public void CancelCaster(string casterId)
        {
            pending.RemoveAll(item => item.CasterId == casterId);
        }

        public void CancelTarget(string targetId)
        {
            pending.RemoveAll(item => item.TargetId == targetId);
        }

        // A lane change cancels outgoing hits against live enemies while
        // retaining the owner's self-clocked recurring passives. CancelCaster
        // would also kill those periodic schedulers permanently.
        public void CancelCasterTargets(string casterId,
            Func<string, bool> isTargetInCombatWorld)
        {
            if (string.IsNullOrWhiteSpace(casterId))
                throw new ArgumentException("공격자 ID가 필요합니다.", nameof(casterId));
            if (isTargetInCombatWorld == null)
                throw new ArgumentNullException(nameof(isTargetInCombatWorld));
            pending.RemoveAll(item => item.CasterId == casterId &&
                isTargetInCombatWorld(item.TargetId));
        }

        public void Advance(double deltaSeconds, Action<double> advanceOtherClocks)
        {
            if (advancing)
                throw new InvalidOperationException(
                    "전투 시계 갱신 중 재진입할 수 없습니다.");
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0 || double.IsInfinity(Time + deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (advanceOtherClocks == null)
                throw new ArgumentNullException(nameof(advanceOtherClocks));

            advancing = true;
            try
            {
                double end = Time + deltaSeconds;
                int processed = 0;
                while (true)
                {
                    int index = -1;
                    for (int i = 0; i < pending.Count; i++)
                    {
                        var item = pending[i];
                        if (item.At > end) continue;
                        if (index < 0 || item.At < pending[index].At ||
                            (item.At == pending[index].At &&
                             item.Sequence < pending[index].Sequence))
                            index = i;
                    }
                    if (index < 0) break;
                    if (++processed > 100000)
                        throw new InvalidOperationException(
                            "동일 프레임의 예약 타격이 지나치게 많습니다.");

                    Item due = pending[index];
                    double elapsed = due.At - Time;
                    // Other clocks may cancel this caster or target, or
                    // schedule another effect. Publish the new battle time
                    // first so newly scheduled effects start after this step.
                    Time = due.At;
                    advanceOtherClocks(elapsed);
                    // The clock callback may have removed/reordered items.
                    // Never execute a canceled hit or remove a stale index.
                    if (!pending.Remove(due)) continue;
                    due.Execute();
                }
                double remaining = end - Time;
                Time = end;
                advanceOtherClocks(remaining);
            }
            finally
            {
                advancing = false;
            }
        }
    }
}
