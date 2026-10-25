using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Data
{
    // Original UnitAbilities.slk object membership only. Presence of an ability
    // (including Aeth, Amim, A08Q or Avul) is NOT evidence of its current
    // live effect, target immunity or JASS-modified ability level.
    public sealed class Original323MonsterObjectAbilityCatalog
    {
        private sealed class ObjectAbilities
        {
            public string WaveRole;
            public readonly HashSet<string> Rawcodes =
                new HashSet<string>(StringComparer.Ordinal);
        }

        public const string OriginalUnitAbilitiesSha256 =
            "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d";
        public const string OriginalAbilityDataSha256 =
            "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda";
        public const string OriginalAbilityNamesSha256 =
            "1e6d9f5ccd22f67ad0edf5264bce77aa6b944032881b81ab7051f6bebf97b979";

        private readonly Dictionary<string, ObjectAbilities> objects =
            new Dictionary<string, ObjectAbilities>(StringComparer.Ordinal);

        public int Count { get { return objects.Count; } }

        private static int RequiredCount(Dictionary<string, object> row, string key)
        {
            double? value = OrdrJson.Number(row, key);
            if (!value.HasValue || double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) || value.Value < 0 ||
                value.Value > int.MaxValue || Math.Floor(value.Value) != value.Value)
                throw new FormatException("원본 몬스터 능력 개수 오류: " + key);
            return (int)value.Value;
        }

        public static Original323MonsterObjectAbilityCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "monster object abilities");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-original-monster-object-abilities-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                OrdrJson.RequiredString(root, "sourceWar3mapJSha256") !=
                    Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256 ||
                OrdrJson.RequiredString(root, "scope") !=
                    "object-starting-ability-identities-and-source-native-codes-not-current-target-immunity-or-live-ability-levels" ||
                !(OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime") is bool) ||
                (bool)OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime") ||
                !(OrdrJson.Value(root, "liveImmunityFlagsVerified") is bool) ||
                (bool)OrdrJson.Value(root, "liveImmunityFlagsVerified"))
                throw new FormatException("원본 객체 능력을 실전 면역 판정으로 오인할 수 없습니다.");

            var expectedFiles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { @"Units\UnitAbilities.slk", OriginalUnitAbilitiesSha256 },
                { @"Units\AbilityData.slk", OriginalAbilityDataSha256 },
                { @"Units\CampaignAbilityStrings.txt", OriginalAbilityNamesSha256 }
            };
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "sourceFiles"),
                "sourceFiles"))
            {
                var row = OrdrJson.Object(item, "sourceFile");
                string path = OrdrJson.RequiredString(row, "path");
                string expected;
                if (!expectedFiles.TryGetValue(path, out expected) ||
                    OrdrJson.RequiredString(row, "sha256") != expected)
                    throw new FormatException("원본 객체 능력 파일 SHA-256이 일치하지 않습니다.");
                expectedFiles.Remove(path);
            }
            if (expectedFiles.Count != 0)
                throw new FormatException("원본 객체 능력 파일이 누락되었습니다.");

            var knownAbilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(
                OrdrJson.Value(root, "abilityDefinitions"), "abilityDefinitions"))
            {
                var definition = OrdrJson.Object(item, "ability definition");
                string rawcode = OrdrJson.RequiredString(definition, "rawcode");
                string baseCode = OrdrJson.RequiredString(definition, "nativeCode");
                if (rawcode.Length != 4 || baseCode.Length != 4 ||
                    !knownAbilities.Add(rawcode) ||
                    string.IsNullOrWhiteSpace(
                        OrdrJson.RequiredString(definition, "originalAbilityName")) ||
                    RequiredCount(definition, "objectDataLevels") < 1)
                    throw new FormatException("원본 몬스터 능력 ID 또는 네이티브 코드가 잘못되었습니다.");
            }

            var summary = OrdrJson.Object(OrdrJson.Value(root, "summary"), "summary");
            if (!(OrdrJson.Value(summary, "initialObjectAbilitiesOnly") is bool) ||
                !(bool)OrdrJson.Value(summary, "initialObjectAbilitiesOnly") ||
                RequiredCount(summary, "candidateObjectCount") != 100 ||
                RequiredCount(summary, "scriptWaveObjectCount") != 68 ||
                RequiredCount(summary, "lineWaveObjectCount") != 60 ||
                RequiredCount(summary, "bossWaveObjectCount") != 8)
                throw new FormatException("원본 몬스터 능력 표본 범위가 일치하지 않습니다.");

            var catalog = new Original323MonsterObjectAbilityCatalog();
            int wave = 0, line = 0, boss = 0;
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                var row = OrdrJson.Object(item, "monster object");
                string rawcode = OrdrJson.RequiredString(row, "rawcode");
                string name = OrdrJson.RequiredString(row, "originalMapName");
                string category = OrdrJson.RequiredString(row, "nameBasedCategory");
                string role = OrdrJson.String(row, "scriptWaveRole");
                if (rawcode.Length != 4 || name.Length == 0 ||
                    category.Length == 0 || catalog.objects.ContainsKey(rawcode) ||
                    (role != null && role != "line" && role != "boss"))
                    throw new FormatException("원본 몬스터 객체 식별값/역할이 올바르지 않습니다.");

                var objectAbilities = new ObjectAbilities { WaveRole = role };
                foreach (object entry in OrdrJson.Array(
                    OrdrJson.Value(row, "objectBaseAbilities"), "objectBaseAbilities"))
                {
                    string ability = entry as string;
                    if (string.IsNullOrWhiteSpace(ability) || ability.Length != 4 ||
                        !knownAbilities.Contains(ability) ||
                        !objectAbilities.Rawcodes.Add(ability))
                        throw new FormatException("등록되지 않은 기본 능력 또는 중복 능력: " +
                            rawcode);
                }
                if (objectAbilities.Rawcodes.Count == 0 ||
                    OrdrJson.Array(OrdrJson.Value(row, "heroSkillList"),
                        "heroSkillList").Count != 0)
                    throw new FormatException("원본 객체 능력 목록/영웅 스킬 범위가 다릅니다.");
                catalog.objects.Add(rawcode, objectAbilities);
                if (role != null) wave++;
                if (role == "line") line++;
                if (role == "boss") boss++;
            }

            if (catalog.Count != 100 || wave != 68 || line != 60 || boss != 8)
                throw new FormatException("원본 몬스터 100종/웨이브 68종 자료가 누락되었습니다.");
            return catalog;
        }

        public bool HasObject(string rawcode)
        {
            return rawcode != null && objects.ContainsKey(rawcode);
        }

        public bool HasObjectBaseAbility(string rawcode, string abilityRawcode)
        {
            ObjectAbilities entry;
            return rawcode != null && abilityRawcode != null &&
                objects.TryGetValue(rawcode, out entry) &&
                entry.Rawcodes.Contains(abilityRawcode);
        }

        // Membership-only cross-check. Does not approve the presence or
        // gameplay effects of an ability on a live enemy instance.
        public bool MatchesWaveObjectRoles(VerifiedWavePlan plan)
        {
            if (plan == null || plan.Spawns == null || plan.Spawns.Count == 0)
                return false;
            foreach (var spawn in plan.Spawns)
            {
                if (spawn == null) return false;
                ObjectAbilities entry;
                if (string.IsNullOrWhiteSpace(spawn.OriginalRawcode) ||
                    !objects.TryGetValue(spawn.OriginalRawcode, out entry))
                    return false;
                string role = spawn.Kind == CharacterEnemyKind.Boss ? "boss" :
                    spawn.Kind == CharacterEnemyKind.Line ||
                    spawn.Kind == CharacterEnemyKind.Enraged ? "line" : null;
                if (entry.WaveRole != role || role == null) return false;
            }
            return true;
        }
    }
}
