using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Only packets whose complete MOBILE result can be represented here are
        // approved. A skill name appearing in a tooltip is not authorization
        // to roll every other skill belonging to the same character.
        private readonly Dictionary<string, double> additionalAutoReadyAt =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private void ForgetAdditionalAutomaticSkills(long instanceId)
        {
            string prefix = InstanceKey(instanceId) + ":";
            var expired = new List<string>();
            foreach (var key in additionalAutoReadyAt.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    expired.Add(key);
            foreach (var key in expired) additionalAutoReadyAt.Remove(key);
            ForgetBulkAutomaticSkills(instanceId);
        }

        private static bool HasAdditionalAutomaticAttack(string rawcode)
        {
            return rawcode == "h04A" || rawcode == "h0A2" ||
                   rawcode == "h058" || rawcode == "h06V" ||
                   rawcode == "h048" || rawcode == "h0A6" ||
                   rawcode == "h0CC" || HasBulkAutomaticOwner(rawcode);
        }

        private static bool IsAdditionalAutoOwner(string catalogId, string rawcode)
        {
            return (catalogId == "I-12" && rawcode == "h04A") ||
                   (catalogId == "MY-03" && rawcode == "h0A2") ||
                   (catalogId == "E-04" && rawcode == "h058") ||
                   (catalogId == "RN-02" && rawcode == "h06V") ||
                   (catalogId == "D-04" && rawcode == "h048") ||
                   (catalogId == "MY-09" && rawcode == "h0A6") ||
                   (catalogId == "MY-10" && rawcode == "h0CC");
        }

        private ImportedCombatSkill AdditionalAutoSkill(CharacterInstance actor,
            string skillId)
        {
            if (actor.Profile == null || actor.Profile.UnitId != actor.Catalog.Id)
                return null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Id == skillId) return skill;
            return null;
        }

        private bool AdditionalAutoRoll(ImportedCombatSkill skill)
        {
            if (skill == null || !skill.Number("chancePercent").HasValue)
                return false;
            double chance = skill.Number("chancePercent").Value;
            return chance >= 0 && chance <= 100 &&
                   sourceModelRules.NextScriptRoll(1000000) <
                   (int)Math.Round(chance * 10000,
                       MidpointRounding.AwayFromZero);
        }

        private List<SpatialCombatEnemy> AdditionalAutoCircle(
            CharacterInstance actor, SpatialCombatEnemy primary,
            double radius, bool casterCentered = false)
        {
            return SourceScriptTargets(actor, primary, new SourceScriptHit(
                casterCentered ? SourceScriptHitShape.AttackerCircle :
                    SourceScriptHitShape.Circle,
                radius, 0, 0, 1, 1, Ordr323AttackType.Spells));
        }

        private void AdditionalAutoDamage(CharacterInstance actor,
            IEnumerable<SpatialCombatEnemy> targets, string skillName,
            double amount, string school)
        {
            foreach (var target in targets)
                ApplySourceModelSeventhTimedHit(actor, target, skillName,
                    amount, school);
        }

        private void AdditionalAutoStatus(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            ApplySourceModelCompositeStatus(actor, primary, skill,
                component, targets);
        }

        private void AdditionalAutoDeleteLine(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            if (primary.Kind != CharacterEnemyKind.Line ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                return;
            // This is the standalone practice definition of 'delete', not
            // an invented spell packet. Native immunity is not asserted.
            long actual;
            long remaining = (long)Math.Ceiling(primary.Enemy.Health);
            if (remaining <= 0 ||
                !resolution.TryApplySkillDamage(InstanceKey(actor.Id),
                    primary.Id, remaining, out actual)) return;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillDamage, InstanceKey(actor.Id),
                primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 일반 라인 몬스터 삭제", actual));
            if (!primary.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EnemyDefeated, InstanceKey(actor.Id),
                    primary.Id, skill.Name, "연습 모델 삭제 효과", actual));
        }

        private void ExecuteAdditionalAutomaticAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, string rawcode)
        {
            ExecuteBulkAutoAttack(actor, primary, rawcode);
            if (!IsAdditionalAutoOwner(actor.Catalog.Id, rawcode)) return;
            if (actor.Catalog.Id == "I-12")
            {
                var skill = AdditionalAutoSkill(actor, "I-12:skill:2");
                if (AdditionalAutoRoll(skill) && skill.Components.Count == 3)
                {
                    var targets = AdditionalAutoCircle(actor, primary,
                        skill.Number("radius").Value);
                    AdditionalAutoDamage(actor, targets, skill.Name,
                        skill.Components[0].Number("amount").Value, "magical");
                    AdditionalAutoStatus(actor, primary, skill,
                        skill.Components[1], targets);
                    // The source describes a 10% lingering slow after 3.5s.
                    // Retain target/lane identity across the delayed transition.
                    var lingering = skill.Components[2];
                    bool lane = IsStoryDeployed(actor.Id);
                    long revision = SourceModelLaneRevision(actor.Id);
                    foreach (var victim in targets)
                    {
                        var captured = victim;
                        effectExecutor.SchedulePracticeSourceAttack(
                            InstanceKey(actor.Id), captured.Id,
                            skill.Components[1].Number("durationSeconds").Value,
                            () => {
                                SpatialCombatEnemy current;
                                if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                        lane, revision) ||
                                    !enemies.TryGetValue(captured.Id, out current) ||
                                    !ReferenceEquals(current, captured) ||
                                    !captured.Enemy.IsAlive) return;
                                var slow = new CombatEffect(
                                    "practice-tooltip:" + InstanceKey(actor.Id) +
                                        ":" + skill.Id + ":lingeringSlow",
                                    CombatEffectType.MovementSlowPercent,
                                    CombatEffectOrigin.TriggeredSkill,
                                    lingering.Number("percent").Value,
                                    lingering.Number("durationSeconds").Value);
                                captured.Enemy.Status.RemoveSource(slow.SourceId);
                                captured.Enemy.Status.Apply(slow);
                            });
                    }
                }
                return;
            }
            if (actor.Catalog.Id == "RN-02")
            {
                var skill = AdditionalAutoSkill(actor, "RN-02:skill:1");
                if (!AdditionalAutoRoll(skill) || skill.Components.Count != 1)
                    return;
                var component = skill.Components[0];
                double seconds = component.Number("durationSeconds").Value;
                int pulses = (int)Math.Floor(seconds + .000001);
                if (pulses <= 0 || pulses > 60) return;
                var targets = AdditionalAutoCircle(actor, primary,
                    skill.Number("radius").Value);
                bool lane = IsStoryDeployed(actor.Id);
                long revision = SourceModelLaneRevision(actor.Id);
                foreach (var target in targets)
                    for (int pulse = 1; pulse <= pulses; pulse++)
                    {
                        var victim = target;
                        effectExecutor.SchedulePracticeSourceAttack(
                            InstanceKey(actor.Id), victim.Id, pulse, () => {
                                SpatialCombatEnemy current;
                                if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                        lane, revision) ||
                                    !enemies.TryGetValue(victim.Id, out current) ||
                                    !ReferenceEquals(current, victim)) return;
                                ApplySourceModelSeventhTimedHit(actor, victim,
                                    skill.Name, component.Number("perSecond").Value,
                                    "magical");
                            });
                    }
                return;
            }
            if (actor.Catalog.Id == "MY-09")
            {
                var skill = AdditionalAutoSkill(actor, "MY-09:skill:0");
                if (!AdditionalAutoRoll(skill) || skill.Components.Count != 2)
                    return;
                if (primary.Kind == CharacterEnemyKind.Line)
                    AdditionalAutoDeleteLine(actor, primary, skill);
                else
                {
                    var component = skill.Components[1];
                    var range = component.Get("multiplierRange") as List<object>;
                    if (range == null || range.Count != 2) return;
                    double scale = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                        1, Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                    AdditionalAutoDamage(actor, new[] { primary }, skill.Name,
                        component.Number("amount").Value * scale, "magical");
                }
                return;
            }
            if (actor.Catalog.Id == "D-04")
            {
                var skill = AdditionalAutoSkill(actor, "D-04:skill:2");
                if (skill == null || skill.Components.Count != 3) return;
                string key = InstanceKey(actor.Id) + ":" + skill.Id;
                double due;
                if (additionalAutoReadyAt.TryGetValue(key, out due) && due > Time ||
                    !AdditionalAutoRoll(skill)) return;
                additionalAutoReadyAt[key] = Time +
                    skill.Number("absoluteCooldownSeconds").Value;
                if (primary.Kind == CharacterEnemyKind.Line)
                    AdditionalAutoDeleteLine(actor, primary, skill);
                else if (primary.Kind == CharacterEnemyKind.Boss ||
                         primary.Kind == CharacterEnemyKind.Story)
                    AdditionalAutoDamage(actor, new[] { primary }, skill.Name,
                        skill.Components[1].Number("amount").Value, "explosive");
                AdditionalAutoStatus(actor, primary, skill, skill.Components[2],
                    AdditionalAutoCircle(actor, primary,
                        skill.Components[2].Number("radius").Value));
                return;
            }
            if (actor.Catalog.Id == "E-04")
            {
                var skill = AdditionalAutoSkill(actor, "E-04:skill:1");
                if (!AdditionalAutoRoll(skill) || skill.Components.Count != 1)
                    return;
                var component = skill.Components[0];
                AdditionalAutoDamage(actor,
                    AdditionalAutoCircle(actor, primary,
                        skill.Number("circleRadius").Value), skill.Name,
                    component.Number("circleDamage").Value, "magical");
                var p = roster.Get(actor.Id);
                double dx = primary.X - p.X, dy = primary.Y - p.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                var line = SourceScriptTargets(actor, primary,
                    new SourceScriptHit(SourceScriptHitShape.ProjectileLine,
                        skill.Number("lineRadius").Value, distance, 0,
                        1, 1, Ordr323AttackType.Spells));
                AdditionalAutoDamage(actor, line, skill.Name,
                    component.Number("lineDamage").Value, "magical");
                return;
            }
            if (actor.Catalog.Id == "MY-03")
            {
                var skill = AdditionalAutoSkill(actor, "MY-03:skill:0");
                if (AdditionalAutoRoll(skill) && skill.Components.Count == 2)
                {
                    var targets = AdditionalAutoCircle(actor, primary,
                        skill.Number("radius").Value);
                    AdditionalAutoDamage(actor, targets, skill.Name,
                        skill.Components[0].Number("amount").Value, "physical");
                    var extra = skill.Components[1];
                    var range = extra.Get("multiplierRange") as List<object>;
                    if (range != null && range.Count == 2)
                    {
                        double roll = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                            1, Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                            sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                        double percent = primary.Kind == CharacterEnemyKind.Boss ||
                            primary.Kind == CharacterEnemyKind.Story ?
                            extra.Number("bossStoryMaxHealthPercent").Value :
                            extra.Number("maxHealthPercent").Value;
                        AdditionalAutoDamage(actor, new[] { primary }, skill.Name,
                            extra.Number("extraSingleDamage").Value * roll +
                            primary.Enemy.MaxHealth * percent / 100, "physical");
                    }
                    ExecuteAdditionalAutoFollowup(actor, primary, skill);
                }
                var sleeve = AdditionalAutoSkill(actor, "MY-03:skill:2");
                if (AdditionalAutoRoll(sleeve) && sleeve.Components.Count == 2)
                {
                    var packet = sleeve.Components[0];
                    var repeat = sleeve.Components[1];
                    var range = packet.Get("multiplierRange") as List<object>;
                    if (range == null || range.Count != 2) return;
                    double random = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                        1, Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                    double percent = primary.Kind == CharacterEnemyKind.Boss ||
                        primary.Kind == CharacterEnemyKind.Story ?
                        packet.Number("bossStoryMaxHealthPercent").Value :
                        packet.Number("plusTargetMaxHealthPercent").Value;
                    double damage = packet.Number("amount").Value * random +
                        primary.Enemy.MaxHealth * percent / 100;
                    AdditionalAutoDamage(actor, new[] { primary }, sleeve.Name,
                        damage, "physical");
                    if (sourceModelRules.NextScriptRoll(1000000) <
                        (int)(repeat.Number("firstChancePercent").Value * 10000))
                    {
                        var extraTargets = AdditionalAutoCircle(actor, primary,
                            repeat.Number("firstRadius").Value);
                        AdditionalAutoDamage(actor, extraTargets, sleeve.Name,
                            damage, "physical");
                        if (sourceModelRules.NextScriptRoll(1000000) <
                            (int)(repeat.Number("secondChancePercent").Value * 10000))
                            AdditionalAutoDamage(actor, extraTargets, sleeve.Name,
                                damage * repeat.Number("secondDamageMultiplier").Value,
                                "physical");
                    }
                    ExecuteAdditionalAutoFollowup(actor, primary, sleeve);
                }
                return;
            }
            if (actor.Catalog.Id == "MY-10")
            {
                var skill = AdditionalAutoSkill(actor, "MY-10:skill:0");
                if (skill == null || skill.Components.Count != 2) return;
                var targets = AdditionalAutoCircle(actor, primary,
                    skill.Components[0].Number("radius").Value);
                double basic = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
                AdditionalAutoDamage(actor, targets, skill.Name,
                    basic * skill.Components[0].Number("basicAttackDamagePercent").Value /
                        100 + skill.Components[0].Number("flatAmount").Value,
                    "physical");
                // Do not stack this armor reduction over a deployed
                // "방어구 관통" provider in the same lane.
                foreach (var unit in characters.Values)
                {
                    if (unit.Id == actor.Id || !unit.AttackReady ||
                        IsStoryDeployed(unit.Id) != IsStoryDeployed(actor.Id) ||
                        unit.Profile == null) continue;
                    foreach (var other in unit.Profile.Skills)
                        if (other.Name == "방어구 관통") return;
                }
                AdditionalAutoStatus(actor, primary, skill,
                    skill.Components[1], targets);
            }
        }

        private void ExecuteAdditionalAutoFollowup(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill activated)
        {
            ExecuteBulkAutoAfterSkill(actor, primary, activated);
            if (actor.Profile == null || activated == null) return;
            if (actor.Catalog.Id == "L-24" &&
                activated.Id == "L-24:skill:0")
            {
                var followup = AdditionalAutoSkill(actor, "L-24:skill:1");
                if (!AdditionalAutoRoll(followup) || followup.Components.Count != 1)
                    return;
                AdditionalAutoDamage(actor,
                    AdditionalAutoCircle(actor, primary,
                        followup.Number("radius").Value), followup.Name,
                    followup.Components[0].Number("amount").Value, "magical");
                return;
            }
            if (actor.Catalog.Id == "TR-02" &&
                activated.Id == "TR-02:skill:1")
            {
                var followup = AdditionalAutoSkill(actor, "TR-02:skill:2");
                if (!AdditionalAutoRoll(followup) || followup.Components.Count != 2)
                    return;
                AdditionalAutoDamage(actor,
                    AdditionalAutoCircle(actor, primary,
                        followup.Number("radius").Value), followup.Name,
                    followup.Components[0].Number("amount").Value, "magical");
                AdditionalAutoDamage(actor, new[] { primary }, followup.Name,
                    followup.Components[1].Number("amount").Value, "magical");
                return;
            }
            if (actor.Catalog.Id == "MY-03" &&
                (activated.Id == "MY-03:skill:0" ||
                 activated.Id == "MY-03:skill:2"))
            {
                var followup = AdditionalAutoSkill(actor, "MY-03:skill:3");
                if (followup == null || followup.Components.Count != 2) return;
                // Direct fixed packet: do not route this through armor.
                long actual;
                if (resolution.TryApplySkillDamage(InstanceKey(actor.Id), primary.Id,
                        (long)followup.Components[0].Number("amount").Value,
                        out actual))
                {
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage,
                        InstanceKey(actor.Id), primary.Id, followup.Name,
                        "TOOLTIP_MODEL_ONLY / 직사의 마안 고정 피해", actual));
                    if (!primary.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated,
                            InstanceKey(actor.Id), primary.Id, followup.Name,
                            "연습 기술 피해로 처치", actual));
                }
                if (primary.Kind == CharacterEnemyKind.Line &&
                    primary.Enemy.IsAlive)
                {
                    var stun = followup.Components[1];
                    string source = "practice-tooltip:" + InstanceKey(actor.Id) +
                        ":" + followup.Id;
                    primary.Enemy.Status.RemoveSource(source);
                    primary.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                        1, stun.Number("durationSeconds").Value));
                }
            }
        }

        private static bool HasAdditionalAutomaticPeriodic(string catalogId)
        {
            return catalogId == "TR-02" || catalogId == "MY-05";
        }

        private double? NextAdditionalAutoPeriodicAt(CharacterInstance actor)
        {
            if (!UsesSourceModelBasicAttacks || !actor.AttackReady ||
                actor.Mapping == null || !HasAdditionalAutomaticPeriodic(actor.Catalog.Id) ||
                actor.Profile == null || actor.Profile.UnitId != actor.Catalog.Id)
                return null;
            string id = actor.Catalog.Id == "TR-02" ? "TR-02:skill:0" :
                "MY-05:skill:1";
            var skill = AdditionalAutoSkill(actor, id);
            if (skill == null || skill.Components.Count == 0) return null;
            double? period = skill.Number("intervalSeconds") ??
                skill.Number("periodSeconds");
            if (!period.HasValue || period.Value <= 0 ||
                double.IsNaN(period.Value) || double.IsInfinity(period.Value))
                return null;
            string key = InstanceKey(actor.Id) + ":" + id;
            double due;
            if (!additionalAutoReadyAt.TryGetValue(key, out due))
            {
                due = Time + period.Value;
                additionalAutoReadyAt.Add(key, due);
            }
            else if (due < Time - period.Value)
            {
                // A unit with no eligible target should not discharge an
                // entire backlog when it re-enters combat.
                due = Time + period.Value;
                additionalAutoReadyAt[key] = due;
            }
            return due;
        }

        private int ExecuteAdditionalAutoPeriodic(int budget)
        {
            if (budget <= 0 || !UsesSourceModelBasicAttacks) return 0;
            int count = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (count >= budget) break;
                var actor = characters[id];
                if (!actor.AttackReady || actor.Mapping == null ||
                    !HasAdditionalAutomaticPeriodic(actor.Catalog.Id)) continue;
                var skillId = actor.Catalog.Id == "TR-02" ?
                    "TR-02:skill:0" : "MY-05:skill:1";
                var skill = AdditionalAutoSkill(actor, skillId);
                double? due = NextAdditionalAutoPeriodicAt(actor);
                if (skill == null || !due.HasValue ||
                    !CombatScheduler.HasReached(Time, due.Value)) continue;
                double period = (skill.Number("intervalSeconds") ??
                    skill.Number("periodSeconds")).Value;
                additionalAutoReadyAt[InstanceKey(actor.Id) + ":" + skillId] =
                    due.Value + period;
                count++;
                var candidates = BuildTargetCandidates(actor);
                string target = attackPipeline.SelectDefaultTarget(
                    InstanceKey(actor.Id), candidates, TargetPriority.Nearest);
                SpatialCombatEnemy primary;
                if (target == null || !enemies.TryGetValue(target, out primary) ||
                    !primary.Enemy.IsAlive) continue;
                var targets = AdditionalAutoCircle(actor, primary,
                    skill.Number("radius").Value,
                    actor.Catalog.Id == "MY-05");
                if (actor.Catalog.Id == "TR-02")
                {
                    var damage = skill.Components[0];
                    for (int hit = 0; hit < (int)damage.Number("hits").Value; hit++)
                        AdditionalAutoDamage(actor, targets, skill.Name,
                            damage.Number("amountPerHit").Value, "magical");
                    AdditionalAutoStatus(actor, primary, skill,
                        skill.Components[1], targets);
                    AdditionalAutoStatus(actor, primary, skill,
                        skill.Components[2], targets);
                }
                else
                {
                    var damage = skill.Components[0];
                    var range = damage.Get("multiplierRange") as List<object>;
                    if (range == null || range.Count != 2) continue;
                    double scale = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                        1, Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                    AdditionalAutoDamage(actor, targets, skill.Name,
                        damage.Number("amount").Value * scale, "magical");
                    ExecuteBulkAutoAfterSkill(actor, primary, skill);
                }
            }
            return count;
        }
    }
}
