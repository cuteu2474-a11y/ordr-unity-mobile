using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Second group of 30 units. This list identifies source object identities,
    // not thirty completed skill kits. A deliberately small allow-list enables
    // *tooltip-based practice packets* only when the complete packet has a
    // supported shape AND its skill name is explicitly allow-listed. Seven
    // resource-consuming practice packets were previously approved; additional
    // complete tooltip packets are now connected via the same gated engine.
    // Other health/mana,
    // form, summon and stacking paths remain deferred until source review.
    public sealed class SourceModelNextThirtyUnit
    {
        public string CatalogId { get; private set; }
        public string Rawcode { get; private set; }
        public IReadOnlyList<string> PracticeAttackSkills { get; private set; }
        public IReadOnlyList<string> PracticeActiveSkills { get; private set; }

        internal SourceModelNextThirtyUnit(string id, string rawcode, params string[] skills)
            : this(id, rawcode, skills, Array.Empty<string>()) { }

        internal SourceModelNextThirtyUnit(string id, string rawcode,
            string[] attackSkills, string[] activeSkills)
        {
            CatalogId = id;
            Rawcode = rawcode;
            PracticeAttackSkills = Array.AsReadOnly(attackSkills);
            PracticeActiveSkills = Array.AsReadOnly(activeSkills);
        }

        public bool Allows(string skillName)
        {
            foreach (string name in PracticeAttackSkills)
                if (string.Equals(name, skillName, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool AllowsActive(string skillName)
        {
            foreach (string name in PracticeActiveSkills)
                if (string.Equals(name, skillName, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    public static class SourceModelNextThirtySkillCatalog
    {
        private static readonly SourceModelNextThirtyUnit[] Entries = {
            new SourceModelNextThirtyUnit("L-03", "h09Z", "전격"),
            new SourceModelNextThirtyUnit("L-04", "h02W", "바람바람 열매"),
            new SourceModelNextThirtyUnit("L-07", "h03A", "명왕의 검술"),
            new SourceModelNextThirtyUnit("L-10", "h02Y"),
            new SourceModelNextThirtyUnit("L-13", "h02Z", "배리어 볼", "배리어 피스트"),
            new SourceModelNextThirtyUnit("L-15", "h02X", "왼손은 수면제", "타격 스턴"),
            new SourceModelNextThirtyUnit("L-17", "h035", "패왕색의 검무"),
            new SourceModelNextThirtyUnit("L-20", "h02V", "화이트블로우", "화이트바인"),
            new SourceModelNextThirtyUnit("L-25", "h03S",
                new[] { "울두건" }, new[] { "미티어 울두건" }),
            new SourceModelNextThirtyUnit("L-33", "h03H"),
            new SourceModelNextThirtyUnit("H-02", "h03U", "보스·광폭화 함포"),
            new SourceModelNextThirtyUnit("H-04", "h03N",
                new[] { "숲 물리" }, new[] { "거대 숲 물리" }),
            new SourceModelNextThirtyUnit("H-07", "h03R",
                Array.Empty<string>(), new[] { "갑판 포격" }),
            new SourceModelNextThirtyUnit("H-12", "h03M", "용의 발톱"),
            new SourceModelNextThirtyUnit("H-16", "h041"),
            new SourceModelNextThirtyUnit("H-22", "h047", "마리조아붕괴"),
            new SourceModelNextThirtyUnit("TR-02", "H08V", "미라쥬 템포"),
            new SourceModelNextThirtyUnit("TR-04", "H098",
                new[] { "범위 마법 스턴", "혼합 타격", "멀티샷 추가 피해" },
                new[] { "체력 소모 광역 공격" }),
            new SourceModelNextThirtyUnit("TR-09", "H094",
                new[] { "광역 3연타", "평타 광역 추가", "크리티컬", "단일 스턴 타격" },
                new[] { "마나 광역 공격", "체력 소모 연타" }),
            new SourceModelNextThirtyUnit("TR-10", "H09I",
                new[] { "타격 스턴", "범위 마법타격" }, new[] { "마나 광역 공격" }),
            new SourceModelNextThirtyUnit("TR-14", "H0B2",
                new[] { "무장색 패기", "컬버린" }, new[] { "킹코브라" }),
            new SourceModelNextThirtyUnit("TR-18", "H0BD",
                "스턴 물리 타격", "마환인", "미환인", "무시빙아"),
            new SourceModelNextThirtyUnit("TR-23", "H091"),
            new SourceModelNextThirtyUnit("TR-30", "H0AA"),
            new SourceModelNextThirtyUnit("E-01", "H0BK",
                new[] { "혼합 광역 타격" }, new[] { "마나 광역기" }),
            new SourceModelNextThirtyUnit("E-04", "h058",
                new[] { "매 타격 추가 마법", "범위 마법 타격", "연속 범위 타격", "확정 스턴" },
                new[] { "마나 고정 피해" }),
            new SourceModelNextThirtyUnit("E-08", "h08R",
                new[] { "범위 물리", "마법 광역", "단일 스턴" },
                new[] { "마나 광역 스턴" }),
            new SourceModelNextThirtyUnit("I-04", "h04J",
                new[] { "광역 물리 타격", "타격·단일 스턴" },
                new[] { "광역 혼합 피해" }),
            new SourceModelNextThirtyUnit("I-06", "h04Q",
                Array.Empty<string>(), new[] { "대자재천신" }),
            new SourceModelNextThirtyUnit("I-11", "h07M")
        };
        private static readonly Dictionary<string, SourceModelNextThirtyUnit> ByRawcode =
            new Dictionary<string, SourceModelNextThirtyUnit>(StringComparer.Ordinal);
        public static readonly IReadOnlyList<SourceModelNextThirtyUnit> All =
            Array.AsReadOnly(Entries);

        static SourceModelNextThirtySkillCatalog()
        {
            foreach (var entry in Entries)
                ByRawcode.Add(entry.Rawcode, entry);
        }

        public static bool TryGet(string rawcode, out SourceModelNextThirtyUnit entry)
        {
            entry = null;
            return rawcode != null && ByRawcode.TryGetValue(rawcode, out entry);
        }

        private static readonly HashSet<string> DamageFields = new HashSet<string>(
            StringComparer.Ordinal) { "type", "amount", "multiplierRange",
            "amountPerHit", "hits", "basis", "percent", "scope", "onlyTargetKinds",
            "plusTargetMaxHealthPercent", "plusTargetMissingHealthPercent",
            "extraTargetMaxHealthPercent", "extraBossMaxHealthPercent",
            "extraEnragedMaxHealthPercent", "extraAmount",
            "singlePrimaryTargetExtraPercent" };

        private static bool FinitePositive(double? number) =>
            number.HasValue && number.Value > 0 &&
            !double.IsNaN(number.Value) && !double.IsInfinity(number.Value);

        private static bool ValidDamage(ImportedCombatEffect effect)
        {
            foreach (string key in effect.Fields.Keys)
                if (!DamageFields.Contains(key)) return false;
            bool hasAmount = effect.Get("amount") != null;
            bool hasPerHit = effect.Get("amountPerHit") != null;
            bool hasBasis = effect.Get("basis") != null;
            if (hasAmount && !FinitePositive(effect.Number("amount"))) return false;
            if (hasPerHit && (!FinitePositive(effect.Number("amountPerHit")) ||
                !FinitePositive(effect.Number("hits")) ||
                effect.Number("hits").Value > 64 ||
                Math.Floor(effect.Number("hits").Value) != effect.Number("hits").Value))
                return false;
            if ((effect.Get("hits") != null) != hasPerHit) return false;
            if (hasBasis && (effect.Text("basis") != "targetMaxHealth" ||
                !FinitePositive(effect.Number("percent")))) return false;
            if (!hasBasis && effect.Get("percent") != null) return false;
            bool extra = false;
            foreach (string key in new[] {
                "plusTargetMaxHealthPercent", "plusTargetMissingHealthPercent",
                "extraTargetMaxHealthPercent", "extraBossMaxHealthPercent",
                "extraEnragedMaxHealthPercent", "extraAmount" })
            {
                if (effect.Get(key) == null) continue;
                if (!FinitePositive(effect.Number(key))) return false;
                extra = true;
            }
            if (!hasAmount && !hasPerHit && !hasBasis && !extra) return false;
            if (effect.Get("singlePrimaryTargetExtraPercent") != null &&
                (!FinitePositive(effect.Number("singlePrimaryTargetExtraPercent")) ||
                 !hasAmount)) return false;
            if (effect.Get("scope") != null &&
                effect.Text("scope") != "singlePrimaryTarget") return false;
            if (effect.Get("onlyTargetKinds") != null)
            {
                var kinds = effect.Get("onlyTargetKinds") as List<object>;
                if (kinds == null || kinds.Count == 0) return false;
                foreach (object kind in kinds)
                    if (!(kind is string) ||
                        ((string)kind != "line" && (string)kind != "boss" &&
                         (string)kind != "story" && (string)kind != "enraged"))
                        return false;
            }
            if (effect.Get("multiplierRange") != null)
            {
                var range = effect.Get("multiplierRange") as List<object>;
                if (range == null || range.Count != 2 ||
                    !(range[0] is double) || !(range[1] is double) ||
                    !FinitePositive((double)range[0]) ||
                    !FinitePositive((double)range[1]) ||
                    (double)range[1] < (double)range[0]) return false;
            }
            return true;
        }

        // Reject an entire skill if any part of its documented packet is
        // unsupported. Tooltip packets are a practice model, never JASS parity.
        public static bool CanModelCompleteAttack(ImportedCombatSkill skill)
        {
            if (SourceModelSixteenthSkillCatalog.IsSpecial(skill))
                return (skill.Trigger == "onAttack" ||
                    skill.Trigger == "onEveryAttack") &&
                    SourceModelSixteenthSkillCatalog.CanModelExactSpecial(skill);
            if (SourceModelFifteenthSkillCatalog.IsSpecial(skill))
                return (skill.Trigger == "onAttack" ||
                    skill.Trigger == "onEveryAttack") &&
                    SourceModelFifteenthSkillCatalog.CanModelExactSpecial(skill);
            bool fourth = IsFourthCompositeAttack(skill);
            bool fifth = IsFifthCompositeSkill(skill);
            bool sixth = IsSixthCompositeSkill(skill);
            bool seventh = IsSeventhCompositeSkill(skill);
            bool eighth = IsEighthCompositeSkill(skill);
            if (skill == null ||
                !(skill.Trigger == "onAttack" || skill.Trigger == "onEveryAttack" ||
                  (eighth && (skill.Trigger == "onAttackWithCondition" ||
                              skill.Trigger == "firstAttackAfterTransform" ||
                              skill.Trigger == "everyNthAttack")) ||
                  (seventh && skill.Id == "TR-11:skill:4" &&
                   skill.Trigger == "attackWithPeriodicCooldown") ||
                  (fifth && skill.Id == "TR-26:skill:0" &&
                   skill.Trigger == "onAttackWithMana")) ||
                !(skill.Target == "area" || skill.Target == "single" ||
                  skill.Target == "casterArea" ||
                  (seventh && skill.Id == "TR-16:skill:0" &&
                   skill.Target == "targetCircle") ||
                  (seventh && skill.Id == "TR-24:skill:2" &&
                   skill.Target == "summon") ||
                  (eighth && skill.Id == "D-01:skill:1" &&
                   skill.Target == null) ||
                  (fourth && skill.Id == "D-03:skill:2" &&
                   skill.Target == null && FinitePositive(skill.Radius)) ||
                  (fifth && skill.Id == "D-03:skill:1" &&
                   skill.Target == null && FinitePositive(skill.Radius)) ||
                  (fifth && skill.Id == "TR-04:skill:3" &&
                   skill.Target == "eachMultishotTarget") ||
                  (sixth && skill.Id == "TR-11:skill:1" &&
                   skill.Target == null && FinitePositive(skill.Radius)) ||
                  ((fourth && skill.Id == "TR-26:skill:4" ||
                    fifth && skill.Id == "TR-07:skill:1") && skill.Target == "line")) ||
                ((skill.Target == "area" || skill.Target == "casterArea" ||
                  skill.Target == "line" || skill.Target == "targetCircle") &&
                 !FinitePositive(skill.Radius)) ||
                ((skill.Trigger == "onAttack" || skill.Trigger == "onAttackWithMana") &&
                 (!FinitePositive(skill.ChancePercent) || skill.ChancePercent.Value > 100)) ||
                (skill.Trigger == "onEveryAttack" && skill.ChancePercent.HasValue) ||
                (eighth && skill.Trigger != "onAttack" && skill.ChancePercent.HasValue) ||
                skill.Components.Count == 0) return false;
            foreach (string key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "chancePercent" &&
                    key != "target" && key != "radius" && key != "components" &&
                    !(fourth && skill.Id == "E-07:skill:3" &&
                      key == "absoluteCooldownSeconds") &&
                    !(fifth && skill.Id == "TR-07:skill:2" && key == "overrides") &&
                    !(fifth && skill.Id == "TR-07:skill:3" &&
                      key == "maxProcChancePercent") &&
                    !(fifth && skill.Id == "TR-26:skill:0" &&
                      (key == "requiredMana" || key == "manaCost")) &&
                    !(sixth && key == "overrides" &&
                      (skill.Id == "TR-09:skill:5" ||
                       skill.Id == "TR-11:skill:1" ||
                       skill.Id == "TR-14:skill:2" ||
                       skill.Id == "TR-14:skill:3" ||
                       skill.Id == "TR-19:skill:1")) &&
                    !(sixth && skill.Id == "TR-15:skill:1" &&
                      key == "absoluteCooldownSeconds") &&
                    !(sixth && skill.Id == "E-12:skill:5" &&
                      key == "enhancedChancePercent") &&
                    !(seventh && skill.Id == "TR-11:skill:4" &&
                      (key == "intervalSeconds" || key == "strengthScalingPerPoint")) &&
                    !(seventh && skill.Id == "TR-16:skill:0" &&
                      (key == "manaAtLeast" || key == "manaCost" ||
                       key == "castTimeSeconds")) &&
                    !(eighth && skill.Id == "TR-18:skill:4" &&
                      key == "requires") &&
                    !(eighth && skill.Id == "D-01:skill:1" &&
                      key == "everyAttacks") &&
                    !(eighth && skill.Id == "TR-18:skill:2" &&
                      key == "overrides"))
                    return false;
            if (eighth) return CanModelEighthCompositeComponents(skill);
            if (seventh) return CanModelSeventhCompositeComponents(skill);
            if (sixth) return CanModelSixthCompositeComponents(skill);
            if (fifth) return CanModelFifthCompositeComponents(skill);
            if (fourth)
            {
                if (skill.Id == "E-07:skill:3" &&
                    !FinitePositive(skill.Number("absoluteCooldownSeconds"))) return false;
                return CanModelFourthCompositeComponents(skill);
            }
            if (IsThirdCompositeAttack(skill))
                return CanModelThirdCompositeComponents(skill);
            return IsSecondCompositeSkill(skill) ?
                CanModelSecondCompositeComponents(skill) : CanModelComponents(skill);
        }

        // A parsable packet is not authorization. Names can be shared by
        // different units, so retain the owning identity at the cast boundary.
        public static bool CanCastApprovedActive(string catalogId,
            IReadOnlyList<string> approvedNames, ImportedCombatSkill skill)
        {
            if (string.IsNullOrWhiteSpace(catalogId) || approvedNames == null ||
                skill == null || string.IsNullOrEmpty(skill.Id) ||
                !skill.Id.StartsWith(catalogId + ":skill:", StringComparison.Ordinal) ||
                !CanModelCompleteActive(skill)) return false;
            foreach (string name in approvedNames)
                if (string.Equals(name, skill.Name, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool CanModelCompleteActive(ImportedCombatSkill skill)
        {
            if (SourceModelSixteenthSkillCatalog.IsSpecial(skill))
                return (skill.Trigger == "activeMana" ||
                    skill.Trigger == "activeHealth") &&
                    SourceModelSixteenthSkillCatalog.CanModelExactSpecial(skill);
            if (SourceModelFifteenthSkillCatalog.IsSpecial(skill))
                return (skill.Trigger == "activeMana" ||
                    skill.Trigger == "activeHealth") &&
                    SourceModelFifteenthSkillCatalog.CanModelExactSpecial(skill);
            bool composite = IsFirstCompositeActive(skill);
            bool second = IsSecondCompositeSkill(skill);
            bool fifth = IsFifthCompositeSkill(skill);
            bool sixth = IsSixthCompositeSkill(skill);
            bool seventh = IsSeventhCompositeSkill(skill);
            if (skill == null ||
                !(skill.Trigger == "activeMana" || skill.Trigger == "activeHealth") ||
                !(skill.Target == "area" || skill.Target == "single") ||
                (skill.Target == "area" && !FinitePositive(skill.Radius)) ||
                skill.ChancePercent.HasValue || skill.Components.Count == 0 ||
                !FinitePositive(skill.Number(skill.Trigger == "activeMana" ?
                    "manaCost" : "healthCost"))) return false;
            foreach (string key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "target" &&
                    key != "radius" && key != "components" &&
                    key != "manaCost" && key != "healthCost" &&
                    key != "cooldownSeconds" && key != "absoluteCooldownSeconds" &&
                    !(composite && skill.Id == "L-25:skill:0" &&
                      (key == "healthRegenPerSecond" || key == "healthRegenPerAttack")) &&
                    !(composite && skill.Id == "D-03:skill:0" &&
                      key == "oneTimeConditionBypass") &&
                    !(second && skill.Id == "TR-08:skill:0" &&
                      key == "singleTargetMagicalDamageMultiplier") &&
                    !(fifth && skill.Id == "E-07:skill:0" &&
                      key == "damageDelaySeconds"))
                    return false;
            foreach (string key in new[] { "cooldownSeconds", "absoluteCooldownSeconds" })
                if (skill.Get(key) != null &&
                    (!skill.Number(key).HasValue || double.IsNaN(skill.Number(key).Value) ||
                     double.IsInfinity(skill.Number(key).Value) || skill.Number(key).Value < 0))
                    return false;
            if (composite && skill.Id == "L-25:skill:0" &&
                (!FinitePositive(skill.Number("healthRegenPerSecond")) ||
                 !FinitePositive(skill.Number("healthRegenPerAttack")))) return false;
            if (composite && skill.Id == "D-03:skill:0")
            {
                var bypass = skill.Get("oneTimeConditionBypass") as List<object>;
                if (bypass == null || bypass.Count != 2 ||
                    !(bypass[0] is string) || (string)bypass[0] != "boss" ||
                    !(bypass[1] is string) || (string)bypass[1] != "enraged")
                    return false;
            }
            if (second && skill.Id == "TR-08:skill:0" &&
                !FinitePositive(skill.Number("singleTargetMagicalDamageMultiplier")))
                return false;
            if (seventh) return CanModelSeventhCompositeComponents(skill);
            if (sixth) return CanModelSixthCompositeComponents(skill);
            if (fifth) return CanModelFifthCompositeComponents(skill);
            if (second) return CanModelSecondCompositeComponents(skill);
            return composite ? CanModelFirstCompositeComponents(skill) :
                CanModelComponents(skill);
        }

        // Explicit second-batch identities. RN-04 already has its own JASS
        // attack/damage callback: only its *missing statuses* will be added.
        public static bool IsSecondCompositeSkill(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "E-09:skill:0": return skill.Name == "광역 노래 마법";
                case "SR-03:skill:0": return skill.Name == "체력 소모 광역공격";
                case "TR-08:skill:0": return skill.Name == "레드호크";
                case "TR-22:skill:0": return skill.Name == "어인가라데";
                case "TR-29:skill:0": return skill.Name == "씽크홀";
                case "LI-06:skill:0": return skill.Name == "범위 광역 스턴";
                case "MY-12:skill:1": return skill.Name == "염동 회오리";
                case "MY-12:skill:3": return skill.Name == "염동 운석낙하";
                case "H-19:skill:0": return skill.Name == "범위 마법·증폭";
                case "LI-02:skill:1": return skill.Name == "광역 물리 둔화";
                case "RN-04:skill:0": return skill.Name == "페인 브링어";
                default: return false;
            }
        }

        // Third batch of twelve attack-triggered tooltip packets. Approval is
        // tied to the owning unit, skill index, name, effect order AND field
        // shape; an unrelated partial tooltip must never inherit this model.
        public static bool IsThirdCompositeAttack(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-08:skill:1": return skill.Name == "평타 광역 연계";
                case "TR-08:skill:2": return skill.Name == "기간트 개틀링";
                case "TR-22:skill:1": return skill.Name == "해류 공격";
                case "TR-22:skill:2": return skill.Name == "진각";
                case "TR-29:skill:1": return skill.Name == "중력장";
                case "TR-29:skill:2": return skill.Name == "중력검";
                case "SR-03:skill:1": return skill.Name == "단일 타격";
                case "H-19:skill:1": return skill.Name == "라인 체력 비례";
                case "LI-02:skill:2": return skill.Name == "물리 연계";
                case "E-09:skill:2": return skill.Name == "음표 병사 연계";
                case "E-09:skill:3": return skill.Name == "3연타 마법";
                case "E-09:skill:4": return skill.Name == "라인·보스 추가 피해";
                default: return false;
            }
        }

        // Follow-on batch: previously omitted, individually source-described
        // procs on twelve existing/identified owners. Exact skill identities
        // remain mandatory even where a packet consists of ordinary damage.
        public static bool IsFourthCompositeAttack(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "L-09:skill:0": return skill.Name == "육왕건";
                case "L-09:skill:1": return skill.Name == "육식 Lv4";
                case "TR-07:skill:0": return skill.Name == "격투 스턴";
                case "R-42:skill:0": return skill.Name == "단일 타격·스턴";
                case "L-25:skill:1": return skill.Name == "울두건";
                case "L-20:skill:1": return skill.Name == "화이트바인";
                case "MY-08:skill:3": return skill.Name == "스타캐치 애로우";
                case "I-09:skill:3": return skill.Name == "범위 마법 스턴";
                case "I-09:skill:4": return skill.Name == "물리 마법 혼합";
                case "D-03:skill:2": return skill.Name == "빙귀";
                case "TR-19:skill:2": return skill.Name == "화염성";
                case "E-07:skill:4": return skill.Name == "적 중심 마법";
                case "E-07:skill:3": return skill.Name == "불주먹";
                case "TR-26:skill:2": return skill.Name == "팔지경";
                case "TR-26:skill:4": return skill.Name == "레이저";
                default: return false;
            }
        }

        // Fifth batch: exact owner/skill-name pairs only. Shapes are checked
        // below so newly imported or partial tooltip rows cannot silently run.
        public static bool IsFifthCompositeSkill(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-07:skill:1": return skill.Name == "람각";
                case "TR-07:skill:2": return skill.Name == "지건";
                case "TR-07:skill:3": return skill.Name == "육왕건";
                case "TR-26:skill:0": return skill.Name == "마나 연타 광역기";
                case "TR-26:skill:3": return skill.Name == "단일 치명타";
                case "E-07:skill:0": return skill.Name == "대염계 염제";
                case "D-03:skill:1": return skill.Name == "역재";
                case "TR-04:skill:3": return skill.Name == "멀티샷 추가 피해";
                default: return false;
            }
        }

        // Sixth connected batch: source-profile identities are exact. Native
        // object procs with the same roll are deliberately NOT approved here.
        public static bool IsSixthCompositeSkill(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-09:skill:0": return skill.Name == "체력 소모 연타";
                case "TR-09:skill:3": return skill.Name == "평타 광역 추가";
                case "TR-09:skill:4": return skill.Name == "크리티컬";
                case "TR-09:skill:5": return skill.Name == "단일 스턴 타격";
                case "TR-11:skill:1": return skill.Name == "불꽃 주먹";
                case "TR-11:skill:2": return skill.Name == "넓은 주먹";
                case "TR-12:skill:1": return skill.Name == "검은다리";
                case "TR-12:skill:3": return skill.Name == "무통 샷";
                case "TR-13:skill:2": return skill.Name == "원형 마법 스턴";
                case "TR-13:skill:3": return skill.Name == "자신중심 마법 스턴";
                case "TR-14:skill:2": return skill.Name == "무장색 패기";
                case "TR-14:skill:3": return skill.Name == "컬버린";
                case "TR-15:skill:1": return skill.Name == "고대병기 포세이돈";
                case "TR-19:skill:1": return skill.Name == "저격";
                case "TR-19:skill:3": return skill.Name == "치명타";
                case "TR-24:skill:3": return skill.Name == "기본 공격 특수 피해";
                case "TR-10:skill:1": return skill.Name == "범위 마법타격";
                case "E-12:skill:3": return skill.Name == "혼합 공격";
                case "E-12:skill:5": return skill.Name == "혼합 스턴 타격";
                case "I-03:skill:1": return skill.Name == "기본 공격 광역 추가";
                default: return false;
            }
        }

        // Full seventh-batch packet approval; no generic "unknown transform"
        // or summon parser may turn an incomplete tooltip into live combat.
        public static bool IsSeventhCompositeSkill(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-11:skill:4": return skill.Name == "화염용왕";
                case "TR-16:skill:0": return skill.Name == "아이스 에이지";
                case "TR-20:skill:0": return skill.Name == "1080 번뇌봉";
                case "TR-24:skill:2": return skill.Name == "콤비네이션 어택";
                default: return false;
            }
        }

        // The eighth group is exact-profile and complete-packet gated. It
        // never approves an unrelated native object proc or partial tooltip.
        public static bool IsEighthCompositeSkill(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-20:skill:1": return skill.Name == "사자의 노래 Lv.2";
                case "TR-20:skill:3": return skill.Name == "삼도류 - 극 호랑이 사냥";
                case "TR-18:skill:4": return skill.Name == "미환인";
                case "TR-18:skill:5": return skill.Name == "무시빙아";
                case "TR-18:skill:2": return skill.Name == "마환인";
                case "D-01:skill:1": return skill.Name == "13타 고정피해";
                case "H-20:skill:1": return skill.Name == "눈보라";
                case "H-14:skill:1": return skill.Name == "흉포한 검술";
                default: return false;
            }
        }

        private static bool CanModelEighthCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "TR-20:skill:1": fields = new[] {
                    "physical|amount,multiplierRange",
                    "singleTargetPhysicalBonus|percent", "stun|durationSeconds,scope",
                    "strengthScaling|aoePerStrength,singlePerStrength" }; break;
                case "TR-20:skill:3": fields = new[] {
                    "physical|amount", "stun|durationSeconds",
                    "strengthScaling|percentPerStrength,appliesTo" }; break;
                case "TR-18:skill:4": fields = new[] {
                    "magical|basicAttackPercent,flatAmount,extraSingleTargetPercent" }; break;
                case "TR-18:skill:2": fields = new[] {
                    "fixed|basicAttackPercent,flatAmount,maxBonusByTargetSlowPercent" }; break;
                case "TR-18:skill:5": fields = new[] {
                    "magical|amount,multiplierRange",
                    "physical|amountPerHit,hits,multiplierRange",
                    "stun|durationSeconds,ignoresResistance" }; break;
                case "D-01:skill:1": fields = new[] {
                    "fixed|amount,basicAttackMultiplier,plusTargetMaxHealthPercent" }; break;
                case "H-20:skill:1": fields = new[] {
                    "magical|amount,extraSingleMultiplierRange" }; break;
                case "H-14:skill:1": fields = new[] {
                    "magical|amount,extraLineMaxHealthPercent" }; break;
                default: return false;
            }
            if (!ValidateExplicitCompositeShape(skill, fields)) return false;
            if (skill.Id == "D-01:skill:1")
                return skill.Number("everyAttacks") == 13;
            if (skill.Id == "TR-18:skill:2")
            {
                var overrides = skill.Get("overrides") as List<object>;
                if (overrides == null || overrides.Count != 2) return false;
                var slow = overrides[0] as Dictionary<string, object>;
                var form = overrides[1] as Dictionary<string, object>;
                return slow != null && slow.Count == 2 &&
                    Equals(slow["state"], "enemyMaxSlow") &&
                    Equals(slow["chancePercent"], 33d) &&
                    form != null && form.Count == 2 &&
                    Equals(form["form"], "beast") &&
                    Equals(form["guaranteedEveryAttack"], true);
            }
            if (skill.Id == "TR-18:skill:4")
                return Equals(skill.Get("requires"), "speedOrTransformation");
            if (skill.Id == "TR-20:skill:1")
                return skill.Components[2].Text("scope") == "single";
            if (skill.Id == "TR-20:skill:3")
                return skill.Components[2].Text("appliesTo") == "삼도류 스킬";
            return true;
        }

        private static bool CanModelSeventhCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "TR-11:skill:4": fields = new[] {
                    "magical|amount", "physical|amount,plusBuffCountMultiplier",
                    "fixed|amount,scope", "stun|durationSeconds,resistanceIgnorePercent" }; break;
                case "TR-16:skill:0": fields = new[] {
                    "magical|amount", "stun|durationSeconds" }; break;
                case "TR-20:skill:0": fields = new[] {
                    "magical|amount,multiplierRange",
                    "physicalDamageOverTime|perTick,tickIntervalSeconds,totalDurationSeconds,affectedBy",
                    "stun|durationSeconds,ignoresResistance" }; break;
                case "TR-24:skill:2": fields = new[] {
                    "summon|unit,durationSeconds,inheritsBaseAttack,legendaryUpgradeAffected,basicAttackExplosivePercent,explosiveRadius,singleMagicalDamagePerHit" }; break;
                default: return false;
            }
            if (!ValidateExplicitCompositeShape(skill, fields)) return false;
            if (skill.Id == "TR-11:skill:4")
                return FinitePositive(skill.Number("intervalSeconds")) &&
                    FinitePositive(skill.Number("strengthScalingPerPoint"));
            if (skill.Id == "TR-16:skill:0")
                return FinitePositive(skill.Number("manaAtLeast")) &&
                    FinitePositive(skill.Number("manaCost")) &&
                    FinitePositive(skill.Number("castTimeSeconds")) &&
                    skill.Number("manaAtLeast").Value >=
                    skill.Number("manaCost").Value;
            if (skill.Id == "TR-20:skill:0")
            {
                var dot = skill.Components[1];
                return dot.Number("tickIntervalSeconds").Value <=
                    dot.Number("totalDurationSeconds").Value;
            }
            return true;
        }

        private static bool CanModelSixthCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "TR-09:skill:0": fields = new[] { "physical|amountPerHit,baseHits,additionalHitsPerDebuff" }; break;
                case "TR-09:skill:3": fields = new[] { "physical|basicAttackPercent,flatAmount" }; break;
                case "TR-09:skill:4": fields = new[] { "critical|multiplier" }; break;
                case "TR-09:skill:5": fields = new[] { "physical|amount", "stun|durationSeconds" }; break;
                case "TR-11:skill:1": fields = new[] { "physical|amount,multiplierRange", "magical|amount,plusOwnBuffCountMultiplier,scope", "physical|plusTargetMaxHealthPercent,onlyTargetKinds,scope", "stun|durationSeconds,scope" }; break;
                case "TR-11:skill:2": fields = new[] { "physical|amount", "movementSlow|percent,durationSeconds" }; break;
                case "TR-12:skill:1": fields = new[] { "stun|durationSeconds", "magical|amount,multiplierRange,whenAnySkill" }; break;
                case "TR-12:skill:3": fields = new[] { "magical|amount", "movementSlow|percent,durationSeconds" }; break;
                case "TR-13:skill:2": fields = new[] { "magical|amount", "stun|durationSeconds,resistanceIgnorePercent" }; break;
                case "TR-13:skill:3": fields = new[] { "magical|amount", "stun|durationSeconds,resistanceIgnorePercent" }; break;
                case "TR-14:skill:2": fields = new[] { "physical|amount,multiplierRange" }; break;
                case "TR-14:skill:3": fields = new[] { "magical|amount,multiplierRange,extraSingleTargetAmount" }; break;
                case "TR-15:skill:1": fields = new[] { "magicalDamageOverTime|amountPerTick,intervalSeconds,durationSeconds,multiplierRange" }; break;
                case "TR-19:skill:1": fields = new[] { "physical|amount", "fixed|basis,bonusPercentByTargetKind,extraMaxHealthPercentByTargetKind" }; break;
                case "TR-19:skill:3": fields = new[] { "critical|multiplier" }; break;
                case "TR-24:skill:3": fields = new[] { "fixed|amountPerBuff", "stun|durationSeconds,ignoresResistance,scope" }; break;
                case "TR-10:skill:1": fields = new[] { "magical|amount,multiplierRange", "additionalCast|chancePercent" }; break;
                case "E-12:skill:3": fields = new[] { "physical|amount", "magical|amount", "movementSlow|percent,durationSeconds" }; break;
                case "E-12:skill:5": fields = new[] { "magical|amount", "physical|amount,multiplierRange", "stun|durationSeconds" }; break;
                case "I-03:skill:1": fields = new[] { "physical|basicAttackPercent" }; break;
                default: return false;
            }
            if (!ValidateExplicitCompositeShape(skill, fields)) return false;
            return SourceModelSixthShapeExtrasValid(skill);
        }

        private static bool SourceModelSixthShapeExtrasValid(ImportedCombatSkill skill)
        {
            // Overlay rows must not acquire unknown effects from future imports.
            var overrides = skill.Get("overrides") as List<object>;
            if (overrides != null)
            {
                if (overrides.Count != 1) return false;
                var row = overrides[0] as Dictionary<string, object>;
                var expected = new Dictionary<string, string[]>(StringComparer.Ordinal) {
                    { "TR-09:skill:5", new[] { "targetKinds", "procMultiplier", "damageMultiplier" } },
                    { "TR-11:skill:1", new[] { "targetKinds", "maxHealthPercent" } },
                    { "TR-14:skill:2", new[] { "form", "chancePercent", "damageMultiplier" } },
                    { "TR-14:skill:3", new[] { "form", "chanceIncreasePercent" } },
                    { "TR-19:skill:1", new[] { "targetKinds", "chancePercent", "hits" } }
                };
                string[] keys;
                if (!expected.TryGetValue(skill.Id, out keys) || row == null ||
                    row.Count != keys.Length) return false;
                foreach (string key in keys)
                    if (!row.ContainsKey(key)) return false;
                if (row.ContainsKey("form") && !Equals(row["form"], "blackMamba"))
                    return false;
                if (row.ContainsKey("targetKinds") &&
                    !ValidSourceModelKinds(row["targetKinds"])) return false;
                foreach (string key in new[] { "procMultiplier", "damageMultiplier",
                    "maxHealthPercent", "chancePercent", "chanceIncreasePercent", "hits" })
                    if (row.ContainsKey(key) && !FinitePositive(Convert.ToDouble(row[key])))
                        return false;
            }
            if (skill.Id == "TR-15:skill:1" &&
                !FinitePositive(skill.Number("absoluteCooldownSeconds"))) return false;
            if (skill.Id == "E-12:skill:5" &&
                (!FinitePositive(skill.Number("enhancedChancePercent")) ||
                 skill.Number("enhancedChancePercent").Value > 100)) return false;
            if (skill.Id == "TR-09:skill:0" &&
                (!FinitePositive(skill.Components[0].Number("baseHits")) ||
                 skill.Components[0].Number("additionalHitsPerDebuff").Value > 1)) return false;
            return true;
        }

        private static bool ValidSourceModelKinds(object data)
        {
            var kinds = data as List<object>;
            if (kinds == null || kinds.Count == 0) return false;
            foreach (object kind in kinds)
                if (!(kind is string) ||
                    ((string)kind != "boss" && (string)kind != "story" &&
                     (string)kind != "enraged" && (string)kind != "line")) return false;
            return true;
        }

        private static bool CanModelFifthCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "TR-07:skill:1": fields = new[] { "magical|amount", "fixed|basis,percent,onlyTargetKinds,scope" }; break;
                case "TR-07:skill:2": fields = new[] { "magical|amount,plusTargetCurrentHealthPercent" }; break;
                case "TR-07:skill:3": fields = new[] { "explosive|amount", "magical|amount,plusTargetCurrentHealthPercent,onlyTargetKinds,scope" }; break;
                case "TR-26:skill:0": fields = new[] { "magical|amountPerHit,agilityScalingPerPoint,multiplierRange,hits" }; break;
                case "TR-26:skill:3": fields = new[] { "critical|multiplier", "stun|durationSeconds" }; break;
                case "E-07:skill:0": fields = new[] { "magical|amount,plusTargetMaxHealthPercent,multiplierRange", "stun|durationSeconds,beforeAndAfterHit" }; break;
                case "D-03:skill:1": fields = new[] { "magical|amount", "armorReduction|amount,durationSeconds" }; break;
                case "TR-04:skill:3": fields = new[] { "physical|basis,multiplier" }; break;
                default: return false;
            }
            if (!ValidateExplicitCompositeShape(skill, fields)) return false;
            if (skill.Id == "TR-07:skill:2")
            {
                var rows = skill.Get("overrides") as List<object>;
                if (rows == null || rows.Count != 1) return false;
                var row = rows[0] as Dictionary<string, object>;
                var kinds = row != null && row.ContainsKey("targetKinds") ?
                    row["targetKinds"] as List<object> : null;
                if (row == null || row.Count != 3 || kinds == null || kinds.Count != 2 ||
                    !kinds.Contains("boss") || !kinds.Contains("story") ||
                    !Equals(row["removeCurrentHealthPercent"], true) ||
                    !Equals(row["extraMagicalAmount"], 500000d)) return false;
            }
            if (skill.Id == "TR-07:skill:3" &&
                (!FinitePositive(skill.Number("maxProcChancePercent")) ||
                 skill.Number("maxProcChancePercent").Value > 100)) return false;
            if (skill.Id == "TR-26:skill:0" &&
                (!FinitePositive(skill.Number("requiredMana")) ||
                 !FinitePositive(skill.Number("manaCost")) ||
                 skill.Number("requiredMana").Value < skill.Number("manaCost").Value)) return false;
            if (skill.Id == "E-07:skill:0" &&
                !FinitePositive(skill.Number("damageDelaySeconds"))) return false;
            return true;
        }

        private static bool CanModelFourthCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "L-09:skill:0": fields = new[] { "magical|amount", "magical|amount,multiplierRange,scope", "magical|basis,percent,onlyTargetKinds,scope" }; break;
                case "L-09:skill:1": fields = new[] { "magical|amount", "stun|durationSeconds" }; break;
                case "TR-07:skill:0": fields = new[] { "physical|amount", "stun|durationSeconds" }; break;
                case "R-42:skill:0": fields = new[] { "physical|amount", "stun|durationSeconds" }; break;
                case "L-25:skill:1": fields = new[] { "physical|basis,percent,flatAmount" }; break;
                case "L-20:skill:1": fields = new[] { "magical|amount,multiplierRange", "armorBreak|stacks,maxArmorReduction" }; break;
                case "MY-08:skill:3": fields = new[] { "magical|amount", "magicDamageAmplification|percent" }; break;
                case "I-09:skill:3": fields = new[] { "magical|amount", "stun|durationSeconds", "armorBreak|stacks,maxStacks" }; break;
                case "I-09:skill:4": fields = new[] { "physical|amountPerHit,hits", "magical|amount" }; break;
                case "D-03:skill:2": fields = new[] { "magical|amount", "stun|durationSeconds", "armorBreak|stacks,max" }; break;
                case "TR-19:skill:2": fields = new[] { "physical|amount,multiplierRange", "magical|amount,scope,targetKindMultiplier" }; break;
                case "E-07:skill:4": fields = new[] { "magical|amount,extraSingleAmount" }; break;
                case "E-07:skill:3": fields = new[] { "magical|amount" }; break;
                case "TR-26:skill:2": fields = new[] { "magical|amountPerHit,hits,agilityScalingPerPoint", "magical|singleExtraPercent" }; break;
                case "TR-26:skill:4": fields = new[] { "magical|amount" }; break;
                default: return false;
            }
            return ValidateExplicitCompositeShape(skill, fields);
        }

        private static bool CanModelThirdCompositeComponents(ImportedCombatSkill skill)
        {
            string[] fields;
            switch (skill.Id)
            {
                case "TR-08:skill:1": fields = new[] { "magical|amount", "physical|basicAttackPercent,flatAmount", "fixed|basicAttackPercent,flatAmount,scope", "movementSlow|percent,durationSeconds" }; break;
                case "TR-08:skill:2": fields = new[] { "magical|amountPerHit,hits", "stun|durationSeconds" }; break;
                case "TR-22:skill:1": fields = new[] { "magical|amount", "physical|amount", "fixed|skillDamagePercent,scope", "armorBreak|stacks,maxStacks", "movementSlow|percent,durationSeconds" }; break;
                case "TR-22:skill:2": fields = new[] { "physical|amount,strengthScalingPerPoint", "armorBreak|stacks,maxStacks" }; break;
                case "TR-29:skill:1": fields = new[] { "physical|basicAttackMultiplier,flatAmount", "stun|durationSeconds", "armorBreak|stacks,maxStacks" }; break;
                case "TR-29:skill:2": fields = new[] { "physical|basicAttackMultiplier,flatAmount", "magical|amount,scope", "stun|durationSeconds", "armorBreak|stacks,maxStacks" }; break;
                case "SR-03:skill:1": fields = new[] { "stun|durationSeconds", "critical|multiplier" }; break;
                case "H-19:skill:1": fields = new[] { "magical|basis,percent,onlyTargetKinds" }; break;
                case "LI-02:skill:2": fields = new[] { "physical|amount,plusLegacyLevelDamage", "magical|bonusPercent,scope" }; break;
                case "E-09:skill:2": fields = new[] { "magical|amountPerSoldier", "magical|amount,scope", "explosive|basis,percentPerSoldier,onlyTargetKinds" }; break;
                case "E-09:skill:3": fields = new[] { "magical|amountPerHit,hits,multiplierRange", "summonSoldier|chancePercent,durationSeconds" }; break;
                case "E-09:skill:4": fields = new[] { "magical|amount,multiplierRange", "magical|amount,onlyTargetKinds", "explosive|basis,percent,plusMissingHealthPercent,hits,onlyTargetKinds", "magical|amount,onlyTargetKinds" }; break;
                default: return false;
            }
            return ValidateExplicitCompositeShape(skill, fields);
        }

        private static bool ValidateExplicitCompositeShape(ImportedCombatSkill skill,
            string[] fields)
        {
            if (skill.Components.Count != fields.Length) return false;
            for (int i = 0; i < fields.Length; i++)
            {
                var parts = fields[i].Split('|');
                var component = skill.Components[i];
                if (component.Type != parts[0]) return false;
                var keys = parts[1].Split(',');
                if (component.Fields.Count != keys.Length + 1) return false;
                foreach (string key in keys)
                {
                    if (!component.Fields.ContainsKey(key)) return false;
                    if (key == "scope")
                    {
                        if (component.Text(key) != "singlePrimaryTarget" &&
                            !(skill.Id == "TR-20:skill:1" &&
                              component.Text(key) == "single")) return false;
                    }
                    else if (key == "appliesTo")
                    {
                        if (skill.Id != "TR-20:skill:3" ||
                            component.Text(key) != "삼도류 스킬") return false;
                    }
                    else if (key == "basis")
                    {
                        if (component.Text(key) != "targetCurrentHealth" &&
                            component.Text(key) != "targetMaxHealth" &&
                            !(skill.Id == "TR-19:skill:1" &&
                              component.Text(key) == "skillDamage") &&
                            !(skill.Id == "L-25:skill:1" &&
                              component.Text(key) == "basicAttackDamage") &&
                            !(skill.Id == "TR-04:skill:3" &&
                              component.Text(key) == "basicAttackDamage")) return false;
                    }
                    else if (key == "beforeAndAfterHit" ||
                             key == "ignoresResistance")
                    {
                        if (!Equals(component.Get(key), true)) return false;
                    }
                    else if (key == "multiplierRange")
                    {
                        if (!ValidSourceModelMultiplierRange(component.Get(key))) return false;
                    }
                    else if (key == "extraSingleMultiplierRange")
                    {
                        if (!ValidSourceModelMultiplierRange(component.Get(key)))
                            return false;
                    }
                    else if (key == "onlyTargetKinds")
                    {
                        var kinds = component.Get(key) as List<object>;
                        if (kinds == null || kinds.Count == 0) return false;
                        foreach (object kind in kinds)
                            if (!(kind is string) ||
                                ((string)kind != "line" && (string)kind != "boss" &&
                                 (string)kind != "enraged" && (string)kind != "story"))
                                return false;
                    }
                    else if (key == "targetKindMultiplier")
                    {
                        if (!ValidSourceModelTargetKindMultiplier(component.Get(key)))
                            return false;
                    }
                    else if (key == "bonusPercentByTargetKind" ||
                             key == "extraMaxHealthPercentByTargetKind")
                    {
                        if (!ValidSourceModelSixthKindNumbers(component.Get(key)))
                            return false;
                    }
                    else if (key == "whenAnySkill")
                    {
                        var names = component.Get(key) as List<object>;
                        if (names == null || names.Count != 3 ||
                            !names.Contains("유성소바 킥") ||
                            !names.Contains("불꽃 스펙트럼") ||
                            !names.Contains("무등샷")) return false;
                    }
                    else if (key == "affectedBy")
                    {
                        var values = component.Get(key) as List<object>;
                        if (values == null || values.Count != 3 ||
                            !values.Contains("초월함 업그레이드") ||
                            !values.Contains("관통 초월함 업그레이드") ||
                            !values.Contains("버프")) return false;
                    }
                    else if (key == "unit")
                    {
                        if (component.Text(key) != "코비 분신") return false;
                    }
                    else if (key == "inheritsBaseAttack" ||
                             key == "legendaryUpgradeAffected")
                    {
                        if (!Equals(component.Get(key), true)) return false;
                    }
                    else if (!FinitePositive(component.Number(key))) return false;
                    if ((key == "hits" || key == "stacks" || key == "maxStacks" ||
                         key == "max" || key == "maxArmorReduction") &&
                        (component.Number(key).Value > 64 && key == "hits" ||
                         component.Number(key).Value > int.MaxValue ||
                         Math.Floor(component.Number(key).Value) !=
                         component.Number(key).Value)) return false;
                    if (key == "chancePercent" && component.Number(key).Value > 100)
                        return false;
                }
            }
            return true;
        }

        private static bool ValidSourceModelSixthKindNumbers(object value)
        {
            var factors = value as Dictionary<string, object>;
            if (factors == null || factors.Count == 0) return false;
            foreach (var pair in factors)
                if ((pair.Key != "boss" && pair.Key != "enraged" &&
                     pair.Key != "story") || !(pair.Value is double) ||
                    !FinitePositive((double)pair.Value)) return false;
            return true;
        }

        private static bool CanModelSecondCompositeComponents(ImportedCombatSkill skill)
        {
            string[] types;
            switch (skill.Id)
            {
                case "E-09:skill:0": types = new[] { "magical", "magicResistanceReduction", "explosiveDamageAmplification", "selfHeal" }; break;
                case "SR-03:skill:0": types = new[] { "physical", "armorBreak", "fixed" }; break;
                case "TR-08:skill:0": types = new[] { "magical", "stun", "armorBreak" }; break;
                case "TR-22:skill:0": types = new[] { "physical", "stun", "armorBreak", "armorReduction" }; break;
                case "TR-29:skill:0": types = new[] { "magical", "movementSlow", "armorBreak" }; break;
                case "LI-06:skill:0": types = new[] { "magical", "stun", "armorBreak", "allyAttackPowerBuff" }; break;
                case "MY-12:skill:1": types = new[] { "magical", "physical", "armorBreak" }; break;
                case "MY-12:skill:3": types = new[] { "magical", "physical", "armorBreak" }; break;
                case "H-19:skill:0": types = new[] { "magical", "magicDamageAmplification" }; break;
                case "LI-02:skill:1": types = new[] { "physical", "movementSlow" }; break;
                case "RN-04:skill:0": types = new[] { "physical", "armorReduction", "magicDamageAmplification" }; break;
                default: return false;
            }
            if (skill.Components.Count != types.Length) return false;
            for (int i = 0; i < types.Length; i++)
            {
                var effect = skill.Components[i];
                if (effect.Type != types[i]) return false;
                if (effect.Type == "physical" || effect.Type == "magical" ||
                    effect.Type == "fixed")
                {
                    if (ValidDamage(effect)) continue;
                    if (skill.Id == "SR-03:skill:0" && effect.Type == "fixed" &&
                        effect.Text("basis") == "skillDamage" &&
                        FinitePositive(effect.Number("percent")) &&
                        effect.Text("scope") == "singlePrimaryTarget" &&
                        effect.Fields.Count == 4) continue;
                    if (skill.Id == "LI-06:skill:0" && effect.Type == "magical" &&
                        FinitePositive(effect.Number("amount")) &&
                        FinitePositive(effect.Number("plusTargetMissingHealthPercentPhysical")) &&
                        effect.Fields.Count == 3) continue;
                    if (skill.Id == "LI-02:skill:1" && effect.Type == "physical" &&
                        FinitePositive(effect.Number("amount")) &&
                        FinitePositive(effect.Number("plusLegacyLevelDamage")) &&
                        effect.Fields.Count == 3) continue;
                    return false;
                }
                if (effect.Type == "stun")
                {
                    if (!FinitePositive(effect.Number("durationSeconds")) ||
                        effect.Fields.Count != 2) return false;
                    continue;
                }
                if (effect.Type == "armorBreak")
                {
                    if (!FinitePositive(effect.Number("stacks")) ||
                        effect.Number("stacks").Value > int.MaxValue ||
                        Math.Floor(effect.Number("stacks").Value) != effect.Number("stacks").Value)
                        return false;
                    if (effect.Fields.Count == 2) continue;
                    if (effect.Fields.Count == 3 &&
                        FinitePositive(effect.Number("maxStacks")) &&
                        effect.Number("maxStacks").Value <= int.MaxValue &&
                        Math.Floor(effect.Number("maxStacks").Value) == effect.Number("maxStacks").Value)
                        continue;
                    return false;
                }
                if (effect.Type == "selfHeal")
                {
                    if (effect.Fields.Count != 2 ||
                        !FinitePositive(effect.Number("amount"))) return false;
                    continue;
                }
                if (effect.Type == "allyAttackPowerBuff")
                {
                    if (effect.Fields.Count != 3 ||
                        !FinitePositive(effect.Number("percent")) ||
                        !FinitePositive(effect.Number("durationSeconds"))) return false;
                    continue;
                }
                if (CommonSkillStatusEffects.IsTimedStatus(effect.Type))
                {
                    CombatEffectType kind;
                    double amount, duration;
                    if (CommonSkillStatusEffects.TryRead(effect, out kind,
                        out amount, out duration)) continue;
                    string field = effect.Type == "armorReduction" ? "amount" : "percent";
                    if (!FinitePositive(effect.Number(field))) return false;
                    if (skill.Id == "TR-22:skill:0" &&
                        effect.Type == "armorReduction" &&
                        effect.Fields.Count == 5 &&
                        effect.Number("requiredArmorBreakStacks") == 10 &&
                        effect.Get("oncePerEnemy") is bool &&
                        (bool)effect.Get("oncePerEnemy") &&
                        effect.Get("exclusiveWith") is List<object> &&
                        ((List<object>)effect.Get("exclusiveWith")).Count == 1 &&
                        (string)((List<object>)effect.Get("exclusiveWith"))[0] == "징베 전설")
                        continue;
                    if (skill.Id == "RN-04:skill:0" &&
                        effect.Type == "armorReduction" &&
                        effect.Text("scope") == "singlePrimaryTarget" &&
                        effect.Fields.Count == 3) continue;
                    if (effect.Fields.Count == 2) continue;
                    return false;
                }
                return false;
            }
            return true;
        }

        // Explicitly reviewed tooltip models for the first composite batch.
        // The ordinary approval gate remains closed to unrelated incomplete
        // packets; each of these still needs the owning-unit allow-list.
        public static bool IsFirstCompositeActive(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "MY-08:skill:1": return skill.Name == "로이드 포저";
                case "I-04:skill:0": return skill.Name == "광역 혼합 피해";
                case "TR-10:skill:0": return skill.Name == "마나 광역 공격";
                case "I-09:skill:0": return skill.Name == "마나 광역 스턴";
                case "LI-07:skill:0": return skill.Name == "뇌영";
                case "L-28:skill:0": return skill.Name == "어인가라데";
                case "RN-13:skill:1": return skill.Name == "슈퍼 페이몬 선풍";
                case "H-04:skill:0": return skill.Name == "거대 숲 물리";
                case "L-25:skill:0": return skill.Name == "미티어 울두건";
                case "D-03:skill:0": return skill.Name == "브라키오 붐버";
                default: return false;
            }
        }

        private static bool CanModelFirstCompositeComponents(ImportedCombatSkill skill)
        {
            string[] expected;
            switch (skill.Id)
            {
                case "MY-08:skill:1": expected = new[] { "magical", "movementSlow" }; break;
                case "I-04:skill:0": expected = new[] { "magical", "physical", "movementSlow" }; break;
                case "TR-10:skill:0": expected = new[] { "magical", "stun", "magicDamageAmplification" }; break;
                case "I-09:skill:0": expected = new[] { "magical", "stun", "magicDamageAmplification" }; break;
                case "LI-07:skill:0": expected = new[] { "magical", "magical", "magicResistanceReduction", "stun" }; break;
                case "L-28:skill:0": expected = new[] { "magical", "magical", "armorReduction" }; break;
                case "RN-13:skill:1": expected = new[] { "magical", "magicDamageAmplification" }; break;
                case "H-04:skill:0": expected = new[] { "physical", "armorReduction" }; break;
                case "L-25:skill:0": expected = new[] { "fixed", "armorBreak" }; break;
                case "D-03:skill:0": expected = new[] { "physical", "magical", "armorBreak" }; break;
                default: return false;
            }
            if (skill.Components.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                var effect = skill.Components[i];
                if (effect.Type != expected[i]) return false;
                if (effect.Type == "physical" || effect.Type == "magical" ||
                    effect.Type == "fixed")
                {
                    if (ValidDamage(effect)) continue;
                    if (skill.Id == "H-04:skill:0" && effect.Type == "physical" &&
                        FinitePositive(effect.Number("amount")) &&
                        FinitePositive(effect.Number("extraStoryFixedPercentOfSkill")) &&
                        ValidSourceModelMultiplierRange(effect.Get("multiplierRange")) &&
                        effect.Fields.Count == 4) continue;
                    if (skill.Id == "L-25:skill:0" && effect.Type == "fixed" &&
                        effect.Text("basis") == "basicAttackDamage" &&
                        FinitePositive(effect.Number("percent")) &&
                        FinitePositive(effect.Number("flatAmount")) &&
                        ValidSourceModelTargetKindMultiplier(
                            effect.Get("targetKindMultiplier")) &&
                        effect.Fields.Count == 5) continue;
                    return false;
                }
                if (effect.Type == "stun")
                {
                    if (!FinitePositive(effect.Number("durationSeconds")) ||
                        effect.Fields.Count != 2) return false;
                    continue;
                }
                if (effect.Type == "armorBreak")
                {
                    if (!FinitePositive(effect.Number("stacks")) ||
                        Math.Floor(effect.Number("stacks").Value) !=
                        effect.Number("stacks").Value) return false;
                    if (skill.Id == "L-25:skill:0" &&
                        FinitePositive(effect.Number("maxArmorReduction")) &&
                        Math.Floor(effect.Number("maxArmorReduction").Value) ==
                            effect.Number("maxArmorReduction").Value &&
                        effect.Fields.Count == 3) continue;
                    if (skill.Id == "D-03:skill:0" &&
                        FinitePositive(effect.Number("max")) &&
                        Math.Floor(effect.Number("max").Value) ==
                            effect.Number("max").Value &&
                        effect.Fields.Count == 3) continue;
                    return false;
                }
                if (CommonSkillStatusEffects.IsTimedStatus(effect.Type))
                {
                    CombatEffectType kind;
                    double amount, duration;
                    if (CommonSkillStatusEffects.TryRead(effect, out kind,
                        out amount, out duration)) continue;
                    // No duration in the imported tooltip: the mobile model
                    // keeps one effect per caster/skill/target until that
                    // enemy dies. No invented number of seconds is attached.
                    if ((effect.Type == "magicResistanceReduction" ||
                         effect.Type == "magicDamageAmplification") &&
                        FinitePositive(effect.Number("percent")) &&
                        (effect.Fields.Count == 2 ||
                         (effect.Fields.Count == 3 &&
                          effect.Text("target") == "hitEnemies"))) continue;
                    if (skill.Id == "L-28:skill:0" &&
                        effect.Type == "armorReduction" &&
                        FinitePositive(effect.Number("amount")) &&
                        ValidJinbeArmorReductionCondition(effect) &&
                        effect.Fields.Count == 5) continue;
                    return false;
                }
                return false;
            }
            return true;
        }

        private static bool ValidSourceModelMultiplierRange(object value)
        {
            var range = value as List<object>;
            if (range == null || range.Count != 2 ||
                !(range[0] is double) || !(range[1] is double)) return false;
            double minimum = (double)range[0], maximum = (double)range[1];
            return minimum > 0 && maximum >= minimum &&
                !double.IsNaN(maximum) && !double.IsInfinity(maximum);
        }

        private static bool ValidSourceModelTargetKindMultiplier(object value)
        {
            var factors = value as Dictionary<string, object>;
            if (factors == null || factors.Count != 2 ||
                !factors.ContainsKey("boss") || !factors.ContainsKey("story")) return false;
            foreach (var pair in factors)
                if (!(pair.Value is double) || (double)pair.Value <= 0 ||
                    double.IsNaN((double)pair.Value) ||
                    double.IsInfinity((double)pair.Value)) return false;
            return true;
        }

        private static bool ValidJinbeArmorReductionCondition(ImportedCombatEffect effect)
        {
            var condition = effect.Get("condition") as Dictionary<string, object>;
            var exclusion = effect.Get("exclusiveWith") as List<object>;
            object minimum;
            if (condition == null || condition.Count != 1 ||
                !condition.TryGetValue("requiredArmorBreakStacks", out minimum) ||
                !(minimum is double) || (double)minimum < 1 ||
                Math.Floor((double)minimum) != (double)minimum ||
                (double)minimum > int.MaxValue ||
                !(effect.Get("onlyOncePerEnemy") is bool) ||
                !(bool)effect.Get("onlyOncePerEnemy") ||
                exclusion == null || exclusion.Count != 1 ||
                !(exclusion[0] is string) || (string)exclusion[0] != "징베 초월")
                return false;
            return true;
        }

        private static bool CanModelComponents(ImportedCombatSkill skill)
        {
            foreach (var effect in skill.Components)
            {
                if (effect.Type == "physical" || effect.Type == "magical" ||
                    effect.Type == "fixed")
                {
                    if (!ValidDamage(effect)) return false;
                }
                else if (effect.Type == "stun")
                {
                    double? duration = effect.Number("durationSeconds");
                    if (!duration.HasValue || duration.Value <= 0 ||
                        double.IsNaN(duration.Value) || double.IsInfinity(duration.Value)) return false;
                    foreach (string key in effect.Fields.Keys)
                        if (key != "type" && key != "durationSeconds" &&
                            key != "scope" && key != "ignoresResistance") return false;
                    string scope = effect.Text("scope");
                    if (scope != null && scope != "singlePrimaryTarget") return false;
                    object resistance = effect.Get("ignoresResistance");
                    if (resistance != null && !(resistance is bool)) return false;
                }
                else return false;
            }
            return true;
        }
    }
}
