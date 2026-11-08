using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
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
        }

        private readonly List<Item> pending = new List<Item>();
        private long nextSequence;
        private bool advancing;
        public double Time { get; private set; }
        public int PendingCount { get { return pending.Count; } }

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
