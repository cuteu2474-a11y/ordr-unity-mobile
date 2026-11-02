using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // These autonomous actors share the *existing* scheduler, dice resolver,
    // HP/death pipeline and same-lane targeting. They never enter the roster.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceIndependentAttacker
        {
            public string Key;
            public string Rawcode;
            public bool StoryLane;
            public double X;
            public double Y;
            public double? ExpiresAt;
            public bool Blocked;
            public CharacterSkillProfile SkillProfile;
        }

        private readonly Dictionary<string, SourceIndependentAttacker> sourceIndependentAttackers =
            new Dictionary<string, SourceIndependentAttacker>(StringComparer.Ordinal);

        private static string SatelliteAttackKey(long id) => "practice-satellite:" + id;
        private static string SummonAttackKey(long id) => "practice-summon:" + id;

        private void RegisterSourceIndependentAttack(string key, string rawcode,
            bool storyLane, double x, double y, double? expiresAt = null,
            CharacterSkillProfile skillProfile = null)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                (expiresAt.HasValue && expiresAt.Value <= scheduler.Time))
                throw new InvalidOperationException("연습 독립 공격자 생성 조건이 맞지 않습니다.");
            var attack = sourceModelRules.SourceObjectAttack(rawcode);
            if (attack == null) throw new InvalidOperationException("원본 독립 공격 주사위 미확인: " + rawcode);
            // The first full cooldown is a documented practice scheduling
            // convention. The native Warcraft initial attack phase is unknown.
            attackPipeline.RegisterUnit(key, new UnitCombatSpec(rawcode, attack,
                Array.Empty<CombatSkillSpec>(), SourceModelBasicCombatRules.Evidence),
                attack.IntervalSeconds);
            unitStatuses.Add(key, new CombatStatusEngine());
            sourceIndependentAttackers.Add(key, new SourceIndependentAttacker {
                Key = key, Rawcode = rawcode, StoryLane = storyLane,
                X = x, Y = y, ExpiresAt = expiresAt, SkillProfile = skillProfile
            });
        }

        private void UnregisterSourceIndependentAttack(string key)
        {
            if (!sourceIndependentAttackers.Remove(key)) return;
            attackPipeline.RemoveUnit(key);
            unitStatuses.Remove(key);
        }

        private bool CanSourceIndependentAttack(SourceIndependentAttacker actor,
            SpatialCombatEnemy enemy)
        {
            if (!enemy.Enemy.IsAlive) return false;
            return actor.StoryLane ? enemy.Kind == CharacterEnemyKind.Story :
                enemy.Kind == CharacterEnemyKind.Line ||
                enemy.Kind == CharacterEnemyKind.Boss ||
                enemy.Kind == CharacterEnemyKind.Enraged;
        }

        private List<TargetCandidate> SourceIndependentTargets(SourceIndependentAttacker actor)
        {
            var candidates = new List<TargetCandidate>();
            foreach (var enemy in enemies.Values)
            {
                if (!CanSourceIndependentAttack(actor, enemy)) continue;
                double dx = enemy.X - actor.X, dy = enemy.Y - actor.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (double.IsInfinity(distanceSquared)) continue;
                candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                    enemy.PathProgress, true));
            }
            candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return candidates;
        }

        private double? NextSourceIndependentAttackAt()
        {
            double? next = null;
            foreach (var actor in sourceIndependentAttackers.Values)
            {
                if (actor.Blocked || SourceIndependentTargets(actor).Count == 0) continue;
                double? at = attackPipeline.NextAttackAt(actor.Key);
                if (!at.HasValue || (actor.ExpiresAt.HasValue &&
                    !CombatScheduler.HasReached(actor.ExpiresAt.Value, at.Value))) continue;
                if (!next.HasValue || at.Value < next.Value) next = at;
            }
            return next;
        }

        private int ExecuteReadySourceIndependentAttacks(int budget)
        {
            int fired = 0;
            var keys = new List<string>(sourceIndependentAttackers.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (fired >= budget) break;
                SourceIndependentAttacker actor;
                if (!sourceIndependentAttackers.TryGetValue(key, out actor) || actor.Blocked)
                    continue;
                var due = attackPipeline.NextAttackAt(key);
                if (!due.HasValue || !CombatScheduler.HasReached(scheduler.Time, due.Value))
                    continue;
                var candidates = SourceIndependentTargets(actor);
                if (candidates.Count == 0) continue;
                BasicAttackResult hit;
                try
                {
                    if (!attackPipeline.TryBasicAttack(key, candidates, out hit)) continue;
                }
                catch (NotSupportedException ex)
                {
                    actor.Blocked = true;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, key, null, null,
                        "MODEL_ONLY 독립 평타 피해 미연결: " + ex.Message, 0));
                    continue;
                }
                fired++;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    key, hit.TargetId, null, "MODEL_ONLY " + actor.Rawcode + " 기본 공격", hit.ActualDamage));
                SpatialCombatEnemy victim;
                if (enemies.TryGetValue(hit.TargetId, out victim))
                    CompleteSourceIndependentHit(actor, victim);
                if (enemies.TryGetValue(hit.TargetId, out victim) && !victim.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        key, hit.TargetId, null, "MODEL_ONLY 독립 공격 처치", 0));
            }
            RemoveDefeatedEnemies();
            return fired;
        }

        // Shared tooltip-damage pipeline for autonomous units and approved
        // fallback packets. It preserves lane, armor, status, actual HP loss,
        // kill accounting and sorted target enumeration. Never raw-subtract HP.
        private void ApplySourceModelAreaPacket(string attackerId, bool storyLane,
            SpatialCombatEnemy center, double radius, double rawDamage,
            Ordr323AttackType attackType, string skillName,
            double primaryBossMaxHealthPercent = 0,
            double primaryEnragedMaxHealthPercent = 0)
        {
            if (radius < 0 || rawDamage < 0 || double.IsNaN(rawDamage) ||
                double.IsInfinity(rawDamage)) throw new ArgumentOutOfRangeException(nameof(rawDamage));
            var targets = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive ||
                    (radius == 0 && target.Id != center.Id) ||
                    (storyLane ? target.Kind != CharacterEnemyKind.Story :
                    target.Kind != CharacterEnemyKind.Line &&
                    target.Kind != CharacterEnemyKind.Boss &&
                    target.Kind != CharacterEnemyKind.Enraged)) continue;
                double dx = target.X - center.X, dy = target.Y - center.Y;
                if (dx * dx + dy * dy <= radius * radius) targets.Add(target);
            }
            targets.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in targets)
            {
                Ordr323ArmorType armor;
                if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        target.Enemy.Defenses.WarcraftArmorType, out armor))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        attackerId, target.Id, skillName,
                        "TOOLTIP_MODEL_ONLY 방어 자료 부족: 범위 피해 보류", 0));
                    continue;
                }
                double amount = rawDamage;
                if (target.Id == center.Id && target.Kind == CharacterEnemyKind.Boss)
                    amount += target.Enemy.MaxHealth * primaryBossMaxHealthPercent / 100.0;
                if (target.Id == center.Id && target.Kind == CharacterEnemyKind.Enraged)
                    amount += target.Enemy.MaxHealth * primaryEnragedMaxHealthPercent / 100.0;
                double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                    amount, attackType, armor, true, true,
                    target.Enemy.Defenses.BaseArmor.Value -
                    SourceModelUnitEffects.ResolvePracticeArmorReduction(
                        target.Enemy.Status.Snapshot()), false, 0, 1);
                if (double.IsNaN(modeled) || double.IsInfinity(modeled) ||
                    modeled >= long.MaxValue)
                    throw new OverflowException("모델 독립 스킬 피해 범위 초과");
                long actual;
                if (!resolution.TryApplySkillDamage(attackerId, target.Id,
                        (long)Math.Floor(modeled), out actual)) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                    attackerId, target.Id, skillName,
                    "TOOLTIP_MODEL_ONLY 원작 최종 면역·타이밍 미검증", actual));
                if (!target.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        attackerId, target.Id, skillName, "모델 추가 피해로 처치", actual));
            }
        }

        private void ApplySourceModelFixedPrimaryPacket(string attackerId,
            SpatialCombatEnemy target, long damage, string skill)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (!target.Enemy.IsAlive) return;
            long actual;
            if (!resolution.TryApplySkillDamage(attackerId, target.Id, damage, out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                attackerId, target.Id, skill,
                "TOOLTIP_MODEL_ONLY 고정 피해 / 원작 면역·방어 무시 조건 미검증", actual));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    attackerId, target.Id, skill, "모델 고정 피해 처치", actual));
        }

        private void CompleteSourceIndependentHit(SourceIndependentAttacker actor,
            SpatialCombatEnemy primary)
        {
            if (actor.Rawcode == "h085")
            {
                var skill = FindProfileSkill(actor.SkillProfile, "인술 분신");
                if (skill == null || skill.Components.Count != 2 ||
                    skill.Components[0].Type != "summon" ||
                    skill.Components[0].Number("basicAttackRadius") != 325 ||
                    skill.Components[0].Number("basicAttackMagicalDamage") != 50000) return;
                ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                    325, 50000, Ordr323AttackType.Spells, "인술 분신 범위 마법");
                var followup = FindProfileSkill(actor.SkillProfile, "급습");
                if (followup != null && followup.Trigger == "onAttack" &&
                    followup.ChancePercent == 15 &&
                    followup.Number("summonedCloneProcChanceMultiplier") == .5 &&
                    followup.Number("summonedCloneEffectMultiplier") == .5 &&
                    followup.Components.Count == 3 &&
                    followup.Components[0].Type == "magical" &&
                    followup.Components[0].Number("amount") == 400000 &&
                    followup.Components[1].Number("percent") == 7 &&
                    followup.Components[2].Number("percent") == 1.25 &&
                    sourceModelRules.NextScriptRoll(1000) < 75)
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, "분신 급습",
                        "TOOLTIP_MODEL_ONLY 분신 7.5% / 피해 50%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        0, 200000, Ordr323AttackType.Spells, "분신 급습");
                    double extra = primary.Kind == CharacterEnemyKind.Line
                        ? primary.Enemy.MaxHealth * .035 :
                        primary.Kind == CharacterEnemyKind.Boss
                            ? primary.Enemy.MaxHealth * .00625 : 0;
                    if (extra > 0 && primary.Enemy.IsAlive)
                        ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                            0, extra, Ordr323AttackType.Spells, "분신 급습 · 비례 피해");
                }
                return;
            }
            if (actor.Rawcode != "H0AB" && actor.Rawcode != "H0AE") return;
            if (actor.Rawcode == "H0AB")
            {
                var stun = FindProfileSkill(actor.SkillProfile, "릴리스 단일 스턴");
                if (stun != null && stun.Trigger == "satelliteOnAttack" &&
                    stun.ChancePercent == 22 && stun.Components.Count == 2 &&
                    stun.Components[0].Type == "physical" &&
                    stun.Components[0].Number("amount") == 220000 &&
                    stun.Components[1].Type == "stun" &&
                    stun.Components[1].Number("durationSeconds") == .75 &&
                    SourceRollSkill(stun))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, stun.Name, "TOOLTIP_MODEL_ONLY 릴리스 22%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        0, 220000, Ordr323AttackType.Spells, stun.Name);
                    ApplySourceModelLineStun(actor.Key, primary, stun.Name, .75);
                }
                var area = FindProfileSkill(actor.SkillProfile, "릴리스 광역 마법");
                if (area != null && area.Trigger == "satelliteOnAttack" &&
                    area.ChancePercent == 11 && area.Radius == 475 &&
                    area.Components.Count == 2 &&
                    area.Components[0].Type == "magical" &&
                    area.Components[0].Number("amount") == 220000 &&
                    area.Components[1].Type == "magical" &&
                    area.Components[1].Number("extraBossMaxHealthPercent") == 2 &&
                    area.Components[1].Number("extraEnragedMaxHealthPercent") == 4.4 &&
                    SourceRollSkill(area))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, area.Name, "TOOLTIP_MODEL_ONLY 릴리스 11%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        475, 220000, Ordr323AttackType.Spells, area.Name, 2, 4.4);
                }
            }
            else
            {
                var explosive = FindProfileSkill(actor.SkillProfile, "아틀라스 평타·치명타");
                if (explosive != null && explosive.Trigger == "satelliteOnAttack" &&
                    explosive.ChancePercent == 25 && explosive.Components.Count == 3 &&
                    explosive.Components[0].Type == "explosive" &&
                    explosive.Components[0].Number("attackPowerPercent") == 45 &&
                    explosive.Components[0].Number("radius") == 400 &&
                    explosive.Components[1].Type == "critical" &&
                    explosive.Components[1].Number("multiplier") == 2.5 &&
                    explosive.Components[2].Type == "stun" &&
                    explosive.Components[2].Number("durationSeconds") == 1 &&
                    SourceRollSkill(explosive))
                {
                    // The map object dice are known, but native attack-power
                    // conversion and crit occurrence order are not witnessed.
                    double power = sourceModelRules.RollSourceObjectAttackPower("H0AE");
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, explosive.Name,
                        "TOOLTIP_MODEL_ONLY 25% / 공격력45% × 2.5 모델", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        400, power * .45 * 2.5, Ordr323AttackType.Spells,
                        explosive.Name);
                    foreach (var target in enemies.Values)
                    {
                        double dx = target.X - primary.X, dy = target.Y - primary.Y;
                        if (dx * dx + dy * dy <= 400 * 400 &&
                            CanSourceIndependentAttack(actor, target))
                            ApplySourceModelLineStun(actor.Key, target, explosive.Name, 1);
                    }
                }
                var haste = FindProfileSkill(actor.SkillProfile, "아틀라스 날뛰기");
                if (haste != null && haste.Trigger == "satelliteOnAttack" &&
                    haste.ChancePercent == 15 && haste.Components.Count == 2 &&
                    haste.Components[0].Type == "selfAttackSpeedBuff" &&
                    haste.Components[0].Number("percent") == 45 &&
                    haste.Components[0].Number("durationSeconds") == 2.25 &&
                    SourceRollSkill(haste))
                {
                    string key = "practice-aura:atlas-haste:" + actor.Key;
                    var status = unitStatuses[actor.Key];
                    status.RemoveSource(key);
                    status.Apply(new CombatEffect(key, CombatEffectType.AttackSpeedBuff,
                        CombatEffectOrigin.TriggeredSkill, 45, 2.25));
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                        actor.Key, actor.Key, haste.Name,
                        "TOOLTIP_MODEL_ONLY 45% 공격속도 2.25초 / 힘 계수 피해 보류", 0));
                    if (haste.Components[1].Type == "magical" &&
                        haste.Components[1].Number("amount") == 400000 &&
                        haste.Components[1].Number("strengthScalingPerPoint") == 4000)
                        ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                            0, 400000, Ordr323AttackType.Spells, haste.Name);
                }
            }
        }

        private static ImportedCombatSkill FindProfileSkill(CharacterSkillProfile profile,
            string name)
        {
            if (profile == null) return null;
            foreach (var skill in profile.Skills)
                if (skill.Name == name) return skill;
            return null;
        }

        private void ApplySourceModelLineStun(string actorKey,
            SpatialCombatEnemy target, string skill, double seconds)
        {
            if (!target.Enemy.IsAlive || target.Kind != CharacterEnemyKind.Line) return;
            string key = "practice-skill-stun:" + actorKey + ":" + skill;
            target.Enemy.Status.RemoveSource(key);
            target.Enemy.Status.Apply(new CombatEffect(key, CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, seconds));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                actorKey, target.Id, skill,
                "TOOLTIP_MODEL_ONLY 라인 기절 / 원본 면역·저항 무시 미검증", 0));
        }
    }
}
