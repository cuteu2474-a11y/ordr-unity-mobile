using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Source-object standalone model, never an IOriginal323VerifiedUpgradeWorld.
    // Native cancellation/completion parity still requires an original runtime.
    public sealed class SourceModelPlayerUpgrades : IOriginal323SourceUpgradeModel
    {
        private sealed class Rule
        {
            public string Id;
            public int Maximum, GoldBase, GoldMod, RequiresBossFrom;
            public double AttackBase, AttackMod, SpeedBase, SpeedMod;
            public double Attack(int level) => level == 0 ? 0 : AttackBase + (level - 1) * AttackMod;
            public double Speed(int level) => level == 0 ? 0 : SpeedBase + (level - 1) * SpeedMod;
        }
        private sealed class Link { public string Id, From; }
        private readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> levels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> initial = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Link>> links = new Dictionary<string, List<Link>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> units = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> shop = new HashSet<string>(StringComparer.Ordinal);
        private bool bossDefeated;

        public SourceModelPlayerUpgrades(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "player-upgrade-runtime");
            if (OrdrJson.RequiredString(root, "format") != "ordr-source-player-upgrades-v1" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") != OriginalCommonSaleRules.OriginalMapSha256 ||
                OrdrJson.RequiredString(root, "sourceJassSha256") !=
                    "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d" ||
                !(OrdrJson.Value(root, "runtimeParityVerified") is bool) ||
                (bool)OrdrJson.Value(root, "runtimeParityVerified"))
                throw new FormatException("강화 모델 원본 버전/검증 구분 불일치");
            foreach (var value in OrdrJson.Array(OrdrJson.Value(root, "upgrades"), "upgrades"))
            {
                var row = OrdrJson.Object(value, "upgrade");
                var r = new Rule { Id = OrdrJson.RequiredString(row, "id"),
                    Maximum = Integer(row, "maxLevel"), GoldBase = Integer(row, "goldBase"),
                    GoldMod = Integer(row, "goldMod"), RequiresBossFrom = Integer(row, "requiresBossFromLevel"),
                    AttackBase = Number(row, "attackBase"), AttackMod = Number(row, "attackMod"),
                    SpeedBase = Number(row, "speedBase"), SpeedMod = Number(row, "speedMod") };
                if (r.Id.Length != 4 || r.Maximum < 1 || rules.ContainsKey(r.Id) ||
                    (r.RequiresBossFrom != 0 && r.RequiresBossFrom != 12))
                    throw new FormatException("강화 객체 중복/상한 오류");
                rules.Add(r.Id, r); levels.Add(r.Id, 0); initial.Add(r.Id, 0);
            }
            foreach (var pair in OrdrJson.Object(OrdrJson.Value(root, "initialLevels"), "initialLevels"))
            {
                var r = Require(pair.Key);
                if (!(pair.Value is double) || (double)pair.Value != 1 || r.Maximum < 1)
                    throw new FormatException("CUo 초기 강화 단계 오류");
                levels[pair.Key] = initial[pair.Key] = 1;
            }
            foreach (var value in OrdrJson.Array(OrdrJson.Value(root, "shopIds"), "shopIds"))
            {
                var id = value as string;
                Require(id);
                if (!shop.Add(id)) throw new FormatException("강화소 중복 항목");
            }
            int expected = 0;
            foreach (var building in new[] { Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding,
                Original323PlayerBuildingResearchCatalog.SecondUpgradeBuilding })
                foreach (var item in Original323PlayerBuildingResearchCatalog.ForBuilding(building))
                {
                    expected++;
                    if (!shop.Contains(item.Rawcode) || Require(item.Rawcode).Maximum != item.MaximumLevel)
                        throw new FormatException("원본 강화소 목록/상한 불일치");
                }
            if (shop.Count != expected) throw new FormatException("강화소 항목 수 불일치");
            foreach (var pair in OrdrJson.Object(OrdrJson.Value(root, "linkedTechUpdates"), "links"))
            {
                Require(pair.Key);
                var list = new List<Link>();
                foreach (var value in OrdrJson.Array(pair.Value, "links"))
                {
                    var row = OrdrJson.Object(value, "link");
                    var link = new Link { Id = OrdrJson.RequiredString(row, "id"), From = OrdrJson.RequiredString(row, "from") };
                    Require(link.Id); Require(link.From); list.Add(link);
                }
                links.Add(pair.Key, list);
            }
            foreach (var pair in OrdrJson.Object(OrdrJson.Value(root, "unitUpgrades"), "unitUpgrades"))
            {
                if (pair.Key.Length != 4) throw new FormatException("강화 대상 rawcode 오류");
                var list = new List<string>();
                foreach (var value in OrdrJson.Array(pair.Value, "unit upgrades"))
                {
                    var id = value as string; Require(id);
                    if (list.Contains(id)) throw new FormatException("중복 유닛 강화");
                    list.Add(id);
                }
                units.Add(pair.Key, list);
            }
        }

        private static double Number(Dictionary<string, object> row, string key)
        {
            var n = OrdrJson.Number(row, key);
            if (!n.HasValue || double.IsNaN(n.Value) || double.IsInfinity(n.Value) || n.Value < 0)
                throw new FormatException("강화 숫자 오류: " + key);
            return n.Value;
        }
        private static int Integer(Dictionary<string, object> row, string key)
        {
            double n = Number(row, key);
            if (n > int.MaxValue || Math.Floor(n) != n) throw new FormatException("강화 정수 오류");
            return (int)n;
        }
        private Rule Require(string id)
        {
            Rule rule;
            if (id == null || !rules.TryGetValue(id, out rule)) throw new FormatException("미등록 강화: " + id);
            return rule;
        }
        public int LevelOf(string rawcode) => rawcode != null && levels.TryGetValue(rawcode, out var level) ? level : 0;
        public void ObserveOriginalBossDefeated(string rawcode)
        {
            // CIN: level-20 Crocodile, not a creep stage or a wave-start event.
            if (rawcode == "o01W") bossDefeated = true;
        }
        public bool IsUnlocked(string rawcode, int nextLevel)
        {
            var r = Require(rawcode);
            return r.RequiresBossFrom == 0 || nextLevel < r.RequiresBossFrom || bossDefeated;
        }
        public Original323SourceUpgradeStep NextStep(string rawcode)
        {
            if (!shop.Contains(rawcode)) return null;
            var r = Require(rawcode);
            int current = LevelOf(rawcode), next = current + 1;
            if (current >= r.Maximum) return null;
            string effect = "공격력 +" + (r.Attack(next) - r.Attack(current)).ToString("0.##", CultureInfo.InvariantCulture);
            double speed = (r.Speed(next) - r.Speed(current)) * 100;
            if (speed > 0) effect += " · 공속 +" + speed.ToString("0.##", CultureInfo.InvariantCulture) + "%p";
            if (links.ContainsKey(rawcode)) effect += " · 원본 연계 유닛 강화 포함";
            return new Original323SourceUpgradeStep(rawcode, next,
                checked(r.GoldBase + (long)current * r.GoldMod), effect);
        }
        public bool TryApply(Original323SourceUpgradeStep step, int previousLevel)
        {
            if (step == null || !shop.Contains(step.Rawcode) || LevelOf(step.Rawcode) != previousLevel ||
                step.Level != previousLevel + 1 || step.Level > Require(step.Rawcode).Maximum ||
                !IsUnlocked(step.Rawcode, step.Level)) return false;
            var next = new Dictionary<string, int>(levels, StringComparer.Ordinal);
            next[step.Rawcode] = step.Level;
            if (links.TryGetValue(step.Rawcode, out var linked))
                foreach (var link in linked)
                    // CV8 order matters: R02W reads the already incremented R005.
                    next[link.Id] = Math.Min(Require(link.Id).Maximum, next[link.From] + 1);
            foreach (var pair in next) levels[pair.Key] = pair.Value;
            return true;
        }
        public double AttackBonus(string rawcode) => Bonus(rawcode, false);
        public double AttackSpeedPercent(string rawcode) => Bonus(rawcode, true) * 100;
        private double Bonus(string rawcode, bool speed)
        {
            if (rawcode == null || !units.TryGetValue(rawcode, out var applied)) return 0;
            double total = 0;
            foreach (string id in applied)
                total += speed ? Require(id).Speed(LevelOf(id)) : Require(id).Attack(LevelOf(id));
            return total;
        }
        public Original323SourceUpgradeState CaptureState() => new Original323SourceUpgradeState {
            Levels = new Dictionary<string, int>(levels, StringComparer.Ordinal), Level20BossDefeated = bossDefeated };
        public void RestoreState(Original323SourceUpgradeState state)
        {
            if (state == null || state.Version != 1 || state.Levels == null || state.Levels.Count != rules.Count)
                throw new ArgumentException("강화 저장 상태가 올바르지 않습니다.");
            foreach (var pair in state.Levels)
            {
                var r = Require(pair.Key);
                if (pair.Value < initial[pair.Key] || pair.Value > r.Maximum)
                    throw new ArgumentException("강화 저장 단계가 범위를 벗어났습니다.");
            }
            foreach (var pair in state.Levels) levels[pair.Key] = pair.Value;
            bossDefeated = state.Level20BossDefeated;
        }
    }
}
