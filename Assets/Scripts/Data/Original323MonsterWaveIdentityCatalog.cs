using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Data
{
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
                    // CVs/CVu: early boss-only waves start on tick 1.
                    // CVc/CVb: late bosses start after the 35 normal ticks.
                    int bossTick = lateBoss ? 36 : 1;
                    if ((!earlyBossOnly && !lateBoss) ||
                        bosses > source.Count ||
                        (lateBoss && lines != 35) ||
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
}
