using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Data
{
    public sealed class ImportedCombatEffect
    {
        public string Type { get; private set; }
        public IReadOnlyDictionary<string, object> Fields { get; private set; }

        internal ImportedCombatEffect(Dictionary<string, object> fields)
        {
            Type = OrdrJson.RequiredString(fields, "type");
            Fields = new Dictionary<string, object>(fields, StringComparer.Ordinal);
        }

        public double? Number(string field)
        {
            var dict = Fields as Dictionary<string, object>;
            return OrdrJson.Number(dict, field);
        }

        public string Text(string field)
        {
            var dict = Fields as Dictionary<string, object>;
            return OrdrJson.String(dict, field);
        }

        public object Get(string field)
        {
            object value;
            return Fields.TryGetValue(field, out value) ? value : null;
        }
    }

    public sealed class ImportedCombatSkill
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Trigger { get; private set; }
        public double? ChancePercent { get; private set; }
        public double? CooldownSeconds { get; private set; }
        public double? PeriodSeconds { get; private set; }
        public double? Radius { get; private set; }
        public string Target { get; private set; }
        public IReadOnlyList<ImportedCombatEffect> Components { get; private set; }
        public IReadOnlyDictionary<string, object> OriginalFields { get; private set; }

        internal ImportedCombatSkill(string unitId, int ordinal, Dictionary<string, object> fields)
        {
            Id = unitId + ":skill:" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Name = OrdrJson.RequiredString(fields, "name");
            Trigger = OrdrJson.RequiredString(fields, "trigger");
            ChancePercent = OrdrJson.Number(fields, "chancePercent");
            CooldownSeconds = OrdrJson.Number(fields, "cooldownSeconds");
            PeriodSeconds = OrdrJson.Number(fields, "intervalSeconds") ??
                            OrdrJson.Number(fields, "periodSeconds");
            Radius = OrdrJson.Number(fields, "radius");
            Target = OrdrJson.String(fields, "target");
            if (ChancePercent.HasValue && (ChancePercent.Value < 0 || ChancePercent.Value > 100))
                throw new FormatException(Id + ": 발동 확률이 0~100% 범위를 벗어났습니다.");
            if (CooldownSeconds.HasValue && CooldownSeconds.Value < 0)
                throw new FormatException(Id + ": 음수 쿨타임.");
            if (Radius.HasValue && Radius.Value < 0)
                throw new FormatException(Id + ": 음수 사거리.");
            var components = OrdrJson.Array(OrdrJson.Value(fields, "components"), "components");
            var effects = new List<ImportedCombatEffect>();
            foreach (var component in components)
                effects.Add(new ImportedCombatEffect(OrdrJson.Object(component, "skill effect")));
            Components = effects.AsReadOnly();
            OriginalFields = new Dictionary<string, object>(fields, StringComparer.Ordinal);
        }

        public object Get(string field)
        {
            object value;
            return OriginalFields.TryGetValue(field, out value) ? value : null;
        }

        public double? Number(string field)
        {
            var dict = OriginalFields as Dictionary<string, object>;
            return OrdrJson.Number(dict, field);
        }
    }

    public sealed class CharacterSkillProfile
    {
        public string UnitId { get; private set; }
        public string SourceUrl { get; private set; }
        public string GameRawCode { get; private set; }
        public string Completeness { get; private set; }
        public IReadOnlyList<ImportedCombatSkill> Skills { get; private set; }
        public IReadOnlyList<ImportedCombatEffect> Passives { get; private set; }
        public IReadOnlyDictionary<string, object> BasicAttack { get; private set; }
        public IReadOnlyDictionary<string, object> OriginalFields { get; private set; }

        internal CharacterSkillProfile(Dictionary<string, object> fields)
        {
            UnitId = OrdrJson.RequiredString(fields, "unitId");
            SourceUrl = OrdrJson.RequiredString(fields, "sourceUrl");
            GameRawCode = OrdrJson.String(fields, "gameRawCode");
            Completeness = OrdrJson.RequiredString(fields, "skillSpecCompleteness");
            var rawAttack = OrdrJson.Object(OrdrJson.Value(fields, "basicAttack"), "basicAttack");
            BasicAttack = new Dictionary<string, object>(rawAttack, StringComparer.Ordinal);
            var skills = new List<ImportedCombatSkill>();
            int index = 0;
            foreach (var item in OrdrJson.Array(OrdrJson.Value(fields, "skills"), "skills"))
            {
                skills.Add(new ImportedCombatSkill(UnitId, index, OrdrJson.Object(item, "skill")));
                index++;
            }
            Skills = skills.AsReadOnly();
            var passives = new List<ImportedCombatEffect>();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(fields, "passives"), "passives"))
                passives.Add(new ImportedCombatEffect(OrdrJson.Object(item, "passive")));
            Passives = passives.AsReadOnly();
            OriginalFields = new Dictionary<string, object>(fields, StringComparer.Ordinal);
        }

        public bool HasVerifiedBasicAttack
        {
            get
            {
                // 모바일에서는 평타가 같은 전장 전체를 공격하므로 사거리 값은 선택.
                // 원작 검증 표시와 공격력·공속·피해계열은 여전히 반드시 필요.
                return OrdrJson.String(OriginalFields as Dictionary<string, object>,
                           "basicAttackVerificationStatus") == "original-confirmed" &&
                       ValueIsNumber("nominalAttackPower") &&
                       ValueIsNumber("intervalSeconds") &&
                       HasText("damageSchool");
            }
        }

        private bool ValueIsNumber(string key)
        {
            object value;
            return BasicAttack.TryGetValue(key, out value) && value is double;
        }

        private bool HasText(string key)
        {
            object value;
            return BasicAttack.TryGetValue(key, out value) && value is string &&
                !string.IsNullOrWhiteSpace((string)value);
        }
    }

    public sealed class CharacterSkillCatalog
    {
        public string GameVersion { get; private set; }
        public int CharacterCount { get { return characters.Count; } }
        public int SkillCount { get; private set; }
        private readonly Dictionary<string, CharacterSkillProfile> characters =
            new Dictionary<string, CharacterSkillProfile>(StringComparer.Ordinal);
        public IEnumerable<CharacterSkillProfile> All { get { return characters.Values; } }

        public CharacterSkillProfile Find(string unitId)
        {
            CharacterSkillProfile value;
            return unitId != null && characters.TryGetValue(unitId, out value) ? value : null;
        }

        public CharacterSkillProfile Require(string unitId)
        {
            var value = Find(unitId);
            if (value == null) throw new KeyNotFoundException("도감 유닛 정보 없음: " + unitId);
            return value;
        }

        public static CharacterSkillCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "document");
            string version = OrdrJson.RequiredString(root, "gameVersion");
            if (version != "S2 2.323[C]")
                throw new FormatException("서로 다른 원랜디 버전의 스킬 데이터를 혼용할 수 없습니다: " + version);
            var catalog = new CharacterSkillCatalog();
            catalog.GameVersion = version;
            foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "profiles"), "profiles"))
            {
                var profile = new CharacterSkillProfile(OrdrJson.Object(item, "profile"));
                if (catalog.characters.ContainsKey(profile.UnitId))
                    throw new FormatException("중복 도감 유닛 ID: " + profile.UnitId);
                catalog.characters.Add(profile.UnitId, profile);
                checked { catalog.SkillCount += profile.Skills.Count; }
            }
            return catalog;
        }
    }
}
