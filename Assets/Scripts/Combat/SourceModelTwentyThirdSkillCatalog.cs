using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Batch 23: only these four complete, owner-specific tooltip packets may
    // enter the mobile OFFLINE active-skill model. This is not JASS parity.
    // Every header, effect field, order and amount must remain source-identical.
    public static class SourceModelTwentyThirdSkillCatalog
    {
        public static bool IsSpecial(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            return skill.Id == "H-09:skill:0" ||
                skill.Id == "H-07:skill:0" ||
                skill.Id == "E-04:skill:0" ||
                skill.Id == "I-06:skill:0" ||
                skill.Id == "RN-05:skill:0";
        }

        private static bool Header(ImportedCombatSkill skill, string id,
            string name, string trigger, double cost, double radius, int count)
        {
            return skill != null && skill.Id == id && skill.Name == name &&
                skill.Trigger == trigger && skill.Target == "area" &&
                skill.Number(trigger == "activeMana" ? "manaCost" : "healthCost") ==
                    cost && skill.Radius == radius &&
                !skill.ChancePercent.HasValue && skill.Components.Count == count &&
                skill.OriginalFields.Count == 6 &&
                skill.OriginalFields.ContainsKey("name") &&
                skill.OriginalFields.ContainsKey("trigger") &&
                skill.OriginalFields.ContainsKey(trigger == "activeMana" ?
                    "manaCost" : "healthCost") &&
                skill.OriginalFields.ContainsKey("target") &&
                skill.OriginalFields.ContainsKey("radius") &&
                skill.OriginalFields.ContainsKey("components");
        }

        private static bool Part(ImportedCombatSkill skill, int index,
            string type, params object[] fields)
        {
            if (index < 0 || index >= skill.Components.Count ||
                fields.Length % 2 != 0) return false;
            var component = skill.Components[index];
            if (component.Type != type ||
                component.Fields.Count != fields.Length / 2 + 1) return false;
            for (int i = 0; i < fields.Length; i += 2)
            {
                object actual;
                if (!component.Fields.TryGetValue((string)fields[i], out actual) ||
                    !(actual is double) || (double)actual != (double)fields[i + 1])
                    return false;
            }
            return true;
        }

        public static bool CanModelExactActive(ImportedCombatSkill skill)
        {
            if (!IsSpecial(skill)) return false;
            switch (skill.Id)
            {
                case "H-09:skill:0":
                    return Header(skill, "H-09:skill:0", "뇌영", "activeMana",
                            150, 800, 4) &&
                        Part(skill, 0, "magical", "amount", 2150000d) &&
                        Part(skill, 1, "stun", "durationSeconds", 1.2d) &&
                        Part(skill, 2, "magicResistanceReduction", "percent", 10d) &&
                        Part(skill, 3, "explosiveDamageAmplification", "percent", 10d);
                case "H-07:skill:0":
                    return Header(skill, "H-07:skill:0", "갑판 포격", "activeMana",
                            95, 800, 1) &&
                        Part(skill, 0, "magical", "amount", 3000000d,
                            "plusTargetMissingHealthPercent", 4d);
                case "E-04:skill:0":
                    return Header(skill, "E-04:skill:0", "마나 고정 피해", "activeMana",
                            175, 750, 1) &&
                        Part(skill, 0, "fixed", "amount", 5250000d,
                            "plusTargetMaxHealthPercent", 5d);
                case "I-06:skill:0":
                    return Header(skill, "I-06:skill:0", "대자재천신", "activeHealth",
                            115, 900, 1) &&
                        Part(skill, 0, "magical", "plusTargetMaxHealthPercent", 1d,
                            "amountPerHit", 900000d, "hits", 6d);
                default: return false;
            }
        }

        public static bool CanModelExactAttack(ImportedCombatSkill skill)
        {
            if (skill == null || skill.Id != "RN-05:skill:0" ||
                skill.Name != "데스노트" || skill.Trigger != "onAttack" ||
                skill.Target != "single" || skill.ChancePercent != 10 ||
                skill.Radius.HasValue || skill.OriginalFields.Count != 5 ||
                skill.Components.Count != 1 ||
                !skill.OriginalFields.ContainsKey("name") ||
                !skill.OriginalFields.ContainsKey("trigger") ||
                !skill.OriginalFields.ContainsKey("chancePercent") ||
                !skill.OriginalFields.ContainsKey("target") ||
                !skill.OriginalFields.ContainsKey("components")) return false;
            var part = skill.Components[0];
            if (part.Type != "magical" || part.Fields.Count != 3 ||
                part.Number("targetCurrentHealthPercent") != 25) return false;
            var excluded = part.Get("exceptTargetKinds") as System.Collections.Generic.List<object>;
            return excluded != null && excluded.Count == 2 &&
                excluded[0] is string && (string)excluded[0] == "boss" &&
                excluded[1] is string && (string)excluded[1] == "story";
        }
    }
}
