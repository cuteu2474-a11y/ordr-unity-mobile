using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Batch 16: explicitly approved complex tooltip packets. Every field is
    // checked, including nested arrays and the original unit skill ordinal.
    // The three uncomplicated companion packets use the existing strict gate.
    public static class SourceModelSixteenthSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "LI-05:skill:2": case "E-05:skill:0": case "E-05:skill:2":
                case "D-02:skill:0": case "D-02:skill:1": case "D-02:skill:2":
                case "H-15:skill:1": case "I-01:skill:1": case "I-01:skill:3":
                case "LI-01:skill:2": case "LI-01:skill:3":
                case "T-02:skill:0": case "T-01:skill:1":
                case "MY-04:skill:0": case "MY-11:skill:3":
                case "RN-12:skill:1": case "E-10:skill:2":
                case "I-08:skill:3": return true;
                default: return false;
            }
        }

        private static bool Same(object actual, object expected)
        {
            if (expected is double) return actual is double && (double)actual == (double)expected;
            if (expected is string) return actual is string && (string)actual == (string)expected;
            if (expected is bool) return actual is bool && (bool)actual == (bool)expected;
            var array = expected as object[];
            if (array != null)
            {
                var actualArray = actual as List<object>;
                if (actualArray == null || array.Length != actualArray.Count) return false;
                for (int i = 0; i < array.Length; ++i)
                    if (!Same(actualArray[i], array[i])) return false;
                return true;
            }
            var dict = expected as Dictionary<string, object>;
            var actualDict = actual as Dictionary<string, object>;
            if (dict == null || actualDict == null || dict.Count != actualDict.Count)
                return false;
            foreach (var pair in dict)
            {
                object value;
                if (!actualDict.TryGetValue(pair.Key, out value) ||
                    !Same(value, pair.Value)) return false;
            }
            return true;
        }

        private static bool Component(ImportedCombatSkill skill, int at,
            string type, params object[] pairs)
        {
            if (at >= skill.Components.Count || pairs.Length % 2 != 0) return false;
            var part = skill.Components[at];
            if (part.Type != type || part.Fields.Count != pairs.Length / 2 + 1)
                return false;
            for (int k = 0; k < pairs.Length; k += 2)
            {
                object value;
                if (!part.Fields.TryGetValue((string)pairs[k], out value) ||
                    !Same(value, pairs[k + 1])) return false;
            }
            return true;
        }

        private static bool Header(ImportedCombatSkill skill, string name,
            string trigger, string target, double? chance, double? radius,
            double? cost = null, string costKey = null, int extraKeys = 0)
        {
            return skill.Name == name && skill.Trigger == trigger &&
                skill.Target == target && skill.ChancePercent == chance &&
                skill.Radius == radius &&
                skill.OriginalFields.Count == (target == null ? 3 : 4) +
                    (chance.HasValue ? 1 : 0) +
                    (radius.HasValue ? 1 : 0) + (costKey == null ? 0 : 1) + extraKeys &&
                (costKey == null || skill.Number(costKey) == cost);
        }

        public static bool CanModelExactSpecial(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "LI-05:skill:2":
                    return Header(skill, "3연타 마법", "onAttack", "area", 5, 500) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "magical", "amountPerHit", 240000d,
                            "hits", 3d, "extraSingleMagicalAmount", 300000d,
                            "enragedExtraMultiplier", 10d);
                case "E-05:skill:0":
                    return Header(skill, "버기 필살타", "onAttack", "single", 7.77, null) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "physical", "amount", 3333333d,
                            "criticalMultiplier", 7.77d) &&
                        Component(skill, 1, "stun", "durationSeconds", 7.77d,
                            "scope", "singlePrimaryTarget");
                case "E-05:skill:2":
                    return Header(skill, "전투 아군 공격력 버프", "onAttack", "allyArea", 4.5, 750) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "allyAttackPowerBuff", "percent", 33d,
                            "durationSeconds", 4.5d,
                            "exclusiveWith", new object[] { "코비 용기의 외침" });
                case "D-02:skill:0":
                    return Header(skill, "어인공수도", "activeMana", null, null, 625,
                            135, "manaCost") && skill.Components.Count == 3 &&
                        Component(skill, 0, "fixed", "amountPerHit", 600000d, "hits", 6d) &&
                        Component(skill, 1, "magical", "amount", 1000000d) &&
                        Component(skill, 2, "stun", "durationSeconds", 1.25d);
                case "D-02:skill:1":
                    return Header(skill, "기와 돌려차기", "onAttack", null, 15, 500) &&
                        skill.Components.Count == 3 &&
                        Component(skill, 0, "magical", "amount", 625000d) &&
                        Component(skill, 1, "fixed", "extraSkillDamagePercent", 100d,
                            "scope", "singlePrimaryTarget") &&
                        Component(skill, 2, "fixed", "extraTargetMaxHealthPercent", 2.5d,
                            "onlyTargetKinds", new object[] { "boss", "enraged" });
                case "D-02:skill:2":
                    return Header(skill, "단일 스턴", "onAttack", "single", 50, null) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "stun", "durationSeconds", .75d,
                            "ignoresResistance", true);
                case "H-15:skill:1":
                    return Header(skill, "일반 포격", "onAttack", "area", 10, 600,
                            null, null, 1) && skill.Get("requires") is string &&
                        (string)skill.Get("requires") == "noVegapunkKnowledge" &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "magical", "amount", 200000d,
                            "extraEnragedMaxHealthPercent", 45d) &&
                        Component(skill, 1, "stun", "durationSeconds", 1.2d);
                case "I-01:skill:1":
                    return Header(skill, "무장색 패기", "onAttack", "area", 10, 500) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "physical", "basicAttackPercent", 95d,
                            "flatAmount", 615000d) &&
                        Component(skill, 1, "stun", "durationSeconds", 2.5d);
                case "I-01:skill:3":
                    return Header(skill, "기본 공격 범위 추가", "onEveryAttack", "area", null, 325) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "physical", "basicAttackPercent", 95d);
                case "LI-01:skill:2":
                    return Header(skill, "단일 스턴", "onAttack", "single", 20, null,
                            null, null, 1) && skill.Components.Count == 2 &&
                        Same(skill.Get("overrides"), new object[] {
                            new Dictionary<string, object> {
                                { "targetKinds", new object[] { "boss" } },
                                { "chanceMultiplier", 2d }, { "damageMultiplier", 2d }
                            }
                        }) &&
                        Component(skill, 0, "physical", "amount", 425000d) &&
                        Component(skill, 1, "stun", "durationSeconds", 1.5d);
                case "LI-01:skill:3":
                    return Header(skill, "평타 단일 고정", "onEveryAttack", "single", null, null) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "fixed", "targetMaxHealthPercent", 2.15d) &&
                        Component(skill, 1, "magical", "amount", 142500d,
                            "onlyTargetKinds", new object[] { "boss", "story" });
                case "T-02:skill:0":
                    return Header(skill, "공작스트링", "onAttack", null, 10, 500) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "magical", "amount", 100000d) &&
                        Component(skill, 1, "physical", "amount", 250000d,
                            "multiplierRange", new object[] { .7d, 1.5d });
                case "T-01:skill:1":
                    return Header(skill, "실실열매", "onAttack", null, 10, 400) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "magical", "amount", 100000d);
                case "MY-04:skill:0":
                    return Header(skill, "비뢰신 이의 단 나선환", "onAttack", "area", 6.25, 600) &&
                        skill.Components.Count == 3 &&
                        Component(skill, 0, "magical", "amount", 1750000d) &&
                        Component(skill, 1, "magical", "singleTargetMaxHealthPercent", 25d,
                            "bossStoryPercent", 1.5d) &&
                        Component(skill, 2, "stun", "durationSeconds", 3d);
                case "MY-11:skill:3":
                    return Header(skill, "스타 플라티나", "onAttack", "area", 15, 400) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "physical", "amount", 850000d) &&
                        Component(skill, 1, "fixed", "extraSingleTargetPercent", 50d);
                case "RN-12:skill:1":
                    return Header(skill, "천쇄참월", "onAttack", "area", 20, 385) &&
                        skill.Components.Count == 2 &&
                        Component(skill, 0, "magical", "amount", 200000d) &&
                        Component(skill, 1, "fixed", "targetMaxHealthPercent", 1.5d,
                            "onlyTargetKinds", new object[] { "line" },
                            "scope", "singlePrimaryTarget");
                case "E-10:skill:2":
                    return Header(skill, "치명타 단일 스턴", "onAttack", "single", 20, null) &&
                        skill.Components.Count == 3 &&
                        Component(skill, 0, "critical", "multiplier", 10d) &&
                        Component(skill, 1, "physical", "amount", 400000d) &&
                        Component(skill, 2, "stun", "durationSeconds", 3d);
                case "I-08:skill:3":
                    return Header(skill, "기본 공격 추가 고정피해", "onAttack", "single", 55,
                            null, null, null, 1) &&
                        Same(skill.Get("overrides"), new object[] {
                            new Dictionary<string, object> {
                                { "targetKinds", new object[] { "story", "boss" } },
                                { "chancePercent", 13d }
                            }
                        }) &&
                        skill.Components.Count == 1 &&
                        Component(skill, 0, "fixed", "basis", "basicAttackDamage",
                            "percent", 525d);
            }
            return false;
        }
    }
}
