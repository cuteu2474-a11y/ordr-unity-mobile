using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Six attack procs plus one separately approved approach event. The original map supplies
    // the owner rawcodes, not a native spell-order/immunity certificate.
    public static class SourceModelTwentyNinthSkillCatalog
    {
        private static readonly Dictionary<string, object> Expected =
            new Dictionary<string, object>(StringComparer.Ordinal) {
            {"LI-08:skill:0", OrdrJson.Parse(@"{""name"":""자기 가속"",""trigger"":""onAttack"",""components"":[{""type"":""selfAttackSpeedMax"",""durationSeconds"":1.5},{""type"":""physical"",""basicAttackPercent"":75,""flatAmount"":100000,""onEveryAttackWhileBuffed"":true}],""chancePercent"":15,""requiresHealthAtLeast"":35,""healthCost"":17,""radius"":415}")},
            {"LI-08:skill:1", OrdrJson.Parse(@"{""name"":""단일 타격"",""trigger"":""onAttack"",""components"":[{""type"":""physical"",""amount"":150000},{""type"":""stun"",""durationSeconds"":1.2}],""chancePercent"":20}")},
            {"LI-08:skill:2", OrdrJson.Parse(@"{""name"":""광역 마법"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":375000}],""chancePercent"":14,""radius"":500}")},
            {"L-37:skill:0", OrdrJson.Parse(@"{""name"":""중력검"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":192500},{""type"":""physical"",""basis"":""skillDamage"",""multiplier"":2.5,""scope"":""singlePrimaryTarget""},{""type"":""stun"",""durationSeconds"":2.4},{""type"":""magicResistanceReduction"",""percent"":1}],""chancePercent"":14,""target"":""area"",""radius"":450}")},
            {"L-34:skill:0", OrdrJson.Parse(@"{""name"":""화재"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":200000},{""type"":""magicalDoT"",""damagePerTick"":42500,""tickIntervalSeconds"":0.27,""durationSeconds"":2.7},{""type"":""armorReduction"",""amount"":20,""exclusiveWith"":[""독약 스킬""]},{""type"":""armorReduction"",""amount"":10,""durationSeconds"":2}],""chancePercent"":14.25,""target"":""area"",""radius"":450}")},
            {"RN-06:skill:0", OrdrJson.Parse(@"{""name"":""다크니스 에로우"",""trigger"":""onAttack"",""components"":[{""type"":""critical"",""multiplier"":14.9},{""type"":""stun"",""durationSeconds"":3,""bossOverrideSeconds"":1.5}],""chancePercent"":14,""target"":""single""}")}
        };

        // Non-authorized skills are checked as part of the owner's identity
        // but never added to the runnable skill allow-list. A changed source
        // record must not silently keep the approved subset alive.
        private static readonly Dictionary<string, object> Deferred =
            new Dictionary<string, object>(StringComparer.Ordinal) {
            {"LI-08:skill:3", OrdrJson.Parse(@"{""name"":""접근 상대 강타"",""trigger"":""onEnemyEnterRange"",""components"":[{""type"":""physical"",""amount"":450000,""extraSinglePhysicalAmount"":4500000},{""type"":""armorBreak"",""stacks"":6}],""chancePercent"":20,""triggerRadius"":850,""targetRadius"":500,""overrides"":[{""targetKinds"":[""boss"",""enraged""],""chancePercent"":100}]}")},
            {"RN-06:skill:1", OrdrJson.Parse(@"{""name"":""행운의 토큰(Q)"",""trigger"":""manual"",""components"":[{""type"":""generateLuckyToken"",""amount"":1},{""type"":""generateLuckyToken"",""additional"":1,""chancePercent"":33},{""type"":""generateLumber"",""amount"":1,""chancePercent"":33}],""cooldownSeconds"":110,""castTimeSeconds"":2}")},
            {"RN-06:skill:2", OrdrJson.Parse(@"{""name"":""토큰 교환"",""trigger"":""verifiedCondition"",""components"":[{""type"":""exchange"",""costLuckyTokens"":3,""options"":[{""name"":""특별함 선택위습"",""chancePercent"":80},{""name"":""희귀함 선택위습"",""chancePercent"":20}]}],""requiredLuckyTokens"":3}")}
        };

        private static readonly Dictionary<string, object[]> ExpectedPassives =
            new Dictionary<string, object[]>(StringComparer.Ordinal) {
            {"LI-08", new[] { OrdrJson.Parse(@"{""type"":""armorReductionAura"",""amount"":30}"),
                OrdrJson.Parse(@"{""type"":""healthRegeneration"",""perSecond"":2.85}") }},
            {"L-37", new[] { OrdrJson.Parse(@"{""type"":""movementSlowAura"",""percent"":25}") }},
            {"L-34", new[] { OrdrJson.Parse(@"{""type"":""armorReductionAura"",""amount"":5}"),
                OrdrJson.Parse(@"{""type"":""movementSlowAura"",""percent"":10}"),
                OrdrJson.Parse(@"{""type"":""onBasicHitDebuff"",""movementSlowPercent"":45,""durationSeconds"":3,""magicalDamagePerSecond"":250000}"),
                OrdrJson.Parse(@"{""type"":""flyingMovement""}") }},
            {"RN-06", new[] { OrdrJson.Parse(@"{""type"":""movementSlowAura"",""percent"":20}") }}
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

        // Approach is an event skill, NOT an on-attack roll. It stays outside
        // CanModelCompleteAttack and the normal declared-hit allow-list.
        public static bool CanModelKatakuriApproach(ImportedCombatSkill skill)
        {
            object packet;
            return skill != null && skill.Id == "LI-08:skill:3" &&
                skill.Trigger == "onEnemyEnterRange" &&
                Deferred.TryGetValue(skill.Id, out packet) &&
                SourceModelExactSkillPackets.Matches(skill.OriginalFields, packet);
        }

        public static bool IsOwner(string id) =>
            id == "LI-08" || id == "L-37" || id == "L-34" || id == "RN-06";

        // All source skill positions are checked, but only attack procs and
        // the separately approved approach have runnable event paths.
        public static bool HasExactApprovedSubset(CharacterSkillProfile profile)
        {
            if (profile == null || !IsOwner(profile.UnitId)) return false;
            int skillCount = profile.UnitId == "LI-08" ? 4 :
                profile.UnitId == "RN-06" ? 3 : 1;
            if (profile.Skills.Count != skillCount) return false;
            object[] passives = ExpectedPassives[profile.UnitId];
            if (profile.Passives.Count != passives.Length) return false;
            for (int i = 0; i < passives.Length; i++)
                if (!SourceModelExactSkillPackets.Matches(
                    profile.Passives[i].Fields, passives[i])) return false;
            for (int i = 0; i < skillCount; i++)
            {
                var skill = profile.Skills[i];
                string id = profile.UnitId + ":skill:" + i;
                if (skill.Id != id) return false;
                object deferred;
                if (Deferred.TryGetValue(id, out deferred))
                {
                    if (!SourceModelExactSkillPackets.Matches(
                            skill.OriginalFields, deferred)) return false;
                }
                else if (!CanModelExact(skill)) return false;
            }
            return true;
        }

        // The LI-08 source omits target type for both attack procs. A missing
        // radius is a single current target; the radius-bearing proc is a
        // target-centered area for mobile practice only.
        public static bool UsesPrimaryTarget(ImportedCombatSkill skill) =>
            CanModelExact(skill) && skill.Id == "LI-08:skill:1";
    }
}
