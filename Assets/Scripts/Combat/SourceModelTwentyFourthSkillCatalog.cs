using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Batch 24: whole tooltip packets only, tied to the original unit/skill.
    // This OFFLINE practice gate is not a claim of native JASS equivalence.
    public static class SourceModelTwentyFourthSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "H-10:skill:0":
                case "I-03:skill:0":
                case "L-07:skill:0":
                case "L-20:skill:0":
                case "L-01:skill:0":
                case "TR-28:skill:0": return true;
                default: return false;
            }
        }

        private static bool Header(ImportedCombatSkill skill, string id,
            string name, string trigger, string target, double cost,
            double radius, int components, params string[] extraFields)
        {
            if (skill.Id != id || skill.Name != name ||
                skill.Trigger != trigger || skill.Target != target ||
                skill.Number(trigger == "activeMana" ? "manaCost" : "healthCost") != cost ||
                skill.Radius != radius || skill.ChancePercent.HasValue ||
                skill.Components.Count != components ||
                skill.OriginalFields.Count != 6 + extraFields.Length) return false;
            foreach (string key in new[] { "name", "trigger", "target", "radius",
                "components", trigger == "activeMana" ? "manaCost" : "healthCost" })
                if (!skill.OriginalFields.ContainsKey(key)) return false;
            foreach (string key in extraFields)
                if (!skill.OriginalFields.ContainsKey(key)) return false;
            return true;
        }

        // Reject extra fields and reordered components, including missing
        // scope or a silently changed random multiplier.
        private static bool Part(ImportedCombatSkill skill, int index,
            string type, params object[] fields)
        {
            if (index < 0 || index >= skill.Components.Count || fields.Length % 2 != 0)
                return false;
            var component = skill.Components[index];
            if (component.Type != type || component.Fields.Count != 1 + fields.Length / 2)
                return false;
            for (int i = 0; i < fields.Length; i += 2)
            {
                object actual;
                if (!component.Fields.TryGetValue((string)fields[i], out actual))
                    return false;
                if (fields[i + 1] is double)
                {
                    if (!(actual is double) ||
                        (double)actual != (double)fields[i + 1]) return false;
                }
                else if (fields[i + 1] is string)
                {
                    if (!(actual is string) ||
                        (string)actual != (string)fields[i + 1]) return false;
                }
                else if (fields[i + 1] is double[])
                {
                    var values = actual as List<object>;
                    var expected = (double[])fields[i + 1];
                    if (values == null || values.Count != expected.Length) return false;
                    for (int j = 0; j < expected.Length; j++)
                        if (!(values[j] is double) || (double)values[j] != expected[j])
                            return false;
                }
                else return false;
            }
            return true;
        }

        private static bool I03BossOverride(ImportedCombatEffect stun)
        {
            if (stun == null || stun.Type != "stun" || stun.Fields.Count != 3 ||
                stun.Number("durationSeconds") != 3) return false;
            var overrides = stun.Get("overrides") as List<object>;
            if (overrides == null || overrides.Count != 1) return false;
            var boss = overrides[0] as Dictionary<string, object>;
            if (boss == null || boss.Count != 2 ||
                !boss.ContainsKey("targetKinds") || !boss.ContainsKey("durationSeconds") ||
                !(boss["durationSeconds"] is double) ||
                (double)boss["durationSeconds"] != 1) return false;
            var kinds = boss["targetKinds"] as List<object>;
            return kinds != null && kinds.Count == 1 &&
                kinds[0] is string && (string)kinds[0] == "boss";
        }

        public static bool CanModelExactActive(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "H-10:skill:0":
                    return Header(skill, skill.Id, "맥시멈 스윙", "activeMana",
                        "targetCircle", 100, 600, 4) &&
                        Part(skill, 0, "magical", "amount", 1250000d) &&
                        Part(skill, 1, "stun", "durationSeconds", 2.5d) &&
                        Part(skill, 2, "armorBreak", "stacks", 4d) &&
                        Part(skill, 3, "armorReduction", "amount", 18d,
                            "radius", 800d, "durationSeconds", 5d);
                case "I-03:skill:0":
                    return Header(skill, skill.Id, "패기발산", "activeMana",
                        "area", 115, 800, 4) &&
                        Part(skill, 0, "physical", "amount", 2750000d,
                            "multiplierRange", new double[] { 1, 1.5 }) &&
                        Part(skill, 1, "magical", "amount", 1250000d) &&
                        I03BossOverride(skill.Components[2]) &&
                        Part(skill, 3, "armorBreak", "stacks", 5d,
                            "maxStacks", 75d);
                case "L-07:skill:0":
                    return Header(skill, skill.Id, "패기발산", "activeMana",
                        "area", 115, 800, 4) &&
                        Part(skill, 0, "physical", "amount", 1200000d,
                            "multiplierRange", new double[] { 1, 1.5 }) &&
                        Part(skill, 1, "magical", "amount", 1000000d) &&
                        Part(skill, 2, "stun", "durationSeconds", 3d) &&
                        Part(skill, 3, "armorBreak", "stacks", 3d,
                            "maxArmorReduction", 75d);
                case "L-20:skill:0":
                    return Header(skill, skill.Id, "화이트 아웃", "activeMana",
                        "area", 50, 500, 5, "manaRegenPerSecond",
                        "manaRegenPerAttack") &&
                        skill.Number("manaRegenPerSecond") == .5 &&
                        skill.Number("manaRegenPerAttack") == .5 &&
                        Part(skill, 0, "magical", "amount", 450000d,
                            "multiplierRange", new double[] { 1, 1.5 }) &&
                        Part(skill, 1, "magicDamageMultiplier", "scope",
                            "singlePrimaryTarget", "multiplier", 2d) &&
                        Part(skill, 2, "removeTargetBuffs", "scope",
                            "singlePrimaryTarget") &&
                        Part(skill, 3, "stun", "durationSeconds", .5d,
                            "scope", "singlePrimaryTarget") &&
                        Part(skill, 4, "armorBreak", "stacks", 3d,
                            "maxArmorReduction", 75d);
                case "L-01:skill:0":
                    return Header(skill, skill.Id, "어둠어둠 열매", "activeHealth",
                        "targetCircle", 40, 700, 1, "healthRegenPerSecond",
                        "healthRegenPerAttack") &&
                        skill.Number("healthRegenPerSecond") == .5 &&
                        skill.Number("healthRegenPerAttack") == 1 &&
                        Part(skill, 0, "magical", "amount", 500000d,
                            "plusTargetCurrentHealthPercent", 1d);
                case "TR-28:skill:0":
                    return Header(skill, skill.Id, "라디칼 빔", "activeMana",
                        "line", 150, 425, 1) &&
                        Part(skill, 0, "magical", "amount", 500000d);
                default: return false;
            }
        }
    }
}
