using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Data
{
    // A timer entry from the original JASS, not permission to create an
    // enemy with unverified Nightmare / Otherworld effective combat stats.
    public sealed class Original323MonsterSpawnTemplate
    {
        public int WaveNumber { get; private set; }
        public string InstanceId { get; private set; }
        public string Rawcode { get; private set; }
        public CharacterEnemyKind Kind { get; private set; }
        public double AtSeconds { get; private set; }
        public int RoleOrdinal { get; private set; }

        internal Original323MonsterSpawnTemplate(int wave, string originalRawcode,
            CharacterEnemyKind kind, int ordinal, double time)
        {
            if (wave < 1 || wave > 65 || originalRawcode == null ||
                originalRawcode.Length != 4 || ordinal < 1 ||
                double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new ArgumentException("원본 JASS 출현 템플릿 값이 올바르지 않습니다.");
            WaveNumber = wave;
            Rawcode = originalRawcode;
            Kind = kind;
            AtSeconds = time;
            RoleOrdinal = ordinal;
            // Never collide when monsters survive across several waves.
            InstanceId = "W" + wave.ToString("D2") + "-" +
                kind.ToString().ToLowerInvariant() + "-" + ordinal.ToString("D2");
        }
    }

    public interface IOriginal323ConditionalEnragedSpawnWitness
    {
        // Must observe the original special spawn event, not infer it from
        // wave number or from a projected Nightmare difficulty curve.
        bool TryConfirm(int waveNumber, out string originalEventEvidence);
    }

    // 2.323[C] JASS spawn rawcodes/roles only. This is NOT an HP, armor,
    // immunity or spawn-position approval, and cannot enable combat by itself.
    public sealed class Original323MonsterWaveIdentityCatalog
    {
        private sealed class Identity
        {
            public string Rawcode;
            public int Count;
            public double CadenceSeconds;
            public int FirstTick;
        }

        private sealed class Round
        {
            public readonly Dictionary<string, Identity> Roles =
                new Dictionary<string, Identity>(StringComparer.Ordinal);
            public bool HasConditionalEnraged;
            public int EnragedTick;
        }

        public const string OriginalWar3mapJSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";

        private readonly Dictionary<int, Round> rounds = new Dictionary<int, Round>();
        public int Count { get { return rounds.Count; } }

        private static int Integer(Dictionary<string, object> row, string key, int min)
        {
            double? value = OrdrJson.Number(row, key);
            if (!value.HasValue || double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) || value.Value < min ||
                value.Value > int.MaxValue || Math.Floor(value.Value) != value.Value)
                throw new FormatException("원본 웨이브 정수 근거 오류: " + key);
            return (int)value.Value;
        }

        public static Original323MonsterWaveIdentityCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original wave identities");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-original-monster-wave-script-evidence-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                OrdrJson.RequiredString(root, "sourceWar3mapJSha256") !=
                    OriginalWar3mapJSha256 ||
                OrdrJson.RequiredString(root, "scope") !=
                    "original-script-spawn-ids-and-cadence-with-object-base-stats-only" ||
                !(OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime") is bool) ||
                (bool)OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime") ||
                !(OrdrJson.Value(root, "modeAdjustedCombatStatsVerified") is bool) ||
                (bool)OrdrJson.Value(root, "modeAdjustedCombatStatsVerified"))
                throw new FormatException("몬스터 출현 ID 추출본의 원본 범위/검증 수준이 다릅니다.");

            var result = new Original323MonsterWaveIdentityCatalog();
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "waves"), "waves"))
            {
                var row = OrdrJson.Object(item, "wave");
                int wave = Integer(row, "waveNumber", 1);
                if (wave > 65 || result.rounds.ContainsKey(wave))
                    throw new FormatException("원본 출현 웨이브 번호/중복 오류: " + wave);
                if (!(OrdrJson.Value(row, "finalHealthAndArmorVerified") is bool) ||
                    (bool)OrdrJson.Value(row, "finalHealthAndArmorVerified") ||
                    !(OrdrJson.Value(row, "exactSpawnPositionsVerified") is bool) ||
                    (bool)OrdrJson.Value(row, "exactSpawnPositionsVerified"))
                    throw new FormatException("출현 ID 자료에 최종 전투값을 혼합하지 마세요.");

                var rule = new Round();
                foreach (object entry in OrdrJson.Array(
                    OrdrJson.Value(row, "normalAndBossSpawns"), "normalAndBossSpawns"))
                {
                    var spawn = OrdrJson.Object(entry, "source spawn");
                    string role = OrdrJson.RequiredString(spawn, "role");
                    string rawcode = OrdrJson.RequiredString(spawn, "rawcode");
                    string array = OrdrJson.RequiredString(spawn, "sourceArray");
                    string assignment = OrdrJson.RequiredString(spawn, "sourceJassAssignment");
                    if ((role != "line" && role != "boss") || rawcode.Length != 4 ||
                        rule.Roles.ContainsKey(role) ||
                        Integer(spawn, "sourceJassLine", 1) <= 0 ||
                        !assignment.StartsWith("set " + array + "=", StringComparison.Ordinal))
                        throw new FormatException("원본 출현 역할/rawcode/JASS 등록 근거 오류");
                    double? cadence = OrdrJson.Number(spawn, "cadenceSeconds");
                    int firstTick = Integer(spawn, "firstSpawnTick", 1);
                    if (!cadence.HasValue || cadence.Value != 0.5 || firstTick != 1)
                        throw new FormatException(
                            "원본 몬스터 0.5초 생성 간격/첫 타이머 틱 근거가 다릅니다.");
                    rule.Roles.Add(role, new Identity {
                        Rawcode = rawcode,
                        Count = Integer(spawn, "spawnsPerActivePlayer", 1),
                        CadenceSeconds = cadence.Value,
                        FirstTick = firstTick
                    });
                }
                if (rule.Roles.Count == 0)
                    throw new FormatException("원본 출현 객체가 없는 웨이브: " + wave);
                if (OrdrJson.Value(row, "conditionalBerserk") != null)
                {
                    var extra = OrdrJson.Object(OrdrJson.Value(row, "conditionalBerserk"),
                        "conditionalBerserk");
                    if (!(OrdrJson.Value(extra, "sameRawcodeAsLine") is bool) ||
                        !(bool)OrdrJson.Value(extra, "sameRawcodeAsLine") ||
                        !rule.Roles.ContainsKey("line") ||
                        Integer(extra, "spawnAtTick", 1) != 15)
                        throw new FormatException("광폭화 원본 역할/타이밍 근거 오류");
                    rule.HasConditionalEnraged = true;
                    rule.EnragedTick = 15;
                }
                result.rounds.Add(wave, rule);
            }
            if (result.rounds.Count != 65)
                throw new FormatException("원본 출현 ID 자료는 65라운드 전부 필요합니다.");
            return result;
        }

        // Build every wave's ORIGINAL ID + 0.5s spawn order without creating
        // combat HP, defense, immunity, movement, positions or round starts.
        // Conditional Berserk requires a separate source-observed event.
        public IReadOnlyList<Original323MonsterSpawnTemplate>
            BuildSinglePlayerSpawnTemplates(int wave, bool conditionalEnraged = false,
                IOriginal323ConditionalEnragedSpawnWitness enragedWitness = null)
        {
            Round round;
            if (!rounds.TryGetValue(wave, out round))
                throw new ArgumentOutOfRangeException(nameof(wave));
            if (conditionalEnraged)
            {
                string evidence;
                if (!round.HasConditionalEnraged || enragedWitness == null ||
                    !enragedWitness.TryConfirm(wave, out evidence) ||
                    !TrustedJassEvent(evidence))
                    throw new InvalidOperationException(
                        "광폭화 몬스터의 원본 출현 이벤트가 확인되지 않았습니다.");
            }

            Identity normal, boss;
            round.Roles.TryGetValue("line", out normal);
            round.Roles.TryGetValue("boss", out boss);
            if (boss != null && boss.Count != 1)
                throw new InvalidOperationException(
                    "보스 출현 횟수에 원본에서 확인되지 않은 변형이 있습니다.");

            var schedule = new List<Original323MonsterSpawnTemplate>();
            int ticks = Math.Max(normal == null ? 0 : normal.Count,
                boss == null ? 0 : boss.Count);
            for (int tick = 1; tick <= ticks; tick++)
            {
                if (tick == 1 && boss != null)
                    schedule.Add(new Original323MonsterSpawnTemplate(
                        wave, boss.Rawcode, CharacterEnemyKind.Boss, 1,
                        boss.CadenceSeconds * boss.FirstTick));
                if (normal != null && tick <= normal.Count)
                    schedule.Add(new Original323MonsterSpawnTemplate(
                        wave, normal.Rawcode, CharacterEnemyKind.Line, tick,
                        normal.CadenceSeconds * (normal.FirstTick + tick - 1)));
                // The original script adds a separately conditional extra
                // AFTER line tick 15, at that very same timestamp.
                if (conditionalEnraged && tick == round.EnragedTick)
                    schedule.Add(new Original323MonsterSpawnTemplate(
                        wave, normal.Rawcode, CharacterEnemyKind.Enraged, 1,
                        normal.CadenceSeconds * tick));
            }
            return schedule.AsReadOnly();
        }

        private static bool TrustedJassEvent(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence) ||
                evidence.IndexOf(OriginalWar3mapJSha256,
                    StringComparison.OrdinalIgnoreCase) < 0) return false;
            foreach (var term in new[] {
                "TEST_ONLY", "TEST_FIXTURE", "SYNTHETIC", "MOCK_ONLY", "MODEL_ONLY"
            })
                if (evidence.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            return true;
        }

        // Full single-player wave plan: 35 normal spawns and, where present,
        // one boss, plus at most one separately conditional enraged spawn.
        // This checks only original membership/count. It NEVER authorizes
        // final HP, defense, immunity, placement or timing.

        // Checks only timer-relative source cadence: the original 0.5-second
        // repeating spawn timer starts separately from any whole-round clock.
        // This is NOT proof of the round's absolute start time, reward trigger,
        // actual spawn coordinates or final combat stats.
        public bool MatchesSinglePlayerSpawnCadence(VerifiedWavePlan plan)
        {
            Round rule;
            if (plan == null || !rounds.TryGetValue(plan.WaveNumber, out rule) ||
                !MatchesSinglePlayerWave(plan))
                return false;

            bool earlyBossOnly = plan.WaveNumber <= 50 &&
                plan.WaveNumber % 10 == 0;
            bool lateBoss = plan.WaveNumber > 50 &&
                plan.WaveNumber % 5 == 0;
            int lines = 0, bosses = 0, enraged = 0;
            foreach (var spawn in plan.Spawns)
            {
                if (spawn == null) return false;
                if (spawn.Kind == CharacterEnemyKind.Line)
                {
                    Identity source;
                    if (!rule.Roles.TryGetValue("line", out source))
                        return false;
                    lines++;
                    if (lines > source.Count ||
                        !Near(spawn.AtSeconds,
                            source.CadenceSeconds * (source.FirstTick + lines - 1)))
                        return false;
                }
                else if (spawn.Kind == CharacterEnemyKind.Boss)
                {
                    Identity source;
                    if (!rule.Roles.TryGetValue("boss", out source))
                        return false;
                    bosses++;
                    // Both CVs/CVp (early) and CVc/CVb (late) summon
                    // the boss on the FIRST 0.5-second spawn timer tick.
                    // CVc then also summons the first normal monster.
                    const int bossTick = 1;
                    if ((!earlyBossOnly && !lateBoss) ||
                        bosses > source.Count ||
                        !Near(spawn.AtSeconds, source.CadenceSeconds * bossTick))
                        return false;
                }
                else if (spawn.Kind == CharacterEnemyKind.Enraged)
                {
                    enraged++;
                    if (!rule.HasConditionalEnraged || enraged > 1 ||
                        lines != rule.EnragedTick ||
                        !Near(spawn.AtSeconds, 0.5 * rule.EnragedTick))
                        return false;
                }
                else return false;
            }
            return true;
        }

        private static bool Near(double observed, double expected)
        {
            return !double.IsNaN(observed) && !double.IsInfinity(observed) &&
                Math.Abs(observed - expected) <= 1e-9;
        }

        public bool MatchesSinglePlayerWave(VerifiedWavePlan plan)
        {
            Round rule;
            if (plan == null || !rounds.TryGetValue(plan.WaveNumber, out rule))
                return false;
            int line = 0, boss = 0, enraged = 0;
            foreach (var spawn in plan.Spawns)
            {
                if (spawn == null || string.IsNullOrWhiteSpace(spawn.OriginalRawcode))
                    return false;
                Identity expected;
                if (spawn.Kind == CharacterEnemyKind.Line)
                {
                    if (!rule.Roles.TryGetValue("line", out expected) ||
                        expected.Rawcode != spawn.OriginalRawcode) return false;
                    line++;
                }
                else if (spawn.Kind == CharacterEnemyKind.Boss)
                {
                    if (!rule.Roles.TryGetValue("boss", out expected) ||
                        expected.Rawcode != spawn.OriginalRawcode) return false;
                    boss++;
                }
                else if (spawn.Kind == CharacterEnemyKind.Enraged)
                {
                    if (!rule.HasConditionalEnraged ||
                        !rule.Roles.TryGetValue("line", out expected) ||
                        expected.Rawcode != spawn.OriginalRawcode) return false;
                    enraged++;
                }
                else return false;
            }
            Identity lineRule, bossRule;
            int expectedLines = rule.Roles.TryGetValue("line", out lineRule) ?
                lineRule.Count : 0;
            int expectedBosses = rule.Roles.TryGetValue("boss", out bossRule) ?
                bossRule.Count : 0;
            return line == expectedLines && boss == expectedBosses &&
                enraged <= (rule.HasConditionalEnraged ? 1 : 0);
        }
    }

    // A real source adapter must supply EACH spawn's final live attributes
    // (post-original mode changes) and position. Static object rows and the
    // reference engine's non-retail model are NOT permitted as this witness.
    public interface IOriginal323VerifiedFinalMonsterSpawnSource
    {
        bool TryResolve(Original323MonsterSpawnTemplate originalTimer,
            out VerifiedWaveEnemy independentlyObservedSpawn,
            out string originalLiveEvidence);
    }

    // Prepares a complete original round from verified source snapshots.
    // Zero guessed HP, armor, resistances, positions or speed: a missing
    // entry rejects the WHOLE plan. An independent per-wave live auditor is
    // also mandatory, just as for plans loaded from a verified JSON file.
    public sealed class Original323VerifiedMonsterWaveAssembler
    {
        private readonly Original323MonsterWaveIdentityCatalog script;
        private readonly Original323MonsterObjectAbilityCatalog objects;
        private readonly IOriginal323LiveMonsterWaveEvidence approval;

        public Original323VerifiedMonsterWaveAssembler(
            Original323MonsterWaveIdentityCatalog sourceScript,
            Original323MonsterObjectAbilityCatalog originalObjects,
            IOriginal323LiveMonsterWaveEvidence independentLiveApproval)
        {
            script = sourceScript ?? throw new ArgumentNullException(nameof(sourceScript));
            objects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            approval = independentLiveApproval ??
                throw new ArgumentNullException(nameof(independentLiveApproval));
        }

        public VerifiedWavePlan Build(int wave,
            IOriginal323VerifiedFinalMonsterSpawnSource finalStats,
            bool includeObservedEnraged = false,
            IOriginal323ConditionalEnragedSpawnWitness enraged = null)
        {
            if (finalStats == null) throw new ArgumentNullException(nameof(finalStats));
            var slots = script.BuildSinglePlayerSpawnTemplates(wave,
                includeObservedEnraged, enraged);
            var final = new List<VerifiedWaveEnemy>(slots.Count);
            foreach (var slot in slots)
            {
                VerifiedWaveEnemy observed;
                string evidence;
                if (!finalStats.TryResolve(slot, out observed, out evidence) ||
                    observed == null || !Trusted(evidence) ||
                    evidence.IndexOf(slot.InstanceId, StringComparison.Ordinal) < 0 ||
                    observed.EnemyId != slot.InstanceId ||
                    observed.OriginalRawcode != slot.Rawcode ||
                    observed.Kind != slot.Kind ||
                    Math.Abs(observed.AtSeconds - slot.AtSeconds) > 1e-9 ||
                    observed.Health <= 0 ||
                    observed.Defenses == null ||
                    observed.Defenses.Kind != slot.Kind ||
                    !observed.Defenses.HasVerifiedArmor ||
                    !Trusted(observed.Defenses.Source) ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        observed.Defenses.WarcraftArmorType, out _) ||
                    (observed.NativePassiveTargetProfile != null &&
                     !Trusted(observed.NativePassiveTargetProfile.Evidence)))
                    throw new InvalidOperationException(
                        "원본 몬스터 최종 체력/방어/좌표/대상 근거가 없거나 출현과 다릅니다: " +
                        slot.InstanceId);
                final.Add(observed);
            }
            var plan = new VerifiedWavePlan(wave, final,
                "independent original live wave review " +
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 + " / " +
                Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256);
            string proof;
            if (!script.MatchesSinglePlayerSpawnCadence(plan) ||
                !objects.MatchesWaveObjectRoles(plan) ||
                !Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                    plan, approval, out proof))
                throw new InvalidOperationException(
                    "원본 라운드 전체의 실전 출현·능력치 독립 승인이 필요합니다: " + wave);
            return plan;
        }

        // All-or-nothing as a returned batch. The caller atomically commits
        // plans to its session only once every requested wave is approved.
        public IReadOnlyList<VerifiedWavePlan> BuildBatch(
            IEnumerable<int> waveNumbers,
            IOriginal323VerifiedFinalMonsterSpawnSource finalStats)
        {
            if (waveNumbers == null)
                throw new ArgumentNullException(nameof(waveNumbers));
            var seen = new HashSet<int>();
            var plans = new List<VerifiedWavePlan>();
            foreach (int wave in waveNumbers)
            {
                if (!seen.Add(wave))
                    throw new InvalidOperationException("같은 몬스터 라운드 중복 요청");
                plans.Add(Build(wave, finalStats));
            }
            if (plans.Count == 0)
                throw new ArgumentException("검증할 출현 라운드가 없습니다.");
            return plans.AsReadOnly();
        }

        private static bool Trusted(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence) ||
                evidence.IndexOf(Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                    StringComparison.OrdinalIgnoreCase) < 0 ||
                evidence.IndexOf(Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256,
                    StringComparison.OrdinalIgnoreCase) < 0) return false;
            foreach (var term in new[] {
                "TEST_ONLY", "TEST_FIXTURE", "SYNTHETIC", "MOCK_ONLY", "MODEL_ONLY"
            })
                if (evidence.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            return true;
        }
    }
}
