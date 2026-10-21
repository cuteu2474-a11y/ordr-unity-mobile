using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // 원본 라운드 데이터가 공급된 경우에만 이 웨이브 일정이 작동한다.
    public sealed class VerifiedWaveEnemy
    {
        public double AtSeconds { get; private set; }
        public string EnemyId { get; private set; }
        public long Health { get; private set; }
        public CharacterEnemyKind Kind { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double PathProgress { get; private set; }
        public EnemyDefenseProfile Defenses { get; private set; }

        public VerifiedWaveEnemy(double atSeconds, string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            EnemyDefenseProfile defenses)
        {
            if (double.IsNaN(atSeconds) || double.IsInfinity(atSeconds) || atSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(atSeconds));
            if (string.IsNullOrWhiteSpace(enemyId) || health <= 0 ||
                !Enum.IsDefined(typeof(CharacterEnemyKind), kind))
                throw new ArgumentException("웨이브 적 ID·체력·분류가 유효하지 않습니다.");
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(pathProgress) || double.IsInfinity(pathProgress))
                throw new ArgumentException("웨이브 적 위치가 유효하지 않습니다.");
            AtSeconds = atSeconds; EnemyId = enemyId; Health = health;
            Kind = kind; X = x; Y = y; PathProgress = pathProgress;
            Defenses = defenses ?? throw new ArgumentNullException(nameof(defenses));
        }
    }

    public sealed class VerifiedWavePlan
    {
        public int WaveNumber { get; private set; }
        public string Evidence { get; private set; }
        public IReadOnlyList<VerifiedWaveEnemy> Spawns { get; private set; }

        public VerifiedWavePlan(int waveNumber,
            IEnumerable<VerifiedWaveEnemy> orders, string evidence)
        {
            if (waveNumber < 1) throw new ArgumentOutOfRangeException(nameof(waveNumber));
            if (string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("해당 버전 웨이브 출처가 필요합니다.");
            if (orders == null) throw new ArgumentNullException(nameof(orders));
            var list = new List<VerifiedWaveEnemy>(orders);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in list)
            {
                if (enemy == null) throw new ArgumentException("빈 몬스터 데이터");
                if (!ids.Add(enemy.EnemyId))
                    throw new ArgumentException("웨이브 내 몬스터 ID 중복");
            }
            // 동시 출현 적은 원본에 등록된 순서를 유지한다.
            for (int i = 1; i < list.Count; i++)
            {
                var value = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].AtSeconds > value.AtSeconds)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = value;
            }
            WaveNumber = waveNumber; Evidence = evidence;
            Spawns = list.AsReadOnly();
        }
    }

    // 웨이브의 정확한 출현 시각 사이에서 기존 전투 시간을 진행한다.
    // 실제 HP/개체 수/웨이브 간격은 원작 계획 데이터를 주입한다.
    public sealed class CombatWaveDirector
    {
        private VerifiedWavePlan active;
        private int nextSpawn;
        private double elapsed;

        public bool IsActive { get { return active != null; } }
        public int RemainingSpawns
        {
            get { return active == null ? 0 : active.Spawns.Count - nextSpawn; }
        }
        public double ElapsedSeconds { get { return elapsed; } }

        public void Begin(CharacterCombatRuntime combat, VerifiedWavePlan plan)
        {
            if (combat == null || plan == null)
                throw new ArgumentNullException("전투와 원본 웨이브 계획이 필요합니다.");
            if (active != null && RemainingSpawns != 0)
                throw new InvalidOperationException("이전 웨이브의 출현이 끝나지 않았습니다.");
            combat.BeginWave(plan.WaveNumber);
            active = plan; elapsed = 0; nextSpawn = 0;
        }

        public IReadOnlyList<CharacterCombatEvent> Step(
            CharacterCombatRuntime combat, double seconds)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (active == null) throw new InvalidOperationException("진행 중인 웨이브가 없습니다.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ||
                double.IsInfinity(elapsed + seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double end = elapsed + seconds;
            var events = new List<CharacterCombatEvent>();
            while (nextSpawn < active.Spawns.Count &&
                   active.Spawns[nextSpawn].AtSeconds <= end)
            {
                double spawnTime = active.Spawns[nextSpawn].AtSeconds;
                double advance = Math.Max(0, spawnTime - elapsed);
                events.AddRange(combat.Step(advance));
                elapsed += advance;
                if (combat.PendingSimulationSeconds > 0)
                    throw new InvalidOperationException(
                        "전투 계산 지연 중입니다. 출현 시점을 건너뛰지 않습니다.");

                // 같은 시각의 적은 모두 생성한 다음 공격을 처리해야 한다.
                // 매 적 사이에 Step(0)을 끼워 넣으면 첫 적만 먼저 맞는 오류가 생김.
                while (nextSpawn < active.Spawns.Count &&
                       active.Spawns[nextSpawn].AtSeconds == spawnTime)
                {
                    var spawn = active.Spawns[nextSpawn];
                    combat.SpawnEnemyWithDefenses(spawn.EnemyId, spawn.Health,
                        spawn.Kind, spawn.X, spawn.Y, spawn.PathProgress,
                        spawn.Defenses);
                    nextSpawn++;
                }
            }
            events.AddRange(combat.Step(end - elapsed));
            elapsed = end;
            return events.AsReadOnly();
        }
    }
}
