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
        // Identifies the original monster object, not the runtime instance ID.
        // Older manually verified wave callers can leave it unknown.
        public string OriginalRawcode { get; private set; }
        public Original323NativePassiveTargetProfile NativePassiveTargetProfile { get; private set; }

        public VerifiedWaveEnemy(double atSeconds, string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            EnemyDefenseProfile defenses)
            : this(atSeconds, enemyId, health, kind, x, y, pathProgress,
                defenses, null) { }

        public VerifiedWaveEnemy(double atSeconds, string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            EnemyDefenseProfile defenses, string originalRawcode,
            Original323NativePassiveTargetProfile nativePassiveTargetProfile = null)
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
            if (originalRawcode != null && originalRawcode.Length != 4)
                throw new ArgumentException("몬스터 원본 rawcode는 4글자여야 합니다.");
            if (nativePassiveTargetProfile != null &&
                nativePassiveTargetProfile.OriginalUnitRawcode != originalRawcode)
                throw new ArgumentException("원본 몬스터와 네이티브 대상 분류 객체가 다릅니다.");
            AtSeconds = atSeconds; EnemyId = enemyId; Health = health;
            Kind = kind; X = x; Y = y; PathProgress = pathProgress;
            Defenses = defenses ?? throw new ArgumentNullException(nameof(defenses));
            OriginalRawcode = originalRawcode;
            NativePassiveTargetProfile = nativePassiveTargetProfile;
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
    // Read-only accounting for one VERIFIED spawn plan. "All defeated" is an
    // observation only, not proof of an original reward, timer or victory rule.
    public sealed class VerifiedWaveProgress
    {
        public int WaveNumber { get; private set; }
        public int Planned { get; private set; }
        public int Spawned { get; private set; }
        public int Defeated { get; private set; }
        public int Escaped { get; private set; }
        public int BossPlanned { get; private set; }
        public int BossSpawned { get; private set; }
        public int BossDefeated { get; private set; }
        public int BossEscaped { get; private set; }
        public int LivingBosses { get { return BossSpawned - BossDefeated - BossEscaped; } }
        // No boss on this round is NOT a boss-clear event.
        public bool AllBossesDefeated
        {
            get { return BossPlanned > 0 && BossSpawned == BossPlanned &&
                         BossDefeated == BossPlanned && BossEscaped == 0; }
        }
        public int PendingSpawns { get { return Planned - Spawned; } }
        public int LivingFromWave { get { return Spawned - Defeated - Escaped; } }
        public bool AllSpawnsIssued { get { return PendingSpawns == 0; } }
        public bool AllWaveEnemiesResolved
        {
            get { return AllSpawnsIssued && LivingFromWave == 0; }
        }
        public bool AllWaveEnemiesDefeated
        {
            get { return AllWaveEnemiesResolved && Escaped == 0; }
        }

        internal VerifiedWaveProgress(int waveNumber, int planned,
            int spawned, int defeated, int escaped,
            int bossPlanned, int bossSpawned, int bossDefeated, int bossEscaped)
        {
            if (planned < 1 || spawned < 0 || spawned > planned ||
                defeated < 0 || escaped < 0 || defeated + escaped > spawned ||
                bossPlanned < 0 || bossPlanned > planned ||
                bossSpawned < 0 || bossSpawned > bossPlanned ||
                bossDefeated < 0 || bossEscaped < 0 ||
                bossDefeated + bossEscaped > bossSpawned ||
                bossDefeated > defeated || bossEscaped > escaped)
                throw new ArgumentException("라운드 몬스터 집계에 모순이 있습니다.");
            WaveNumber = waveNumber; Planned = planned; Spawned = spawned;
            Defeated = defeated; Escaped = escaped;
            BossPlanned = bossPlanned; BossSpawned = bossSpawned;
            BossDefeated = bossDefeated; BossEscaped = bossEscaped;
        }
    }

    // Every wave owns its enemy instances independently. Existing monsters
    // may remain alive after a subsequent verified wave is allowed to start:
    // the source map, not this bookkeeping layer, must decide round timing.
    public sealed class CombatWaveDirector
    {
        private sealed class WaveLedger
        {
            public readonly VerifiedWavePlan Plan;
            public int Spawned;
            public int Defeated;
            public int Escaped;
            public int BossPlanned;
            public int BossSpawned;
            public int BossDefeated;
            public int BossEscaped;
            // Records only an actual kill event from CombatResolution.
            public readonly Dictionary<string, string> DefeatedBossRawcodes =
                new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> DefeatedMonsterRawcodes =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public WaveLedger(VerifiedWavePlan plan)
            {
                Plan = plan;
                foreach (var spawn in plan.Spawns)
                    if (spawn.Kind == CharacterEnemyKind.Boss) BossPlanned++;
            }

            public VerifiedWaveProgress Snapshot()
            {
                return new VerifiedWaveProgress(Plan.WaveNumber,
                    Plan.Spawns.Count, Spawned, Defeated, Escaped,
                    BossPlanned, BossSpawned, BossDefeated, BossEscaped);
            }
        }

        private readonly Dictionary<int, WaveLedger> waves =
            new Dictionary<int, WaveLedger>();
        private readonly Dictionary<string, int> spawnedOwners =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> liveBossInstances =
            new HashSet<string>(StringComparer.Ordinal);
        private CharacterCombatRuntime boundCombat;
        private VerifiedWavePlan active;
        private WaveLedger activeLedger;
        private int nextSpawn;
        private double elapsed;

        // Raised after a verified actual death is recorded. Escapes and
        // unscheduled enemies must never award death-only original resources.
        public event Action<int, string> VerifiedMonsterDefeated;
        // Real escape events never count as kills or provide kill-only rewards.
        public event Action<int, string> VerifiedMonsterEscaped;

        public bool IsActive { get { return active != null; } }
        public int RemainingSpawns
        {
            get { return active == null ? 0 : active.Spawns.Count - nextSpawn; }
        }
        public double ElapsedSeconds { get { return elapsed; } }
        public VerifiedWaveProgress CurrentProgress
        {
            get { return activeLedger == null ? null : activeLedger.Snapshot(); }
        }

        public bool TryGetWaveProgress(int waveNumber, out VerifiedWaveProgress progress)
        {
            WaveLedger ledger;
            if (waves.TryGetValue(waveNumber, out ledger))
            {
                progress = ledger.Snapshot();
                return true;
            }
            progress = null;
            return false;
        }

        // Only an actual, still-living verified spawn may receive a native
        // wave-bound exit event. Unscheduled enemies are never approved.
        public bool TryGetLivingMonsterSpawnWave(string instanceId, out int waveNumber)
        {
            waveNumber = 0;
            return !string.IsNullOrWhiteSpace(instanceId) &&
                spawnedOwners.TryGetValue(instanceId, out waveNumber);
        }

        // Includes normal, boss and enraged deaths. Escape is NOT a death.
        // The death-time global round can differ from the spawn-wave number.
        public bool TryGetDefeatedMonsterRawcode(int spawnWaveNumber,
            string instanceId, out string originalRawcode)
        {
            originalRawcode = null;
            WaveLedger ledger;
            return !string.IsNullOrWhiteSpace(instanceId) &&
                waves.TryGetValue(spawnWaveNumber, out ledger) &&
                ledger.DefeatedMonsterRawcodes.TryGetValue(instanceId,
                    out originalRawcode);
        }

        // A separate original-timer witness must still confirm kill timing
        // before any score may be awarded. A boss escape is not a kill.
        public bool TryGetDefeatedBossRawcode(int waveNumber, string instanceId,
            out string originalRawcode)
        {
            originalRawcode = null;
            WaveLedger ledger;
            return !string.IsNullOrWhiteSpace(instanceId) &&
                waves.TryGetValue(waveNumber, out ledger) &&
                ledger.DefeatedBossRawcodes.TryGetValue(instanceId,
                    out originalRawcode);
        }

        private void OnEnemyExited(string enemyId, CombatEnemyExitReason reason)
        {
            int waveNumber;
            if (!spawnedOwners.TryGetValue(enemyId, out waveNumber))
                return; // Spawned outside the verified schedule, not this wave.
            spawnedOwners.Remove(enemyId);
            bool boss = liveBossInstances.Remove(enemyId);
            WaveLedger ledger = waves[waveNumber];
            if (reason == CombatEnemyExitReason.Defeated)
            {
                ledger.Defeated++;
                foreach (var spawn in ledger.Plan.Spawns)
                    if (spawn.EnemyId == enemyId)
                    {
                        ledger.DefeatedMonsterRawcodes.Add(enemyId,
                            spawn.OriginalRawcode);
                        break;
                    }
                if (boss)
                {
                    ledger.BossDefeated++;
                    // Preserve the original wave-specific boss rawcode as
                    // proof of what died; IDs can be reused in later waves.
                    foreach (var spawn in ledger.Plan.Spawns)
                        if (spawn.Kind == CharacterEnemyKind.Boss &&
                            spawn.EnemyId == enemyId)
                        {
                            ledger.DefeatedBossRawcodes.Add(enemyId,
                                spawn.OriginalRawcode);
                            break;
                        }
                }
                var defeated = VerifiedMonsterDefeated;
                if (defeated != null) defeated(waveNumber, enemyId);
            }
            else if (reason == CombatEnemyExitReason.Escaped)
            {
                ledger.Escaped++;
                if (boss) ledger.BossEscaped++;
                var callback = VerifiedMonsterEscaped;
                if (callback != null) callback(waveNumber, enemyId);
            }
            else throw new InvalidOperationException("지원하지 않는 몬스터 종료 종류입니다.");
        }

        public void Begin(CharacterCombatRuntime combat, VerifiedWavePlan plan)
        {
            if (combat == null || plan == null)
                throw new ArgumentNullException("전투와 원본 웨이브 계획이 필요합니다.");
            if (active != null && RemainingSpawns != 0)
                throw new InvalidOperationException("이전 웨이브의 출현이 끝나지 않았습니다.");
            if (boundCombat != null && !ReferenceEquals(boundCombat, combat))
                throw new InvalidOperationException("진행 기록은 하나의 전투 세션에만 연결됩니다.");
            if (waves.ContainsKey(plan.WaveNumber))
                throw new InvalidOperationException("같은 라운드를 두 번 시작할 수 없습니다.");

            // Do not attach an observer or start a plan if the sequential
            // GameSession wave validation fails.
            combat.BeginWave(plan.WaveNumber);
            if (boundCombat == null)
            {
                boundCombat = combat;
                boundCombat.EnemyExited += OnEnemyExited;
            }
            var ledger = new WaveLedger(plan);
            waves.Add(plan.WaveNumber, ledger);
            active = plan; activeLedger = ledger;
            elapsed = 0; nextSpawn = 0;
        }

        public IReadOnlyList<CharacterCombatEvent> Step(
            CharacterCombatRuntime combat, double seconds)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (active == null) throw new InvalidOperationException("진행 중인 웨이브가 없습니다.");
            if (!ReferenceEquals(boundCombat, combat))
                throw new InvalidOperationException("라운드 시작 때 사용한 전투 세션이 아닙니다.");
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

                // Spawn same-time monsters before attacking any of them.
                while (nextSpawn < active.Spawns.Count &&
                       active.Spawns[nextSpawn].AtSeconds == spawnTime)
                {
                    var spawn = active.Spawns[nextSpawn];
                    if (spawnedOwners.ContainsKey(spawn.EnemyId))
                        throw new InvalidOperationException(
                            "이전 라운드에서 생존 중인 몬스터 ID와 중복됩니다.");
                    combat.SpawnEnemyWithDefenses(spawn.EnemyId, spawn.Health,
                        spawn.Kind, spawn.X, spawn.Y, spawn.PathProgress,
                        spawn.Defenses);
                    if (spawn.NativePassiveTargetProfile != null &&
                        !combat.EnemyOf(spawn.EnemyId).TrySetNativePassiveTargetProfile(
                            0, spawn.NativePassiveTargetProfile))
                        throw new InvalidOperationException("실제 몬스터의 네이티브 스킬 대상 상태 연결 실패");
                    // Register only after a successful actual spawn.
                    spawnedOwners.Add(spawn.EnemyId, active.WaveNumber);
                    if (spawn.Kind == CharacterEnemyKind.Boss)
                    {
                        liveBossInstances.Add(spawn.EnemyId);
                        activeLedger.BossSpawned++;
                    }
                    activeLedger.Spawned++;
                    nextSpawn++;
                }
            }
            events.AddRange(combat.Step(end - elapsed));
            elapsed = end;
            return events.AsReadOnly();
        }
    }
}
