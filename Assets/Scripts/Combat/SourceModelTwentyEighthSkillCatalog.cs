using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Batch 28: four complete 2.323[C] tooltip packets, anchored to source
    // object identities. Unknown native proc timing / immunities stay unverified.
    public static class SourceModelTwentyEighthSkillCatalog
    {
        private static readonly Dictionary<string, object> Expected =
            new Dictionary<string, object>(StringComparer.Ordinal) {
            {"R-02:skill:0", OrdrJson.Parse(@"{""name"":""거프 타격"",""trigger"":""onAttack"",""components"":[{""type"":""physical"",""amount"":12500},{""type"":""magical"",""amount"":40000,""scope"":""singlePrimaryTarget""},{""type"":""stun"",""durationSeconds"":2.5,""scope"":""singlePrimaryTarget""},{""type"":""armorBreak"",""stacks"":1,""chancePercent"":50,""scope"":""singlePrimaryTarget""}],""chancePercent"":12.5,""target"":""area"",""radius"":500}")},
            {"LI-09:skill:0", OrdrJson.Parse(@"{""name"":""단일 참격"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":800000,""multiplierRange"":[1,2]}],""chancePercent"":6}")},
            {"LI-09:skill:1", OrdrJson.Parse(@"{""name"":""광역 물리"",""trigger"":""onAttack"",""components"":[{""type"":""physical"",""amount"":850000}],""chancePercent"":10,""radius"":425}")},
            {"LI-09:skill:2", OrdrJson.Parse(@"{""name"":""혼합 스턴"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":200000},{""type"":""physical"",""amount"":1000000,""multiplierRange"":[1,1.5]},{""type"":""stun"",""durationSeconds"":2.5}],""chancePercent"":5,""radius"":550}")}
        };

        private static readonly object[] ExpectedCrocodilePassives = {
            OrdrJson.Parse(@"{""type"":""armorReductionAura"",""amount"":25}"),
            OrdrJson.Parse(@"{""type"":""movementSlowAura"",""percent"":40}")
        };

        public static bool IsSpecial(ImportedCombatSkill skill) =>
            skill != null && Expected.ContainsKey(skill.Id);

        public static bool CanModelExact(ImportedCombatSkill skill)
        {
            object packet;
            return skill != null && skill.Trigger == "onAttack" &&
                Expected.TryGetValue(skill.Id, out packet) &&
                SourceModelExactSkillPackets.Matches(skill.OriginalFields, packet);
        }

        public static bool IsOwner(string catalogId) =>
            catalogId == "R-02" || catalogId == "LI-09";

        public static bool HasExactOwner(CharacterSkillProfile profile, string catalogId)
        {
            if (profile == null || profile.UnitId != catalogId ||
                !IsOwner(catalogId)) return false;
            int count = catalogId == "R-02" ? 1 : 3;
            if (profile.Skills.Count != count ||
                profile.Passives.Count != (catalogId == "R-02" ? 0 : 2))
                return false;
            for (int i = 0; i < count; i++)
                if (profile.Skills[i].Id != catalogId + ":skill:" + i ||
                    !CanModelExact(profile.Skills[i])) return false;
            if (catalogId == "LI-09")
                for (int i = 0; i < ExpectedCrocodilePassives.Length; i++)
                    if (!SourceModelExactSkillPackets.Matches(
                        profile.Passives[i].Fields, ExpectedCrocodilePassives[i]))
                        return false;
            return true;
        }

        // The original tooltip omits its target descriptor. The mobile
        // practice policy uses the attack target for the un-ranged slash;
        // the two skills with explicit radii affect the target-centered area.
        public static bool UsesPrimaryTarget(ImportedCombatSkill skill) =>
            CanModelExact(skill) && skill.Id == "LI-09:skill:0";
    }
}
