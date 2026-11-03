using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, double> sourceModelPoseidonReadyAt =
            new Dictionary<long, double>();

        private static string SourceModelKind(SpatialCombatEnemy enemy)
        {
            switch (enemy.Kind)
            {
                case CharacterEnemyKind.Boss: return "boss";
                case CharacterEnemyKind.Enraged: return "enraged";
                case CharacterEnemyKind.Story: return "story";
                default: return "line";
            }
        }

        private int SourceModelActiveBuffCount(long actorId)
        {
            CombatStatusEngine status;
            int count = 0;
            if (unitStatuses.TryGetValue(InstanceKey(actorId), out status))
                foreach (var effect in status.Snapshot())
                    if (effect.Type == CombatEffectType.AttackSpeedBuff ||
                        effect.Type == CombatEffectType.AttackPowerBuff ||
                        effect.Type == CombatEffectType.HealthRegeneration)
                        count++;
            return count;
        }

        private static double SourceModelKindNumber(object source, string kind)
        {
            var rows = source as Dictionary<string, object>;
            object value;
            return rows != null && rows.TryGetValue(kind, out value) ?
                Convert.ToDouble(value) : 0;
        }

        private static bool SourceModelKindInOverride(
            ImportedCombatSkill skill, SpatialCombatEnemy target)
        {
            var rows = skill.Get("overrides") as List<object>;
            if (rows == null || rows.Count == 0) return false;
            var row = rows[0] as Dictionary<string, object>;
            object kinds;
            return row != null && row.TryGetValue("targetKinds", out kinds) &&
                (kinds as List<object>)?.Contains(SourceModelKind(target)) == true;
        }

        private double SourceModelSixthProcChance(ImportedCombatSkill skill,
            SpatialCombatEnemy primary, CharacterInstance actor)
        {
            double chance = SourceModelFifthProcChance(skill);
            if (!SourceModelNextThirtySkillCatalog.IsSixthCompositeSkill(skill))
                return chance;
            // The temporary black-Mamba form already exists in the common
            // status engine. Read its *live* lifetime instead of inventing a
            // second transform timer or assuming the form is permanent.
            if (skill.Id == "TR-14:skill:2" &&
                PracticeFormOf(actor.Id) == "blackMamba")
                return 70; // source tooltip on-form chance
            if (skill.Id == "TR-14:skill:3" &&
                PracticeFormOf(actor.Id) == "blackMamba")
                return Math.Min(100, chance * 1.2);
            if (!SourceModelKindInOverride(skill, primary)) return chance;
            var row = ((List<object>)skill.Get("overrides"))[0] as
                Dictionary<string, object>;
            if (skill.Id == "TR-09:skill:5")
                chance *= Convert.ToDouble(row["procMultiplier"]);
            else if (skill.Id == "TR-19:skill:1")
                chance = Convert.ToDouble(row["chancePercent"]);
            return Math.Min(100, chance);
        }

        private double SourceModelSixthRawDamageAdjustment(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component, SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelNextThirtySkillCatalog.IsSixthCompositeSkill(skill))
                return raw;
            if (skill.Id == "TR-09:skill:5" &&
                SourceModelKindInOverride(skill, target))
                raw *= 1.7; // Exact profile boss/enraged override.
            if (skill.Id == "TR-14:skill:2" &&
                PracticeFormOf(actor.Id) == "blackMamba")
                raw *= 1.5;
            if (skill.Id == "TR-11:skill:1")
            {
                if (component.Type == "physical" &&
                    component.Get("amount") != null &&
                    target.Kind == CharacterEnemyKind.Boss)
                    raw += target.Enemy.MaxHealth * .01;
                if (component.Get("plusOwnBuffCountMultiplier") != null &&
                    ReferenceEquals(primary, target))
                    raw += SourceModelActiveBuffCount(actor.Id) *
                        component.Number("plusOwnBuffCountMultiplier").Value;
            }
            if (skill.Id == "TR-14:skill:3" &&
                component.Get("extraSingleTargetAmount") != null &&
                ReferenceEquals(primary, target))
                raw += component.Number("extraSingleTargetAmount").Value;
            if (skill.Id == "TR-19:skill:1" && component.Type == "fixed")
            {
                string kind = SourceModelKind(target);
                double bonusPercent = SourceModelKindNumber(
                    component.Get("bonusPercentByTargetKind"), kind);
                double extraHealthPercent = SourceModelKindNumber(
                    component.Get("extraMaxHealthPercentByTargetKind"), kind);
                raw = skill.Components[0].Number("amount").Value *
                    bonusPercent / 100 + target.Enemy.MaxHealth * extraHealthPercent / 100;
            }
            if (skill.Id == "TR-24:skill:3" &&
                component.Get("amountPerBuff") != null)
                raw = SourceModelActiveBuffCount(actor.Id) *
                    component.Number("amountPerBuff").Value;
            return raw;
        }

        private int SourceModelSixthHitCount(ImportedCombatSkill skill,
            ImportedCombatEffect component, SpatialCombatEnemy target)
        {
            if (skill.Id == "TR-09:skill:0" &&
                component.Get("baseHits") != null)
            {
                var kinds = new HashSet<CombatEffectType>();
                foreach (var status in target.Enemy.Status.Snapshot())
                    if (status.Type == CombatEffectType.Stun ||
                        status.Type == CombatEffectType.ArmorReduction ||
                        status.Type == CombatEffectType.MovementSlowPercent ||
                        status.Type == CombatEffectType.MagicResistanceReduction)
                        kinds.Add(status.Type);
                double fractional = component.Number("baseHits").Value +
                    kinds.Count * component.Number("additionalHitsPerDebuff").Value;
                int whole = (int)Math.Floor(fractional);
                // Fractional half-strikes are resolved with the shared battle
                // RNG; never silently round up or manufacture an extra hit.
                if (sourceModelRules.NextScriptRoll(1000000) <
                    (int)Math.Round((fractional - whole) * 1000000)) whole++;
                return whole;
            }
            if (skill.Id == "TR-19:skill:1" &&
                SourceModelKindInOverride(skill, target)) return 2;
            return (int)(component.Number("hits") ?? 1);
        }

        private void ScheduleSourceModelPoseidon(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            double interval = component.Number("intervalSeconds").Value;
            int ticks = (int)Math.Floor(component.Number("durationSeconds").Value /
                interval + .0000001);
            foreach (var target in targets)
                for (int tick = 1; tick <= ticks; tick++)
                {
                    var victim = target;
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                        victim.Id, tick * interval, () => {
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(victim.Id, out current) ||
                                !ReferenceEquals(current, victim) ||
                                !victim.Enemy.IsAlive ||
                                !CanAttackInThisLane(actor, victim)) return;
                            Ordr323ArmorType armor;
                            if (!victim.Enemy.Defenses.BaseArmor.HasValue ||
                                !Ordr323OriginalDamageRules.TryReadArmorType(
                                    victim.Enemy.Defenses.WarcraftArmorType, out armor)) return;
                            var range = component.Get("multiplierRange") as List<object>;
                            double factor = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                                1, (double)range[0], (double)range[1],
                                sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                            double raw = component.Number("amountPerTick").Value * factor;
                            double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                                raw, Ordr323AttackType.Spells, armor, true, true,
                                victim.Enemy.Defenses.BaseArmor.Value -
                                SourceModelUnitEffects.ResolvePracticeArmorReduction(
                                    victim.Enemy.Status.Snapshot()) -
                                ArmorBreakStacksOf(victim.Id), false, 0, 1);
                            modeled *= SourceModelCompositeMagicFactor(victim, "magical");
                            if (modeled >= long.MaxValue || double.IsNaN(modeled) ||
                                double.IsInfinity(modeled))
                                throw new OverflowException("포세이돈 지속 피해 범위 초과");
                            long actual;
                            if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id),
                                victim.Id, (long)Math.Floor(modeled), out actual)) return;
                            events.Add(new CharacterCombatEvent(
                                CharacterCombatEventType.SkillDamage,
                                InstanceKey(actor.Id), victim.Id, skill.Name,
                                "TOOLTIP_MODEL_ONLY / 0.25초 간격 포세이돈 지속 피해", actual));
                            if (!victim.Enemy.IsAlive)
                                events.Add(new CharacterCombatEvent(
                                    CharacterCombatEventType.EnemyDefeated,
                                    InstanceKey(actor.Id), victim.Id, skill.Name,
                                    "연습 지속 피해로 적 처치", actual));
                        });
                }
        }
    }
}
