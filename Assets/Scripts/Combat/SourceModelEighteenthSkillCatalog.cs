using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Explicit exception to the generic tooltip gate for seven complete
    // source-tooltip packets. Names, target shapes, chance, radius and every
    // nested field must match before a practice attack can be emitted.
    // This does not imply original Warcraft callback/timing parity.
    public static class SourceModelEighteenthSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "H-10:skill:1": case "RN-10:skill:0":
                case "R-42:skill:1": case "R-25:skill:1":
                case "D-05:skill:0": case "MY-10:skill:2":
                case "MY-10:skill:3": return true;
                default: return false;
            }
        }

        private static bool Same(object actual, object expected)
        {
            if (expected is double) return actual is double && (double)actual == (double)expected;
            if (expected is string) return actual is string && (string)actual == (string)expected;
            var expectedList = expected as object[];
            if (expectedList == null) return false;
            var actualList = actual as List<object>;
            if (actualList == null || actualList.Count != expectedList.Length) return false;
            for (int index = 0; index < expectedList.Length; index++)
                if (!Same(actualList[index], expectedList[index])) return false;
            return true;
        }

        private static bool Component(ImportedCombatSkill skill, int index,
            string type, params object[] fields)
        {
            if (fields.Length % 2 != 0 || index >= skill.Components.Count) return false;
            var part = skill.Components[index];
            if (part.Type != type || part.Fields.Count != fields.Length / 2 + 1)
                return false;
            for (int at = 0; at < fields.Length; at += 2)
            {
                object actual;
                if (!part.Fields.TryGetValue((string)fields[at], out actual) ||
                    !Same(actual, fields[at + 1])) return false;
            }
            return true;
        }

        private static bool Header(ImportedCombatSkill skill, string name,
            string target, double chance, double? radius, int components)
        {
            return skill.Name == name && skill.Trigger == "onAttack" &&
                skill.Target == target && skill.ChancePercent == chance &&
                skill.Radius == radius && skill.Components.Count == components &&
                skill.OriginalFields.Count == (radius.HasValue ? 6 : 5);
        }

        public static bool CanModelExactSpecial(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "H-10:skill:1":
                    return Header(skill, "기본 공격 발동형 타격", "targetCircle", 20, 400, 2) &&
                        Component(skill, 0, "physical", "amount", 240000d) &&
                        Component(skill, 1, "armorBreak", "stacks", 1d);
                case "RN-10:skill:0":
                    return Header(skill, "무기화 빌드 Lv1", "area", 15, 400, 1) &&
                        Component(skill, 0, "magical", "amount", 250000d,
                            "multiplierRange", new object[] { 1d, 2d },
                            "extraSingleDamagePercent", 100d);
                case "R-42:skill:1":
                    return Header(skill, "매혹 계열 범위 공격", "targetCircle", 10, 350, 2) &&
                        Component(skill, 0, "magical", "amount", 6000d) &&
                        Component(skill, 1, "explosive", "basis", "targetCurrentHealth",
                            "percent", 10d, "onlyTargetKinds", new object[] { "line" },
                            "scope", "singlePrimaryTarget");
                case "R-25:skill:1":
                    return Header(skill, "현재 체력 폭발", "single", 10, null, 1) &&
                        Component(skill, 0, "explosive", "targetCurrentHealthPercent", 15d,
                            "onlyTargetKinds", new object[] { "line" });
                case "D-05:skill:0":
                    return Header(skill, "불주먹", "line", 10, 320, 1) &&
                        Component(skill, 0, "magical", "amount", 350000d,
                            "extraPrimaryTargetAmount", 500000d);
                case "MY-10:skill:2":
                    return Header(skill, "단령검 성불득탈참", "line", 10, 600, 2) &&
                        Component(skill, 0, "magical", "amount", 250000d) &&
                        Component(skill, 1, "movementSlow", "percent", 50d,
                            "durationSeconds", 3d);
                case "MY-10:skill:3":
                    return Header(skill, "단미검 미진자항참", "line", 10, 800, 2) &&
                        Component(skill, 0, "physical", "basicAttackDamagePercent", 800d,
                            "flatAmount", 900000d) &&
                        Component(skill, 1, "fixed", "targetMaxHealthPercent", 4d,
                            "exceptTargetKinds", new object[] { "story" },
                            "scope", "singlePrimaryTarget");
            }
            return false;
        }
    }
}
