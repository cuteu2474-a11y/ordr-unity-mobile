using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 이속 및 감속/기절의 계산은 원작 정책이 공급한다.
    // 목적지 도달의 '누수/스토리 성공/도전 실패' 결과 또한 호출자가 판단한다.
    public interface IOriginalEnemyMovementRules
    {
        bool TryResolveSpeed(CombatEnemy enemy,
            IReadOnlyList<CombatEffect> activeEffects,
            out double worldUnitsPerSecond);
    }

    public sealed class CombatPathPoint
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public CombatPathPoint(double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Y = y;
        }
    }

    public sealed class VerifiedCombatPath
    {
        private readonly List<CombatPathPoint> points;
        private readonly List<double> accumulated = new List<double>();
        public string Evidence { get; private set; }
        public double Length { get; private set; }

        public VerifiedCombatPath(IEnumerable<CombatPathPoint> path, string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("경로가 확인된 원작 근거를 명시하세요.");
            if (path == null) throw new ArgumentNullException(nameof(path));
            points = new List<CombatPathPoint>(path);
            if (points.Count < 2)
                throw new ArgumentException("적 이동 경로에는 최소 2개 지점이 필요합니다.");
            accumulated.Add(0);
            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].X - points[i-1].X;
                double dy = points[i].Y - points[i-1].Y;
                double length = Math.Sqrt(dx*dx + dy*dy);
                if (length <= 0 || double.IsInfinity(length))
                    throw new ArgumentException("경로 구간 길이가 잘못됐습니다.");
                Length += length;
                accumulated.Add(Length);
            }
            Evidence = evidence;
        }

        public CombatPathPoint AtDistance(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) ||
                distance < 0)
                throw new ArgumentOutOfRangeException(nameof(distance));
            distance = Math.Min(distance, Length);
            for (int i = 1; i < accumulated.Count; i++)
            {
                if (distance > accumulated[i]) continue;
                double part = (distance - accumulated[i-1]) /
                    (accumulated[i] - accumulated[i-1]);
                return new CombatPathPoint(
                    points[i-1].X + (points[i].X - points[i-1].X) * part,
                    points[i-1].Y + (points[i].Y - points[i-1].Y) * part);
            }
            return points[points.Count - 1];
        }
    }

    // 적별로 검증된 경로를 별도 관리한다.
    public sealed class CombatEnemyPathDriver
    {
        private sealed class Track
        {
            public VerifiedCombatPath Path;
            public double Distance;
        }

        private readonly Dictionary<string, Track> tracks =
            new Dictionary<string, Track>(StringComparer.Ordinal);

        public void Attach(string enemyId, VerifiedCombatPath path,
            double startDistance = 0)
        {
            if (string.IsNullOrWhiteSpace(enemyId) || path == null)
                throw new ArgumentException("몬스터 ID와 이동 경로가 필요합니다.");
            if (double.IsNaN(startDistance) || double.IsInfinity(startDistance) ||
                startDistance < 0 || startDistance > path.Length)
                throw new ArgumentOutOfRangeException(nameof(startDistance));
            tracks.Add(enemyId, new Track { Path = path, Distance = startDistance });
        }

        public void Detach(string enemyId) { tracks.Remove(enemyId); }

        // 종료된 대상은 리스트에 반환만 한다. 원본 누수/완료 규칙을 임의 적용하지 않는다.
        public IReadOnlyList<string> Advance(CharacterCombatRuntime runtime,
            IOriginalEnemyMovementRules originalRules, double seconds)
        {
            if (runtime == null || originalRules == null)
                throw new ArgumentNullException("실제 전투와 이동 수치 정책이 필요합니다.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            var reachedEnd = new List<string>();
            var keys = new List<string>(tracks.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string id in keys)
            {
                Track track = tracks[id];
                var enemy = runtime.EnemyOf(id);
                if (enemy == null || !enemy.IsAlive)
                {
                    tracks.Remove(id);
                    continue;
                }
                var status = runtime.StatusOf(id);
                if (status != null && status.IsStunned()) continue;
                double speed;
                if (!originalRules.TryResolveSpeed(enemy,
                    status == null
                        ? new List<CombatEffect>().AsReadOnly()
                        : status.Snapshot(), out speed)) continue;
                if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0 ||
                    double.IsInfinity(speed * seconds))
                    throw new InvalidOperationException("적 이동속도 공식 결과가 잘못됐습니다.");
                double moved = track.Distance + speed * seconds;
                if (double.IsInfinity(moved))
                    throw new OverflowException("경로 이동 거리가 초과되었습니다.");
                track.Distance = Math.Min(track.Path.Length, moved);
                var point = track.Path.AtDistance(track.Distance);
                runtime.TryMoveEnemy(id, point.X, point.Y,
                    track.Distance / track.Path.Length);
                if (track.Distance >= track.Path.Length)
                {
                    reachedEnd.Add(id);
                    tracks.Remove(id);
                }
            }
            return reachedEnd.AsReadOnly();
        }
    }

    // An independent live adapter supplies native monster positions already
    // resolved by the original path, collision, slow, stun and immunity rules.
    // The Unity scene must never infer a speed or path from spawn locations.
    public interface IOriginal323ObservedMonsterPositionSource
    {
        bool TryObserve(string monsterInstanceId, double combatTime,
            out double x, out double y, out double pathProgress,
            out string sourceEvidence);
    }

    public sealed class Original323ObservedMonsterMovementWorld
    {
        private sealed class Position
        {
            public string Id;
            public double X, Y, Progress;
        }

        private readonly IOriginal323ObservedMonsterPositionSource source;

        public Original323ObservedMonsterMovementWorld(
            IOriginal323ObservedMonsterPositionSource observedSource)
        {
            source = observedSource ?? throw new ArgumentNullException(nameof(observedSource));
        }

        public int ApplyAtCurrentCombatTime(CharacterCombatRuntime combat)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (combat.Game.Phase != SessionPhase.Running) return 0;
            double time = combat.Time;
            var before = combat.MobileEnemySnapshot();
            var pending = new List<Position>();
            foreach (var enemy in before)
            {
                double x, y, progress;
                string evidence;
                if (!source.TryObserve(enemy.Id, time, out x, out y,
                    out progress, out evidence)) continue;
                if (double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y) ||
                    double.IsNaN(progress) || double.IsInfinity(progress) ||
                    progress < 0 || progress > 1 || !Trusted(evidence) ||
                    evidence.IndexOf(
                        Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256,
                        StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException(
                        "몬스터 위치·경로·원본 관측 근거가 확인되지 않았습니다.");
                pending.Add(new Position { Id = enemy.Id,
                    X = x, Y = y, Progress = progress });
            }

            // Observing is read-only. Nothing moves if an earlier observed
            // enemy or the combat clock was altered by the source adapter.
            var after = combat.MobileEnemySnapshot();
            if (combat.Time != time || before.Count != after.Count)
                throw new InvalidOperationException("몬스터 이동 관측 중 전투 상태가 변경됐습니다.");
            for (int i = 0; i < before.Count; i++)
                if (before[i].Id != after[i].Id ||
                    before[i].X != after[i].X || before[i].Y != after[i].Y ||
                    before[i].Health != after[i].Health ||
                    before[i].MaxHealth != after[i].MaxHealth)
                    throw new InvalidOperationException(
                        "몬스터 위치 관측기가 전투 상태를 변경했습니다.");

            int moved = 0;
            foreach (var target in pending)
                if (combat.TryMoveEnemy(target.Id, target.X, target.Y,
                    target.Progress)) moved++;
            return moved;
        }

        private static bool Trusted(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence)) return false;
            foreach (var disallowed in new[] {
                "TEST_ONLY", "TEST_FIXTURE", "SYNTHETIC", "MOCK_ONLY", "MODEL_ONLY" })
                if (evidence.IndexOf(disallowed,
                    StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }
    }
}
