using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Original catalog IDs H096/H0B4 are anchored to the extracted W3X object
    // table. An entire tooltip packet must match before it can be modeled.
    // L-30 and D-06 are deliberately absent: their tooltip descriptions name
    // different people from the original map (카르가라/바제스).
    public static class SourceModelTwentyFifthSkillCatalog
    {
        private static readonly Dictionary<string, object> ExpectedSkills =
            new Dictionary<string, object>(StringComparer.Ordinal) {
            {"TR-05:skill:0", OrdrJson.Parse(@"{""name"":""룸"",""trigger"":""activeMana"",""components"":[{""type"":""selfAttackPowerFlat"",""amount"":30000},{""type"":""allyAttackPowerBuff"",""percent"":20,""durationSeconds"":9},{""type"":""movementSlow"",""percent"":45,""durationSeconds"":5},{""type"":""selfHeal"",""rate"":null}],""manaCost"":135,""target"":""casterArea"",""radius"":825,""roomDurationSeconds"":5}")},
            {"TR-05:skill:1", OrdrJson.Parse(@"{""name"":""룸 연계 광역 공격"",""trigger"":""activeHealth"",""components"":[{""type"":""magical"",""amount"":2500000}],""healthCost"":250,""requires"":""roomActive"",""maxPerRoom"":1,""target"":""area"",""radius"":600}")},
            {"TR-05:skill:2", OrdrJson.Parse(@"{""name"":""인젝션 샷"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":175000,""plusTargetCurrentHealthPercent"":0.4},{""type"":""movementSlow"",""percent"":99,""durationSeconds"":3,""initialRootSeconds"":1.25},{""type"":""areaSlow"",""percent"":40,""durationSeconds"":3}],""chancePercent"":12.5,""target"":""area"",""radius"":485,""overrides"":[{""targetKinds"":[""enraged""],""removeArmorBuff"":true,""extraFixedCurrentHealthPercent"":33}]}")},
            {"TR-05:skill:3", OrdrJson.Parse(@"{""name"":""수술수술 열매"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amountPerHit"":155000,""hits"":2},{""type"":""fixed"",""amount"":255000,""plusTargetCurrentHealthPercent"":1.25}],""chancePercent"":5,""target"":""area"",""radius"":575,""overrides"":[{""targetKinds"":[""enraged""],""removeArmorBuff"":true,""extraFixedCurrentHealthPercent"":50}]}")},
            {"TR-25:skill:0", OrdrJson.Parse(@"{""name"":""펑크 깁슨"",""trigger"":""activeHealth"",""components"":[{""type"":""magical"",""amount"":4400000,""strengthScalingPerPoint"":50000},{""type"":""magical"",""centerExtraPercent"":50,""centerRadius"":250},{""type"":""stun"",""durationSeconds"":2.2}],""healthCost"":40,""castTimeSeconds"":0.5,""target"":""area"",""radius"":500}")},
            {"TR-25:skill:1", OrdrJson.Parse(@"{""name"":""리펠"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""lineDamage"":300000,""explosionDamage"":300000,""extraExplosionWhenEnemyCountAtLeast"":15,""checkRadius"":250}],""chancePercent"":10,""target"":""lineThenExplosion"",""lineRadius"":300,""explosionRadius"":450}")},
            {"TR-25:skill:2", OrdrJson.Parse(@"{""name"":""자성 타격"",""trigger"":""onAttack"",""components"":[{""type"":""magical"",""amount"":500000,""strengthScalingPerPoint"":5000},{""type"":""magical"",""singleExtraPercent"":200},{""type"":""stun"",""durationSeconds"":3,""scope"":""singlePrimaryTarget""},{""type"":""magnetObjectGain"",""amount"":1,""afterEvery"":5,""empoweredTripleCast"":true,""empoweredDamagePercent"":20}],""chancePercent"":6.5,""target"":""area"",""radius"":500}")},
            {"TR-25:skill:3", OrdrJson.Parse(@"{""name"":""단일 대체 공격"",""trigger"":""onEveryAttack"",""components"":[{""type"":""physical"",""amount"":360000},{""type"":""stun"",""durationSeconds"":1.5}],""chancePercent"":35,""overridesBasicMagicSplash"":true}")}
        };

        private static readonly Dictionary<string, object[]> ExpectedPassives =
            new Dictionary<string, object[]>(StringComparer.Ordinal) {
                {"TR-05", new object[] {
                    OrdrJson.Parse(@"{""type"":""teleport""}"),
                    OrdrJson.Parse(@"{""type"":""basicAttackExplosive"",""radius"":415,""attackPowerPercent"":55}")
                }},
                {"TR-25", new object[] {
                    OrdrJson.Parse(@"{""type"":""movementSlowAura"",""percent"":35}"),
                    OrdrJson.Parse(@"{""type"":""selfAttackPowerFlat"",""amount"":50000}"),
                    OrdrJson.Parse(@"{""type"":""skillDamageBonusPerEnemyInArea"",""percentPerEnemy"":0.25}"),
                    OrdrJson.Parse(@"{""type"":""basicAttackAreaMagic"",""amount"":40000,""radius"":215,""exceptPrimaryTarget"":true}")
                }}
            };

        // The only source-backed Kid trait upgrade. The entire two-row
        // replacement packet must match; partial patches must not activate.
        private static readonly object ExpectedKidEnhancement = OrdrJson.Parse(@"[{""type"":""replaceSkill"",""from"":""펑크 깁슨"",""to"":""펑크 크래시"",""healthCost"":50,""radius"":525,""magicalDamagePerHit"":2000000,""hits"":3,""centerRadius"":325,""centerBonusPercent"":50,""strengthScalingPerPoint"":60000,""stunPerHitSeconds"":2.85},{""type"":""replaceSkill"",""from"":""리펠"",""to"":""댐드 펑크"",""chancePercent"":10,""lineRadius"":450,""explosionRadius"":450,""magicalLineDamage"":600000,""magicalExplosionDamage"":450000,""explosionDamageBonusPerEnemyPercent"":1}]");

        public static bool CanModelExactKidEnhancement(CharacterSkillProfile profile)
        {
            object changes;
            return HasExactOwner(profile, "TR-25") &&
                profile.OriginalFields.TryGetValue("enhancedChanges", out changes) &&
                EqualJson(changes, ExpectedKidEnhancement);
        }

        // These imported tooltip names are for different source-map owners.
        // Quarantine both their active execution and their public UI listing.
        public static bool IsKnownForeignProfile(CharacterSkillProfile profile)
        {
            if (profile == null || profile.Skills.Count == 0) return false;
            if (profile.UnitId == "L-30")
            {
                foreach (var skill in profile.Skills)
                    if (skill.Name == "샨디아의 창") return true;
            }
            if (profile.UnitId == "D-06")
            {
                foreach (var skill in profile.Skills)
                    if (skill.Name == "파동 엘보") return true;
            }
            return false;
        }

        private static bool EqualJson(object actual, object expected)
        {
            if (actual == null || expected == null) return actual == expected;
            var leftObject = actual as IDictionary<string, object>;
            var rightObject = expected as IDictionary<string, object>;
            if (leftObject != null || rightObject != null)
            {
                if (leftObject == null || rightObject == null ||
                    leftObject.Count != rightObject.Count) return false;
                foreach (var field in rightObject)
                {
                    object value;
                    if (!leftObject.TryGetValue(field.Key, out value) ||
                        !EqualJson(value, field.Value)) return false;
                }
                return true;
            }
            var leftArray = actual as IList<object>;
            var rightArray = expected as IList<object>;
            if (leftArray != null || rightArray != null)
            {
                if (leftArray == null || rightArray == null ||
                    leftArray.Count != rightArray.Count) return false;
                for (int i = 0; i < leftArray.Count; i++)
                    if (!EqualJson(leftArray[i], rightArray[i])) return false;
                return true;
            }
            return Equals(actual, expected);
        }

        public static bool IsSpecial(ImportedCombatSkill skill) =>
            skill != null && ExpectedSkills.ContainsKey(skill.Id);

        public static bool CanModelExact(ImportedCombatSkill skill)
        {
            object expected;
            return skill != null && ExpectedSkills.TryGetValue(skill.Id, out expected) &&
                EqualJson(skill.OriginalFields, expected);
        }

        public static bool HasExactOwner(CharacterSkillProfile profile, string owner)
        {
            object[] passives;
            if (profile == null || profile.UnitId != owner ||
                !ExpectedPassives.TryGetValue(owner, out passives) ||
                profile.Passives.Count != passives.Length ||
                profile.Skills.Count != 4) return false;
            for (int i = 0; i < passives.Length; i++)
                if (!EqualJson(profile.Passives[i].Fields, passives[i])) return false;
            for (int i = 0; i < 4; i++)
                if (profile.Skills[i].Id != owner + ":skill:" + i ||
                    !CanModelExact(profile.Skills[i])) return false;
            return true;
        }
    }
}
