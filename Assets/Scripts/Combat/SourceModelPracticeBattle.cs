using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
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
        public int LastRound { get; private set; }
        public int EnemyLimit => 70;
        public int RoundWisps => 2;
        public CombatPathPoint Spawn => route.AtDistance(0);
        private static string RoundKey(int number, string role) => number + ":" + role;
        public Round GetRound(int number, CharacterEnemyKind kind)
        {
            return rounds[RoundKey(number, kind == CharacterEnemyKind.Boss ? "boss" : "line")];
        }
        public CombatPathPoint Position(double travelled) => route.AtDistance(travelled % route.Length);

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
                throw new FormatException("별도 연습 전투 모델의 출처·미검증 표시가 필요합니다.");
            var points = new List<CombatPathPoint>();
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "routeCenters"), "route"))
            {
                var row = OrdrJson.Object(raw, "point");
                points.Add(new CombatPathPoint(Number(row, "x"), Number(row, "y")));
            }
            if (points.Count != 4) throw new FormatException("원본 라인 경로 중심 4개가 필요합니다.");
            RouteCorners = new List<CombatPathPoint>(points).AsReadOnly();
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
        private static double Number(Dictionary<string, object> row, string key)
        {
            double value;
            if (!OrdrJson.TryNumber(row, key, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("연습 자료 숫자 누락: " + key);
            return value;
        }
        public VerifiedWavePlan BuildPracticePlan(int number, Original323MonsterWaveIdentityCatalog original)
        {
            if (number < 1 || number > LastRound) throw new ArgumentOutOfRangeException(nameof(number));
            var spawns = new List<VerifiedWaveEnemy>();
            var point = Spawn;
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
        public int BossScoreAwarded { get; internal set; }
    }

    // Drives the existing combat runtime/director/wallet. No parallel damage
    // engine, forged native observer, fake live approval or free unit spawns.
    public sealed class SourceModelPracticeBattle : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly CombatWaveDirector waves;
        private readonly SharedEconomySession economy;
        private readonly SourceModelPracticeCatalog catalog;
        private readonly SourceModelBasicCombatRules damage;
        private readonly IOriginalWispRandom scriptRandom;
        private readonly Action roundStarted;
        private readonly List<VerifiedWavePlan> plans = new List<VerifiedWavePlan>();
        private readonly Dictionary<string, double> bornAt = new Dictionary<string, double>();
        private readonly Dictionary<string, double> distanceTravelled = new Dictionary<string, double>();
        private readonly HashSet<string> rewarded = new HashSet<string>();
        private double roundStart;
        private int roundStartingDefeated;
        private long roundStartingGold;
        private int roundStartingBossScore;
        private readonly List<SourceModelPracticeRoundResult> history = new List<SourceModelPracticeRoundResult>();
        public IReadOnlyList<SourceModelPracticeRoundResult> RoundHistory => history.AsReadOnly();
        public bool Started { get; private set; }
        public string ResultMessage { get; private set; }
        public long GoldAwarded { get; private set; }
        // New-world boss points are not a wallet currency or monster-kill gold.
        public int BossScoreAwarded { get; private set; }
        public int RoundWispsAwarded { get; private set; }
        public double SecondsRemaining
        {
            get
            {
                int seconds;
                if (!Started || !Original323BossRoundRules.TryGetCountdownSeconds(
                    combat.Game.CurrentWave, out seconds)) return 0;
                double remaining = seconds - (combat.Time - roundStart);
                return remaining <= 1e-8 ? 0 : remaining;
            }
        }
        public IReadOnlyList<CombatPathPoint> RouteCorners => catalog.RouteCorners;
        public int LastRound => catalog.LastRound;
        public int EnemyLimit => catalog.EnemyLimit;

        public SourceModelPracticeBattle(CharacterCombatRuntime world, CombatWaveDirector director,
            SharedEconomySession shared, SourceModelPracticeCatalog model,
            Original323MonsterWaveIdentityCatalog originalSpawns,
            SourceModelBasicCombatRules rules, IOriginalWispRandom sharedScriptRandom,
            Action onRoundStarted)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            waves = director ?? throw new ArgumentNullException(nameof(director));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            catalog = model ?? throw new ArgumentNullException(nameof(model));
            damage = rules ?? throw new ArgumentNullException(nameof(rules));
            scriptRandom = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            roundStarted = onRoundStarted;
            if (!ReferenceEquals(world.Roster, shared.Units)) throw new ArgumentException("공유 세션 불일치");
            // Validate every selected source identity before mutating the session.
            for (int wave = 1; wave <= catalog.LastRound; wave++)
                plans.Add(catalog.BuildPracticePlan(wave, originalSpawns));
        }
        public void Start()
        {
            if (Started || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running ||
                combat.Game.CurrentWave != 0 || !combat.UsesSourceModelBasicAttacks)
                throw new InvalidOperationException("유닛 준비 상태에서 연습 전투를 시작하세요.");
            waves.VerifiedMonsterDefeated += RewardActualDeath;
            combat.SourceModelTimeAdvancing += MoveLivingMonsters;
            Started = true;
            BeginRound(1);
        }
        private void BeginRound(int wave)
        {
            if (Started && combat.Game.CurrentWave > 0) RecordRound();
            roundStart = combat.Time;
            roundStartingDefeated = combat.Game.DefeatedEnemies;
            roundStartingGold = GoldAwarded;
            roundStartingBossScore = BossScoreAwarded;
            var plan = plans[wave - 1];
            foreach (var spawn in plan.Spawns)
            {
                bornAt.Add(spawn.EnemyId, roundStart + spawn.AtSeconds);
                damage.UnitEffects?.RegisterPracticeTarget(spawn.EnemyId);
            }
            waves.Begin(combat, plan);
            if (wave > 1)
            {
                economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, catalog.RoundWisps));
                RoundWispsAwarded += catalog.RoundWisps;
            }
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
                double step = Math.Min(left, SecondsRemaining > 0 ? Math.Min(0.1, SecondsRemaining) : 0.1);
                if (step <= 0) throw new InvalidOperationException("연습 라운드 시간이 진행되지 않습니다.");
                events.AddRange(waves.Step(combat, step));
                left = Math.Max(0, left - step);
                if (combat.Game.AliveEnemies >= catalog.EnemyLimit)
                {
                    Finish(false, "몬스터가 " + catalog.EnemyLimit + "마리에 도달했습니다.");
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
                Finish(true, catalog.LastRound + "라운드 연습 완료");
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
                int wave;
                double birth;
                if (!waves.TryGetLivingMonsterSpawnWave(enemy.Id, out wave) || !bornAt.TryGetValue(enemy.Id, out birth))
                    throw new InvalidOperationException("연습 몬스터 출현 이력이 없습니다.");
                var status = combat.StatusOf(enemy.Id);
                double active = Math.Max(0, elapsed - Math.Min(elapsed, status.RemainingStunSeconds));
                double slow = SourceModelUnitEffects.ResolveSingleAura(status.Snapshot(), CombatEffectType.MovementSlowPercent);
                double speed = catalog.GetRound(wave, enemy.Kind).MovementSpeed;
                // Explicit reference movement model. Native acceleration and
                // collision/Arsk remain outside the retail approval boundary.
                speed = Math.Max(Original323AuraCommonRules.MapMinimumUnitSpeed,
                    Math.Min(Original323AuraCommonRules.MapMaximumUnitSpeed, speed * (1 - slow / 100.0)));
                double travelled;
                distanceTravelled.TryGetValue(enemy.Id, out travelled);
                travelled += active * speed;
                distanceTravelled[enemy.Id] = travelled;
                var point = catalog.Position(travelled);
                // A completed lap remains a living enemy. No invented leak,
                // kill, reward, or player-life reduction at the route end.
                combat.MoveSourceModelEnemy(enemy.Id, point.X, point.Y);
            }
        }
        private void RewardActualDeath(int spawnWave, string id)
        {
            if (!rewarded.Add(id)) return;
            string defeatedBossRawcode;
            int bossPoints;
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
        }
    }
}
