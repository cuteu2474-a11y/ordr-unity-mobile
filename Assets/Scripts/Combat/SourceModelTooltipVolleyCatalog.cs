using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Strict tooltip identity/shape check. These are only documented mobile
    // practice packets, not proof of WC3 hit timing or native immunity.
    public static class SourceModelTooltipVolleyCatalog
    {
        private static bool Kinds(object value, params string[] names)
        {
            var list = value as List<object>;
            if (list == null || list.Count != names.Length) return false;
            for (int i = 0; i < names.Length; i++)
                if (!Equals(list[i], names[i])) return false;
            return true;
        }

        public static bool IsApproved(ImportedCombatSkill skill)
        {
            if (skill == null || skill.ChancePercent.HasValue) return false;
            switch (skill.Id)
            {
                case "E-06:skill:0":
                    if (skill.Name != "광역 난사" ||
                        skill.Trigger != "activeMana" ||
                        skill.Target != "twoRandomTargets" ||
                        skill.Number("manaCost") != 150 ||
                        skill.OriginalFields.Count != 5 ||
                        skill.Components.Count != 2) return false;
                    var volley = skill.Components[0];
                    var boss = skill.Components[1];
                    return volley.Type == "fixed" && volley.Fields.Count == 4 &&
                        volley.Number("amountPerHit") == 300000 &&
                        volley.Number("plusTargetMaxHealthPercentPerHit") == 10 &&
                        volley.Number("hits") == 20 &&
                        boss.Type == "magical" && boss.Fields.Count == 3 &&
                        boss.Number("amount") == 500000 &&
                        Kinds(boss.Get("onlyTargetKinds"), "boss");

                case "E-12:skill:0":
                    if (skill.Name != "마나 25회 타격" ||
                        skill.Trigger != "activeMana" ||
                        skill.Target != "casterArea" || skill.Radius != 750 ||
                        skill.Number("manaCost") != 175 ||
                        skill.OriginalFields.Count != 6 ||
                        skill.Components.Count != 4) return false;
                    var physical = skill.Components[0];
                    var magical = skill.Components[1];
                    var bonus = skill.Components[2];
                    var stun = skill.Components[3];
                    var overrides = bonus.Get("overrides") as List<object>;
                    var exception = overrides != null && overrides.Count == 1
                        ? overrides[0] as Dictionary<string, object> : null;
                    return physical.Type == "physical" &&
                        physical.Fields.Count == 3 &&
                        physical.Number("amountPerHit") == 360000 &&
                        physical.Number("hits") == 25 &&
                        magical.Type == "magical" && magical.Fields.Count == 3 &&
                        magical.Number("amountPerHit") == 50000 &&
                        magical.Number("hits") == 25 &&
                        bonus.Type == "magical" && bonus.Fields.Count == 3 &&
                        bonus.Number("extraTargetMaxHealthPercentPerHit") == 16 &&
                        exception != null && exception.Count == 2 &&
                        Kinds(exception["targetKinds"], "boss", "story") &&
                        Equals(exception["flatAmount"], 300000d) &&
                        stun.Type == "stun" && stun.Fields.Count == 4 &&
                        stun.Number("durationSeconds") == 2.5 &&
                        Equals(stun.Get("ignoresResistance"), true) &&
                        Equals(stun.Get("onFirstAndLastHit"), true);
                default:
                    return false;
            }
        }
    }
}
