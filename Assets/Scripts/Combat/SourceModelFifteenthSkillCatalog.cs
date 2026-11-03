using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Batch 15: exact original-tooltip packets requiring an extension to the
    // shared damage/target/resource executor. The other nine approved skills
    // pass the existing generic complete-packet gate unchanged.
    public static class SourceModelFifteenthSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "TR-03:skill:1": case "TR-27:skill:2":
                case "T-01:skill:0": case "LI-05:skill:1":
                case "L-18:skill:0": case "H-21:skill:0":
                case "RN-14:skill:0": case "I-12:skill:0":
                case "H-14:skill:0": case "H-20:skill:0":
                case "I-08:skill:0": return true;
                default: return false;
            }
        }

        private static bool SameValue(object actual, object expected)
        {
            if (expected is double) return actual is double && (double)actual == (double)expected;
            if (expected is string) return actual is string && (string)actual == (string)expected;
            if (expected is bool) return actual is bool && (bool)actual == (bool)expected;
            var expectedArray = expected as object[];
            if (expectedArray != null)
            {
                var list = actual as List<object>;
                if (list == null || list.Count != expectedArray.Length) return false;
                for (int i = 0; i < list.Count; i++)
                    if (!SameValue(list[i], expectedArray[i])) return false;
                return true;
            }
            var expectedDict = expected as Dictionary<string, object>;
            var actualDict = actual as Dictionary<string, object>;
            if (expectedDict != null && actualDict != null &&
                expectedDict.Count == actualDict.Count)
            {
                foreach (var pair in expectedDict)
                {
                    object found;
                    if (!actualDict.TryGetValue(pair.Key, out found) ||
                        !SameValue(found, pair.Value)) return false;
                }
                return true;
            }
            return false;
        }

        private static bool Effect(ImportedCombatSkill skill, int index,
            string type, params object[] pairs)
        {
            if (index >= skill.Components.Count || pairs.Length % 2 != 0) return false;
            var effect = skill.Components[index];
            if (effect.Type != type || effect.Fields.Count != pairs.Length / 2 + 1)
                return false;
            for (int i = 0; i < pairs.Length; i += 2)
            {
                object actual;
                if (!effect.Fields.TryGetValue((string)pairs[i], out actual) ||
                    !SameValue(actual, pairs[i + 1])) return false;
            }
            return true;
        }

        private static bool Meta(ImportedCombatSkill skill, string name,
            string trigger, string target, double? chance, double? radius,
            double? cost = null, string costField = null, int extras = 0)
        {
            int keys = 4 + (chance.HasValue ? 1 : 0) +
                (radius.HasValue ? 1 : 0) + (costField == null ? 0 : 1) + extras;
            return skill.Name == name && skill.Trigger == trigger &&
                skill.Target == target && skill.ChancePercent == chance &&
                skill.Radius == radius && skill.OriginalFields.Count == keys &&
                (costField == null || skill.Number(costField) == cost);
        }

        // Every source field (including nested ratios and the boss override)
        // must be identical to the pinned 2.323[C] tooltip. A partially
        // imported profile cannot silently consume mana/health or deal damage.
        public static bool CanModelExactSpecial(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "TR-03:skill:1":
                    return Meta(skill, "범위 물리 타격", "onAttack", "area", 12.5, 525) &&
                        skill.Components.Count == 1 &&
                        Effect(skill, 0, "physical", "amount", 750000d,
                            "strengthScalingPerPoint", 10000d);
                case "TR-27:skill:2":
                    return Meta(skill, "육식 체", "onAttack", "area", 17, 450) &&
                        skill.Components.Count == 1 &&
                        Effect(skill, 0, "magical", "amount", 350000d,
                            "strengthScalingPerPoint", 2500d,
                            "basicAttackPercent", 600d);
                case "T-01:skill:0":
                    return Meta(skill, "오버히트", "onAttack", "single", 10, null) &&
                        skill.Components.Count == 1 &&
                        Effect(skill, 0, "magical", "amount", 200000d,
                            "plusTargetCurrentHealthPercentAgainstLine", 25d);
                case "LI-05:skill:1":
                    return Meta(skill, "2연타 마법", "onAttack", "area", 16, 475) &&
                        skill.Components.Count == 1 &&
                        Effect(skill, 0, "magical", "amountPerHit", 320000d,
                            "hits", 2d, "extraSingleMagicalAmount", 300000d,
                            "enragedExtraMultiplier", 10d);
                case "L-18:skill:0":
                    return Meta(skill, "사람사람열매", "onAttack", "area", 10, 600) &&
                        skill.Components.Count == 2 &&
                        Effect(skill, 0, "magical", "amount", 245000d) &&
                        Effect(skill, 1, "physical", "basis", "skillDamage",
                            "percent", 225d, "scope", "singlePrimaryTarget",
                            "exceptTargetKinds", new object[] { "line" });
                case "H-21:skill:0":
                    return Meta(skill, "돌격 연타", "onAttack", "single", 10, null,
                            null, null, 1) &&
                        SameValue(skill.Get("overrides"), new object[] {
                            new Dictionary<string, object> {
                                { "targetKinds", new object[] { "boss" } },
                                { "chancePercent", 40d }
                            }
                        }) && skill.Components.Count == 2 &&
                        Effect(skill, 0, "magical", "amount", 85000d,
                            "multiplierRange", new object[] { 1d, 3d }) &&
                        Effect(skill, 1, "magical", "extraBossCurrentHealthPercent", .9d,
                            "extraEnragedMaxHealthPercent", 5.75d);
                case "RN-14:skill:0":
                    return Meta(skill, "고양이 발톱", "onAttack", "area", 4, 500) &&
                        skill.Components.Count == 2 &&
                        Effect(skill, 0, "magical", "amount", 150000d) &&
                        Effect(skill, 1, "movementSlow", "percent", 70d,
                            "durationSeconds", 3d);
                case "I-12:skill:0":
                    return Meta(skill, "체력 소모 범위 공격", "activeHealth", "area",
                            null, 700, 115, "healthCost") && skill.Components.Count == 3 &&
                        Effect(skill, 0, "magical", "amount", 500000d, "hits", 1d) &&
                        Effect(skill, 1, "physical", "amountPerHit", 2750000d,
                            "hits", 2d, "multiplierRange", new object[] { 1d, 1.5d }) &&
                        Effect(skill, 2, "stun", "durationSeconds", 3d);
                case "H-14:skill:0":
                    return Meta(skill, "혈우", "activeMana", "area", null, 600,
                            100, "manaCost") && skill.Components.Count == 1 &&
                        Effect(skill, 0, "magical", "amount", 2250000d,
                            "plusTargetMaxHealthPercent", 1.75d,
                            "exceptTargetKinds", new object[] { "story" });
                case "H-20:skill:0":
                    return Meta(skill, "잔설검", "activeMana", "single", null, null,
                            35, "manaCost") && skill.Components.Count == 1 &&
                        Effect(skill, 0, "magical", "amount", 450000d,
                            "extraMaxHealthPercentByTargetKind", new Dictionary<string, object> {
                                { "line", 35d }, { "enraged", 35d }, { "boss", 7d }
                            });
                case "I-08:skill:0":
                    return Meta(skill, "전체체력 비례 고정피해", "activeMana", "single",
                            null, null, 45, "manaCost") && skill.Components.Count == 1 &&
                        Effect(skill, 0, "fixed", "basis", "targetMaxHealth",
                            "percentByTargetKind", new Dictionary<string, object> {
                                { "line", 50d }, { "boss", 10d }, { "story", 5d }
                            });
            }
            return false;
        }
    }
}
