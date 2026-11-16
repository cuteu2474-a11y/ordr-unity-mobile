using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Mobile-only positions derived from the source model's four route centers.
    // These numbers describe the simplified phone layout, not original WC3
    // observed placements; no gameplay range, speed or damage is fabricated.
    public sealed class SourceModelMainLaneGeometry
    {
        public double LeftX { get; private set; }
        public double BottomY { get; private set; }
        public double TopY { get; private set; }
        private double Height => TopY - BottomY;

        public SourceModelMainLaneGeometry(IReadOnlyList<CombatPathPoint> corners)
        {
            if (corners == null || corners.Count != 4)
                throw new ArgumentException("메인 전장 사각 경로 네 지점이 필요합니다.", nameof(corners));
            double minX = double.MaxValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in corners)
            {
                if (p == null) throw new ArgumentException("경로 지점이 없습니다.", nameof(corners));
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
            if (maxY <= minY) throw new ArgumentException("보스 왕복 구간이 없습니다.", nameof(corners));
            LeftX = minX; BottomY = minY; TopY = maxY;
        }

        public CombatPathPoint BossPosition(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0)
                throw new ArgumentOutOfRangeException(nameof(distance));
            double offset = distance % (2d * Height);
            // Zero-duration turns: top -> bottom -> top, repeat without leaks.
            double y = offset <= Height ? TopY - offset : BottomY + offset - Height;
            return new CombatPathPoint(LeftX, y);
        }

        public double BossProgress(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0)
                throw new ArgumentOutOfRangeException(nameof(distance));
            return (distance % (2d * Height)) / (2d * Height);
        }

        // One left-middle rally anchor. Physical markers occupy compact nearby
        // positions; targeting still uses each actor's real finite source range.
        public CombatPathPoint RallyPoint =>
            new CombatPathPoint(LeftX - 120d, BottomY + Height / 2d);

        private static readonly int[] CenterOutRows =
            { 0, -1, 1, -2, 2, -3, 3, -4, 4 };

        public CombatPathPoint UnitPosition(long instanceId)
        {
            if (instanceId <= 0) throw new ArgumentOutOfRangeException(nameof(instanceId));
            // Fill the center of one shared rally first, rather than placing
            // early recruits along the full 2,176-unit-high left boundary.
            // Two columns per band, nine center-out rows, two bands = 36 slots.
            long ordinal = instanceId - 1;
            int slot = (int)(ordinal % 36);
            int column = (slot / 18) * 2 + slot % 2;
            int row = CenterOutRows[(slot % 18) / 2];
            double spacing = Math.Min(64d, Height / 12d);
            long layer = ordinal / 36;
            // Extra actors share the same rally cluster with small, stable
            // offsets. No expanding rows can push later recruits out of range.
            double staggerX = layer == 0 ? 0 :
                spacing * (1 + (layer - 1) % 3) / 10d;
            double staggerY = layer == 0 ? 0 :
                spacing * (1 + ((layer - 1) / 3) % 3) / 8d *
                (layer % 2 == 0 ? -1d : 1d);
            var rally = RallyPoint;
            return new CombatPathPoint(rally.X - column * spacing - staggerX,
                rally.Y + row * spacing + staggerY);
        }
    }

    // Explicit model input, separate from the verified-wave JSON schema.
    // Immutable original spawn identity/timing is combined with the existing
    // reference-engine Nightmare HP model. It never becomes retail evidence.
    public sealed class SourceModelPracticeCatalog
    {
        public sealed class Round
        {
            public int Number { get; internal set; }
            public string Rawcode { get; internal set; }
            public string Name { get; internal set; }
            public string Role { get; internal set; }
            public long Health { get; internal set; }
            public double Armor { get; internal set; }
            public string ArmorType { get; internal set; }
            public double MovementSpeed { get; internal set; }
        }
        // Each new-world boss wave can contain both a line enemy and a boss.
        // Never let the last row in the JSON overwrite the other role.
        private readonly Dictionary<string, Round> rounds = new Dictionary<string, Round>();
        private readonly int? standaloneInitialMagicAbilityLevel;
        private readonly VerifiedCombatPath route;
        public IReadOnlyList<CombatPathPoint> RouteCorners { get; private set; }
        public SourceModelMainLaneGeometry MainLane { get; private set; }
        public int LastRound { get; private set; }
        public int EnemyLimit => 70;
        public int RoundWisps => 2;
        // Source-backed pre-wave pauses only in the standalone 65R model.
        // They are not independent proof of Warcraft's absolute round clock.
        public double ModelLeadInSeconds(int round)
        {
            if (round < 1 || round > LastRound) throw new ArgumentOutOfRangeException(nameof(round));
            if (!standaloneInitialMagicAbilityLevel.HasValue) return 0;
            return round == 1 ? Original323BossRoundRules.FirstRoundWaitSeconds :
                round == 51 ? Original323BossRoundRules.PostRound50TransitionSeconds : 0;
        }
        public CombatPathPoint Spawn => route.AtDistance(0);
        public CombatPathPoint BossSpawn => MainLane.BossPosition(0);
        private static string RoundKey(int number, string role) => number + ":" + role;
        public Round GetRound(int number, CharacterEnemyKind kind)
        {
            return rounds[RoundKey(number, kind == CharacterEnemyKind.Boss ? "boss" : "line")];
        }
        public CombatPathPoint Position(double travelled) => route.AtDistance(travelled % route.Length);
        public CombatPathPoint BossPosition(double travelled) => MainLane.BossPosition(travelled);
        public double BossProgress(double travelled) => MainLane.BossProgress(travelled);
        // TargetPriority.FurthestAlongPath consumes this 0..1 route progress.
        // A lap is never an escape; after looping, priority follows the current lap.
        public double PathProgress(double travelled)
        {
            if (double.IsNaN(travelled) || double.IsInfinity(travelled) || travelled < 0)
                throw new ArgumentOutOfRangeException(nameof(travelled));
            return (travelled % route.Length) / route.Length;
        }

        public SourceModelPracticeCatalog(string json, int lastRound = 10)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "practice model");
            string format = OrdrJson.RequiredString(root, "format");
            int sourceLastRound = (int)Number(root, "lastRound");
            bool originalTraining = format == "ordr-mobile-practice-model-v1" && sourceLastRound == 20;
            bool standalone = format == "ordr-mobile-standalone-source-model-v1" && sourceLastRound == 65;
            if (lastRound != 10 && lastRound != 20 && lastRound != 65)
                throw new ArgumentOutOfRangeException(nameof(lastRound));
            if ((!originalTraining && !standalone) || lastRound > sourceLastRound)
                throw new ArgumentOutOfRangeException(nameof(lastRound), "요청한 라운드 전체의 원본 모델이 필요합니다.");
            LastRound = lastRound;
            if (standalone)
            {
                double level = Number(root, "initialMagicAbilityLevel");
                if (level != Original323NightmareA04QSpawnState.NightmareInitialLevel)
                    throw new FormatException("독립 65R 적의 원본 초기 A04Q 레벨이 다릅니다.");
                standaloneInitialMagicAbilityLevel = (int)level;
            }
            if (
                OrdrJson.RequiredString(root, "sourceW3xSha256") != OriginalCommonSaleRules.OriginalMapSha256 ||
                OrdrJson.RequiredString(root, "sourceJassSha256") != Original323SourceCommonWispWorld.PinnedWar3mapJSha256 ||
                !Equals(OrdrJson.Value(root, "retailParityVerified"), false) ||
                !Equals(OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime"), false) ||
                Number(root, "initialEnemyLimit") != EnemyLimit ||
                Number(root, "roundRandomWisps") != RoundWisps)
                throw new FormatException("몬스터 전투 모델의 출처·미검증 표시가 필요합니다.");
            var points = new List<CombatPathPoint>();
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "routeCenters"), "route"))
            {
                var row = OrdrJson.Object(raw, "point");
                points.Add(new CombatPathPoint(Number(row, "x"), Number(row, "y")));
            }
            if (points.Count != 4) throw new FormatException("원본 라인 경로 중심 4개가 필요합니다.");
            RouteCorners = new List<CombatPathPoint>(points).AsReadOnly();
            MainLane = new SourceModelMainLaneGeometry(RouteCorners);
            points.Add(new CombatPathPoint(points[0].X, points[0].Y));
            route = new VerifiedCombatPath(points, "MODEL_ONLY: CSR/CSU/CSX/CSa rectangle centers; native turning unverified");
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "rounds"), "rounds"))
            {
                var row = OrdrJson.Object(raw, "round");
                double number = Number(row, "wave"), hp = Number(row, "health");
                double speed = Number(row, "objectMovementSpeed");
                string code = OrdrJson.RequiredString(row, "rawcode");
                string armor = OrdrJson.RequiredString(row, "armorType");
                Ordr323ArmorType parsed;
                string role = OrdrJson.RequiredString(row, "role");
                string key = RoundKey((int)number, role);
                if (number < 1 || number > sourceLastRound || number != Math.Floor(number) ||
                    hp < 1 || hp >= long.MaxValue || hp != Math.Floor(hp) || speed <= 0 ||
                    code.Length != 4 || (role != "line" && role != "boss") ||
                    rounds.ContainsKey(key) ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(armor, out parsed))
                    throw new FormatException("연습 몬스터의 출처 수치가 올바르지 않습니다.");
                rounds.Add(key, new Round { Number = (int)number,
                    Rawcode = code, Name = OrdrJson.RequiredString(row, "name"),
                    Role = role, Health = (long)hp,
                    Armor = Number(row, "armor"), ArmorType = armor, MovementSpeed = speed });
            }
            int expected = sourceLastRound == 20 ? 20 : 68;
            if (rounds.Count != expected)
                throw new FormatException("원본의 일반·보스 라운드 모델 자료 누락");
        }
        // A saved wave's evidence includes native first/transition lead-in.
        // Reusing the base plan alone would reject valid mid-round saves.
        public VerifiedWavePlan BuildRecoverablePracticePlan(int number,
            Original323MonsterWaveIdentityCatalog original,
            bool mobileBossPatrol = false)
        {
            var basePlan = BuildPracticePlan(number, original, mobileBossPatrol);
            double lead = ModelLeadInSeconds(number);
            if (lead <= 0) return basePlan;
            var shifted = new List<VerifiedWaveEnemy>();
            foreach (var spawn in basePlan.Spawns)
                shifted.Add(new VerifiedWaveEnemy(
                    spawn.AtSeconds + lead, spawn.EnemyId, spawn.Health,
                    spawn.Kind, spawn.X, spawn.Y, spawn.PathProgress, spawn.Defenses,
                    spawn.OriginalRawcode, spawn.NativePassiveTargetProfile));
            return new VerifiedWavePlan(number, shifted,
                basePlan.Evidence + "; MODEL_ONLY: original first/transition lead-in");
        }

        private static double Number(Dictionary<string, object> row, string key)
        {
            double value;
            if (!OrdrJson.TryNumber(row, key, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("연습 자료 숫자 누락: " + key);
            return value;
        }
        public VerifiedWavePlan BuildPracticePlan(int number, Original323MonsterWaveIdentityCatalog original,
            bool mobileBossPatrol = false)
        {
            if (number < 1 || number > LastRound) throw new ArgumentOutOfRangeException(nameof(number));
            var spawns = new List<VerifiedWaveEnemy>();
            foreach (var entry in original.BuildSinglePlayerSpawnTemplates(number))
            {
                Round model;
                if (!rounds.TryGetValue(RoundKey(number,
                    entry.Kind == CharacterEnemyKind.Boss ? "boss" : "line"), out model))
                    throw new FormatException("라운드 몬스터 역할 모델 누락");
                if (entry.Rawcode != model.Rawcode ||
                    (entry.Kind == CharacterEnemyKind.Boss ? "boss" : "line") != model.Role)
                    throw new FormatException("연습 모델과 원본 출현 객체가 다릅니다.");
                // A04Q is assigned at native spawn. Its starting Nightmare level
                // and Def5 table are source-derived, while later events remain unmodeled.
                EnemyDefenseProfile defense = standaloneInitialMagicAbilityLevel.HasValue
                    ? Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                        entry.Kind, model.Armor, model.ArmorType,
                        standaloneInitialMagicAbilityLevel.Value,
                        "MODEL_ONLY: JASS initial A04Q + W3A Def5")
                    : new EnemyDefenseProfile(entry.Kind, model.Armor, null,
                        model.ArmorType, "MODEL_ONLY: reference-engine initial defense");
                var point = mobileBossPatrol && entry.Kind == CharacterEnemyKind.Boss
                    ? BossSpawn : Spawn;
                spawns.Add(new VerifiedWaveEnemy(entry.AtSeconds, entry.InstanceId, model.Health,
                    entry.Kind, point.X, point.Y, 0, defense, model.Rawcode));
            }
            return new VerifiedWavePlan(number, spawns, "MODEL_ONLY: development practice; NOT retail live evidence");
        }
    }

    // Read-only session history. Values describe this practice interval,
    // including kills of enemies carried over from earlier rounds.
    public sealed class SourceModelPracticeRoundResult
    {
        public int Wave { get; internal set; }
        public double ElapsedSeconds { get; internal set; }
        public int Defeated { get; internal set; }
        public int AliveEnemies { get; internal set; }
        public long GoldAwarded { get; internal set; }
        public double LeadInSeconds { get; internal set; }
        public int BossScoreAwarded { get; internal set; }
    }

    // Instant read-only roster readiness and separately observed COMMITTED
    // wave-HP damage. The damage witness comes from the shared resolution
    // event, not GUI events, estimates, attack previews or synthetic kills.
    public sealed class SourceModelPracticeActorDamage
    {
        public string ActorId { get; internal set; }
        public long AppliedHpDamage { get; internal set; }
        public int DamageEvents { get; internal set; }
    }

    public sealed class SourceModelPracticeCombatPressure
    {
        public int Wave { get; internal set; }
        public int AliveEnemies { get; internal set; }
        public IReadOnlyList<BasicAttackReadiness> Units { get; internal set; }
        public int Ready { get; internal set; }
        public int Cooldown { get; internal set; }
        public int UnverifiedSource { get; internal set; }
        public int UnverifiedTarget { get; internal set; }
        public int OutOfRange { get; internal set; }
        public int FormulaDeferred { get; internal set; }
        public int NoBattlefieldTarget { get; internal set; }
        public int EligibleToAttack => Ready + Cooldown;
        // Observational counters only; a restored checkpoint starts an
        // explicitly incomplete observation and never replays old damage.
        public long ObservedWaveHpDamageThisRound { get; internal set; }
        public long ObservedWaveHpDamageSinceAttach { get; internal set; }
        public int ObservedDamageEventsThisRound { get; internal set; }
        public int ObservedDamagingActorsThisRound { get; internal set; }
        public IReadOnlyList<SourceModelPracticeActorDamage> ActorDamageThisRound
        { get; internal set; }
        public bool ObservationIncludesRoundStart { get; internal set; }
    }

    // Drives the existing combat runtime/director/wallet. No parallel damage
    // engine, forged native observer, fake live approval or free unit spawns.
    [Serializable]
    public sealed class SourceModelPracticeEnemyClockState
    {
        public string EnemyId;
        public double Time;
    }

    [Serializable]
    public sealed class SourceModelPracticeRoundHistoryState
    {
        public int Wave;
        public double ElapsedSeconds;
        public int Defeated;
        public int AliveEnemies;
        public long GoldAwarded;
        public double LeadInSeconds;
        public int BossScoreAwarded;
    }

    [Serializable]
    public sealed class SourceModelPracticeBattleState
    {
        public int Version = 1;
        public int LastRound;
        public int CurrentRound;
        public bool Started;
        public bool MobileMainLane;
        public double RoundStartTime;
        public double RoundLeadInSeconds;
        // Optional v1 fields: old checkpoints without these fields are inactive.
        public bool DeathCountActive;
        public double DeathCountStartedAt = -1d;
        public int RoundStartingDefeated;
        public long RoundStartingGold;
        public int RoundStartingBossScore;
        public long GoldAwarded;
        public int BossScoreAwarded;
        public int RoundWispsAwarded;
        public string ResultMessage;
        public List<SourceModelPracticeEnemyClockState> BornAt =
            new List<SourceModelPracticeEnemyClockState>();
        public List<SourceModelPracticeEnemyClockState> DistanceTravelled =
            new List<SourceModelPracticeEnemyClockState>();
        public List<string> RewardedEnemyIds = new List<string>();
        public List<SourceModelPracticeRoundHistoryState> History =
            new List<SourceModelPracticeRoundHistoryState>();
    }

    public sealed class SourceModelPracticeBattle : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly CombatWaveDirector waves;
        private readonly SharedEconomySession economy;
        private readonly SourceModelPracticeCatalog catalog;
        private readonly SourceModelBasicCombatRules damage;
        private readonly IOriginalWispRandom scriptRandom;
        private readonly Action roundStarted;
        private readonly bool mobileMainLaneEnabled;
        private readonly MonsterCountDeathCountdown deathCount;
        public bool MobileMainLaneEnabled => mobileMainLaneEnabled;
        private readonly List<VerifiedWavePlan> plans = new List<VerifiedWavePlan>();
        private readonly Dictionary<string, double> bornAt = new Dictionary<string, double>();
        private readonly Dictionary<string, double> distanceTravelled = new Dictionary<string, double>();
        private readonly HashSet<string> rewarded = new HashSet<string>();
        // Bounded by actual damage sources in one round. No saved/gameplay
        // balance state: reset on each round, and mark restore as partial.
        private readonly Dictionary<string, SourceModelPracticeActorDamage> roundDamageByActor =
            new Dictionary<string, SourceModelPracticeActorDamage>(StringComparer.Ordinal);
        private long observedRoundWaveHpDamage;
        private long observedWaveHpDamageSinceAttach;
        private int observedRoundDamageEvents;
        private bool observationIncludesRoundStart;
        private double roundStart;
        private double roundLeadInSeconds;
        private int roundStartingDefeated;
        private long roundStartingGold;
        private int roundStartingBossScore;
        internal Action<string, string> MoneyGamblingBossDefeated;
        private readonly List<SourceModelPracticeRoundResult> history = new List<SourceModelPracticeRoundResult>();
        public IReadOnlyList<SourceModelPracticeRoundResult> RoundHistory => history.AsReadOnly();
        public bool Started { get; private set; }
        public string ResultMessage { get; private set; }
        public long GoldAwarded { get; private set; }
        // New-world boss points are not a wallet currency or monster-kill gold.
        public int BossScoreAwarded { get; private set; }
        public int RoundWispsAwarded { get; private set; }
        public double PreparationSecondsRemaining =>
            !Started ? 0 : Math.Max(0, roundLeadInSeconds - (combat.Time - roundStart));
        public double SecondsRemaining
        {
            get
            {
                int seconds;
                if (!Started || !Original323BossRoundRules.TryGetCountdownSeconds(
                    combat.Game.CurrentWave, out seconds)) return 0;
                double remaining = seconds + roundLeadInSeconds - (combat.Time - roundStart);
                return remaining <= 1e-8 ? 0 : remaining;
            }
        }
        public IReadOnlyList<CombatPathPoint> RouteCorners => catalog.RouteCorners;
        public int LastRound => catalog.LastRound;
        public int EnemyLimit => catalog.EnemyLimit;
        public bool IsDeathCountdownActive => deathCount.IsActive;
        public double DeathCountdownSecondsRemaining =>
            deathCount.RemainingSeconds(combat.Time);
        public double DeathCountdownGraceSeconds => deathCount.GraceSeconds;

        // On-demand only: never scan all enemies/units in OnGUI or on every
        // combat substep. Includes off-screen roster pages; neither changes
        // attack schedules nor consumes the shared game RNG.
        public SourceModelPracticeCombatPressure CaptureCombatPressure()
        {
            if (!Started) throw new InvalidOperationException("전투 시작 전 압박도는 조회할 수 없습니다.");
            var actors = combat.CaptureBasicAttackDiagnostics();
            var report = new SourceModelPracticeCombatPressure {
                Wave = combat.Game.CurrentWave,
                AliveEnemies = combat.Game.AliveEnemies,
                Units = actors,
                ObservedWaveHpDamageThisRound = observedRoundWaveHpDamage,
                ObservedWaveHpDamageSinceAttach = observedWaveHpDamageSinceAttach,
                ObservedDamageEventsThisRound = observedRoundDamageEvents,
                ObservedDamagingActorsThisRound = roundDamageByActor.Count,
                ObservationIncludesRoundStart = observationIncludesRoundStart
            };
            var damageRows = new List<SourceModelPracticeActorDamage>();
            foreach (var pair in roundDamageByActor)
                damageRows.Add(new SourceModelPracticeActorDamage {
                    ActorId = pair.Key,
                    AppliedHpDamage = pair.Value.AppliedHpDamage,
                    DamageEvents = pair.Value.DamageEvents
                });
            damageRows.Sort((a, b) => string.CompareOrdinal(a.ActorId, b.ActorId));
            report.ActorDamageThisRound = damageRows.AsReadOnly();
            foreach (var actor in actors)
                switch (actor.Kind)
                {
                    case BasicAttackReadinessKind.Ready: report.Ready++; break;
                    case BasicAttackReadinessKind.Cooldown: report.Cooldown++; break;
                    case BasicAttackReadinessKind.UnverifiedSource: report.UnverifiedSource++; break;
                    case BasicAttackReadinessKind.UnverifiedTarget: report.UnverifiedTarget++; break;
                    case BasicAttackReadinessKind.OutOfRange: report.OutOfRange++; break;
                    case BasicAttackReadinessKind.FormulaDeferred: report.FormulaDeferred++; break;
                    case BasicAttackReadinessKind.NoBattlefieldTarget: report.NoBattlefieldTarget++; break;
                    default: throw new InvalidOperationException("알 수 없는 평타 진단 상태입니다.");
                }
            return report;
        }
        // Observes real post-armor, post-resistance HP damage while wave
        // ownership is still registered (death cleanup happens afterwards).
        // Story/creep/challenge hits, missing hits and non-damage defeats are
        // deliberately excluded. Telemetry must never throw/alter combat.
        private void ObserveActualWaveDamage(string actorId, string enemyId,
            long actualDamage, bool killed)
        {
            int spawnWave;
            if (!Started || actualDamage <= 0 ||
                !waves.TryGetLivingMonsterSpawnWave(enemyId, out spawnWave))
                return;
            observedRoundWaveHpDamage = SaturatingDamageAdd(
                observedRoundWaveHpDamage, actualDamage);
            observedWaveHpDamageSinceAttach = SaturatingDamageAdd(
                observedWaveHpDamageSinceAttach, actualDamage);
            if (observedRoundDamageEvents < int.MaxValue)
                observedRoundDamageEvents++;
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                SourceModelPracticeActorDamage row;
                if (!roundDamageByActor.TryGetValue(actorId, out row))
                {
                    row = new SourceModelPracticeActorDamage { ActorId = actorId };
                    roundDamageByActor.Add(actorId, row);
                }
                row.AppliedHpDamage = SaturatingDamageAdd(
                    row.AppliedHpDamage, actualDamage);
                if (row.DamageEvents < int.MaxValue) row.DamageEvents++;
            }
        }

        private static long SaturatingDamageAdd(long existing, long damage) =>
            damage > long.MaxValue - existing ? long.MaxValue : existing + damage;

        private void ResetRoundDamageObservation(bool includesRoundStart)
        {
            observedRoundWaveHpDamage = 0;
            observedRoundDamageEvents = 0;
            roundDamageByActor.Clear();
            observationIncludesRoundStart = includesRoundStart;
        }

        public SourceModelPracticeBattleState CaptureState()
        {
            if (!Started || combat.PendingSimulationSeconds != 0)
                throw new InvalidOperationException(
                    "시작되지 않았거나 미처리 전투 시간이 있는 라운드는 저장할 수 없습니다.");
            var state = new SourceModelPracticeBattleState {
                LastRound = LastRound,
                CurrentRound = combat.Game.CurrentWave,
                Started = Started, MobileMainLane = mobileMainLaneEnabled,
                RoundStartTime = roundStart,
                RoundLeadInSeconds = roundLeadInSeconds,
                DeathCountActive = deathCount.IsActive,
                DeathCountStartedAt = deathCount.StartedAt,
                RoundStartingDefeated = roundStartingDefeated,
                RoundStartingGold = roundStartingGold,
                RoundStartingBossScore = roundStartingBossScore,
                GoldAwarded = GoldAwarded, BossScoreAwarded = BossScoreAwarded,
                RoundWispsAwarded = RoundWispsAwarded,
                ResultMessage = ResultMessage
            };
            foreach (var pair in bornAt)
                state.BornAt.Add(new SourceModelPracticeEnemyClockState {
                    EnemyId = pair.Key, Time = pair.Value
                });
            foreach (var pair in distanceTravelled)
                state.DistanceTravelled.Add(new SourceModelPracticeEnemyClockState {
                    EnemyId = pair.Key, Time = pair.Value
                });
            state.BornAt.Sort((a, b) =>
                string.CompareOrdinal(a.EnemyId, b.EnemyId));
            state.DistanceTravelled.Sort((a, b) =>
                string.CompareOrdinal(a.EnemyId, b.EnemyId));
            state.RewardedEnemyIds.AddRange(rewarded);
            state.RewardedEnemyIds.Sort(StringComparer.Ordinal);
            foreach (var item in history)
                state.History.Add(new SourceModelPracticeRoundHistoryState {
                    Wave = item.Wave, ElapsedSeconds = item.ElapsedSeconds,
                    Defeated = item.Defeated, AliveEnemies = item.AliveEnemies,
                    GoldAwarded = item.GoldAwarded,
                    LeadInSeconds = item.LeadInSeconds,
                    BossScoreAwarded = item.BossScoreAwarded
                });
            return state;
        }

        // Deterministic and side-effect-free validation for the captured
        // 65R clock/reward ledger. This never awards Wisp, gold or mission
        // progress, and never starts a new wave during restoration.
        public static void ValidateState(SourceModelPracticeBattleState state,
            int expectedLastRound, int expectedWave, double logicalTime)
        {
            if (state == null || state.Version != 1 || !state.Started ||
                state.LastRound != expectedLastRound ||
                state.CurrentRound != expectedWave ||
                state.CurrentRound < 1 || state.CurrentRound > state.LastRound ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                state.RoundStartTime < 0 || double.IsNaN(state.RoundStartTime) ||
                double.IsInfinity(state.RoundStartTime) || state.RoundStartTime > logicalTime ||
                double.IsNaN(state.RoundLeadInSeconds) ||
                double.IsInfinity(state.RoundLeadInSeconds) ||
                state.RoundLeadInSeconds < 0 ||
                (state.DeathCountActive && (double.IsNaN(state.DeathCountStartedAt) ||
                    double.IsInfinity(state.DeathCountStartedAt) ||
                    state.DeathCountStartedAt < 0 ||
                    state.DeathCountStartedAt > logicalTime + 1e-8 ||
                    logicalTime - state.DeathCountStartedAt >=
                        MonsterCountDeathCountdown.DefaultGraceSeconds + 1e-8)) ||
                state.RoundStartingDefeated < 0 || state.RoundStartingGold < 0 ||
                state.RoundStartingBossScore < 0 || state.GoldAwarded < 0 ||
                state.BossScoreAwarded < 0 || state.RoundWispsAwarded < 0 ||
                state.RoundStartingGold > state.GoldAwarded ||
                state.RoundStartingBossScore > state.BossScoreAwarded ||
                state.BornAt == null || state.DistanceTravelled == null ||
                state.RewardedEnemyIds == null || state.History == null ||
                state.History.Count >= state.CurrentRound)
                throw new FormatException("65라운드 진행/보상 저장본이 올바르지 않습니다.");
            var born = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clock in state.BornAt)
                if (clock == null || string.IsNullOrWhiteSpace(clock.EnemyId) ||
                    double.IsNaN(clock.Time) || double.IsInfinity(clock.Time) ||
                    clock.Time < 0 || !born.Add(clock.EnemyId))
                    throw new FormatException("몬스터 원본 생성 시각이 중복되거나 손상됐습니다.");
            var distances = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clock in state.DistanceTravelled)
                if (clock == null || string.IsNullOrWhiteSpace(clock.EnemyId) ||
                    double.IsNaN(clock.Time) || double.IsInfinity(clock.Time) ||
                    clock.Time < 0 || !born.Contains(clock.EnemyId) ||
                    !distances.Add(clock.EnemyId))
                    throw new FormatException("적 이동 누적 거리/신원이 손상됐습니다.");
            var rewards = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in state.RewardedEnemyIds)
                if (string.IsNullOrWhiteSpace(id) || !born.Contains(id) || !rewards.Add(id))
                    throw new FormatException("몬스터 처치 보상 중복/신원이 손상됐습니다.");
            int previous = 0;
            foreach (var item in state.History)
                if (item == null || item.Wave != ++previous ||
                    item.ElapsedSeconds < 0 || double.IsNaN(item.ElapsedSeconds) ||
                    double.IsInfinity(item.ElapsedSeconds) ||
                    item.LeadInSeconds < 0 || double.IsNaN(item.LeadInSeconds) ||
                    double.IsInfinity(item.LeadInSeconds) ||
                    item.Defeated < 0 || item.AliveEnemies < 0 ||
                    item.GoldAwarded < 0 || item.BossScoreAwarded < 0)
                    throw new FormatException("라운드 결과 이력이 손상됐습니다.");
        }

        // Restores only into a separately reconstructed combat + wave world.
        // It cannot repair missing enemy identities, hero effects, story lanes
        // or RNG: the full S03 owner must finish those BEFORE calling this.
        public void RestoreState(SourceModelPracticeBattleState state)
        {
            ValidateState(state, LastRound, combat.Game.CurrentWave, combat.Time);
            if (Started || !economy.HasStarted ||
                combat.Game.Phase != SessionPhase.Running ||
                !combat.UsesSourceModelBasicAttacks ||
                waves.CaptureState().ActiveWave != state.CurrentRound ||
                state.MobileMainLane != mobileMainLaneEnabled)
                throw new InvalidOperationException("복원된 65R 전투/웨이브 월드를 먼저 연결해야 합니다.");
            roundStart = state.RoundStartTime;
            roundLeadInSeconds = state.RoundLeadInSeconds;
            deathCount.Restore(state.DeathCountActive, state.DeathCountStartedAt,
                combat.Game.AliveEnemies, combat.Time);
            roundStartingDefeated = state.RoundStartingDefeated;
            roundStartingGold = state.RoundStartingGold;
            roundStartingBossScore = state.RoundStartingBossScore;
            GoldAwarded = state.GoldAwarded;
            BossScoreAwarded = state.BossScoreAwarded;
            RoundWispsAwarded = state.RoundWispsAwarded;
            ResultMessage = state.ResultMessage;
            foreach (var item in state.BornAt) bornAt.Add(item.EnemyId, item.Time);
            foreach (var item in state.DistanceTravelled)
                distanceTravelled.Add(item.EnemyId, item.Time);
            foreach (var id in state.RewardedEnemyIds) rewarded.Add(id);
            foreach (var item in state.History)
                history.Add(new SourceModelPracticeRoundResult {
                    Wave = item.Wave, ElapsedSeconds = item.ElapsedSeconds,
                    Defeated = item.Defeated, AliveEnemies = item.AliveEnemies,
                    GoldAwarded = item.GoldAwarded,
                    LeadInSeconds = item.LeadInSeconds,
                    BossScoreAwarded = item.BossScoreAwarded
                });
            waves.VerifiedMonsterDefeated += RewardActualDeath;
            combat.SourceModelTimeAdvancing += MoveLivingMonsters;
            combat.ActualEnemyDamage += ObserveActualWaveDamage;
            // Previous HP damage is not reconstructed from restored enemies.
            ResetRoundDamageObservation(false);
            Started = true;
        }

        public bool IsFullGameClear => Started && LastRound == 65 &&
            combat.Game.Phase == SessionPhase.Won &&
            combat.Game.CurrentWave == 65 && combat.Game.AliveEnemies == 0 &&
            waves.RemainingSpawns == 0 &&
            waves.CurrentProgress.BossPlanned > 0 &&
            waves.CurrentProgress.AllBossesDefeated;

        public SourceModelPracticeBattle(CharacterCombatRuntime world, CombatWaveDirector director,
            SharedEconomySession shared, SourceModelPracticeCatalog model,
            Original323MonsterWaveIdentityCatalog originalSpawns,
            SourceModelBasicCombatRules rules, IOriginalWispRandom sharedScriptRandom,
            Action onRoundStarted, bool mobileMainLane = false)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            waves = director ?? throw new ArgumentNullException(nameof(director));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            catalog = model ?? throw new ArgumentNullException(nameof(model));
            damage = rules ?? throw new ArgumentNullException(nameof(rules));
            scriptRandom = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            roundStarted = onRoundStarted;
            // Only the playable 65-round phone session explicitly opts into
            // constrained coordinates and finite ranges. Legacy source-model
            // combat fixtures remain independent of this user-facing layout.
            if (mobileMainLane && catalog.LastRound != 65)
                throw new ArgumentException(
                    "모바일 전장 배치는 독립 65라운드 모드에서만 사용합니다.",
                    nameof(mobileMainLane));
            mobileMainLaneEnabled = mobileMainLane;
            deathCount = new MonsterCountDeathCountdown(catalog.EnemyLimit);
            if (!ReferenceEquals(world.Roster, shared.Units)) throw new ArgumentException("공유 세션 불일치");
            // Validate every selected source identity before mutating the session.
            for (int wave = 1; wave <= catalog.LastRound; wave++)
                plans.Add(catalog.BuildPracticePlan(wave, originalSpawns,
                    mobileMainLaneEnabled));
            // Route corners are a validated part of the catalog, but may only
            // be installed after the host enables the source-model runtime.
            // Preparation constructs this object before that transition.
        }
        public void Start()
        {
            if (Started || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running ||
                combat.Game.CurrentWave != 0 || !combat.UsesSourceModelBasicAttacks)
                throw new InvalidOperationException("유닛 준비 상태에서 전투를 시작하세요.");
            combat.ConfigureSourceModelRouteCorners(catalog.RouteCorners);
            if (mobileMainLaneEnabled)
                combat.ConfigureSourceModelMobileMainLane(catalog.MainLane);
            waves.VerifiedMonsterDefeated += RewardActualDeath;
            combat.SourceModelTimeAdvancing += MoveLivingMonsters;
            combat.ActualEnemyDamage += ObserveActualWaveDamage;
            Started = true;
            BeginRound(1);
        }
        private void BeginRound(int wave)
        {
            if (Started && combat.Game.CurrentWave > 0) RecordRound();
            ResetRoundDamageObservation(true);
            roundStart = combat.Time;
            roundStartingDefeated = combat.Game.DefeatedEnemies;
            roundStartingGold = GoldAwarded;
            roundStartingBossScore = BossScoreAwarded;
            roundLeadInSeconds = catalog.ModelLeadInSeconds(wave);
            var plan = plans[wave - 1];
            var delayed = roundLeadInSeconds > 0 ? new List<VerifiedWaveEnemy>() : null;
            foreach (var spawn in plan.Spawns)
            {
                bornAt.Add(spawn.EnemyId, roundStart + roundLeadInSeconds + spawn.AtSeconds);
                damage.UnitEffects?.RegisterPracticeTarget(spawn.EnemyId);
                combat.RegisterSourceModelWaveA04R(spawn.EnemyId, spawn.OriginalRawcode);
                if (delayed != null)
                    delayed.Add(new VerifiedWaveEnemy(
                        spawn.AtSeconds + roundLeadInSeconds, spawn.EnemyId, spawn.Health,
                        spawn.Kind, spawn.X, spawn.Y, spawn.PathProgress, spawn.Defenses,
                        spawn.OriginalRawcode, spawn.NativePassiveTargetProfile));
            }
            waves.Begin(combat, delayed == null ? plan :
                new VerifiedWavePlan(wave, delayed,
                    plan.Evidence + "; MODEL_ONLY: original first/transition lead-in"));
            if (wave > 1)
            {
                economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, catalog.RoundWisps));
                RoundWispsAwarded += catalog.RoundWisps;
            }
            combat.OnSourceModelPracticeRoundStarted(wave);
            roundStarted?.Invoke();
        }
        public IReadOnlyList<CharacterCombatEvent> Advance(double deltaSeconds)
        {
            if (!Started || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            var events = new List<CharacterCombatEvent>();
            double left = deltaSeconds;
            while (left > 0 && combat.Game.Phase == SessionPhase.Running)
            {
                if (TryEndOrAdvance()) break;
                // Use one logical clock for waves, attacks and the death timer.
                // Pausing/backgrounding and game-speed multipliers remain
                // controlled by the owner of Advance(), not wall time.
                if (deathCount.Observe(combat.Game.AliveEnemies, combat.Time))
                {
                    Finish(false, "몬스터가 " + catalog.EnemyLimit +
                        "마리 이상인 상태로 20초가 지나 패배했습니다.");
                    break;
                }
                double step = Math.Min(left,
                    SecondsRemaining > 0 ? Math.Min(0.1, SecondsRemaining) : 0.1);
                if (deathCount.IsActive && deathCount.RemainingSeconds(combat.Time) > 1e-8)
                    step = Math.Min(step, deathCount.RemainingSeconds(combat.Time));
                if (step <= 0) throw new InvalidOperationException("연습 라운드 시간이 진행되지 않습니다.");
                events.AddRange(waves.Step(combat, step));
                left = Math.Max(0, left - step);
                if (deathCount.Observe(combat.Game.AliveEnemies, combat.Time))
                {
                    Finish(false, "몬스터가 " + catalog.EnemyLimit +
                        "마리 이상인 상태로 20초가 지나 패배했습니다.");
                    break;
                }
                if (TryEndOrAdvance()) break;
            }
            return events.AsReadOnly();
        }
        // Returns true only if the practice finished, false after a transition.
        private bool TryEndOrAdvance()
        {
            if (combat.Game.Phase != SessionPhase.Running) return true;
            int wave = combat.Game.CurrentWave;
            if (wave == catalog.LastRound && waves.RemainingSpawns == 0 && combat.Game.AliveEnemies == 0)
            {
                if (waves.CurrentProgress.BossPlanned > 0 &&
                    !waves.CurrentProgress.AllBossesDefeated)
                    Finish(false, "마지막 보스를 처치하지 못했습니다.");
                else
                    Finish(true, catalog.LastRound == 65 ? "65라운드 클리어" :
                        catalog.LastRound + "라운드 연습 완료");
                return true;
            }
            if (SecondsRemaining > 0 || waves.RemainingSpawns != 0) return false;
            var progress = waves.CurrentProgress;
            if (progress.BossPlanned > 0 && !progress.AllBossesDefeated)
            {
                Finish(false, "보스 제한시간 안에 처치하지 못했습니다.");
                return true;
            }
            if (wave >= catalog.LastRound) return false; // Boss cleared: finish the remaining line enemies.
            BeginRound(wave + 1);
            return false;
        }
        private void MoveLivingMonsters(double elapsed)
        {
            foreach (var enemy in combat.MobileEnemySnapshot())
            {
                // Only waves have bornAt and route velocity. A story BUILDING,
                // creep or challenge is not a 65R moving spawn.
                if (enemy.Kind != CharacterEnemyKind.Line &&
                    enemy.Kind != CharacterEnemyKind.Boss &&
                    enemy.Kind != CharacterEnemyKind.Enraged) continue;
                int wave;
                double birth;
                bool inWave = waves.TryGetLivingMonsterSpawnWave(enemy.Id, out wave);
                bool hasBirth = bornAt.TryGetValue(enemy.Id, out birth);
                // External test/skill-spawned line enemies have no wave plan
                // and must not be put on the 65R path or claim its rewards.
                // A half-registered real wave spawn remains a hard failure.
                if (!inWave && !hasBirth) continue;
                if (!inWave || !hasBirth)
                    throw new InvalidOperationException("연습 몬스터 출현 이력이 없습니다.");
                if (combat.IsSourceModelPullHeld(enemy.Id)) continue;
                var status = combat.StatusOf(enemy.Id);
                double active = Math.Max(0, elapsed - Math.Min(elapsed, status.RemainingStunSeconds));
                double slow = SourceModelUnitEffects.ResolvePracticeMovementSlow(status.Snapshot());
                double speed = catalog.GetRound(wave, enemy.Kind).MovementSpeed;
                // Explicit reference movement model. Native acceleration and
                // collision/Arsk remain outside the retail approval boundary.
                speed = Math.Max(Original323AuraCommonRules.MapMinimumUnitSpeed,
                    Math.Min(Original323AuraCommonRules.MapMaximumUnitSpeed, speed * (1 - slow / 100.0)));
                double travelled;
                distanceTravelled.TryGetValue(enemy.Id, out travelled);
                travelled += active * speed;
                distanceTravelled[enemy.Id] = travelled;
                bool boss = mobileMainLaneEnabled && enemy.Kind == CharacterEnemyKind.Boss;
                var point = boss ? catalog.BossPosition(travelled) : catalog.Position(travelled);
                // A completed lap or boss turnaround never changes identity,
                // HP, death credit, the round deadline or spawn ownership.
                combat.MoveSourceModelEnemy(enemy.Id, point.X, point.Y,
                    boss ? catalog.BossProgress(travelled) : catalog.PathProgress(travelled));
            }
        }
        private void RewardActualDeath(int spawnWave, string id)
        {
            if (!rewarded.Add(id)) return;
            string defeatedBossRawcode;
            int bossPoints;
            if (waves.TryGetDefeatedBossRawcode(spawnWave, id, out defeatedBossRawcode))
                MoneyGamblingBossDefeated?.Invoke(id, defeatedBossRawcode);
            // Bdf credits points only for actual 55/60/65 boss deaths. The
            // simulated round clock is explicit model time, not a live witness.
            if (spawnWave == combat.Game.CurrentWave &&
                waves.TryGetDefeatedBossRawcode(spawnWave, id, out defeatedBossRawcode) &&
                Original323BossRoundRules.TryGetLateBossScore(
                    spawnWave, combat.Time - roundStart, out bossPoints))
                BossScoreAwarded = checked(BossScoreAwarded + bossPoints);
            int roll = scriptRandom.NextInclusive(1, 4), amount;
            if (!Original323MonsterGoldRules.TryEvaluateObservedRoll(
                combat.Game.CurrentWave, roll, 0, out amount))
                throw new InvalidOperationException("연습 골드 난수 범위가 올바르지 않습니다.");
            var reward = new ResourceBundle();
            if (amount > 0) reward.Add("gold", amount);
            if (economy.Wallet.TryGrantOnce("practice-death:" + spawnWave + ":" + id, reward))
                GoldAwarded = checked(GoldAwarded + amount);
            damage.ForgetEnemy(id);
            distanceTravelled.Remove(id);
            damage.UnitEffects?.ForgetPracticeTarget(id);
        }
        private void RecordRound()
        {
            int wave = combat.Game.CurrentWave;
            if (wave <= 0 || (history.Count > 0 && history[history.Count - 1].Wave == wave)) return;
            history.Add(new SourceModelPracticeRoundResult { Wave = wave,
                ElapsedSeconds = combat.Time - roundStart,
                LeadInSeconds = roundLeadInSeconds,
                Defeated = combat.Game.DefeatedEnemies - roundStartingDefeated,
                AliveEnemies = combat.Game.AliveEnemies, GoldAwarded = GoldAwarded - roundStartingGold,
                BossScoreAwarded = BossScoreAwarded - roundStartingBossScore });
        }
        private void Finish(bool won, string message)
        {
            RecordRound();
            ResultMessage = message;
            combat.Game.Finish(won);
        }
        public void Dispose()
        {
            waves.VerifiedMonsterDefeated -= RewardActualDeath;
            combat.SourceModelTimeAdvancing -= MoveLivingMonsters;
            combat.ActualEnemyDamage -= ObserveActualWaveDamage;
            MoneyGamblingBossDefeated = null;
        }
    }
}
