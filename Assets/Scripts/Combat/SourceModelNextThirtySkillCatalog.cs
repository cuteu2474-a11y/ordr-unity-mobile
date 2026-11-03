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
            new SourceModelNextThirtyUnit("L-20", "h02V", "화이트블로우"),
            new SourceModelNextThirtyUnit("L-25", "h03S",
                Array.Empty<string>(), new[] { "미티어 울두건" }),
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
                new[] { "범위 마법 스턴", "혼합 타격" },
                new[] { "체력 소모 광역 공격" }),
            new SourceModelNextThirtyUnit("TR-09", "H094",
                new[] { "광역 3연타" }, new[] { "마나 광역 공격" }),
            new SourceModelNextThirtyUnit("TR-10", "H09I",
                new[] { "타격 스턴" }, new[] { "마나 광역 공격" }),
            new SourceModelNextThirtyUnit("TR-14", "H0B2",
                Array.Empty<string>(), new[] { "킹코브라" }),
            new SourceModelNextThirtyUnit("TR-18", "H0BD", "스턴 물리 타격"),
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
            if (skill == null ||
                !(skill.Trigger == "onAttack" || skill.Trigger == "onEveryAttack") ||
                !(skill.Target == "area" || skill.Target == "single" ||
                  skill.Target == "casterArea") ||
                ((skill.Target == "area" || skill.Target == "casterArea") &&
                 !FinitePositive(skill.Radius)) ||
                (skill.Trigger == "onAttack" &&
                 (!FinitePositive(skill.ChancePercent) || skill.ChancePercent.Value > 100)) ||
                (skill.Trigger == "onEveryAttack" && skill.ChancePercent.HasValue) ||
                skill.Components.Count == 0) return false;
            foreach (string key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "chancePercent" &&
                    key != "target" && key != "radius" && key != "components")
                    return false;
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
            bool composite = IsFirstCompositeActive(skill);
            bool second = IsSecondCompositeSkill(skill);
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
                      key == "singleTargetMagicalDamageMultiplier"))
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
                        if (component.Text(key) != "singlePrimaryTarget") return false;
                    }
                    else if (key == "basis")
                    {
                        if (component.Text(key) != "targetCurrentHealth" &&
                            component.Text(key) != "targetMaxHealth") return false;
                    }
                    else if (key == "multiplierRange")
                    {
                        if (!ValidSourceModelMultiplierRange(component.Get(key))) return false;
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
                    else if (!FinitePositive(component.Number(key))) return false;
                    if ((key == "hits" || key == "stacks" || key == "maxStacks") &&
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
