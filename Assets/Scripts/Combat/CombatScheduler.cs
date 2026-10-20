using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // 게임 시간은 호출자가 전달한다. 실제 공속 공식은 검증된 도감 데이터로 주입한다.
    public sealed class CombatScheduler
    {
        private readonly Dictionary<string, double> nextReady = new Dictionary<string, double>(StringComparer.Ordinal);
        public double Time { get; private set; }

        public void Register(string unitId, double firstAttackDelay)
        {
            ValidateId(unitId);
            ValidateDuration(firstAttackDelay);
            if (nextReady.ContainsKey(unitId)) throw new InvalidOperationException("이미 등록된 유닛입니다.");
            nextReady.Add(unitId, Time + firstAttackDelay);
        }

        public void Advance(double deltaSeconds)
        {
            ValidateDuration(deltaSeconds);
            if (double.IsInfinity(Time + deltaSeconds)) throw new OverflowException("전투 시간이 초과되었습니다.");
            Time += deltaSeconds;
        }

        public bool IsReady(string unitId)
        {
            ValidateId(unitId);
            double readyAt;
            return nextReady.TryGetValue(unitId, out readyAt) && Time >= readyAt;
        }

        public bool TryCommitAttack(string unitId, double nextAttackDelay)
        {
            ValidateId(unitId);
            ValidatePositive(nextAttackDelay);
            if (!IsReady(unitId)) return false;
            double readyAt = Time + nextAttackDelay;
            if (double.IsInfinity(readyAt)) throw new OverflowException("공격 시간이 초과되었습니다.");
            nextReady[unitId] = readyAt;
            return true;
        }

        public void Remove(string unitId)
        {
            ValidateId(unitId);
            nextReady.Remove(unitId);
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("유닛 ID가 필요합니다.");
        }

        private static void ValidateDuration(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static void ValidatePositive(double value)
        {
            ValidateDuration(value);
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public sealed class TargetCandidate
    {
        public string Id { get; }
        public double DistanceSquared { get; }
        public double PathProgress { get; }
        public bool Alive { get; }

        public TargetCandidate(string id, double distanceSquared, double pathProgress, bool alive)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("대상 ID가 필요합니다.");
            if (double.IsNaN(distanceSquared) || double.IsInfinity(distanceSquared) || distanceSquared < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            if (double.IsNaN(pathProgress) || double.IsInfinity(pathProgress))
                throw new ArgumentOutOfRangeException(nameof(pathProgress));
            Id = id;
            DistanceSquared = distanceSquared;
            PathProgress = pathProgress;
            Alive = alive;
        }
    }

    public enum TargetPriority { Nearest, FurthestAlongPath }

    public static class CombatTargeting
    {
        // 타깃 우선순위는 원작 검증 전까지 외부에서 명시적으로 지정한다.
        public static string Select(IEnumerable<TargetCandidate> candidates, double range,
            TargetPriority priority)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (double.IsNaN(range) || double.IsInfinity(range) || range < 0)
                throw new ArgumentOutOfRangeException(nameof(range));
            double rangeSquared = range * range;
            TargetCandidate best = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null) throw new ArgumentException("null 대상은 허용하지 않습니다.");
                if (!candidate.Alive || candidate.DistanceSquared > rangeSquared) continue;
                if (best == null || IsBetter(candidate, best, priority)) best = candidate;
            }
            return best == null ? null : best.Id;
        }

        private static bool IsBetter(TargetCandidate candidate, TargetCandidate current, TargetPriority priority)
        {
            switch (priority)
            {
                case TargetPriority.Nearest:
                    if (candidate.DistanceSquared != current.DistanceSquared)
                        return candidate.DistanceSquared < current.DistanceSquared;
                    break;
                case TargetPriority.FurthestAlongPath:
                    if (candidate.PathProgress != current.PathProgress)
                        return candidate.PathProgress > current.PathProgress;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(priority));
            }
            return string.CompareOrdinal(candidate.Id, current.Id) < 0;
        }
    }
}
