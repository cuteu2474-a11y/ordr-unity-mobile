using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Data
{
    // Read-only Warcraft object data. Runtime buffs, upgrades, JASS, target
    // resistance and timing are separate and are NOT verified by these rows.
    public sealed class OriginalMapBasicAttack
    {
        public double? MinimumBaseDamage { get; private set; }
        public double? MaximumBaseDamage { get; private set; }
        public int? BaseDamagePlus { get; private set; }
        public int? DiceCount { get; private set; }
        public int? DiceSides { get; private set; }
        public double? AttackCooldownSeconds { get; private set; }
        // Map-object baseline only; buffs, upgrades and script timing can
        // change the effective attacks per second at runtime.
        public double? BaseAttacksPerSecond
        {
            get { return AttackCooldownSeconds.HasValue
                ? (double?)(1.0 / AttackCooldownSeconds.Value) : null; }
        }
        public double? OriginalRange { get; private set; }
        public double? AttackPointSeconds { get; private set; }
        public string OriginalAttackType { get; private set; }
        public string OriginalWeaponType { get; private set; }

        private static int? IntegerField(Dictionary<string, object> source, string field, string id)
        {
            double? number = OrdrJson.Number(source, field);
            if (!number.HasValue) return null;
            if (number.Value < 0 || number.Value > int.MaxValue ||
                Math.Floor(number.Value) != number.Value)
                throw new FormatException(id + ": 원본 주사위 필드 오류: " + field);
            return (int)number.Value;
        }

        internal OriginalMapBasicAttack(Dictionary<string, object> source, string id)
        {
            MinimumBaseDamage = OrdrJson.Number(source, "minimumBaseDamage");
            MaximumBaseDamage = OrdrJson.Number(source, "maximumBaseDamage");
            BaseDamagePlus = IntegerField(source, "baseDamagePlus", id);
            DiceCount = IntegerField(source, "diceCount", id);
            DiceSides = IntegerField(source, "diceSides", id);
            AttackCooldownSeconds = OrdrJson.Number(source, "attackCooldownSeconds");
            OriginalRange = OrdrJson.Number(source, "originalRange");
            AttackPointSeconds = OrdrJson.Number(source, "attackPointSeconds");
            OriginalAttackType = OrdrJson.String(source, "originalAttackType");
            OriginalWeaponType = OrdrJson.String(source, "originalWeaponType");
            if (MinimumBaseDamage.HasValue != MaximumBaseDamage.HasValue ||
                (MinimumBaseDamage.HasValue &&
                 (MinimumBaseDamage.Value < 0 ||
                  MaximumBaseDamage.Value < MinimumBaseDamage.Value)) ||
                (AttackCooldownSeconds.HasValue && AttackCooldownSeconds.Value <= 0))
                throw new FormatException(id + ": 잘못된 원본 공격력/공격 주기");
            bool anyDiceField = BaseDamagePlus.HasValue || DiceCount.HasValue || DiceSides.HasValue;
            if (anyDiceField &&
                (!BaseDamagePlus.HasValue || !DiceCount.HasValue || !DiceSides.HasValue ||
                 DiceSides.Value < 1 || !MinimumBaseDamage.HasValue ||
                 !MaximumBaseDamage.HasValue ||
                 MinimumBaseDamage.Value != BaseDamagePlus.Value + DiceCount.Value ||
                 MaximumBaseDamage.Value != BaseDamagePlus.Value +
                     (long)DiceCount.Value * DiceSides.Value))
                throw new FormatException(id + ": 원본 공격 주사위와 최소·최대 피해량이 불일치합니다.");
        }
    }

    public sealed class OriginalMapCharacterBasics
    {
        public string UnitId { get; private set; }
        public string OriginalRawcode { get; private set; }
        public string MapName { get; private set; }
        public string MappingStatus { get; private set; }
        public IReadOnlyList<string> AlternateRawcodes { get; private set; }
        public IReadOnlyList<string> SourceJassFusionAbilityRawcodes { get; private set; }
        public OriginalMapBasicAttack Attack1 { get; private set; }
        public OriginalMapBasicAttack Attack2 { get; private set; }
        public double? BaseArmor { get; private set; }
        public double? BaseHitPoints { get; private set; }
        public bool CombatParityVerified { get { return false; } }

        internal OriginalMapCharacterBasics(Dictionary<string, object> source,
            CharacterUnitCatalog catalog)
        {
            UnitId = OrdrJson.RequiredString(source, "catalogId");
            var unit = catalog.Find(UnitId);
            if (unit == null || unit.Name != OrdrJson.RequiredString(source, "catalogName") ||
                unit.Grade != OrdrJson.RequiredString(source, "grade"))
                throw new FormatException(UnitId + ": 원본 맵 링크와 도감 정보가 다릅니다.");
            OriginalRawcode = OrdrJson.String(source, "originalRawcode");
            MapName = OrdrJson.String(source, "sourceMapName");
            MappingStatus = OrdrJson.RequiredString(source, "mappingStatus");
            AlternateRawcodes = new List<string>().AsReadOnly();
            SourceJassFusionAbilityRawcodes = new List<string>().AsReadOnly();
            object parity = OrdrJson.Value(source, "combatParityVerified");
            if (!(parity is bool) || (bool)parity)
                throw new FormatException(UnitId + ": 기본 스탯만으로 전투 완성을 선언할 수 없습니다.");
            if (OriginalRawcode == null)
            {
                if (MapName != null || OrdrJson.Value(source, "attack1") != null ||
                    OrdrJson.Value(source, "baseStats") != null)
                    throw new FormatException(UnitId + ": 미확정 매핑에 능력치가 입력됐습니다.");
                return;
            }
            if (OriginalRawcode.Length != 4 || string.IsNullOrEmpty(MapName) ||
                (MappingStatus != "verified-common-map-identity" &&
                 MappingStatus != "unique-map-name-and-grade" &&
                 MappingStatus != "verified-jass-fusion-result"))
                throw new FormatException(UnitId + ": 원본 rawcode 매핑 근거가 불완전합니다.");
            if (MappingStatus == "verified-jass-fusion-result")
            {
                var abilities = new List<string>();
                foreach (var item in OrdrJson.Array(
                    OrdrJson.Value(source, "sourceJassFusionAbilityRawcodes"),
                    "sourceJassFusionAbilityRawcodes"))
                {
                    var raw = item as string;
                    if (raw == null || raw.Length != 4 || abilities.Contains(raw))
                        throw new FormatException(UnitId + ": JASS 조합 결과 출처 오류");
                    abilities.Add(raw);
                }
                var alternatives = new List<string>();
                foreach (var item in OrdrJson.Array(
                    OrdrJson.Value(source, "alternateRawcodes"), "alternateRawcodes"))
                {
                    var raw = item as string;
                    if (raw == null || raw.Length != 4 || raw == OriginalRawcode ||
                        alternatives.Contains(raw))
                        throw new FormatException(UnitId + ": 원본 대체 유닛 ID 오류");
                    alternatives.Add(raw);
                }
                if (abilities.Count == 0 || alternatives.Count == 0)
                    throw new FormatException(UnitId + ": 중복 유닛 JASS 근거가 없습니다.");
                SourceJassFusionAbilityRawcodes = abilities.AsReadOnly();
                AlternateRawcodes = alternatives.AsReadOnly();
            }
            object primary = OrdrJson.Value(source, "attack1");
            if (primary != null)
                Attack1 = new OriginalMapBasicAttack(OrdrJson.Object(primary, "attack1"), UnitId);
            object secondary = OrdrJson.Value(source, "attack2");
            if (secondary != null)
                Attack2 = new OriginalMapBasicAttack(OrdrJson.Object(secondary, "attack2"), UnitId);
            var balance = OrdrJson.Object(OrdrJson.Value(source, "baseStats"), "baseStats");
            BaseArmor = OrdrJson.Number(balance, "armor");
            BaseHitPoints = OrdrJson.Number(balance, "hitPoints");
        }
    }

    public sealed class OriginalMapBaseStatCatalog
    {
        private readonly Dictionary<string, OriginalMapCharacterBasics> entries =
            new Dictionary<string, OriginalMapCharacterBasics>(StringComparer.Ordinal);

        public int Count { get { return entries.Count; } }
        public int LinkedCount { get; private set; }

        public OriginalMapCharacterBasics Find(string id)
        {
            OriginalMapCharacterBasics result;
            return id != null && entries.TryGetValue(id, out result) ? result : null;
        }

        public static OriginalMapBaseStatCatalog FromJson(string json, CharacterUnitCatalog units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original-map-base-stats");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "format") != "ordr-source-map-base-stats-v1" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256").Length != 64)
                throw new FormatException("원본 맵 능력치 데이터 출처가 다릅니다.");
            var result = new OriginalMapBaseStatCatalog();
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "catalogEntries"), "catalogEntries"))
            {
                var basic = new OriginalMapCharacterBasics(OrdrJson.Object(raw, "map basic"), units);
                if (result.entries.ContainsKey(basic.UnitId))
                    throw new FormatException("원본 맵 기본 능력치 중복: " + basic.UnitId);
                result.entries.Add(basic.UnitId, basic);
                if (basic.OriginalRawcode != null) result.LinkedCount++;
            }
            if (result.Count != units.Count)
                throw new FormatException("맵 스탯 링크와 전체 캐릭터 수가 다릅니다.");
            return result;
        }
    }
}
