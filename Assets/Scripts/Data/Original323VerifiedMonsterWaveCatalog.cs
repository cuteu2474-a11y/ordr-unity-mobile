using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Data
{
    // Production approval is an independent source/target review. JSON evidence
    // strings and a map checksum alone are never a permit to start real combat.
    public interface IOriginal323LiveMonsterWaveEvidence
    {
        bool TryApprove(VerifiedWavePlan plan, out string approvalEvidence);
    }

    // The playable-character UnitBalance.slk rows are NOT monster stats.
    // Only independently reviewed final spawn values may enter this catalog.
    // A matching map checksum does not, by itself, prove a JASS-modified HP
    // or armor value. Explicit per-field evidence remains mandatory.
    public sealed class Original323VerifiedMonsterWaveCatalog
    {
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        private readonly Dictionary<int, VerifiedWavePlan> waves =
            new Dictionary<int, VerifiedWavePlan>();

        public int Count { get { return waves.Count; } }

        public bool TryGetPlan(int waveNumber, out VerifiedWavePlan plan)
        {
            return waves.TryGetValue(waveNumber, out plan);
        }

        // A synthetic test world may use FromJson, but must never enter the
        // shipped MainGame wave path. Even apparently valid strings require an
        // independent runtime proof of final HP, armor, target flags and spawn.
        public static bool TryAuthorizeForLive(VerifiedWavePlan plan,
            IOriginal323LiveMonsterWaveEvidence verifiedSource,
            out string approvalEvidence)
        {
            approvalEvidence = null;
            if (plan == null || verifiedSource == null || plan.WaveNumber < 1 ||
                plan.WaveNumber > 65 || plan.Spawns == null || plan.Spawns.Count == 0 ||
                IsTestEvidence(plan.Evidence))
                return false;

            foreach (var spawn in plan.Spawns)
            {
                if (spawn == null || string.IsNullOrWhiteSpace(spawn.OriginalRawcode) ||
                    spawn.OriginalRawcode.Length != 4 || spawn.Health <= 0 ||
                    spawn.Defenses == null || !spawn.Defenses.HasVerifiedArmor ||
                    IsTestEvidence(spawn.Defenses.Source))
                    return false;
            }

            string proof;
            if (!verifiedSource.TryApprove(plan, out proof) ||
                IsTestEvidence(proof))
                return false;
            approvalEvidence = proof;
            return true;
        }

        private static bool IsTestEvidence(string source)
        {
            return string.IsNullOrWhiteSpace(source) ||
                source.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("MODEL_ONLY", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double RequiredFinite(Dictionary<string, object> data,
            string field, bool nonnegative = false)
        {
            double? value = OrdrJson.Number(data, field);
            if (!value.HasValue || double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) || (nonnegative && value.Value < 0))
                throw new FormatException("몬스터 웨이브 숫자 오류: " + field);
            return value.Value;
        }

        private static int RequiredInteger(Dictionary<string, object> data,
            string field, int minimum)
        {
            double value = RequiredFinite(data, field);
            if (value < minimum || value > int.MaxValue || Math.Floor(value) != value)
                throw new FormatException("몬스터 웨이브 정수 오류: " + field);
            return (int)value;
        }

        private static long RequiredHealth(Dictionary<string, object> data)
        {
            double value = RequiredFinite(data, "health");
            // The JSON parser represents numbers as doubles; larger integers
            // are not guaranteed to survive the parse without changing HP.
            if (value < 1 || value > 9007199254740991d || Math.Floor(value) != value)
                throw new FormatException("원본 몬스터 체력은 정밀도가 보존된 양의 정수여야 합니다.");
            return (long)value;
        }

        private static VerifiedWaveEnemy ReadSpawn(Dictionary<string, object> data,
            string source, int waveNumber)
        {
            string id = OrdrJson.RequiredString(data, "instanceId");
            string rawcode = OrdrJson.RequiredString(data, "originalRawcode");
            if (rawcode.Length != 4)
                throw new FormatException("원본 몬스터 rawcode 길이 오류: " + id);

            CharacterEnemyKind kind;
            if (!Enum.TryParse(OrdrJson.RequiredString(data, "kind"), false, out kind) ||
                !Enum.IsDefined(typeof(CharacterEnemyKind), kind))
                throw new FormatException("원본 몬스터 분류 오류: " + id);

            string healthEvidence = OrdrJson.RequiredString(data, "healthEvidence");
            string armorEvidence = OrdrJson.RequiredString(data, "armorEvidence");
            string spawnEvidence = OrdrJson.RequiredString(data, "spawnEvidence");
            string kindEvidence = OrdrJson.RequiredString(data, "kindEvidence");
            string positionEvidence = OrdrJson.RequiredString(data, "positionEvidence");
            long health = RequiredHealth(data);
            double baseArmor = RequiredFinite(data, "baseArmor");
            string armorType = OrdrJson.RequiredString(data, "warcraftArmorType");
            Ordr323ArmorType parsedArmor;
            if (!Ordr323OriginalDamageRules.TryReadArmorType(armorType, out parsedArmor))
                throw new FormatException("Warcraft 방어 유형 미확인: " + armorType);

            double? magicResistance = OrdrJson.Number(data, "magicResistancePercent");
            string magicEvidence = OrdrJson.String(data, "magicResistanceEvidence");
            int? a04qLevel = null;
            if (OrdrJson.Value(data, "a04qLevel") != null)
                a04qLevel = RequiredInteger(data, "a04qLevel", 1);
            if (magicResistance.HasValue &&
                (double.IsNaN(magicResistance.Value) ||
                 double.IsInfinity(magicResistance.Value) ||
                 magicResistance.Value < 0 || magicResistance.Value > 100 ||
                 string.IsNullOrWhiteSpace(magicEvidence)))
                throw new FormatException("몬스터 마법 저항 증거 오류: " + id);
            if ((!magicResistance.HasValue &&
                 (!string.IsNullOrWhiteSpace(magicEvidence) || a04qLevel.HasValue)))
                throw new FormatException("미확인 마방에 검증된 능력 레벨을 붙일 수 없습니다.");

            var defenses = new EnemyDefenseProfile(kind, baseArmor,
                magicResistance, armorType,
                source + "/wave:" + waveNumber + "/" + rawcode +
                    "/hp:" + healthEvidence + "/armor:" + armorEvidence +
                    "/kind:" + kindEvidence + "/spawn:" + spawnEvidence +
                    "/position:" + positionEvidence +
                    (magicResistance.HasValue ? "/magic:" + magicEvidence : ""),
                a04qLevel);
            return new VerifiedWaveEnemy(
                RequiredFinite(data, "atSeconds", true), id, health, kind,
                RequiredFinite(data, "x"), RequiredFinite(data, "y"),
                RequiredFinite(data, "pathProgress", true), defenses,
                rawcode);
        }

        public static Original323VerifiedMonsterWaveCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "verified monster waves");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-verified-monster-waves-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "mode") != GameSession.ModeId ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") != SourceW3xSha256)
                throw new FormatException("원본 몬스터 자료가 2.323[C] 악몽 이세계모드와 다릅니다.");

            var result = new Original323VerifiedMonsterWaveCatalog();
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "waves"), "waves"))
            {
                var wave = OrdrJson.Object(item, "wave");
                int number = RequiredInteger(wave, "waveNumber", 1);
                string evidence = OrdrJson.RequiredString(wave, "waveEvidence");
                var entries = new List<VerifiedWaveEnemy>();
                foreach (object spawn in OrdrJson.Array(OrdrJson.Value(wave, "spawns"), "spawns"))
                {
                    var row = OrdrJson.Object(spawn, "spawn");
                    // A schedule source and placement evidence must be present;
                    // a bare list of made-up monster HP values is rejected.
                    OrdrJson.RequiredString(row, "spawnEvidence");
                    OrdrJson.RequiredString(row, "positionEvidence");
                    entries.Add(ReadSpawn(row, SourceW3xSha256, number));
                }
                if (entries.Count == 0)
                    throw new FormatException("출현 개체가 없는 라운드는 검증된 웨이브가 아닙니다.");
                if (result.waves.ContainsKey(number))
                    throw new FormatException("원본 몬스터 웨이브 번호 중복: " + number);
                result.waves.Add(number, new VerifiedWavePlan(number, entries,
                    "2.323[C]/" + SourceW3xSha256 + "/" + evidence));
            }
            return result;
        }
    }
}
