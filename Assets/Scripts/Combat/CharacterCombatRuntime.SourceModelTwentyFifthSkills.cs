using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceModelLawRoom
        {
            public double ExpiresAt;
            public long LaneRevision;
            public bool StoryLane;
            public bool FollowupUsed;
        }

        // A declaration may cause other JASS/tooltip procs before the native
        // damage resolves. Keep this decision tied to ONE attack and target:
        // the 35% packet replaces the basic magic splash, not the native hit.
        private sealed class SourceModelKidBasicReplacement
        {
            public string TargetId;
            public long LaneRevision;
            public bool StoryLane;
        }

        private readonly Dictionary<long, SourceModelLawRoom> sourceModelLawRooms =
            new Dictionary<long, SourceModelLawRoom>();
        private readonly Dictionary<long, int> sourceModelKidMagnetCounters =
            new Dictionary<long, int>();
        private readonly Dictionary<long, SourceModelKidBasicReplacement>
            sourceModelKidBasicReplacements =
                new Dictionary<long, SourceModelKidBasicReplacement>();
        private readonly Dictionary<long, HashSet<string>> sourceModelKidAuraVictims =
            new Dictionary<long, HashSet<string>>();
        // The source profile describes trait replacement, but does not state
        // a sufficient purchase/eligibility formula. Only its verified owner
        // may attest that a particular unit has purchased the enhancement.
        private Func<long, bool> sourceModelKidEnhancedEligibility;

        public void ConfigureSourceModelKidEnhancedEligibility(Func<long, bool> eligible)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceModelKidEnhancedEligibility != null)
                throw new InvalidOperationException(
                    "키드 특성강화 검증자는 전투 시작 전에 한 번만 연결합니다.");
            sourceModelKidEnhancedEligibility = eligible ??
                throw new ArgumentNullException(nameof(eligible));
        }

        public bool PracticeKidEnhancedOf(long id)
        {
            CharacterInstance actor;
            return sourceModelKidEnhancedEligibility != null &&
                characters.TryGetValue(id, out actor) &&
                SourceModelTwentyFifthOwner(actor) &&
                SourceModelTwentyFifthSkillCatalog.CanModelExactKidEnhancement(
                    actor.Profile) && sourceModelKidEnhancedEligibility(id);
        }

        private bool SourceModelTwentyFifthOwner(CharacterInstance actor)
        {
            return UsesSourceModelBasicAttacks && sourceModelRules != null &&
                actor != null && actor.AttackReady && actor.Mapping != null &&
                ((actor.Catalog.Id == "TR-05" &&
                  actor.Mapping.AttackSpec.UnitRawCode == "H096") ||
                 (actor.Catalog.Id == "TR-25" &&
                  actor.Mapping.AttackSpec.UnitRawCode == "H0B4")) &&
                SourceModelTwentyFifthSkillCatalog.HasExactOwner(
                    actor.Profile, actor.Catalog.Id);
        }

        private SourceModelLawRoom SourceModelTwentyFifthRoom(long actorId)
        {
            SourceModelLawRoom state;
            if (!sourceModelLawRooms.TryGetValue(actorId, out state)) return null;
            if (state.ExpiresAt > Time &&
                state.LaneRevision == SourceModelLaneRevision(actorId) &&
                state.StoryLane == IsStoryDeployed(actorId)) return state;
            sourceModelLawRooms.Remove(actorId);
            return null;
        }

        private double SourceModelTwentyFifthFlatAttackPower(CharacterInstance actor)
        {
            if (!SourceModelTwentyFifthOwner(actor)) return 0;
            if (actor.Catalog.Id == "TR-25")
                return actor.Profile.Passives[1].Number("amount").Value;
            return SourceModelTwentyFifthRoom(actor.Id) == null ? 0 :
                actor.Profile.Skills[0].Components[0].Number("amount").Value;
        }

        // The four source skills of each owner are checked as a WHOLE.
        // Keep the same HP/MP ledger and lane-bound target selection as other
        // source actives, but do not call the generic executor that would
        // silently discard the ROOM prerequisite and Kid's 0.5 s cast time.
        private bool TryCastSourceModelTwentyFifthActive(CharacterInstance actor,
            ImportedCombatSkill skill, string primaryId, out AbilityCastResult result)
        {
            result = AbilityCastResult.MissingOrUnsupportedRequirement;
            if (!SourceModelTwentyFifthOwner(actor) ||
                !SourceModelTwentyFifthSkillCatalog.CanModelExact(skill)) return true;
            var resources = actor.Abilities.Resources;
            if (resources == null)
            {
                result = AbilityCastResult.NoResourceState;
                return true;
            }
            SourceModelLawRoom room = skill.Id == "TR-05:skill:1" ?
                SourceModelTwentyFifthRoom(actor.Id) : null;
            if (skill.Id == "TR-05:skill:1" &&
                (room == null || room.FollowupUsed)) return true;

            SpatialCombatEnemy primary;
            if (string.IsNullOrWhiteSpace(primaryId) ||
                !enemies.TryGetValue(primaryId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            bool kidEnhanced = actor.Catalog.Id == "TR-25" &&
                PracticeKidEnhancedOf(actor.Id);
            double hp = kidEnhanced && skill.Id == "TR-25:skill:0" ? 50 :
                skill.Number("healthCost") ?? 0;
            double mp = skill.Number("manaCost") ?? 0;
            if (!resources.CanSpend(hp, mp))
            {
                result = AbilityCastResult.InsufficientResources;
                return true;
            }
            var shape = skill.Id == "TR-05:skill:0" ?
                SourceScriptHitShape.AttackerCircle : SourceScriptHitShape.Circle;
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                shape, kidEnhanced && skill.Id == "TR-25:skill:0" ? 525 :
                    skill.Radius.Value, 0, 0, 1, 1,
                Ordr323AttackType.Spells));
            if (skill.Id != "TR-05:skill:0" && targets.Count == 0)
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            // No costs are consumed if the documented damage cannot pass
            // the shared defense preflight. ROOM itself has no damage packet.
            if (skill.Id != "TR-05:skill:0")
                foreach (var target in targets)
                {
                    Ordr323ArmorType armor;
                    if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                        !Ordr323OriginalDamageRules.TryReadArmorType(
                            target.Enemy.Defenses.WarcraftArmorType, out armor))
                    {
                        result = AbilityCastResult.UnsupportedEffect;
                        return true;
                    }
                }
            resources.Spend(hp, mp);
            if (room != null) room.FollowupUsed = true;
            string displayName = kidEnhanced && skill.Id == "TR-25:skill:0" ?
                "펑크 크래시" : skill.Name;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, displayName,
                "TOOLTIP_MODEL_ONLY / 원본 세부 시전 위상 미검증", 0));
            switch (skill.Id)
            {
                case "TR-05:skill:0":
                    sourceModelLawRooms[actor.Id] = new SourceModelLawRoom {
                        ExpiresAt = Time + skill.Number("roomDurationSeconds").Value,
                        LaneRevision = SourceModelLaneRevision(actor.Id),
                        StoryLane = IsStoryDeployed(actor.Id)
                    };
                    ApplySourceModelCompositeAllyPower(actor, skill, skill.Components[1]);
                    ApplySourceModelCompositeStatus(actor, primary, skill,
                        skill.Components[2], targets);
                    // "selfHeal.rate" is literally null in the source profile.
                    // Do not infer a heal amount or consume it as HP recovery.
                    ReportSourceModelNextThirtyDeferred(InstanceKey(actor.Id),
                        primary.Id, "로우 ROOM 자체 치유량 원본 미기재");
                    break;
                case "TR-05:skill:1":
                    foreach (var target in targets)
                        SourceModelTwentyFifthDamage(actor, target, skill,
                            skill.Components[0].Number("amount").Value, "magical");
                    break;
                case "TR-25:skill:0":
                    bool lane = IsStoryDeployed(actor.Id);
                    long revision = SourceModelLaneRevision(actor.Id);
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                        primary.Id, skill.Number("castTimeSeconds").Value, () => {
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(primary.Id, out current) ||
                                !ReferenceEquals(primary, current) ||
                                !current.Enemy.IsAlive ||
                                !CanAttackInThisLane(actor, current)) return;
                            var hits = SourceScriptTargets(actor, current,
                                new SourceScriptHit(SourceScriptHitShape.Circle,
                                    kidEnhanced ? 525 : skill.Radius.Value,
                                    0, 0, 1, 1,
                                    Ordr323AttackType.Spells));
                            double raw = SourceModelTwentyFifthStrengthDamage(actor,
                                skill.Components[0], kidEnhanced ? 2000000 : (double?)null,
                                kidEnhanced ? 60000 : (double?)null);
                            double factor = SourceModelTwentyFifthKidAreaFactor(hits.Count);
                            foreach (var target in hits)
                            {
                                double x = target.X - current.X;
                                double y = target.Y - current.Y;
                                double bonus = x * x + y * y <=
                                    Math.Pow(kidEnhanced ? 325 :
                                        skill.Components[1].Number("centerRadius").Value, 2)
                                    ? (kidEnhanced ? 50 :
                                        skill.Components[1].Number("centerExtraPercent").Value) / 100 : 0;
                                int hitCount = kidEnhanced ? 3 : 1;
                                for (int i = 0; i < hitCount; i++)
                                {
                                    SourceModelTwentyFifthDamage(actor, target, skill,
                                        raw * (1 + bonus) * factor, "magical", displayName);
                                    SourceModelTwentyFifthStun(actor, target, skill,
                                        kidEnhanced ? 2.85 :
                                            skill.Components[2].Number("durationSeconds").Value);
                                }
                            }
                        });
                    break;
            }
            RemoveDefeatedEnemies();
            result = AbilityCastResult.Cast;
            return true;
        }

        private double SourceModelTwentyFifthStrengthDamage(CharacterInstance actor,
            ImportedCombatEffect component, double? enhancedBase = null,
            double? enhancedScaling = null)
        {
            double strength = 0;
            if (sourceModelStrengthProvider != null)
            {
                double? known = sourceModelStrengthProvider(actor.Id);
                if (known.HasValue && known.Value >= 0 &&
                    !double.IsNaN(known.Value) && !double.IsInfinity(known.Value))
                    strength = known.Value;
            }
            // Original strength only. With no witness the bonus is zero,
            // not a fabricated "typical" hero stat.
            return (enhancedBase ?? component.Number("amount").Value) +
                strength * (enhancedScaling ??
                    component.Number("strengthScalingPerPoint").Value);
        }

        private static double SourceModelTwentyFifthKidAreaFactor(int count) =>
            1 + count * .25 / 100;

        private void SourceModelTwentyFifthDamage(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill,
            double raw, string school, string displayName = null)
        {
            if (!target.Enemy.IsAlive || raw <= 0) return;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw >= long.MaxValue)
                throw new OverflowException("25차 피해량 범위 초과");
            if (school == "fixed")
                SourceModelTwentiethDamage(actor, target, skill, raw, "fixed");
            else
                ApplySourceModelSeventhTimedHit(actor, target,
                    displayName ?? skill.Name, raw, school);
        }

        private void SourceModelTwentyFifthStun(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill, double duration)
        {
            // Existing conservative practice status policy: boss resistance
            // has no original witness; do not silently stun a boss.
            if (!target.Enemy.IsAlive || target.Kind != CharacterEnemyKind.Line)
                return;
            var status = target.Enemy.Status;
            string source = "practice-tooltip-stun:" + actor.Id + ":" + skill.Id;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, duration));
        }

        private void SourceModelTwentyFifthSlow(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill,
            double percent, double duration, string variant)
        {
            if (!target.Enemy.IsAlive) return;
            string source = "practice-tooltip-slow:" + actor.Id + ":" +
                skill.Id + ":" + variant;
            target.Enemy.Status.RemoveSource(source);
            target.Enemy.Status.Apply(new CombatEffect(source,
                CombatEffectType.MovementSlowPercent,
                CombatEffectOrigin.TriggeredSkill, percent, duration));
        }

        private void ExecuteSourceModelTwentyFifthAttacks(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceModelUnconnectedSkillRow row)
        {
            if (!SourceModelTwentyFifthOwner(actor) ||
                row.CatalogId != actor.Catalog.Id)
            {
                ReportSourceModelNextThirtyDeferred(InstanceKey(actor.Id),
                    primary.Id, "25차 프로필/원본 객체 ID 불일치");
                return;
            }
            if (actor.Catalog.Id == "TR-05")
            {
                if (SourceModelTwentyFifthRoll(actor.Profile.Skills[2]))
                    SourceModelTwentyFifthLawAttack(actor, primary, actor.Profile.Skills[2]);
                if (primary.Enemy.IsAlive &&
                    SourceModelTwentyFifthRoll(actor.Profile.Skills[3]))
                    SourceModelTwentyFifthLawAttack(actor, primary, actor.Profile.Skills[3]);
            }
            else if (actor.Catalog.Id == "TR-25")
            {
                // Always invalidate the preceding declaration, including one
                // whose target died before basic damage could land.
                sourceModelKidBasicReplacements.Remove(actor.Id);
                if (SourceModelTwentyFifthRoll(actor.Profile.Skills[1]))
                {
                    if (PracticeKidEnhancedOf(actor.Id))
                        SourceModelTwentySeventhKidDamnedPunk(actor, primary,
                            actor.Profile.Skills[1]);
                    else
                        SourceModelTwentyFifthKidRepel(actor, primary,
                            actor.Profile.Skills[1]);
                }
                if (primary.Enemy.IsAlive &&
                    SourceModelTwentyFifthRoll(actor.Profile.Skills[2]))
                    SourceModelTwentyFifthKidMagnet(actor, primary, actor.Profile.Skills[2]);
                // Roll on attack declaration; apply only on a real native
                // hit through the shared successful-hit callback below.
                // Never append the packet to an already applied splash.
                if (SourceModelTwentyFifthRoll(actor.Profile.Skills[3]))
                    sourceModelKidBasicReplacements[actor.Id] =
                        new SourceModelKidBasicReplacement {
                            TargetId = primary.Id,
                            LaneRevision = SourceModelLaneRevision(actor.Id),
                            StoryLane = IsStoryDeployed(actor.Id)
                        };
            }
        }

        private bool SourceModelTwentyFifthRoll(ImportedCombatSkill skill) =>
            SourceModelTwentyFifthSkillCatalog.CanModelExact(skill) &&
            sourceModelRules.NextScriptRoll(1000000) <
            (int)Math.Round(skill.ChancePercent.Value * 10000,
                MidpointRounding.AwayFromZero);

        private void SourceModelTwentyFifthLawAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 원본 룸/상태 전이 순서 미검증", 0));
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive) continue;
                if (skill.Id == "TR-05:skill:2")
                {
                    double raw = skill.Components[0].Number("amount").Value +
                        target.Enemy.Health *
                        skill.Components[0].Number("plusTargetCurrentHealthPercent").Value / 100;
                    SourceModelTwentyFifthDamage(actor, target, skill, raw, "magical");
                    if (target.Enemy.IsAlive)
                    {
                        bool direct = ReferenceEquals(target, primary);
                        // 99% direct slow/root versus 40% area slow.
                        SourceModelTwentyFifthSlow(actor, target, skill,
                            direct ? skill.Components[1].Number("percent").Value :
                                skill.Components[2].Number("percent").Value,
                            skill.Components[1].Number("durationSeconds").Value,
                            direct ? "direct" : "area");
                        if (direct) SourceModelTwentyFifthSlow(actor, target, skill,
                            100, skill.Components[1].Number("initialRootSeconds").Value,
                            "root"); // Movement root; NOT a fake attack-disabling stun.
                    }
                }
                else
                {
                    // Two magical hits each read the target's current state;
                    // the fixed packet is separate and ignores armor.
                    for (int i = 0; i < (int)skill.Components[0].Number("hits").Value; i++)
                        SourceModelTwentyFifthDamage(actor, target, skill,
                            skill.Components[0].Number("amountPerHit").Value, "magical");
                    if (target.Enemy.IsAlive)
                        SourceModelTwentyFifthDamage(actor, target, skill,
                            skill.Components[1].Number("amount").Value +
                            target.Enemy.Health *
                            skill.Components[1].Number("plusTargetCurrentHealthPercent").Value / 100,
                            "fixed");
                }
                if (target.Kind == CharacterEnemyKind.Enraged && target.Enemy.IsAlive)
                {
                    // The source specifically grants an additional fixed
                    // current-HP packet for enraged monsters. Native armor
                    // buffs have no positive-armor type in this status engine.
                    var overrides = skill.Get("overrides") as List<object>;
                    var rage = (Dictionary<string, object>)overrides[0];
                    SourceModelTwentyFifthDamage(actor, target, skill,
                        target.Enemy.Health *
                        Convert.ToDouble(rage["extraFixedCurrentHealthPercent"]) / 100,
                        "fixed");
                }
            }
        }

        private void SourceModelTwentyFifthKidRepel(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var position = roster.Get(actor.Id);
            double dx = primary.X - position.X;
            double dy = primary.Y - position.Y;
            double length = Math.Sqrt(dx * dx + dy * dy) +
                skill.Number("lineRadius").Value;
            var line = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.ProjectileLine, skill.Number("lineRadius").Value,
                length, 0, 1, 1, Ordr323AttackType.Spells));
            var blast = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Number("explosionRadius").Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            var nearby = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Components[0].Number("checkRadius").Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            double factor = SourceModelTwentyFifthKidAreaFactor(blast.Count);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 직선 후 원형 폭발", 0));
            foreach (var target in line)
                SourceModelTwentyFifthDamage(actor, target, skill,
                    skill.Components[0].Number("lineDamage").Value * factor, "magical");
            int explosions = nearby.Count >=
                skill.Components[0].Number("extraExplosionWhenEnemyCountAtLeast").Value ? 2 : 1;
            for (int i = 0; i < explosions; i++)
                foreach (var target in blast)
                    SourceModelTwentyFifthDamage(actor, target, skill,
                        skill.Components[0].Number("explosionDamage").Value * factor,
                        "magical");
        }

        private void SourceModelTwentySeventhKidDamnedPunk(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var position = roster.Get(actor.Id);
            double dx = primary.X - position.X;
            double dy = primary.Y - position.Y;
            double length = Math.Sqrt(dx * dx + dy * dy) + 450;
            var line = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.ProjectileLine, 450,
                length, 0, 1, 1, Ordr323AttackType.Spells));
            var blast = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, 450, 0, 0, 1, 1,
                Ordr323AttackType.Spells));
            // Damned Punk replaces Repel. The old >=15 bonus explosion
            // must not leak into this source-declared replacement packet.
            double factor = SourceModelTwentyFifthKidAreaFactor(blast.Count);
            double blastFactor = factor * (1 + blast.Count * .01);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, "댐드 펑크",
                "TOOLTIP_MODEL_ONLY / 특성강화 리펠 대체 · 직선 후 폭발", 0));
            foreach (var target in line)
                SourceModelTwentyFifthDamage(actor, target, skill,
                    600000 * factor, "magical", "댐드 펑크");
            foreach (var target in blast)
                SourceModelTwentyFifthDamage(actor, target, skill,
                    450000 * blastFactor, "magical", "댐드 펑크");
        }

        private void SourceModelTwentyFifthKidMagnet(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            int prior;
            sourceModelKidMagnetCounters.TryGetValue(actor.Id, out prior);
            int current = checked(prior + (int)skill.Components[3].Number("amount").Value);
            sourceModelKidMagnetCounters[actor.Id] = current;
            bool empowered = current % (int)skill.Components[3].Number("afterEvery").Value == 0;
            int casts = empowered && Equals(skill.Components[3].Get("empoweredTripleCast"), true)
                ? 3 : 1;
            double extra = empowered ?
                1 + skill.Components[3].Number("empoweredDamagePercent").Value / 100 : 1;
            double raw = SourceModelTwentyFifthStrengthDamage(actor, skill.Components[0]);
            double factor = SourceModelTwentyFifthKidAreaFactor(targets.Count);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 자력 누적 " + current +
                (empowered ? " · 5번째 강화 3회" : ""), current));
            for (int i = 0; i < casts; i++)
            {
                foreach (var target in targets)
                {
                    SourceModelTwentyFifthDamage(actor, target, skill,
                        raw * extra * factor, "magical");
                    if (ReferenceEquals(primary, target))
                        SourceModelTwentyFifthDamage(actor, target, skill,
                            raw * extra * factor *
                            skill.Components[1].Number("singleExtraPercent").Value / 100,
                            "magical");
                }
                SourceModelTwentyFifthStun(actor, primary, skill,
                    skill.Components[2].Number("durationSeconds").Value);
            }
        }

        private void OnSourceModelTwentyFifthSuccessfulBasicHit(string actorKey,
            string rawcode, string targetId, long actual)
        {
            if (!UsesSourceModelBasicAttacks || !processingStep || actual <= 0 ||
                (rawcode != "H0B4" && rawcode != "H096")) return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(actorKey, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !SourceModelTwentyFifthOwner(actor) ||
                !enemies.TryGetValue(targetId, out primary)) return;
            if (actor.Catalog.Id == "TR-25")
            {
                SourceModelKidBasicReplacement pending;
                bool replacesSplash =
                    sourceModelKidBasicReplacements.TryGetValue(id, out pending) &&
                    pending.TargetId == targetId &&
                    pending.LaneRevision == SourceModelLaneRevision(id) &&
                    pending.StoryLane == IsStoryDeployed(id);
                // Consume any pending decision even when it belongs to a
                // stale target/lane. It must never leak to a later hit.
                sourceModelKidBasicReplacements.Remove(id);
                if (replacesSplash)
                {
                    var skill = actor.Profile.Skills[3];
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, actorKey,
                        targetId, skill.Name,
                        "TOOLTIP_MODEL_ONLY / 35% 단일 대체 공격 · 기존 4만 광역 스플래시 억제", 0));
                    // Preserve the original native basic hit. Replace ONLY
                    // its tooltip's separate area-magic side packet.
                    SourceModelTwentyFifthDamage(actor, primary, skill,
                        skill.Components[0].Number("amount").Value, "physical");
                    SourceModelTwentyFifthStun(actor, primary, skill,
                        skill.Components[1].Number("durationSeconds").Value);
                    return;
                }
            }
            var passive = actor.Catalog.Id == "TR-25" ?
                actor.Profile.Passives[3] : actor.Profile.Passives[1];
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, passive.Number("radius").Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            double raw = actor.Catalog.Id == "TR-25" ?
                passive.Number("amount").Value :
                SourceModelSeventhKnownAttackBuff(actor.Id,
                    actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower +
                    SourceModelTwentyFifthFlatAttackPower(actor)) *
                    passive.Number("attackPowerPercent").Value / 100;
            foreach (var target in targets)
                if (!ReferenceEquals(target, primary))
                    ApplySourceModelSeventhTimedHit(actor, target,
                        actor.Catalog.Id == "TR-25" ? "키드 · 기본 공격 부가 마법" :
                            "로우 · 기본 공격 폭발", raw,
                        actor.Catalog.Id == "TR-25" ? "magical" : "explosive");
        }

        private void AdvanceSourceModelTwentyFifthStates(double elapsed)
        {
            if (!UsesSourceModelBasicAttacks || elapsed <= 0 ||
                game.Phase != SessionPhase.Running) return;
            var stale = new List<long>();
            foreach (var pair in sourceModelLawRooms)
                if (pair.Value.ExpiresAt <= Time ||
                    !characters.ContainsKey(pair.Key) ||
                    pair.Value.LaneRevision != SourceModelLaneRevision(pair.Key) ||
                    pair.Value.StoryLane != IsStoryDeployed(pair.Key))
                    stale.Add(pair.Key);
            foreach (long id in stale) sourceModelLawRooms.Remove(id);

            var owners = new List<long>(sourceModelKidAuraVictims.Keys);
            foreach (var actor in characters.Values)
                if (actor.Catalog.Id == "TR-25" &&
                    !owners.Contains(actor.Id)) owners.Add(actor.Id);
            foreach (long id in owners)
            {
                CharacterInstance actor;
                HashSet<string> previous;
                if (!sourceModelKidAuraVictims.TryGetValue(id, out previous))
                    previous = new HashSet<string>(StringComparer.Ordinal);
                var current = new HashSet<string>(StringComparer.Ordinal);
                if (characters.TryGetValue(id, out actor) &&
                    SourceModelTwentyFifthOwner(actor))
                    foreach (var enemy in enemies.Values)
                        if (enemy.Enemy.IsAlive && CanAttackInThisLane(actor, enemy))
                            current.Add(enemy.Id);
                string source = "practice-tooltip:kid-aura:" + id;
                foreach (var victimId in previous)
                    if (!current.Contains(victimId))
                    {
                        SpatialCombatEnemy victim;
                        if (enemies.TryGetValue(victimId, out victim))
                            victim.Enemy.Status.RemoveSource(source);
                    }
                foreach (var victimId in current)
                    if (!previous.Contains(victimId))
                    {
                        SpatialCombatEnemy victim;
                        if (enemies.TryGetValue(victimId, out victim))
                            victim.Enemy.Status.Apply(new CombatEffect(source,
                                CombatEffectType.MovementSlowPercent,
                                CombatEffectOrigin.Aura,
                                actor.Profile.Passives[0].Number("percent").Value, 0));
                    }
                if (current.Count > 0) sourceModelKidAuraVictims[id] = current;
                else sourceModelKidAuraVictims.Remove(id);
            }
        }

        private void ForgetSourceModelTwentyFifthActor(long actorId)
        {
            sourceModelLawRooms.Remove(actorId);
            sourceModelKidMagnetCounters.Remove(actorId);
            sourceModelKidBasicReplacements.Remove(actorId);
            HashSet<string> victims;
            if (sourceModelKidAuraVictims.TryGetValue(actorId, out victims))
            {
                string source = "practice-tooltip:kid-aura:" + actorId;
                foreach (string id in victims)
                {
                    SpatialCombatEnemy enemy;
                    if (enemies.TryGetValue(id, out enemy))
                        enemy.Enemy.Status.RemoveSource(source);
                }
                sourceModelKidAuraVictims.Remove(actorId);
            }
        }
    }
}
