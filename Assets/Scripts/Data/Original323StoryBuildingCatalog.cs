using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Data
{
    // Source object identities for the 14 STORY BUILDINGS, not moving creeps.
    // Base HP/armor are not final Nightmare/Isekai runtime values.
    public sealed class Original323StoryBuilding
    {
        public int Stage { get; internal set; }
        public string Rawcode { get; internal set; }
        public string Name { get; internal set; }
        public long BaseHitPoints { get; internal set; }
        public double BaseArmor { get; internal set; }
        public string ArmorType { get; internal set; }
        public bool FinalModeStatsVerified => false;
        public EnemyDefenseProfile PracticeDefense() =>
            new EnemyDefenseProfile(CharacterEnemyKind.Story, BaseArmor, null,
                ArmorType, "MODEL_ONLY: 2.323[C] story building object base " + Rawcode);
    }

    public sealed class Original323StoryBuildingCatalog
    {
        private readonly Dictionary<int, Original323StoryBuilding> stages =
            new Dictionary<int, Original323StoryBuilding>();
        public int Count => stages.Count;
        public Original323StoryBuilding Find(int stage) =>
            stages.TryGetValue(stage, out var building) ? building : null;

        public static Original323StoryBuildingCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "story building objects");
            if (OrdrJson.RequiredString(root, "format") != "ordr-original-monster-object-stats-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323CreepObjectCatalog.SourceW3xSha256 ||
                OrdrJson.RequiredString(root, "scope") !=
                    "original-object-base-stats-not-mode-adjusted-wave-spawns" ||
                !Equals(OrdrJson.Value(root, "safeForVerifiedMonsterWaveRuntime"), false))
                throw new FormatException("원본 스토리 건물 기본 자료의 출처/검증 상태 오류");

            var catalog = new Original323StoryBuildingCatalog();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal) {
                {"n000",1},{"n002",2},{"n003",3},{"n004",4},
                {"n005",5},{"n006",6},{"n007",7},{"n008",8},
                {"n00A",9},{"n001",10},{"n00C",11},{"n00D",12},
                {"n00B",13},{"n009",14}
            };
            int total = 0;
            foreach (var item in OrdrJson.Array(OrdrJson.Value(root,"entries"),"entries"))
            {
                total++;
                var row = OrdrJson.Object(item, "object");
                if (OrdrJson.RequiredString(row,"nameBasedCategory") != "story-labeled")
                    continue;
                string rawcode = OrdrJson.RequiredString(row,"rawcode");
                if (!ids.TryGetValue(rawcode, out int stage) || catalog.stages.ContainsKey(stage))
                    throw new FormatException("중복 또는 미확인 스토리 건물");
                string name = OrdrJson.RequiredString(row,"originalMapName");
                double? hp = OrdrJson.Number(row,"objectBaseHitPoints");
                double? armor = OrdrJson.Number(row,"objectBaseArmor");
                string type = OrdrJson.RequiredString(row,"warcraftArmorType");
                if (!name.StartsWith(stage + ". ", StringComparison.Ordinal) ||
                    !hp.HasValue || hp.Value < 1 || hp.Value > long.MaxValue ||
                    Math.Floor(hp.Value) != hp.Value ||
                    !armor.HasValue || double.IsNaN(armor.Value) ||
                    double.IsInfinity(armor.Value) || type != "fort" ||
                    !Equals(OrdrJson.Value(row,"runtimeEnemyRoleVerified"),false) ||
                    !Equals(OrdrJson.Value(row,"nightmareOtherworldFinalStatsVerified"),false))
                    throw new FormatException("스토리 건물 원본 객체 정보가 유효하지 않습니다.");
                catalog.stages.Add(stage,new Original323StoryBuilding {
                    Stage=stage, Rawcode=rawcode, Name=name,
                    BaseHitPoints=(long)hp.Value, BaseArmor=armor.Value, ArmorType=type
                });
            }
            if (total != 100 || catalog.Count != 14)
                throw new FormatException("원본 100개 객체 중 스토리 건물 14종 누락");
            return catalog;
        }
    }
}
