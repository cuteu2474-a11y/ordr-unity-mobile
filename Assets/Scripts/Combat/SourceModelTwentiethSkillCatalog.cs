using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // S2 2.323[C] tooltip-only practice models. Exact-source field checks
    // prevent partially understood upgrades from silently changing combat.
    public static class SourceModelTwentiethSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill) =>
            skill != null && (skill.Id.StartsWith("E-03:skill:", StringComparison.Ordinal) ||
                skill.Id.StartsWith("E-11:skill:", StringComparison.Ordinal) ||
                skill.Id.StartsWith("I-05:skill:", StringComparison.Ordinal));

        private static bool Same(object value, object expected)
        {
            if (expected is double) return value is double && (double)value == (double)expected;
            if (expected is string) return value is string && (string)value == (string)expected;
            if (expected is bool) return value is bool && (bool)value == (bool)expected;
            var expectedMap = expected as Dictionary<string, object>;
            if (expectedMap != null)
            {
                var actualMap = value as Dictionary<string, object>;
                if (actualMap == null || actualMap.Count != expectedMap.Count) return false;
                foreach (var pair in expectedMap)
                {
                    object item;
                    if (!actualMap.TryGetValue(pair.Key, out item) ||
                        !Same(item, pair.Value)) return false;
                }
                return true;
            }
            var values = value as List<object>;
            var pattern = expected as object[];
            if (values == null || pattern == null || values.Count != pattern.Length) return false;
            for (int i = 0; i < values.Count; i++)
                if (!Same(values[i], pattern[i])) return false;
            return true;
        }

        private static bool Header(ImportedCombatSkill skill, string name,
            string trigger, string target, double? chance, double? radius,
            int parts, params object[] extra)
        {
            if (extra.Length % 2 != 0 || skill.Name != name ||
                skill.Trigger != trigger || skill.Target != target ||
                skill.ChancePercent != chance || skill.Radius != radius ||
                skill.Components.Count != parts ||
                skill.OriginalFields.Count != 3 + (target == null ? 0 : 1) +
                    (chance.HasValue ? 1 : 0) + (radius.HasValue ? 1 : 0) +
                    extra.Length / 2) return false;
            for (int i = 0; i < extra.Length; i += 2)
            {
                object actual;
                if (!skill.OriginalFields.TryGetValue((string)extra[i], out actual) ||
                    !Same(actual, extra[i + 1])) return false;
            }
            return true;
        }

        private static bool Part(ImportedCombatSkill skill, int index, string type,
            params object[] extra)
        {
            if (extra.Length % 2 != 0 || index >= skill.Components.Count) return false;
            var component = skill.Components[index];
            if (component.Type != type || component.Fields.Count != extra.Length / 2 + 1)
                return false;
            for (int i = 0; i < extra.Length; i += 2)
            {
                object actual;
                if (!component.Fields.TryGetValue((string)extra[i], out actual) ||
                    !Same(actual, extra[i + 1])) return false;
            }
            return true;
        }

        public static bool CanModelExact(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "E-03:skill:0":
                    return Header(skill, "마나 공격", "activeMana", "area", null, 500, 1,
                        "manaCost", 150d) &&
                        Part(skill, 0, "magical", "amount", 2500000d,
                            "maxHealthPercentPerSoulStack", .005d);
                case "E-03:skill:1":
                    return Header(skill, "영혼 고정 타격", "onAttack", "area", 25, 300, 1) &&
                        Part(skill, 0, "fixed", "amountPerSoulStack", 160d,
                            "flatAmount", 160000d);
                case "E-03:skill:2":
                    return Header(skill, "기본 피해량 조건 범위 공격",
                        "afterBasicDamageThreshold", "area", null, 600, 1,
                        "minimumBasicDamage", 120000d) &&
                        Part(skill, 0, "magical", "amount", 180000d);
                case "E-03:skill:3":
                    return Header(skill, "영혼 마법 타격", "onAttack", "area", 15, 500, 2) &&
                        Part(skill, 0, "magical", "amount", 400000d,
                            "plusSoulStackMultiplier", 450d) &&
                        Part(skill, 1, "soulStacks", "chancePercent", 20d,
                            "add", 2d, "absoluteCooldownSeconds", 5d);
                case "E-03:skill:4":
                    return Header(skill, "혼 스택 마법 타격", "onAttack", "area", 12, 400, 2) &&
                        Part(skill, 0, "magical", "amount", 250000d,
                            "plusSoulStackMultiplier", 2500000d) &&
                        Part(skill, 1, "explosive", "plusTargetMissingHealthPercent", 9d,
                            "onlyTargetKinds", new object[] { "line" });
                case "E-03:skill:5":
                    return Header(skill, "영혼 획득", "onAttack", null, 10, null, 1) &&
                        Part(skill, 0, "soulStacks", "add", 1d);
                case "E-03:skill:6":
                    return Header(skill, "참룡도", "onAttack", "single", 8.5, null, 2,
                        "requiresSoulStacks", 100d) &&
                        Part(skill, 0, "critical", "multiplier", 12d) &&
                        Part(skill, 1, "stun", "durationSeconds", 3d,
                            "ignoresResistance", true);
                case "E-11:skill:0":
                    return Header(skill, "골드 연계 체력 소모 공격", "activeHealth", "area",
                        null, 625, 3, "healthCost", 90d) &&
                        Part(skill, 0, "magical", "amount", 2500000d,
                            "multiplierRange", new object[] { 1d, 1.5d }) &&
                        Part(skill, 1, "stun", "durationSeconds", 2.4d) &&
                        Part(skill, 2, "explosive", "basis", "targetMissingHealth",
                            "percentPer25000Gold", .9d, "goldCap", 250000d);
                case "E-11:skill:1":
                    return Header(skill, "공격 발동 마법 타격", "onAttack", "area",
                        12, 625, 3) &&
                        Part(skill, 0, "magical", "amount", 200000d,
                            "multiplierRange", new object[] { 1d, 1.5d }) &&
                        Part(skill, 1, "stun", "durationSeconds", 2.4d) &&
                        Part(skill, 2, "fixed", "basis", "playerGold",
                            "multiplier", 2d, "goldCap", 250000d);
                case "E-11:skill:2":
                    return Header(skill, "곤 인페르노", "periodic", "area",
                        null, 325, 3, "intervalSeconds", 1.5d) &&
                        Part(skill, 0, "magical", "amount", 200000d,
                            "multiplierRange", new object[] { 1d, 1.5d }) &&
                        Part(skill, 1, "explosive", "percent", 15d,
                            "basis", "targetMaxHealth", "onlyTargetKinds",
                            new object[] { "line" }, "scope", "singlePrimaryTarget") &&
                        Part(skill, 2, "fixed", "amount", 400000d,
                            "onlyTargetKinds", new object[] { "boss", "enraged", "story" },
                            "scope", "singlePrimaryTarget");
                case "E-11:skill:3":
                    return Header(skill, "곤 인페르노 2회 누적 발동", "counter", "area",
                        null, 425, 3, "afterSkill", "곤 인페르노",
                        "requiredOccurrences", 2d) &&
                        Part(skill, 0, "magical", "amount", 500000d,
                            "multiplierRange", new object[] { 1d, 1.5d }) &&
                        Part(skill, 1, "explosive", "basis", "targetMissingHealth",
                            "percentPer25000Gold", .45d,
                            "onlyTargetKinds", new object[] { "line", "boss", "story", "enraged" },
                            "goldCap", 250000d) &&
                        Part(skill, 2, "explosive", "basis", "targetMaxHealth",
                            "percent", 20d, "onlyTargetKinds", new object[] { "line" },
                            "scope", "singlePrimaryTarget");
                case "I-05:skill:0":
                    return Header(skill, "데어 슈테르크슈테 스트라이크", "activeMana",
                        "area", null, 600, 2, "manaCost", 150d,
                        "uniqueAllItemsBonus", SkillDict("magicalDoTPerTick", 50000d,
                            "tickSeconds", .1d, "durationSeconds", 3d),
                        "legendaryAllItemsBonus", SkillDict("extraTargetMaxHealthPercent", 10d,
                            "damageSchool", "physical", "stunSeconds", 3d)) &&
                        Part(skill, 0, "physical", "firstHitBase", 1500000d,
                            "firstHitMultiplierByAttackItemStacks", .05d,
                            "repeatedDamageBase", 100000d, "repeatedHits", 12d,
                            "repeatedMultiplierByAttackItemStacks", .01d) &&
                        Part(skill, 1, "stun", "durationSeconds", 1d);
                case "I-05:skill:1":
                    return Header(skill, "패왕색 패기", "onAttack", "casterArea", 5,
                        400, 1, "exceptTargetKinds", new object[] { "boss", "story" },
                        "uniqueItemBonus", SkillDict("physicalAmount", 250000d,
                            "currentHealthPercentAgainstBossStory", 1d),
                        "legendaryItemBonus", SkillDict("chanceToRepeatPercent", 50d)) &&
                        Part(skill, 0, "physical", "amount", 250000d,
                            "plusTargetCurrentHealthPercent", 2d,
                            "damageBonusPercentPerAttackItemStack", 1d);
                case "I-05:skill:3":
                    return Header(skill, "괴물의 후계자", "onEveryAttack", "area", null,
                        400, 3, "uniqueArmorItemBonus",
                        SkillDict("movementSlowAuraPercent", 20d),
                        "legendaryArmorItemBonus", SkillDict("whenBasicDamageAtLeast", 600000d,
                            "fixedDamagePercentOfBasic", 50d)) &&
                        Part(skill, 0, "physical", "basicAttackPercent", 100d) &&
                        Part(skill, 1, "critical", "multiplier", 4d, "chancePercent", 15d) &&
                        Part(skill, 2, "stun", "durationSeconds", 2d, "chancePercent", 15d);
            }
            return false;
        }

        private static Dictionary<string, object> SkillDict(params object[] fields)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i += 2)
                result.Add((string)fields[i], fields[i + 1]);
            return result;
        }

        public static bool CanModelAttack(ImportedCombatSkill skill) =>
            CanModelExact(skill) && (skill.Trigger == "onAttack" ||
                skill.Trigger == "onEveryAttack");

        public static bool CanModelActive(ImportedCombatSkill skill) =>
            CanModelExact(skill) && (skill.Trigger == "activeMana" ||
                skill.Trigger == "activeHealth");

        public static bool HasExactPassive(CharacterSkillProfile profile, string id)
        {
            if (profile == null || profile.UnitId != id) return false;
            if (id == "E-03")
            {
                if (profile.Passives.Count != 1) return false;
                var soul = profile.Passives[0];
                if (soul.Type != "samuraiSoulStacks" || soul.Fields.Count != 2) return false;
                var thresholds = soul.Get("thresholds") as List<object>;
                if (thresholds == null || thresholds.Count != 3) return false;
                var first = thresholds[0] as Dictionary<string, object>;
                var second = thresholds[1] as Dictionary<string, object>;
                var third = thresholds[2] as Dictionary<string, object>;
                return first != null && first.Count == 2 &&
                    first.ContainsKey("stacks") && first.ContainsKey("unlockSkill") &&
                    Same(first["stacks"], 100d) && Same(first["unlockSkill"], "참룡도") &&
                    second != null && second.Count == 2 &&
                    second.ContainsKey("stacks") &&
                    second.ContainsKey("attackSpeedIncreasePercent") &&
                    Same(second["stacks"], 400d) &&
                    Same(second["attackSpeedIncreasePercent"], 50d) &&
                    third != null && third.Count == 2 &&
                    third.ContainsKey("stacks") &&
                    third.ContainsKey("attackPowerIncrease") &&
                    Same(third["stacks"], 700d) &&
                    Same(third["attackPowerIncrease"], 100000d);
            }
            if (id == "E-11")
                return profile.Passives.Count == 2 &&
                    profile.Passives[0].Type == "attackSpeedBuff" &&
                    profile.Passives[0].Fields.Count == 2 &&
                    profile.Passives[0].Number("percent") == 25 &&
                    profile.Passives[1].Type == "goldBasedSkillBonus" &&
                    profile.Passives[1].Fields.Count == 2 &&
                    profile.Passives[1].Number("goldCap") == 250000;
            return id == "I-05" && profile.Passives.Count == 0;
        }
    }
}
