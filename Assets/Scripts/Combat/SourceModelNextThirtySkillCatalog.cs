using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Second group of 30 units. This list identifies source object identities,
    // not thirty completed skill kits. A deliberately small allow-list enables
    // *tooltip-based practice packets* only when the complete packet has a
    // supported shape. Complex form, mana, health, summons and stacking paths
    // stay deferred until their original JASS/native state can be witnessed.
    public sealed class SourceModelNextThirtyUnit
    {
        public string CatalogId { get; private set; }
        public string Rawcode { get; private set; }
        public IReadOnlyList<string> PracticeAttackSkills { get; private set; }

        internal SourceModelNextThirtyUnit(string id, string rawcode, params string[] skills)
        {
            CatalogId = id;
            Rawcode = rawcode;
            PracticeAttackSkills = Array.AsReadOnly(skills);
        }

        public bool Allows(string skillName)
        {
            foreach (string name in PracticeAttackSkills)
                if (string.Equals(name, skillName, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    public static class SourceModelNextThirtySkillCatalog
    {
        private static readonly SourceModelNextThirtyUnit[] Entries = {
            new SourceModelNextThirtyUnit("L-03", "h09Z"),
            new SourceModelNextThirtyUnit("L-04", "h02W", "바람바람 열매"),
            new SourceModelNextThirtyUnit("L-07", "h03A"),
            new SourceModelNextThirtyUnit("L-10", "h02Y"),
            new SourceModelNextThirtyUnit("L-13", "h02Z", "배리어 볼"),
            new SourceModelNextThirtyUnit("L-15", "h02X", "왼손은 수면제"),
            new SourceModelNextThirtyUnit("L-17", "h035", "패왕색의 검무"),
            new SourceModelNextThirtyUnit("L-20", "h02V"),
            new SourceModelNextThirtyUnit("L-25", "h03S"),
            new SourceModelNextThirtyUnit("L-33", "h03H"),
            new SourceModelNextThirtyUnit("H-02", "h03U"),
            new SourceModelNextThirtyUnit("H-04", "h03N", "숲 물리"),
            new SourceModelNextThirtyUnit("H-07", "h03R"),
            new SourceModelNextThirtyUnit("H-12", "h03M"),
            new SourceModelNextThirtyUnit("H-16", "h041"),
            new SourceModelNextThirtyUnit("H-22", "h047", "마리조아붕괴"),
            new SourceModelNextThirtyUnit("TR-02", "H08V", "미라쥬 템포"),
            new SourceModelNextThirtyUnit("TR-04", "H098", "범위 마법 스턴", "혼합 타격"),
            new SourceModelNextThirtyUnit("TR-09", "H094"),
            new SourceModelNextThirtyUnit("TR-10", "H09I"),
            new SourceModelNextThirtyUnit("TR-14", "H0B2"),
            new SourceModelNextThirtyUnit("TR-18", "H0BD", "스턴 물리 타격"),
            new SourceModelNextThirtyUnit("TR-23", "H091"),
            new SourceModelNextThirtyUnit("TR-30", "H0AA"),
            new SourceModelNextThirtyUnit("E-01", "H0BK", "혼합 광역 타격"),
            new SourceModelNextThirtyUnit("E-04", "h058", "매 타격 추가 마법", "범위 마법 타격"),
            new SourceModelNextThirtyUnit("E-08", "h08R", "범위 물리", "마법 광역"),
            new SourceModelNextThirtyUnit("I-04", "h04J", "광역 물리 타격"),
            new SourceModelNextThirtyUnit("I-06", "h04Q"),
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

        // Reject an entire skill if *any* part of its documented packet is
        // unsupported. Applying only its first component would alter the
        // original outcome while appearing to execute the full ability.
        public static bool CanModelCompleteAttack(ImportedCombatSkill skill)
        {
            if (skill == null ||
                !(skill.Trigger == "onAttack" || skill.Trigger == "onEveryAttack") ||
                !(skill.Target == "area" || skill.Target == "single") ||
                (skill.Target == "area" && (!skill.Radius.HasValue || skill.Radius.Value <= 0)) ||
                (skill.Trigger == "onAttack" &&
                 (!skill.ChancePercent.HasValue || skill.ChancePercent.Value <= 0)) ||
                (skill.Trigger == "onEveryAttack" && skill.ChancePercent.HasValue) ||
                skill.Components.Count == 0) return false;
            foreach (string key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "chancePercent" &&
                    key != "target" && key != "radius" && key != "components")
                    return false;
            bool hasDamage = false;
            foreach (var effect in skill.Components)
            {
                if (effect.Type == "physical" || effect.Type == "magical")
                {
                    double? amount = effect.Number("amount");
                    if (!amount.HasValue || amount.Value <= 0 ||
                        double.IsNaN(amount.Value) || double.IsInfinity(amount.Value)) return false;
                    foreach (string key in effect.Fields.Keys)
                        if (key != "type" && key != "amount" && key != "multiplierRange")
                            return false;
                    var range = effect.Get("multiplierRange");
                    if (range != null)
                    {
                        var values = range as List<object>;
                        if (values == null || values.Count != 2 ||
                            !(values[0] is double) || !(values[1] is double) ||
                            (double)values[0] <= 0 || (double)values[1] < (double)values[0] ||
                            double.IsNaN((double)values[0]) || double.IsNaN((double)values[1]) ||
                            double.IsInfinity((double)values[1])) return false;
                    }
                    hasDamage = true;
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
            return hasDamage;
        }
    }
}
