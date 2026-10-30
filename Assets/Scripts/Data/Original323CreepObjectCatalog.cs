using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Data
{
    // Extracted 2.323[C] object BASE statistics. These are not the final
    // nightmare/otherworld HP, armor, spawn timing, rewards or live immunities.
    public sealed class Original323CreepObject
    {
        public int Stage { get; private set; }
        public string Rawcode { get; private set; }
        public string OriginalMapName { get; private set; }
        public long ObjectBaseHitPoints { get; private set; }
        public double ObjectBaseArmor { get; private set; }
        public string WarcraftArmorType { get; private set; }
        public bool FinalNightmareStatsVerified { get { return false; } }

        internal Original323CreepObject(int stage, string rawcode, string name,
            long hitPoints, double armor, string armorType)
        {
            Stage = stage; Rawcode = rawcode; OriginalMapName = name;
            ObjectBaseHitPoints = hitPoints; ObjectBaseArmor = armor;
            WarcraftArmorType = armorType;
        }

        // The source object armor is useful to the explicitly labeled practice
        // model, but the live ability/auras must never be inferred here.
        public EnemyDefenseProfile CreateObjectBasePracticeDefenses()
        {
            return new EnemyDefenseProfile(CharacterEnemyKind.Creep,
                ObjectBaseArmor, null, WarcraftArmorType,
                "2.323[C] UnitBalance.slk OBJECT BASE only: " + Rawcode +
                " (nightmare/otherworld final values and Amim state unverified)");
        }
    }

    public sealed class Original323CreepObjectCatalog
    {
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string OriginalUnitBalanceSha256 =
            "f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5";
        public const string OriginalCampaignNamesSha256 =
            "3e16625f147113e5e4c58a4ad1a58a8b259eeccba57e638db6f9e21cd0cf1a10";

        private readonly Dictionary<int, Original323CreepObject> stages =
            new Dictionary<int, Original323CreepObject>();

        public int Count { get { return stages.Count; } }

        public Original323CreepObject Find(int stage)
        {
            Original323CreepObject entry;
            return stages.TryGetValue(stage, out entry) ? entry : null;
        }

        private static long RequiredPositiveWholeNumber(
            Dictionary<string, object> row, string key)
        {
            double? value = OrdrJson.Number(row, key);
            if (!value.HasValue || double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) || value.Value < 1 ||
                value.Value > 9007199254740991d || Math.Floor(value.Value) != value.Value)
                throw new FormatException("크립 원본 체력 오류: " + key);
            return (long)value.Value;
        }

        public static Original323CreepObjectCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "monster object stats");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-original-monster-object-stats-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") != SourceW3xSha256 ||
                OrdrJson.RequiredString(root, "scope") !=
                    "original-object-base-stats-not-mode-adjusted-wave-spawns" ||
                !(OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime") is bool) ||
                (bool)OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime"))
                throw new FormatException("원본 객체 기본 스탯을 실전 최종 수치로 사용할 수 없습니다.");

            var requiredFiles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { @"Units\UnitBalance.slk", OriginalUnitBalanceSha256 },
                { @"Units\CampaignUnitStrings.txt", OriginalCampaignNamesSha256 }
            };
            foreach (object item in OrdrJson.Array(
                OrdrJson.Value(root, "sourceFiles"), "sourceFiles"))
            {
                var file = OrdrJson.Object(item, "source file");
                string name = OrdrJson.RequiredString(file, "path");
                string hash;
                if (!requiredFiles.TryGetValue(name, out hash) ||
                    OrdrJson.RequiredString(file, "sha256") != hash)
                    throw new FormatException("크립 객체 출처 파일이 다릅니다.");
                requiredFiles.Remove(name);
            }
            if (requiredFiles.Count != 0)
                throw new FormatException("크립 객체 출처 파일 누락");

            var catalog = new Original323CreepObjectCatalog();
            var expected = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "n00E", 1 }, { "n00F", 2 }, { "n00G", 3 }
            };
            int total = 0;
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                total++;
                var row = OrdrJson.Object(item, "monster object");
                string category = OrdrJson.RequiredString(row, "nameBasedCategory");
                if (category != "creep-labeled") continue;
                string rawcode = OrdrJson.RequiredString(row, "rawcode");
                int stage;
                if (!expected.TryGetValue(rawcode, out stage) ||
                    catalog.stages.ContainsKey(stage))
                    throw new FormatException("미확인/중복 크립 객체: " + rawcode);
                string name = OrdrJson.RequiredString(row, "originalMapName");
                string expectedName = stage == 1 ? "1단계 크립 듀공" :
                    stage == 2 ? "2단계 크립 파파구" : "3단계 크립 슈슈";
                double? armor = OrdrJson.Number(row, "objectBaseArmor");
                string armorType = OrdrJson.RequiredString(row, "warcraftArmorType");
                if (name != expectedName || !armor.HasValue ||
                    double.IsNaN(armor.Value) || double.IsInfinity(armor.Value) ||
                    armorType != "normal" ||
                    !(OrdrJson.Value(row, "runtimeEnemyRoleVerified") is bool) ||
                    (bool)OrdrJson.Value(row, "runtimeEnemyRoleVerified") ||
                    !(OrdrJson.Value(row, "nightmareOtherworldFinalStatsVerified") is bool) ||
                    (bool)OrdrJson.Value(row, "nightmareOtherworldFinalStatsVerified"))
                    throw new FormatException("크립 원본 객체/최종 검증 상태가 다릅니다.");
                catalog.stages.Add(stage, new Original323CreepObject(stage, rawcode,
                    name, RequiredPositiveWholeNumber(row, "objectBaseHitPoints"),
                    armor.Value, armorType));
            }
            if (total != 100 || catalog.Count != 3)
                throw new FormatException("원본 100객체 중 크립 3종이 온전히 존재해야 합니다.");
            return catalog;
        }
    }
}
