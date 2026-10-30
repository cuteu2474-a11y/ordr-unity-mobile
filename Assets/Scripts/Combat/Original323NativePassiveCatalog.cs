using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum OriginalNativePassiveKind { Critical, Stun, CriticalStun }

    public sealed class OriginalNativePassive
    {
        public string CatalogId { get; internal set; }
        public string OwnerRawcode { get; internal set; }
        public string AbilityRawcode { get; internal set; }
        public string Name { get; internal set; }
        public OriginalNativePassiveKind Kind { get; internal set; }
        public int ChancePerMillion { get; internal set; }
        public double CriticalMultiplier { get; internal set; }
        public double NormalStunSeconds { get; internal set; }
        public double HeroStunSeconds { get; internal set; }
        public string OriginalTargetMask { get; internal set; }
    }

    // Only COMPLETE effects: no damage bonus, mixed crit/stun, multiple AHbh,
    // inherited/missing fields or multi-level assumptions are admitted.
    public sealed class Original323NativePassiveCatalog
    {
        private readonly Dictionary<string, OriginalNativePassive> byOwner =
            new Dictionary<string, OriginalNativePassive>(StringComparer.Ordinal);
        // Separate practice-only catalog: never exposed to the verified native path.
        private readonly Dictionary<string, OriginalNativePassive> practiceFirstLevel =
            new Dictionary<string, OriginalNativePassive>(StringComparer.Ordinal);
        public int Count { get { return byOwner.Count; } }
        public IEnumerable<OriginalNativePassive> All { get { return byOwner.Values; } }
        public OriginalNativePassive ForRawcode(string rawcode)
        {
            OriginalNativePassive value;
            return rawcode != null && byOwner.TryGetValue(rawcode, out value) ? value : null;
        }
        public OriginalNativePassive PracticeFirstLevelForRawcode(string rawcode)
        {
            OriginalNativePassive value;
            return rawcode != null && practiceFirstLevel.TryGetValue(rawcode, out value) ? value : null;
        }
        public int PracticeFirstLevelCount { get { return practiceFirstLevel.Count; } }

        public static Original323NativePassiveCatalog FromJson(string json,
            OriginalMapBaseStatCatalog originalMap)
        {
            if (originalMap == null || originalMap.SourceW3xSha256 !=
                Original323UncommonAbilityCatalog.SourceW3xSha256)
                throw new ArgumentException("원본 맵 객체 카탈로그가 필요합니다.");
            var root = OrdrJson.Object(OrdrJson.Parse(json), "native passives");
            if (OrdrJson.RequiredString(root, "format") != "ordr-2323C-native-pure-passives-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") != originalMap.SourceW3xSha256 ||
                OrdrJson.RequiredString(root, "abilityDataSha256") !=
                    Original323UncommonAbilityCatalog.AbilityDataSlkSha256 ||
                OrdrJson.RequiredString(root, "abilityOverridesSha256") !=
                    "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0" ||
                OrdrJson.RequiredString(root, "unitAbilitiesSha256") !=
                    "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d")
                throw new FormatException("원본 네이티브 스킬 자료 지문 불일치");
            var result = new Original323NativePassiveCatalog();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                var row = OrdrJson.Object(item, "native passive");
                string id = OrdrJson.RequiredString(row, "catalogId");
                string raw = OrdrJson.RequiredString(row, "originalUnitRawcode");
                string code = OrdrJson.RequiredString(row, "abilityRawcode");
                var owner = originalMap.Find(id);
                if (owner == null || owner.OriginalRawcode != raw || code.Length != 4 ||
                    !Contains(owner.OriginalAbilityRawcodes, code) ||
                    Contains(owner.SourceJassFusionAbilityRawcodes, code) ||
                    OrdrJson.Number(row, "level") != 1 || result.byOwner.ContainsKey(raw))
                    throw new FormatException("원본 스킬 소유 관계 또는 레벨 불일치: " + id);
                var fields = OrdrJson.Object(OrdrJson.Value(row, "sourceFields"), "source fields");
                double chance = Field(fields, "DataA1");
                double multiplier = Field(fields, "DataB1");
                double bonus = Field(fields, "DataC1");
                double normal = Field(fields, "Dur1");
                double hero = Field(fields, "HeroDur1");
                double scaled = chance * 10000;
                string mask = OrdrJson.RequiredString(fields, "targs1");
                string kind = OrdrJson.RequiredString(row, "kind");
                bool crit = kind == "critical";
                if ((kind != "critical" && kind != "stun") ||
                    chance <= 0 || chance > 100 || scaled != Math.Floor(scaled) ||
                    bonus != 0 || Field(fields, "Cool1") != 0 || Field(fields, "DataD1") != 0 ||
                    (crit ? multiplier <= 1 || normal != 0 || hero != 0 :
                            multiplier != 0 || normal <= 0 || hero < 0) ||
                    OrdrJson.Number(row, "chancePerMillion") != scaled ||
                    OrdrJson.Number(row, "criticalMultiplier") != multiplier ||
                    OrdrJson.Number(row, "normalStunSeconds") != normal ||
                    OrdrJson.Number(row, "heroStunSeconds") != hero ||
                    OrdrJson.RequiredString(row, "targetMask") != mask)
                    throw new FormatException("원본 스킬 수치/전체 효과 불일치: " + code);
                result.byOwner.Add(raw, new OriginalNativePassive {
                    CatalogId = id, OwnerRawcode = raw, AbilityRawcode = code,
                    Name = OrdrJson.RequiredString(row, "catalogName") +
                        (crit ? " · 크리티컬" : " · 기절"),
                    Kind = crit ? OriginalNativePassiveKind.Critical : OriginalNativePassiveKind.Stun,
                    ChancePerMillion = (int)scaled, CriticalMultiplier = multiplier,
                    NormalStunSeconds = normal, HeroStunSeconds = hero, OriginalTargetMask = mask
                });
            }
            object practiceRows = OrdrJson.Value(root, "practiceFirstLevelEntries");
            if (practiceRows != null)
            foreach (var item in OrdrJson.Array(practiceRows, "practice first-level passives"))
            {
                var row = OrdrJson.Object(item, "practice passive");
                string id = OrdrJson.RequiredString(row, "catalogId");
                string raw = OrdrJson.RequiredString(row, "originalUnitRawcode");
                string code = OrdrJson.RequiredString(row, "abilityRawcode");
                string kind = OrdrJson.RequiredString(row, "kind");
                double? levels = OrdrJson.Number(row, "declaredLevels");
                var owner = originalMap.Find(id);
                if (owner == null || owner.OriginalRawcode != raw || code.Length != 4 ||
                    !Contains(owner.OriginalAbilityRawcodes, code) ||
                    Contains(owner.SourceJassFusionAbilityRawcodes, code) ||
                    OrdrJson.Number(row, "level") != 1 || !levels.HasValue ||
                    levels.Value < 1 || levels.Value > 3 || Math.Floor(levels.Value) != levels.Value ||
                    result.byOwner.ContainsKey(raw) || result.practiceFirstLevel.ContainsKey(raw))
                    throw new FormatException("연습 패시브 소유 관계 오류: " + id);
                var fields = OrdrJson.Object(OrdrJson.Value(row, "sourceFields"), "source fields");
                double chance = Field(fields, "DataA1"), multiplier = Field(fields, "DataB1");
                double bonus = Field(fields, "DataC1"), normal = Field(fields, "Dur1");
                double hero = Field(fields, "HeroDur1"), scaled = chance * 10000;
                string mask = OrdrJson.RequiredString(fields, "targs1");
                bool critical = kind == "critical" || kind == "critical-stun";
                bool stun = kind == "stun" || kind == "critical-stun";
                if ((!critical && !stun) || (levels.Value == 1 && kind != "critical-stun") ||
                    chance <= 0 || chance > 100 || scaled != Math.Floor(scaled) ||
                    bonus != 0 || Field(fields, "Cool1") != 0 || Field(fields, "DataD1") != 0 ||
                    (critical ? multiplier <= 1 : multiplier != 0) ||
                    (stun ? normal <= 0 || hero < 0 : normal != 0 || hero != 0) ||
                    OrdrJson.Number(row, "chancePerMillion") != scaled ||
                    OrdrJson.Number(row, "criticalMultiplier") != multiplier ||
                    OrdrJson.Number(row, "normalStunSeconds") != normal ||
                    OrdrJson.Number(row, "heroStunSeconds") != hero ||
                    OrdrJson.RequiredString(row, "targetMask") != mask)
                    throw new FormatException("연습 패시브 원본 수치 오류: " + code);
                result.practiceFirstLevel.Add(raw, new OriginalNativePassive {
                    CatalogId = id, OwnerRawcode = raw, AbilityRawcode = code,
                    Name = OrdrJson.RequiredString(row, "catalogName") +
                        (kind == "critical-stun" ? " · 크리티컬과 기절" : critical ? " · 크리티컬" : " · 기절"),
                    Kind = kind == "critical-stun" ? OriginalNativePassiveKind.CriticalStun :
                        critical ? OriginalNativePassiveKind.Critical : OriginalNativePassiveKind.Stun,
                    ChancePerMillion = (int)scaled, CriticalMultiplier = multiplier,
                    NormalStunSeconds = normal, HeroStunSeconds = hero, OriginalTargetMask = mask
                });
            }
            return result;
        }

        private static bool Contains(IReadOnlyList<string> values, string code)
        { foreach (string value in values) if (value == code) return true; return false; }
        private static double Field(Dictionary<string, object> row, string key)
        {
            double value;
            if (!double.TryParse(OrdrJson.RequiredString(row, key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("원본 능력 필드 누락: " + key);
            return value;
        }
    }

    // Owned, immutable native target state, not a witness returning future
    // skill impacts or HP damage. Revalidate when immunity/hero state changes.
    // Eligibility means the EXACT original target mask and immunity passed.
    public sealed class Original323NativePassiveTargetProfile
    {
        private readonly HashSet<string> eligible = new HashSet<string>(StringComparer.Ordinal);
        public string OriginalUnitRawcode { get; private set; }
        public bool WarcraftHero { get; private set; }
        public string Evidence { get; private set; }
        public Original323NativePassiveTargetProfile(string originalUnitRawcode,
            bool warcraftHero, IEnumerable<string> verifiedEligibleAbilities,
            string sourceW3xSha256, string currentTargetEvidence)
        {
            if (originalUnitRawcode == null || originalUnitRawcode.Length != 4 ||
                verifiedEligibleAbilities == null || string.IsNullOrWhiteSpace(currentTargetEvidence) ||
                sourceW3xSha256 != Original323UncommonAbilityCatalog.SourceW3xSha256)
                throw new ArgumentException("원본 대상 분류·면역·스킬 대상 마스크 상태가 필요합니다.");
            foreach (string code in verifiedEligibleAbilities)
                if (code == null || code.Length != 4 || !eligible.Add(code))
                    throw new ArgumentException("네이티브 스킬 대상 자격 중복/오류");
            OriginalUnitRawcode = originalUnitRawcode; WarcraftHero = warcraftHero;
            Evidence = currentTargetEvidence;
        }
        public bool Allows(string abilityRawcode) { return eligible.Contains(abilityRawcode); }
    }

    public sealed class Original323NativePassiveActivation
    {
        public OriginalNativePassive Skill { get; private set; }
        internal CombatEnemy Target { get; private set; }
        internal Original323NativePassiveTargetProfile TargetProfile { get; private set; }
        internal long TargetRevision { get; private set; }
        internal Original323NativePassiveActivation(OriginalNativePassive skill, CombatEnemy target)
        {
            Skill = skill; Target = target; TargetProfile = target.NativePassiveTargetProfile;
            TargetRevision = target.NativePassiveTargetRevision;
        }
    }

    // Concrete execution: source-owned proc chance, pre-armor multiplier,
    // and typed activation carried on this exact attack through HP commit.
    // No external observer supplies the proc result or the final skill damage.
    public sealed class Original323NativePassiveHitSource :
        IOrdr323VerifiedHitSource, IOrdr323VerifiedSkillLookup
    {
        private readonly IOrdr323VerifiedHitSource original;
        private readonly Original323NativePassiveCatalog catalog;
        private readonly ICombatProcRandom random;
        private readonly HashSet<string> excluded;
        public Original323NativePassiveHitSource(IOrdr323VerifiedHitSource original,
            Original323NativePassiveCatalog catalog, ICombatProcRandom random,
            IEnumerable<string> externallyHandledAbilities = null)
        {
            this.original = original ?? throw new ArgumentNullException(nameof(original));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            excluded = new HashSet<string>(externallyHandledAbilities ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
        public bool TryResolveBasic(BasicDamageRequest request, CombatEnemy enemy,
            out Ordr323VerifiedDamageHit hit)
        {
            hit = null;
            if (request == null || enemy == null || request.TargetInstanceId != enemy.Id ||
                !original.TryResolveBasic(request, enemy, out hit) || hit == null) return false;
            var skill = catalog.ForRawcode(request.UnitRawCode);
            var target = enemy.NativePassiveTargetProfile;
            if (skill == null || excluded.Contains(skill.AbilityRawcode) || !enemy.IsAlive ||
                target == null || !target.Allows(skill.AbilityRawcode)) return true;
            int roll = skill.ChancePerMillion == 1000000 ? 0 : random.Next(1000000);
            if (roll < 0 || roll >= 1000000) throw new InvalidOperationException("네이티브 스킬 난수 범위 오류");
            if (roll >= skill.ChancePerMillion) return true;
            if (skill.Kind == OriginalNativePassiveKind.Critical)
            {
                double enhanced = hit.RawDamage * skill.CriticalMultiplier;
                if (double.IsNaN(enhanced) || double.IsInfinity(enhanced))
                    throw new OverflowException("원본 크리티컬 원시 피해 초과");
                hit = new Ordr323VerifiedDamageHit(enhanced, hit.AttackType,
                    hit.UseAttackTable, hit.UseArmorRating, hit.UseMagicResistance,
                    hit.Traits, hit.Evidence + " / source native " + skill.AbilityRawcode,
                    hit.School);
            }
            request.RecordNativePassive(new Original323NativePassiveActivation(skill, enemy));
            return true;
        }
        public bool TryResolveSkill(CharacterSkillDamageRequest request, out Ordr323VerifiedDamageHit hit)
        { return original.TryResolveSkill(request, out hit); }
        public bool CanResolveSkillComponent(CharacterSkillProfile profile, ImportedCombatSkill skill,
            ImportedCombatEffect effect)
        {
            var lookup = original as IOrdr323VerifiedSkillLookup;
            return lookup != null && lookup.CanResolveSkillComponent(profile, skill, effect);
        }
    }
}
