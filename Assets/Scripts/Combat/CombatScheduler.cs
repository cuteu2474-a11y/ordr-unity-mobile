using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // 게임 시간은 호출자가 전달한다. 실제 공속 공식은 검증된 도감 데이터로 주입한다.
    public sealed class CombatScheduler
    {
        private readonly Dictionary<string, double> nextReady = new Dictionary<string, double>(StringComparer.Ordinal);
        public double Time { get; private set; }

        // Small tolerance for IEEE-754 additions (e.g. 2 + .34 + .01).
        // double.Epsilon is subnormal minimum, not machine epsilon.
        internal static bool HasReached(double time, double due)
        {
            if (double.IsNaN(time) || double.IsNaN(due)) return false;
            if (time >= due) return true;
            if (double.IsInfinity(time) || double.IsInfinity(due)) return false;
            const double binary64UnitRoundoff = 2.2204460492503131e-16;
            double scale = Math.Max(1.0, Math.Max(Math.Abs(time), Math.Abs(due)));
            return due - time <= 8.0 * binary64UnitRoundoff * scale;
        }

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
            return nextReady.TryGetValue(unitId, out readyAt) && HasReached(Time, readyAt);
        }

        // 스텝이 길어질 때 공격 준비시각까지 정확히 진행할 수 있게 한다.
        public double? NextReadyAt(string unitId)
        {
            ValidateId(unitId);
            double value;
            return nextReady.TryGetValue(unitId, out value) ? (double?)value : null;
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
        // 일반 워크래프트 방식은 기본 공격 사거리를 그대로 적용한다.
        // 모바일 모드에서는 호출자가 먼저 전장(메인/스토리/도전)을 분리해야 한다.
        public static string Select(IEnumerable<TargetCandidate> candidates, double range,
            TargetPriority priority)
        {
            return Select(candidates, range, priority, false);
        }

        // ignoreUnitRange=true는 평타의 유닛별 '도달 거리'만 무시한다.
        // 광역 스킬의 반경, 오라 범위, 타깃 우선순위는 바꾸지 않는다.
        public static string Select(IEnumerable<TargetCandidate> candidates, double range,
            TargetPriority priority, bool ignoreUnitRange)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (double.IsNaN(range) || double.IsInfinity(range) || range < 0)
                throw new ArgumentOutOfRangeException(nameof(range));
            double rangeSquared = range * range;
            TargetCandidate best = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null) throw new ArgumentException("null 대상은 허용하지 않습니다.");
                if (!candidate.Alive ||
                    (!ignoreUnitRange && candidate.DistanceSquared > rangeSquared))
                    continue;
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
