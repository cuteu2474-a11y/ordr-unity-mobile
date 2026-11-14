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

    [Serializable]
    public sealed class CombatWaveLedgerState
    {
        public int WaveNumber;
        public string PlanEvidence;
        public int Spawned;
        public int Defeated;
        public int Escaped;
        public int BossSpawned;
        public int BossDefeated;
        public int BossEscaped;
        public List<string> DefeatedEnemyIds = new List<string>();
        public List<string> DefeatedBossIds = new List<string>();
    }

    [Serializable]
    public sealed class CombatWaveOwnerState
    {
        public string EnemyId;
        public int WaveNumber;
    }

    [Serializable]
    public sealed class CombatWaveDirectorState
    {
        public int Version = 1;
        public int ActiveWave;
        public double Elapsed;
        public int NextSpawn;
        public List<CombatWaveLedgerState> Waves =
            new List<CombatWaveLedgerState>();
        public List<CombatWaveOwnerState> Alive =
            new List<CombatWaveOwnerState>();
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
            // Immutable original ID -> rawcode index, created only once
            // at each wave start instead of searching all spawns per kill.
            public readonly Dictionary<string, string> OriginalRawcodes =
                new Dictionary<string, string>(StringComparer.Ordinal);
            // Records only an actual kill event from CombatResolution.
            public readonly Dictionary<string, string> DefeatedBossRawcodes =
                new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> DefeatedMonsterRawcodes =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public WaveLedger(VerifiedWavePlan plan)
            {
                Plan = plan;
                foreach (var spawn in plan.Spawns)
                {
                    OriginalRawcodes.Add(spawn.EnemyId, spawn.OriginalRawcode);
                    if (spawn.Kind == CharacterEnemyKind.Boss) BossPlanned++;
                }
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

        public CombatWaveDirectorState CaptureState()
        {
            var state = new CombatWaveDirectorState {
                ActiveWave = active == null ? 0 : active.WaveNumber,
                Elapsed = elapsed, NextSpawn = nextSpawn
            };
            var ids = new List<int>(waves.Keys);
            ids.Sort();
            foreach (var id in ids)
            {
                var ledger = waves[id];
                var row = new CombatWaveLedgerState {
                    WaveNumber = id, PlanEvidence = ledger.Plan.Evidence,
                    Spawned = ledger.Spawned, Defeated = ledger.Defeated,
                    Escaped = ledger.Escaped, BossSpawned = ledger.BossSpawned,
                    BossDefeated = ledger.BossDefeated,
                    BossEscaped = ledger.BossEscaped
                };
                row.DefeatedEnemyIds.AddRange(
                    ledger.DefeatedMonsterRawcodes.Keys);
                row.DefeatedBossIds.AddRange(
                    ledger.DefeatedBossRawcodes.Keys);
                row.DefeatedEnemyIds.Sort(StringComparer.Ordinal);
                row.DefeatedBossIds.Sort(StringComparer.Ordinal);
                state.Waves.Add(row);
            }
            foreach (var pair in spawnedOwners)
                state.Alive.Add(new CombatWaveOwnerState {
                    EnemyId = pair.Key, WaveNumber = pair.Value
                });
            state.Alive.Sort((a, b) =>
                string.CompareOrdinal(a.EnemyId, b.EnemyId));
            return state;
        }

        // All saved wave plans come from the CURRENT pinned source catalog,
        // not from a user-controlled save file. Restore never calls Begin:
        // that would respawn enemies and grant an extra round's rewards.
        public static CombatWaveDirector Restore(
            CharacterCombatRuntime combat, CombatWaveDirectorState state,
            Func<int, VerifiedWavePlan> sourcePlans)
        {
            if (combat == null || sourcePlans == null || state == null ||
                state.Version != 1 || state.Waves == null ||
                state.Alive == null || state.ActiveWave < 0 ||
                double.IsNaN(state.Elapsed) ||
                double.IsInfinity(state.Elapsed) || state.Elapsed < 0 ||
                state.NextSpawn < 0 ||
                state.ActiveWave != combat.Game.CurrentWave)
                throw new FormatException("웨이브 저장 시계/라운드가 잘못됐습니다.");
            if (state.ActiveWave == 0)
            {
                if (state.Waves.Count != 0 || state.Alive.Count != 0 ||
                    state.NextSpawn != 0 || state.Elapsed != 0)
                    throw new FormatException(
                        "시작 전 라운드에 적의 진행 기록이 남아 있습니다.");
                return new CombatWaveDirector();
            }
            var restored = new CombatWaveDirector();
            int previous = 0;
            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in state.Alive)
                if (item == null || string.IsNullOrWhiteSpace(item.EnemyId) ||
                    item.WaveNumber < 1 ||
                    item.WaveNumber > state.ActiveWave ||
                    !live.TryAdd(item.EnemyId, item.WaveNumber) ||
                    combat.EnemyOf(item.EnemyId) == null)
                    throw new FormatException(
                        "생존 몬스터 ID/라운드가 복구된 전장과 다릅니다.");
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var bossLive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in state.Waves)
            {
                if (saved == null || saved.WaveNumber != previous + 1 ||
                    saved.WaveNumber > state.ActiveWave ||
                    saved.DefeatedEnemyIds == null ||
                    saved.DefeatedBossIds == null)
                    throw new FormatException("라운드 장부의 순서가 잘못됐습니다.");
                var plan = sourcePlans(saved.WaveNumber);
                if (plan == null || plan.WaveNumber != saved.WaveNumber ||
                    saved.PlanEvidence != plan.Evidence ||
                    saved.Spawned < 0 || saved.Spawned > plan.Spawns.Count ||
                    saved.Defeated < 0 || saved.Escaped < 0 ||
                    saved.Defeated + saved.Escaped > saved.Spawned ||
                    saved.DefeatedEnemyIds.Count != saved.Defeated ||
                    saved.BossDefeated < 0 || saved.BossEscaped < 0 ||
                    saved.BossSpawned < 0 ||
                    saved.BossDefeated + saved.BossEscaped > saved.BossSpawned ||
                    saved.BossDefeated != saved.DefeatedBossIds.Count)
                    throw new FormatException("원본 몬스터 생성/처치 장부가 다릅니다.");
                int bossSpawned = 0;
                var spawnedIds = new HashSet<string>(StringComparer.Ordinal);
                var spawnedBosses = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < saved.Spawned; j++)
                {
                    var spawn = plan.Spawns[j];
                    spawnedIds.Add(spawn.EnemyId);
                    if (spawn.Kind == CharacterEnemyKind.Boss)
                    {
                        bossSpawned++;
                        spawnedBosses.Add(spawn.EnemyId);
                    }
                }
                if (bossSpawned != saved.BossSpawned)
                    throw new FormatException("보스 등장 장부가 일치하지 않습니다.");
                var dead = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in saved.DefeatedEnemyIds)
                    if (!spawnedIds.Contains(id) || !dead.Add(id) ||
                        live.ContainsKey(id))
                        throw new FormatException(
                            "처치된 몬스터가 원본 생성 계획과 다릅니다.");
                var deadBosses = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in saved.DefeatedBossIds)
                    if (!spawnedBosses.Contains(id) || !dead.Contains(id) ||
                        !deadBosses.Add(id))
                        throw new FormatException(
                            "보스 처치 장부가 원본 계획과 다릅니다.");
                int thisLive = 0, thisBossLive = 0;
                foreach (var pair in live)
                    if (pair.Value == saved.WaveNumber)
                    {
                        if (!spawnedIds.Contains(pair.Key) ||
                            !covered.Add(pair.Key))
                            throw new FormatException(
                                "생존 몬스터가 생성되지 않았거나 중복됩니다.");
                        thisLive++;
                        if (spawnedBosses.Contains(pair.Key))
                        {
                            thisBossLive++;
                            bossLive.Add(pair.Key);
                        }
                    }
                if (thisLive != saved.Spawned - saved.Defeated - saved.Escaped ||
                    thisBossLive != saved.BossSpawned - saved.BossDefeated -
                        saved.BossEscaped)
                    throw new FormatException(
                        "살아있는 일반/보스 적의 수가 장부와 다릅니다.");
                var ledger = new WaveLedger(plan) {
                    Spawned = saved.Spawned,
                    Defeated = saved.Defeated, Escaped = saved.Escaped,
                    BossSpawned = saved.BossSpawned,
                    BossDefeated = saved.BossDefeated,
                    BossEscaped = saved.BossEscaped
                };
                foreach (string id in dead)
                    ledger.DefeatedMonsterRawcodes.Add(
                        id, ledger.OriginalRawcodes[id]);
                foreach (string id in deadBosses)
                    ledger.DefeatedBossRawcodes.Add(
                        id, ledger.OriginalRawcodes[id]);
                restored.waves.Add(saved.WaveNumber, ledger);
                previous = saved.WaveNumber;
            }
            if (previous != state.ActiveWave ||
                covered.Count != state.Alive.Count)
                throw new FormatException("복원할 라운드/몬스터가 누락됐습니다.");
            var current = restored.waves[state.ActiveWave];
            if (state.NextSpawn != current.Spawned ||
                (state.NextSpawn > 0 &&
                 state.Elapsed < current.Plan.Spawns[state.NextSpawn - 1].AtSeconds) ||
                (state.NextSpawn < current.Plan.Spawns.Count &&
                 state.Elapsed > current.Plan.Spawns[state.NextSpawn].AtSeconds))
                throw new FormatException("출현 예정 시각과 생성 장부가 다릅니다.");
            restored.active = current.Plan;
            restored.activeLedger = current;
            restored.nextSpawn = state.NextSpawn;
            restored.elapsed = state.Elapsed;
            foreach (var pair in live)
                restored.spawnedOwners.Add(pair.Key, pair.Value);
            foreach (string id in bossLive)
                restored.liveBossInstances.Add(id);
            // Attach only after EVERY dependency validated successfully.
            restored.boundCombat = combat;
            combat.EnemyExited += restored.OnEnemyExited;
            return restored;
        }

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
                string rawcode = ledger.OriginalRawcodes[enemyId];
                ledger.DefeatedMonsterRawcodes.Add(enemyId, rawcode);
                if (boss)
                {
                    ledger.BossDefeated++;
                    ledger.DefeatedBossRawcodes.Add(enemyId, rawcode);
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

        // A crowded formation may exceed CharacterCombatRuntime's safe attack
        // budget for one Step. Drain the exact unprocessed time with zero NEW
        // seconds before spawning the next monster or exposing a later round.
        // Never skip attacks, re-add elapsed time, or synthesize a kill.
        private static void StepCombatFully(CharacterCombatRuntime combat,
            double seconds, List<CharacterCombatEvent> events)
        {
            events.AddRange(combat.Step(seconds));
            const int maximumContinuationBatches = 64;
            int batches = 0;
            while (combat.PendingSimulationSeconds > 0)
            {
                if (++batches > maximumContinuationBatches)
                    throw new InvalidOperationException(
                        "한 전투 구간의 공격량이 안전 처리 상한을 초과했습니다.");
                events.AddRange(combat.Step(0));
            }
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
                StepCombatFully(combat, advance, events);
                elapsed += advance;

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
            StepCombatFully(combat, end - elapsed, events);
            elapsed = end;
            return events.AsReadOnly();
        }
    }
}
