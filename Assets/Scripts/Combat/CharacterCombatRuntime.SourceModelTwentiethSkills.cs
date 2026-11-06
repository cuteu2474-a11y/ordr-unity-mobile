using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // No independent "practice gold" value: Tezoro samples the one
        // shared, transaction-backed wallet at each actual skill activation.
        private Func<long> sourceModelTwentiethPlayerGold;
        private readonly Dictionary<long, int> sourceModelTwentiethSoulStacks =
            new Dictionary<long, int>();
        private readonly Dictionary<long, double> sourceModelTwentiethSoulBonusReadyAt =
            new Dictionary<long, double>();
        private readonly Dictionary<long, int> sourceModelTwentiethTezoroPeriodCount =
            new Dictionary<long, int>();

        public void ConfigureSourceModelPlayerGold(Func<long> readSharedGold)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelTwentiethPlayerGold != null ||
                readSharedGold == null || processingStep)
                throw new InvalidOperationException("독립 전투의 공유 골드 공급자를 연결할 수 없습니다.");
            sourceModelTwentiethPlayerGold = readSharedGold;
        }

        public int SourceModelSoulStacksOf(long instanceId)
        {
            CharacterInstance actor;
            int stacks;
            return UsesSourceModelBasicAttacks &&
                characters.TryGetValue(instanceId, out actor) &&
                actor.Catalog.Id == "E-03" &&
                sourceModelTwentiethSoulStacks.TryGetValue(instanceId, out stacks)
                ? stacks : 0;
        }

        private double SourceModelTwentiethFlatAttackPower(string actorKey)
        {
            long id;
            CharacterInstance actor;
            if (!long.TryParse(actorKey, out id) ||
                !characters.TryGetValue(id, out actor))
                return 0;
            double existing = actor.Catalog.Id == "E-03" &&
                SourceModelTwentiethSkillCatalog.HasExactPassive(actor.Profile, "E-03") &&
                SourceModelSoulStacksOf(id) >= 700 ? 100000 : 0;
            double heroGrowth = original323HeroLevelWorld == null ? 0 :
                original323HeroLevelWorld.AdditionalBasicAttackDamageOf(id);
            return existing + SourceModelTwentyFifthFlatAttackPower(actor) + heroGrowth;
        }

        // Called both for actors already present before the practice battle
        // begins and newly spawned actors. Never add preview effects in the
        // original-observed / verified Warcraft combat mode.
        private void SourceModelTwentiethAttachPassives(CharacterInstance actor)
        {
            if (!UsesSourceModelBasicAttacks || actor == null || actor.Profile == null)
                return;
            if (actor.Catalog.Id == "E-11" &&
                SourceModelTwentiethSkillCatalog.HasExactPassive(actor.Profile, "E-11"))
            {
                string key = "practice-tooltip:tezoro-speed:" + actor.Id;
                var status = unitStatuses[InstanceKey(actor.Id)];
                status.RemoveSource(key);
                status.Apply(new CombatEffect(key, CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.Aura, 25, 0));
            }
        }

        private bool SourceModelTwentiethCanActivate(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            return actor != null && actor.Profile != null &&
                SourceModelTwentiethSkillCatalog.CanModelExact(skill) &&
                SourceModelTwentiethSkillCatalog.HasExactPassive(actor.Profile,
                    actor.Catalog.Id) &&
                (actor.Catalog.Id != "E-11" || sourceModelTwentiethPlayerGold != null) &&
                (skill.Id != "E-03:skill:6" ||
                 SourceModelSoulStacksOf(actor.Id) >= 100);
        }

        private static bool SourceModelTwentiethChance(SourceModelBasicCombatRules rules,
            double chance)
        {
            return rules.NextScriptRoll(1000000) <
                (int)Math.Round(chance * 10000, MidpointRounding.AwayFromZero);
        }

        private long SourceModelTwentiethGold(int cap)
        {
            if (sourceModelTwentiethPlayerGold == null)
                throw new InvalidOperationException("공유 골드 공급자가 연결되지 않았습니다.");
            long gold = sourceModelTwentiethPlayerGold();
            if (gold < 0)
                throw new InvalidOperationException("공유 골드 장부의 값이 음수입니다.");
            return Math.Min(gold, cap);
        }

        private void SourceModelTwentiethEarnSoul(CharacterInstance actor,
            ImportedCombatSkill skill, int add)
        {
            if (actor.Catalog.Id != "E-03" || add <= 0 ||
                !SourceModelTwentiethSkillCatalog.HasExactPassive(actor.Profile, "E-03"))
                return;
            int previous = SourceModelSoulStacksOf(actor.Id);
            int next = checked(previous + add);
            sourceModelTwentiethSoulStacks[actor.Id] = next;
            if (previous < 400 && next >= 400)
            {
                string key = "practice-tooltip:ryuma-speed:" + actor.Id;
                var status = unitStatuses[InstanceKey(actor.Id)];
                status.RemoveSource(key);
                status.Apply(new CombatEffect(key, CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.Aura, 50, 0));
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), InstanceKey(actor.Id), skill.Name,
                "TOOLTIP_MODEL_ONLY 영혼 " + previous + " → " + next +
                " / 100 참룡도 · 400 공속 +50% · 700 공격력 +100,000", next));
        }

        private void SourceModelTwentiethDamage(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill,
            double raw, string school)
        {
            if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target) ||
                raw <= 0) return;
            if (double.IsInfinity(raw) || double.IsNaN(raw) || raw >= long.MaxValue)
                throw new OverflowException("20차 연습 피해량이 유효한 범위를 초과했습니다.");
            if (school != "fixed")
            {
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name, raw, school);
                return;
            }
            long actual;
            if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id), target.Id,
                    (long)Math.Floor(raw), out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                InstanceKey(actor.Id), target.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 원본 저항·피해 순서 미검증 고정 피해", actual));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "연습 고정 피해로 처치", actual));
        }

        private void SourceModelTwentiethStun(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill, double seconds,
            bool ignoresResistance = false)
        {
            if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target) ||
                (!ignoresResistance && target.Kind != CharacterEnemyKind.Line)) return;
            string key = "practice-tooltip-stun:" + actor.Id + ":" + skill.Id;
            target.Enemy.Status.RemoveSource(key);
            target.Enemy.Status.Apply(new CombatEffect(key,
                CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill, 1, seconds));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), target.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 기절·원본 면역 판정 미검증", 0));
        }

        // A complete source-approved packet is dispatched once, never both
        // here and through the older generic tooltip composite executor.
        private void ApplySourceModelTwentiethSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            if (!SourceModelTwentiethCanActivate(actor, skill)) return;
            int souls = SourceModelSoulStacksOf(actor.Id);
            double multiplier = 1;
            if (actor.Catalog.Id == "E-11")
                multiplier = 1 + sourceModelRules.NextScriptRoll(1000000) / 2000000.0;
            var bulletItems = actor.Catalog.Id == "I-05" ?
                SourceModelEquippedItemsOf(actor.Id) : null;

            // E-03:skill:5 has no target packet; its sole effect is a stack.
            if (skill.Id == "E-03:skill:5")
            {
                SourceModelTwentiethEarnSoul(actor, skill, 1);
                return;
            }

            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target)) continue;
                switch (skill.Id)
                {
                    case "E-03:skill:0":
                        SourceModelTwentiethDamage(actor, target, skill,
                            2500000 + target.Enemy.MaxHealth * souls * .005 / 100,
                            "magical");
                        break;
                    case "E-03:skill:1":
                        SourceModelTwentiethDamage(actor, target, skill,
                            160000 + souls * 160.0, "fixed");
                        break;
                    case "E-03:skill:2":
                        SourceModelTwentiethDamage(actor, target, skill, 180000, "magical");
                        break;
                    case "E-03:skill:3":
                        SourceModelTwentiethDamage(actor, target, skill,
                            400000 + souls * 450.0, "magical");
                        break;
                    case "E-03:skill:4":
                        SourceModelTwentiethDamage(actor, target, skill,
                            250000 + souls * 2500000.0, "magical");
                        if (target.Kind == CharacterEnemyKind.Line)
                            SourceModelTwentiethDamage(actor, target, skill,
                                (target.Enemy.MaxHealth - target.Enemy.Health) * .09,
                                "magical");
                        break;
                    case "E-03:skill:6":
                        SourceModelTwentiethDamage(actor, target, skill,
                            SourceModelUpgradeAdjustedAttackPower(actor) * 12.0,
                            "physical");
                        SourceModelTwentiethStun(actor, target, skill, 3, true);
                        break;
                    case "E-11:skill:0":
                        SourceModelTwentiethDamage(actor, target, skill,
                            2500000 * multiplier, "magical");
                        SourceModelTwentiethStun(actor, target, skill, 2.4);
                        SourceModelTwentiethDamage(actor, target, skill,
                            (target.Enemy.MaxHealth - target.Enemy.Health) *
                                (SourceModelTwentiethGold(250000) / 25000.0) * .009,
                            "magical");
                        break;
                    case "E-11:skill:1":
                        SourceModelTwentiethDamage(actor, target, skill,
                            200000 * multiplier, "magical");
                        SourceModelTwentiethStun(actor, target, skill, 2.4);
                        SourceModelTwentiethDamage(actor, target, skill,
                            SourceModelTwentiethGold(250000) * 2.0, "fixed");
                        break;
                    case "E-11:skill:2":
                        SourceModelTwentiethDamage(actor, target, skill,
                            200000 * multiplier, "magical");
                        if (ReferenceEquals(primary, target))
                        {
                            if (target.Kind == CharacterEnemyKind.Line)
                                SourceModelTwentiethDamage(actor, target, skill,
                                    target.Enemy.MaxHealth * .15, "magical");
                            else if (target.Kind == CharacterEnemyKind.Boss ||
                                     target.Kind == CharacterEnemyKind.Enraged ||
                                     target.Kind == CharacterEnemyKind.Story)
                                SourceModelTwentiethDamage(actor, target, skill,
                                    400000, "fixed");
                        }
                        break;
                    case "E-11:skill:3":
                        SourceModelTwentiethDamage(actor, target, skill,
                            500000 * multiplier, "magical");
                        SourceModelTwentiethDamage(actor, target, skill,
                            (target.Enemy.MaxHealth - target.Enemy.Health) *
                                (SourceModelTwentiethGold(250000) / 25000.0) * .0045,
                            "magical");
                        if (ReferenceEquals(primary, target) &&
                            target.Kind == CharacterEnemyKind.Line)
                            SourceModelTwentiethDamage(actor, target, skill,
                                target.Enemy.MaxHealth * .20, "magical");
                        break;
                    case "I-05:skill:0":
                        SourceModelTwentiethDamage(actor, target, skill,
                            1500000 * (1 + bulletItems.Attack * .05), "physical");
                        for (int hit = 0; hit < 12 && target.Enemy.IsAlive; hit++)
                            SourceModelTwentiethDamage(actor, target, skill,
                                100000 * (1 + bulletItems.Attack * .01), "physical");
                        SourceModelTwentiethStun(actor, target, skill, 1);
                        if (bulletItems.AllUnique)
                            ScheduleSourceModelBulletUniqueStrike(actor, target, skill);
                        if (bulletItems.AllLegendary)
                        {
                            SourceModelTwentiethDamage(actor, target, skill,
                                target.Enemy.MaxHealth * .10, "physical");
                            SourceModelTwentiethStun(actor, target, skill, 3, true);
                        }
                        break;
                    case "I-05:skill:1":
                        if (target.Kind == CharacterEnemyKind.Boss ||
                            target.Kind == CharacterEnemyKind.Story)
                        {
                            if (bulletItems.HasUnique)
                            {
                                double bonus = 250000 + target.Enemy.Health * .01;
                                SourceModelTwentiethDamage(actor, target, skill,
                                    bonus, "physical");
                                if (bulletItems.HasLegendary &&
                                    SourceModelTwentiethChance(sourceModelRules, 50))
                                    SourceModelTwentiethDamage(actor, target, skill,
                                        bonus, "physical");
                            }
                            break;
                        }
                        double conq = (250000 + target.Enemy.Health * .02) *
                            (1 + bulletItems.Attack * .01);
                        SourceModelTwentiethDamage(actor, target, skill, conq, "physical");
                        if (bulletItems.HasLegendary &&
                            SourceModelTwentiethChance(sourceModelRules, 50))
                            SourceModelTwentiethDamage(actor, target, skill, conq,
                                "physical");
                        break;
                    case "I-05:skill:2":
                        // The 7% roll refreshes a three-second stance. The
                        // per-attack stance packet runs once AFTER the rolls.
                        if (ReferenceEquals(primary, target))
                            ActivateSourceModelBulletRokushiki(actor, skill);
                        break;
                    case "I-05:skill:3":
                        double basic = SourceModelUpgradeAdjustedAttackPower(actor);
                        if (SourceModelTwentiethChance(sourceModelRules, 15)) basic *= 4;
                        SourceModelTwentiethDamage(actor, target, skill, basic, "physical");
                        if (SourceModelTwentiethChance(sourceModelRules, 15))
                            SourceModelTwentiethStun(actor, target, skill, 2);
                        break;
                }
            }
            if (skill.Id == "E-03:skill:3")
            {
                double readyAt;
                if ((!sourceModelTwentiethSoulBonusReadyAt.TryGetValue(actor.Id,
                        out readyAt) || readyAt <= Time) &&
                    SourceModelTwentiethChance(sourceModelRules, 20))
                {
                    sourceModelTwentiethSoulBonusReadyAt[actor.Id] = Time + 5;
                    SourceModelTwentiethEarnSoul(actor, skill, 2);
                }
            }
        }

        private void SourceModelTwentiethAfterPeriodic(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill periodic)
        {
            if (periodic.Id != "E-11:skill:2" ||
                !SourceModelTwentiethCanActivate(actor, periodic)) return;
            int count;
            sourceModelTwentiethTezoroPeriodCount.TryGetValue(actor.Id, out count);
            count++;
            if (count < 2)
            {
                sourceModelTwentiethTezoroPeriodCount[actor.Id] = count;
                return;
            }
            sourceModelTwentiethTezoroPeriodCount[actor.Id] = 0;
            ImportedCombatSkill chained = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == "E-11:skill:3") { chained = candidate; break; }
            if (!SourceModelTwentiethCanActivate(actor, chained)) return;
            if (!primary.Enemy.IsAlive)
            {
                var choices = BuildTargetCandidates(actor);
                string chosen = choices.Count == 0 ? null : ChoosePreviewTarget(actor, choices);
                if (chosen == null || !enemies.TryGetValue(chosen, out primary) ||
                    !primary.Enemy.IsAlive) return;
            }
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, chained.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, chained.Name,
                "TOOLTIP_MODEL_ONLY / 곤 인페르노 2회 완료 · 공유 골드 현재값", 0));
            ApplySourceModelTwentiethSkill(actor, primary, chained, targets);
        }

        private void SourceModelTwentiethAfterBasicHit(string actorKey,
            string rawcode, string targetId, long actual)
        {
            if (!UsesSourceModelBasicAttacks || !processingStep) return;
            if (rawcode == "h081")
            {
                SourceModelBulletAfterConfirmedBasicHit(actorKey, targetId, actual);
                return;
            }
            if (actual < 120000 || rawcode != "h0CJ") return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(actorKey, out id) ||
                !characters.TryGetValue(id, out actor) || !actor.AttackReady ||
                actor.Catalog.Id != "E-03" ||
                !enemies.TryGetValue(targetId, out primary) ||
                !CanAttackInThisLane(actor, primary)) return;
            ImportedCombatSkill skill = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == "E-03:skill:2") { skill = candidate; break; }
            if (!SourceModelTwentiethCanActivate(actor, skill) ||
                actual < skill.Number("minimumBasicDamage").Value) return;
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                actorKey, primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 기본 공격 실제 명중 피해 120,000 이상", actual));
            ApplySourceModelTwentiethSkill(actor, primary, skill, targets);
        }

        private void ForgetSourceModelTwentiethActor(long actorId)
        {
            ForgetSourceModelFusionNativeActor(actorId);
            sourceModelTwentiethSoulStacks.Remove(actorId);
            sourceModelTwentiethSoulBonusReadyAt.Remove(actorId);
            sourceModelTwentiethTezoroPeriodCount.Remove(actorId);
            ForgetSourceModelTwentyFirstEquipment(actorId);
        }
    }
}

