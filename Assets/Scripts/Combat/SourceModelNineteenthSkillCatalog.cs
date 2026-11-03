using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Source-tooltip practice packets not represented by an existing JASS
    // attack callback. Each individual field, including nested metadata,
    // must agree with the imported S2 2.323[C] profile before activation.
    public static class SourceModelNineteenthSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "L-06:skill:0": case "L-06:skill:1":
                case "SR-04:skill:2": case "T-04:skill:0":
                case "D-04:skill:1": case "MY-04:skill:2":
                case "T-05:skill:1": case "T-03:skill:1":
                case "SR-02:skill:1": case "SR-02:skill:2": return true;
                default: return false;
            }
        }

        private static bool Same(object actual, object expected)
        {
            if (expected is double)
                return actual is double && (double)actual == (double)expected;
            if (expected is string)
                return actual is string && (string)actual == (string)expected;
            if (expected is bool)
                return actual is bool && (bool)actual == (bool)expected;
            var expectedArray = expected as object[];
            var actualArray = actual as List<object>;
            if (expectedArray == null || actualArray == null ||
                expectedArray.Length != actualArray.Count) return false;
            for (int i = 0; i < expectedArray.Length; i++)
                if (!Same(actualArray[i], expectedArray[i])) return false;
            return true;
        }

        private static bool Header(ImportedCombatSkill skill, string name,
            string target, double chance, double? radius, int parts,
            params object[] extra)
        {
            if (extra.Length % 2 != 0 || skill.Name != name ||
                skill.Trigger != "onAttack" || skill.Target != target ||
                skill.ChancePercent != chance || skill.Radius != radius ||
                skill.Components.Count != parts ||
                skill.OriginalFields.Count != (target == null ? 4 : 5) +
                    (radius.HasValue ? 1 : 0) + extra.Length / 2) return false;
            for (int i = 0; i < extra.Length; i += 2)
            {
                object actual;
                if (!skill.OriginalFields.TryGetValue((string)extra[i], out actual) ||
                    !Same(actual, extra[i + 1])) return false;
            }
            return true;
        }

        private static bool Part(ImportedCombatSkill skill, int index,
            string type, params object[] fields)
        {
            if (fields.Length % 2 != 0 || index >= skill.Components.Count) return false;
            var part = skill.Components[index];
            if (part.Type != type || part.Fields.Count != fields.Length / 2 + 1)
                return false;
            for (int i = 0; i < fields.Length; i += 2)
            {
                object actual;
                if (!part.Fields.TryGetValue((string)fields[i], out actual) ||
                    !Same(actual, fields[i + 1])) return false;
            }
            return true;
        }

        public static bool CanModelExactSpecial(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "L-06:skill:0":
                    return Header(skill, "핑크호넷", "area", 5.5, 333, 2) &&
                        Part(skill, 0, "magical", "amountPerHit", 100000d, "hits", 3d) &&
                        Part(skill, 1, "explosive", "basis", "targetCurrentHealth",
                            "percentPerHit", 8.25d, "hits", 3d,
                            "onlyTargetKinds", new object[] { "line" });
                case "L-06:skill:1":
                    return Header(skill, "독구슬", "area", 10, 333, 2) &&
                        Part(skill, 0, "magical", "amount", 210000d) &&
                        Part(skill, 1, "explosive", "basis", "targetCurrentHealth",
                            "percentPerHit", 10.5d, "hits", 2d,
                            "onlyTargetKinds", new object[] { "line" });
                case "SR-04:skill:2":
                    return Header(skill, "압력포 :: 베어", "area", 12.5, 500, 2) &&
                        Part(skill, 0, "magical", "amount", 200000d) &&
                        Part(skill, 1, "magicResistanceReduction", "percent", 1d);
                case "T-04:skill:0":
                    return Header(skill, "람각", null, 15, 500, 2) &&
                        Part(skill, 0, "magical", "amount", 300000d) &&
                        Part(skill, 1, "explosive", "targetMaxHealthPercent", 5d,
                            "onlyTargetKinds", new object[] { "line" },
                            "scope", "singlePrimaryTarget");
                case "D-04:skill:1":
                    return Header(skill, "고스트 랩", null, 12.5, 360, 2) &&
                        Part(skill, 0, "explosive", "amount", 300000d) &&
                        Part(skill, 1, "explosive", "extraAmount", 500000d,
                            "chancePercent", 20d);
                case "MY-04:skill:2":
                    return Header(skill, "섬광연무", "area", 13.5, 600, 3) &&
                        Part(skill, 0, "magical", "amount", 800000d) &&
                        Part(skill, 1, "magicResistanceReduction", "percent", 3d) &&
                        Part(skill, 2, "stun", "durationSeconds", 3d);
                case "T-05:skill:1":
                    return Header(skill, "일렉트리컬 루나", null, 50, 425, 2) &&
                        Part(skill, 0, "explosive", "amount", 100000d) &&
                        Part(skill, 1, "explosiveDamageAmplification", "percent", 1d,
                            "maxPercent", 25d);
                case "T-03:skill:1":
                    return Header(skill, "무기무기열매", null, 10, 450, 2) &&
                        Part(skill, 0, "magical", "amount", 100000d,
                            "extraSingleMagicalAmount", 70000d) &&
                        Part(skill, 1, "armorBreak", "stacks", 1d, "max", 75d);
                case "SR-02:skill:1":
                    return Header(skill, "일섬", "area", 10, 500, 3,
                            "extraChancePercent", 10d,
                            "extraChancePercentAgainst", new object[] { "boss", "enraged" }) &&
                        Part(skill, 0, "magical", "amount", 350000d) &&
                        Part(skill, 1, "magical", "bonusPercent", 100d,
                            "scope", "singlePrimaryTarget") &&
                        Part(skill, 2, "armorBreak", "stacks", 2d,
                            "scope", "singlePrimaryTarget");
                case "SR-02:skill:2":
                    return Header(skill, "광역 참격", "area", 7, 550, 2,
                            "extraChancePercent", 10d,
                            "extraChancePercentAgainst", new object[] { "boss", "enraged" },
                            "absoluteCooldownSeconds", 3d) &&
                        Part(skill, 0, "magical", "amount", 1000000d) &&
                        Part(skill, 1, "magical", "basis", "targetMaxHealth",
                            "percent", 5d,
                            "onlyTargetKinds", new object[] { "boss", "enraged" });
            }
            return false;
        }
    }
}
