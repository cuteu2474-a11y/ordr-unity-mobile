using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, string> sourceAkainuBossTargets =
            new Dictionary<long, string>();
        private readonly Dictionary<long, int> sourceAkainuBossStacks =
            new Dictionary<long, int>();
        private long sourceAkainuBossSerial;
        private Func<long, double?> sourceModelRankyakuFacing;

        // Optional current GetUnitFacing-style evidence. If provided but
        // unavailable, do not silently substitute an auto-aim direction.
        public void ConfigureSourceModelRankyakuFacing(
            Func<long, double?> currentAttackerFacingDegrees)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || characters.Count != 0 ||
                sourceModelRankyakuFacing != null ||
                currentAttackerFacingDegrees == null)
                throw new InvalidOperationException(
                    "람각 방향 공급자는 유닛 소환 전에 한 번만 연결합니다.");
            sourceModelRankyakuFacing = currentAttackerFacingDegrees;
        }

        private double? SourceModelRankyakuAttackFacing(
            CharacterInstance actor, SpatialCombatEnemy target)
        {
            if (sourceModelRankyakuFacing != null)
            {
                double? facing = sourceModelRankyakuFacing(actor.Id);
                return facing.HasValue && !double.IsNaN(facing.Value) &&
                    !double.IsInfinity(facing.Value) ? facing : null;
            }
            // The mobile model's normal attack already steers the shooter
            // toward its selected victim. Snapshot that shooter-facing now:
            // the dummy spell TARGET POINT remains the shooter position.
            var source = roster.Get(actor.Id);
            double dx = target.X - source.X, dy = target.Y - source.Y;
            if (dx == 0 && dy == 0) return null;
            return Math.Atan2(dy, dx) * 180 / Math.PI;
        }

        private void ForgetSourceAkainuBoss(long actorId)
        {
            sourceAkainuBossTargets.Remove(actorId);
            sourceAkainuBossStacks.Remove(actorId);
        }

        // Bq9 opens Bq8 on bosses, or on A903/enraged targets before the
        // caster receives B05G. Consecutive protected-target attacks share
        // the original per-attacker target/counter, resetting on new targets.
        private void ExecuteSourceAkainuBossDeclared(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            int stacks;
            string lastTarget;
            if (sourceAkainuBossTargets.TryGetValue(actor.Id, out lastTarget) &&
                lastTarget == target.Id &&
                sourceAkainuBossStacks.TryGetValue(actor.Id, out stacks))
                stacks = checked(stacks + 1);
            else stacks = 1;
            sourceAkainuBossTargets[actor.Id] = target.Id;
            sourceAkainuBossStacks[actor.Id] = stacks;

            var position = roster.Get(actor.Id);
            double dx = target.X - position.X, dy = target.Y - position.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            // Bq8's projectile starts 10 units forward, advances 30 per .01s,
            // then waits .1s after entering the target's 60-unit radius.
            double steps = Math.Ceiling(Math.Max(0, distance - 70) / 30);
            double delay = .01 * (steps + 1) + .1;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            double impactX = target.X, impactY = target.Y;
            // Bq8: Bdi boss gets 2.5m × stack; A903/enraged instead
            // receives an additional 18% of its maximum health as Chaos.
            SourceScriptHit finisher = target.Kind == CharacterEnemyKind.Boss
                ? new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 2500000.0 * stacks, 1, 1,
                    Ordr323AttackType.Spells)
                : new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 0, 1, 1, Ordr323AttackType.Chaos,
                    enragedMaxHealthPercent: 18);
            var packet = new SourceScriptAttackSpec("H-17", "h03Z",
                "Bq8", 1, 1, Array.Empty<SourceScriptHit>(),
                new[] {
                    new SourceScriptHit(SourceScriptHitShape.Direct,
                        0, 0, 250000.0 * stacks, 1, 1,
                        Ordr323AttackType.Spells),
                    new SourceScriptHit(SourceScriptHitShape.Circle,
                        500, 0, 100000, 1, 1, Ordr323AttackType.Spells),
                    finisher
                }, null);
            if (sourceAkainuBossSerial == long.MaxValue)
                throw new OverflowException("히든 아카이누 보스 공격 예약 순번 초과");
            long serial = ++sourceAkainuBossSerial;
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                "practice-akainu-boss:" + actor.Id + ":" + serial,
                delay, () => {
                    if (CanFinishSourceModelDelayedAttack(actor.Id, lane, revision))
                        ApplySourceScriptHits(actor, target, packet,
                            packet.OnProc, impactX, impactY);
                });
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), target.Id, "아카이누 · 보스 연속 타격",
                "MODEL_ONLY Bq9→Bq8 " +
                (target.Kind == CharacterEnemyKind.Boss ? "보스" : "광폭화") +
                " " + stacks + "중첩 / 지연 " +
                delay.ToString(CultureInfo.InvariantCulture) +
                "초 / A14X·B05G 방어력 수치 미공급", 0));
        }

        // These source-script callbacks run ONLY in the independent offline
        // practice model. The original verified/WC3 observer path is untouched.
        // Warcraft native dummy casts, exact defense semantics, original
        // enumeration order and final damage quantization remain unverified.
        private void OnSourceModelScriptAttackDeclared(
            string attackerId, string rawcode, string primaryTargetId)
        {
            // A08B has no w5 callback; its source object is an autonomous
            // Unholy Frenzy (Auhf). Cast targeting is a mobile policy, not
            // a fabricated JASS callback. See TrySourceModelLowFrenzy.
            if (rawcode == "h01H" && UsesSourceModelBasicAttacks && processingStep)
                TrySourceModelLowFrenzy(attackerId);
            SourceScriptAttackSpec script = null;
            SourceRemainingCallback remaining = null;
            bool hasRemaining = SourceRemainingCallbackCatalog.TryGet(
                rawcode, out remaining);
            SourceModelBatchAttackSpec batch = null;
            SourceModelNextThirtyUnit nextThirty = null;
            SourceModelUnconnectedSkillRow unconnected = null;
            string sourceSpecialUnitId;
            string nativeSourceUnitId;
            bool specialFromJass =
                SourceModelOriginalSpecialCallbackRules.TryGetUnit(
                    rawcode, out sourceSpecialUnitId);
            bool nativeFromJass = SourceModelNativeCharacterRules.TryGetNativeAttack(
                rawcode, out nativeSourceUnitId);
            bool practiceBonney = rawcode == "H0BX" || rawcode == "H0BY";
            // B7X registers B7U for both native Uta forms. The transformed
            // h0AF has no separate tooltip catalog row, but still declares
            // the same HP/MP and B6r native callback on every attack.
            bool nativeUtaForm = rawcode == "h0AF";
            bool eleventhFormOwner = SourceModelEleventhFormHasAttack(rawcode);
            bool standaloneTemporary = IsStandaloneSourceTemporaryAttack(rawcode);
            bool ninthOwner = SourceModelNinthSkillCatalog.HasOwner(rawcode);
            // Always discover every relevant catalogue. A previous OR chain
            // stopped at the first source callback and never reached separate
            // approved tooltip packets (notably TR-03/TR-17/TR-27).
            bool hasLegacyScript =
                SourceModelScriptAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelNextAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelThirdAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelFourthAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelFifthAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelSixthAttackCatalog.TryGet(rawcode, out script) ||
                SourceModelSeventhAttackCatalog.TryGet(rawcode, out script);
            bool hasBatch = SourceModelBatchAttackCatalog.TryGet(rawcode, out batch);
            bool hasNext = SourceModelNextThirtySkillCatalog.TryGet(
                rawcode, out nextThirty);
            bool hasUnconnected = SourceModelUnconnectedSkillCatalog.TryGet(
                rawcode, out unconnected);
            bool hasAdditionalAuto = HasAdditionalAutomaticAttack(rawcode);
            // An already modeled JASS script must not also roll an
            // independent tooltip skill for the same source attack.
            if (hasLegacyScript)
            {
                nextThirty = null;
                unconnected = null;
                hasNext = hasUnconnected = false;
            }
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                !processingStep || game.Phase != SessionPhase.Running ||
                (!nativeUtaForm && !practiceBonney && !eleventhFormOwner &&
                 !hasLegacyScript && !hasBatch && !hasNext &&
                 !hasUnconnected && !ninthOwner && !standaloneTemporary &&
                 !hasRemaining &&
                 !specialFromJass && !nativeFromJass && !hasAdditionalAuto))
                return;

            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                (actor.Mapping.AttackSpec.UnitRawCode != rawcode &&
                 sourceModelRules.SourceFormRawcodeOf(attackerId) != rawcode) ||
                !enemies.TryGetValue(primaryTargetId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                return;

            // Independent model-only tooltip packets may coexist with an
            // original script/native basic hit. Their explicit owner ID and
            // rawcode gates prevent foreign-profile and duplicate rolls.
            if (hasRemaining && actor.Catalog.Id == remaining.CatalogId)
                OnSourceRemainingDeclared(actor, primary, remaining);
            ExecuteAdditionalAutomaticAttack(actor, primary, rawcode);
            TrySourceBuggyAllyAttackBuff(actor);

            // B8V is an ATTACK-DECLARED self-buff, not a successful-hit
            // proc. It must run even when Tezoro's 12% spell does not roll.
            ApplySourceModelFusionTesoroFrostArmor(actor, rawcode, primary);
            ApplySourceModelUtaNativeAttackResources(actor, rawcode, primary);

            if (ninthOwner)
                ExecuteSourceModelNinthDeclaredAttack(actor, primary, rawcode);
            ExecuteSourceModelTenthFormDeclaredAttack(actor, primary, rawcode);

            // Ulti's tooltip combines her HP-cost active with +1 HP on each
            // declared basic attack. Only source-managed practice HP is
            // restored; externally supplied verified resource pools are not.
            if (actor.Catalog.Id == "L-25" && rawcode == "h03S" &&
                actor.Profile != null &&
                sourceModelManagedResourceRows.ContainsKey(actor.Id) &&
                actor.Abilities.Resources != null)
            {
                foreach (var ability in actor.Profile.Skills)
                    if (ability.Name == "미티어 울두건" &&
                        SourceModelNextThirtySkillCatalog.IsFirstCompositeActive(ability))
                    {
                        double restored = ability.Number("healthRegenPerAttack") ?? 0;
                        if (restored > 0)
                        {
                            var hp = actor.Abilities.Resources;
                            hp.SetHealth(Math.Min(hp.MaximumHealth, hp.Health + restored));
                        }
                        break;
                    }
            }

            RestoreSourceModelTwentyFourthAttackResources(actor, rawcode);

            if (specialFromJass)
            {
                if (actor.Catalog.Id == sourceSpecialUnitId)
                    ExecuteSourceModelOriginalSpecial(actor, primary);
                return;
            }
            if (nativeFromJass)
            {
                if (actor.Catalog.Id == nativeSourceUnitId)
                    ExecuteNativeSourceCharacterAttack(actor, primary);
                return;
            }

            if (standaloneTemporary)
            {
                ExecuteSourceTemporaryAttack(actor, primary);
                // TR-14's form proc and I-03's clone pulse coexist with
                // independently approved on-hit packets. The old early return
                // silently discarded those sixth-batch attacks on every hit.
                if (nextThirty != null && nextThirty.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelNextThirtyProfileAttacks(actor, primary, nextThirty);
                if (unconnected != null && unconnected.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelUnconnectedProfileAttacks(actor, primary, unconnected);
                return;
            }

            if (practiceBonney)
            {
                if (actor.Catalog.Id == "TR-21" && rawcode == "H0BX")
                    ApplySourceModelBonneyDeclaredArmor(actor, primary);
                return;
            }

            if (batch != null)
            {
                if (batch.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelBatchAttack(actor, primary, batch);
                // Original JASS batch hits and explicitly approved tooltip
                // skills are independent effects of the SAME attack event.
                // Never replace the native batch with the tooltip packet.
                if (unconnected != null &&
                    unconnected.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelUnconnectedProfileAttacks(actor, primary,
                        unconnected);
                return;
            }

            if (nextThirty != null)
            {
                if (nextThirty.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelNextThirtyProfileAttacks(actor, primary, nextThirty);
                return;
            }

            if (unconnected != null)
            {
                if (unconnected.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelUnconnectedProfileAttacks(actor, primary, unconnected);
                return;
            }

            // Native/temporary/form-only attacks can declare independent
            // effects above without having a legacy script. Do not fall into
            // a null SourceScriptAttackSpec after those callbacks.
            if (script == null) return;

            // Bq9: every boss attack, and the first enraged attack
            // while A14X/B05G is absent, grants frostarmor and opens Bq8.
            // Later enraged attacks with the buff use the separate 16% roll.
            if (script.CatalogId == "H-17")
            {
                bool enraged = primary.Kind == CharacterEnemyKind.Enraged;
                if (primary.Kind == CharacterEnemyKind.Boss ||
                    (enraged && !RemainingHasFrostArmor(actor, "A14X")))
                {
                    ApplySourceNativeFrostArmorIdentity(actor, "A14X", "B05G");
                    ExecuteSourceAkainuBossDeclared(actor, primary);
                    return;
                }
            }

            // JASS Btd (Cracker) uses Bcb every attacked event, THEN rolls
            // its separate e04X/area branch. Do not reverse this ordering.
            ApplySourceScriptHits(actor, primary, script, script.Always);
            // CEm has a deterministic 50000 direct hit, with no second
            // proc roll. Do not consume an invented RNG draw on each attack.
            if (script.OnProc.Count == 0 && script.RollSides == 1 &&
                string.IsNullOrEmpty(script.DeferredNativeEffect)) return;
            // R-27 player tooltip: 10% Meteor Volcano, 700-radius
            // 8,000 magical damage. Warcraft's Blizzard is only an internal
            // dummy command; do not invent ice or a freeze status in mobile.
            // This is independent of the existing 12%/10,000 direct hit.
            if (script.CatalogId == "R-27" &&
                sourceModelRules.NextScriptRoll(100) < 10)
            {
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attackerId,
                    primaryTargetId, "유성 화산",
                    "TOOLTIP_MODEL_ONLY 아카이누 10% / 700 범위 / 8,000 마법", 0));
                var meteor = new SourceScriptAttackSpec("R-27", "h01P",
                    "meteor-volcano-tooltip", 1, 1,
                    Array.Empty<SourceScriptHit>(),
                    new[] { new SourceScriptHit(SourceScriptHitShape.Circle,
                        700, 0, 8000, 1, 1, Ordr323AttackType.Spells) }, null);
                ApplySourceScriptHits(actor, primary, meteor, meteor.OnProc);
            }
            int roll = script.RollSides == 1 ? 1 :
                sourceModelRules.NextScriptRoll(script.RollSides) + 1;
            // Independent clone proc, after the already-registered Bt2 roll:
            // retaining that order avoids changing its historical RNG draw.
            if (rawcode == "h042") ExecuteSourceTimedClone(actor, primary);
            if (roll > script.RollSuccess) return;
            string skillName = actor.Catalog.Name + " 공격 기술";
            if (script.CatalogId == "U-04" || script.CatalogId == "U-13")
                skillName = "람각";
            else if (script.CatalogId == "S-18")
                skillName = "갈래 번개";
            else if (script.CatalogId == "S-03")
                skillName = "크리마텍트";
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attackerId,
                primaryTargetId, skillName,
                "MODEL_ONLY 원본 " + script.JassFunction + " 공격 선언 분기 " +
                roll + "/" + script.RollSides,
                0));
            BeforeSourceModelLegacyProc(actor, primary, script);
            double? rankyakuFacing = null;
            if (script.CatalogId == "U-04" || script.CatalogId == "U-13")
            {
                rankyakuFacing = SourceModelRankyakuAttackFacing(actor, primary);
                if (!rankyakuFacing.HasValue)
                {
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, attackerId,
                        primaryTargetId, skillName,
                        "A0VS 시전자 방향을 알 수 없어 파동 보류", 0));
                    return;
                }
            }
            if (script.CatalogId == "R-06")
                BeginSourceAttackPause(actor, primary,
                    "루피 기어써드 · 거인 공격", .44);
            if (script.DelaySeconds > 0)
            {
                // BoB captures the attacked unit's LOCATION at state 0. The
                // AoE still happens there if that primary dies before .29s.
                // Use a synthetic target key so the normal target-death
                // cancellation cannot erase a location-based JASS effect.
                double pinnedX = primary.X, pinnedY = primary.Y;
                bool sourceStoryLane = IsStoryDeployed(actor.Id);
                long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-slam:" + attackerId, script.DelaySeconds, () => {
                        if (CanFinishSourceModelDelayedAttack(actor.Id,
                                sourceStoryLane, sourceLaneRevision))
                            ApplySourceScriptHits(actor, primary, script, script.OnProc,
                                pinnedX, pinnedY);
                    });
            }
            else
                ApplySourceScriptHits(actor, primary, script, script.OnProc,
                    rankyakuFacingDegrees: rankyakuFacing);

            // Bq9: when an enraged A903 target is already protected by
            // B05G, the successful ordinary 16% packet adds exactly one
            // 18%-of-max-HP Chaos hit. Bosses never enter this branch.
            if (script.CatalogId == "H-17" &&
                primary.Kind == CharacterEnemyKind.Enraged)
            {
                var enragedBonus = new SourceScriptAttackSpec(
                    "H-17", "h03Z", "Bq9", 1, 1,
                    Array.Empty<SourceScriptHit>(),
                    new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                        0, 0, 0, 1, 1, Ordr323AttackType.Chaos,
                        enragedMaxHealthPercent: 18) }, null);
                ApplySourceScriptHits(actor, primary, enragedBonus,
                    enragedBonus.OnProc);
            }

            // RN-04 already resolves CCg's 17%/1.5m damage and delayed
            // primary-target Faerie Fire in the JASS-backed callback. Only
            // attach the tooltip's 18% area magic amplification to that same
            // successful roll; never roll or deal the attack damage twice.
            if (script.CatalogId == "RN-04")
                ApplySourceModelSentinelMagicAmplification(actor, primary, script);

            if (script.CatalogId == "S-12")
                ScheduleSourceModelPhoenixFire(actor, primary, script);
            AfterSourceModelLegacyProc(actor, primary, script);
            if (!string.IsNullOrWhiteSpace(script.DeferredNativeEffect))
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, attackerId,
                    primaryTargetId, skillName,
                    "원본 네이티브 추가 효과 미검증: " + script.DeferredNativeEffect,
                    0));
        }


        // The original w5 dispatcher runs BEFORE damage, whereas Bo1,
        // BrU/BrV and Bpk react only to a POSITIVE basic-hit damage event.
        // Reuse the combat pipeline's successful-hit observer; never award
        // these effects on a missed, blocked, or zero-damage basic attack.
        private long sourceAfterHitAttackSerial;

        private void OnSourceModelAfterSuccessfulBasicHit(
            string attackerId, string rawcode, string primaryTargetId, long actual)
        {
            OnSourceRemainingAfterPositiveBasicHit(attackerId, rawcode,
                primaryTargetId, actual);
            SourceAfterHitAttackUnit entry;
            if (actual <= 0 || !UsesSourceModelBasicAttacks ||
                sourceModelRules == null || !processingStep ||
                game.Phase != SessionPhase.Running ||
                !SourceModelAfterHitAttackCatalog.TryGet(rawcode, out entry))
                return;

            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                actor.Catalog.Id != entry.CatalogId ||
                (actor.Mapping.AttackSpec.UnitRawCode != rawcode &&
                 sourceModelRules.SourceFormRawcodeOf(attackerId) != rawcode) ||
                !enemies.TryGetValue(primaryTargetId, out primary) ||
                !CanAttackInThisLane(actor, primary))
                return;

            // Independent branches each draw once; R-33 uses elseif and
            // therefore draws its second 8% roll ONLY if the first failed.
            foreach (var script in entry.Branches)
            {
                // An original 100% damage listener does not consume an
                // extra BFi RNG call at its positive-hit stage.
                int roll = script.RollSuccess == script.RollSides
                    ? script.RollSides
                    : sourceModelRules.NextScriptRoll(script.RollSides) + 1;
                if (roll > script.RollSuccess) continue;

                if (script.JassFunction == "Bso/HP25")
                {
                    // h02T source balance is HP25, regen .5. No guessed HP
                    // is accepted: this counter belongs to a pinned pool.
                    var hp = actor.Abilities.Resources;
                    if (hp == null ||
                        !sourceModelManagedResourceRows.ContainsKey(actor.Id) ||
                        Math.Abs(hp.MaximumHealth - 25) > .0001)
                    {
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EffectDeferred,
                            attackerId, primaryTargetId,
                            actor.Catalog.Name + " 불사조 HP 기술",
                            "MODEL_ONLY Bso 원본 HP25 자원 미공급: 후속 타격 보류", 0));
                        continue;
                    }
                    if (hp.Health < 25 - .0001)
                    {
                        hp.SetHealth(Math.Min(25, hp.Health + 1));
                        continue;
                    }
                    hp.SetHealth(1);
                }

                string skillName = actor.Catalog.Name + " 공격 기술";
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated,
                    attackerId, primaryTargetId, skillName,
                    "MODEL_ONLY " + script.JassFunction +
                    " 기본공격 적중 후 " + roll + "/" + script.RollSides, 0));

                if (script.DelaySeconds > 0)
                {
                    double pinnedX = primary.X, pinnedY = primary.Y;
                    bool sourceStoryLane = IsStoryDeployed(actor.Id);
                    long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
                    if (sourceAfterHitAttackSerial == long.MaxValue)
                        throw new OverflowException("후속 기술 사건 순번 범위 초과");
                    long serial = ++sourceAfterHitAttackSerial;
                    effectExecutor.SchedulePracticeSourceAttack(attackerId,
                        "practice-after-hit:" + attackerId + ":" + serial,
                        script.DelaySeconds, () => {
                            if (CanFinishSourceModelDelayedAttack(actor.Id,
                                    sourceStoryLane, sourceLaneRevision))
                            {
                                ApplySourceScriptHits(actor, primary, script,
                                    script.OnProc, pinnedX, pinnedY, actual);
                                // Do not re-run the original 25-HP counter,
                                // and do not double-count the area impact.
                                if (script.JassFunction == "Bso/HP25")
                                    ApplySourceModelMarcoFeatherFollowups(
                                        actor, primary, actual, pinnedX, pinnedY);
                            }
                        });
                }
                else
                    ApplySourceScriptHits(actor, primary, script, script.OnProc,
                        sourceBasicDamage: actual);

                if (!string.IsNullOrWhiteSpace(script.DeferredNativeEffect))
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred,
                        attackerId, primaryTargetId, skillName,
                        "원본 네이티브 추가 효과 미검증: " +
                        script.DeferredNativeEffect, 0));

                if (entry.ExclusiveAlternatives) break;
            }
        }

        private List<SpatialCombatEnemy> SourceScriptTargets(
            CharacterInstance actor, SpatialCombatEnemy primary, SourceScriptHit hit,
            double? pinnedX = null, double? pinnedY = null,
            double? rankyakuFacingDegrees = null)
        {
            var found = new List<SpatialCombatEnemy>();
            if (hit.Shape == SourceScriptHitShape.Direct)
            {
                SpatialCombatEnemy current;
                if (primary.Enemy.IsAlive &&
                    enemies.TryGetValue(primary.Id, out current) &&
                    ReferenceEquals(current, primary) &&
                    CanAttackInThisLane(actor, primary)) found.Add(primary);
                return found;
            }
            var position = roster.Get(actor.Id);
            // Capped caster-centered native attacks (AEfk/A08L) reuse
            // Robin's eligible-target selector, counting ALL hits toward
            // the cap. Other uncapped areas retain their old ordering.
            if (hit.Shape == SourceScriptHitShape.AttackerCircle &&
                hit.MaxTargets > 0)
                return RemainingNativeTargets(actor, position.X, position.Y,
                    hit.Radius, hit.MaxTargets);
            double dx = primary.X - position.X;
            double dy = primary.Y - position.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            // ANfl targets a unit within Rng1 and fans out from the dummy
            // at the attacker; DataC1 is the independent cone reach.
            if (hit.Shape == SourceScriptHitShape.ForkedLightningCone &&
                (length <= 0 || length > hit.CastRange)) return found;
            double targetHeading = length > 0 ?
                Math.Atan2(dy, dx) * 180.0 / Math.PI : 0;
            foreach (var candidate in enemies.Values)
            {
                if (!candidate.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, candidate)) continue;
                if (hit.Shape == SourceScriptHitShape.Circle ||
                    hit.Shape == SourceScriptHitShape.AttackerCircle)
                {
                    double cx = hit.Shape == SourceScriptHitShape.AttackerCircle
                        ? position.X : (pinnedX ?? primary.X);
                    double cy = hit.Shape == SourceScriptHitShape.AttackerCircle
                        ? position.Y : (pinnedY ?? primary.Y);
                    double x = candidate.X - cx;
                    double y = candidate.Y - cy;
                    if (x * x + y * y <= hit.Radius * hit.Radius)
                        found.Add(candidate);
                }
                else if (hit.Shape == SourceScriptHitShape.RankyakuLine)
                {
                    // Caster-origin e0KD; 800 travel, tapered 200->250 width.
                    // No target-location cast or distance-to-victim extension.
                    if (rankyakuFacingDegrees.HasValue &&
                        SourceModelRankyakuGeometry.Contains(hit,
                            position.X, position.Y, rankyakuFacingDegrees.Value,
                            candidate.X, candidate.Y))
                        found.Add(candidate);
                }
                else if (hit.Shape == SourceScriptHitShape.ForkedLightningCone)
                {
                    // Original ANfl: Area1=initial width, DataD1=final
                    // width, DataC1=distance. Never damage behind the caster.
                    if (SourceModelTaperedConeGeometry.Contains(hit,
                            position.X, position.Y, targetHeading,
                            candidate.X, candidate.Y))
                        found.Add(candidate);
                }
                else if (hit.Shape == SourceScriptHitShape.ProjectileLine &&
                         length > 0)
                {
                    // Unrelated script projectiles retain their own reach.
                    double x = candidate.X - position.X;
                    double y = candidate.Y - position.Y;
                    double forward = (x * dx + y * dy) / length;
                    double side = Math.Abs(x * dy - y * dx) / length;
                    double forwardLimit = Math.Max(hit.Length, length);
                    if (forward >= 0 && forward <= forwardLimit &&
                        side <= hit.Radius)
                        found.Add(candidate);
                }
            }
            if (hit.Shape == SourceScriptHitShape.ForkedLightningCone)
            {
                // Unit-targeted ANfl starts with the attacked unit. Fill
                // remaining slots by nearest eligible cone targets, with a
                // stable ID tie-break. Retail target tie-order is unverified.
                found.Sort((a, b) => {
                    if (ReferenceEquals(a, primary)) return ReferenceEquals(b, primary) ? 0 : -1;
                    if (ReferenceEquals(b, primary)) return 1;
                    double ax = a.X - position.X, ay = a.Y - position.Y;
                    double bx = b.X - position.X, by = b.Y - position.Y;
                    int distanceOrder = (ax * ax + ay * ay).CompareTo(bx * bx + by * by);
                    return distanceOrder != 0 ? distanceOrder :
                        StringComparer.Ordinal.Compare(a.Id, b.Id);
                });
                if (found.Count > hit.MaxTargets)
                    found.RemoveRange(hit.MaxTargets, found.Count - hit.MaxTargets);
                return found;
            }
            // Other model skills retain their existing deterministic order.
            found.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return found;
        }

        private void ScheduleSourceModelPhoenixFire(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec script)
        {
            // Source: Bn4 creates h0C5 with A08O/Apxf at the attacker for
            // 1.2s. A08O has 250 damage, .2s cooldown, 800 acquisition range.
            // First fire and retargeting are not JASS-observed, hence this is
            // a deliberately labeled mobile result approximation. Five full
            // .2s intervals before expiry; no imaginary permanent summon.
            var spawn = roster.Get(actor.Id);
            string attackerId = InstanceKey(actor.Id);
            bool sourceStoryLane = IsStoryDeployed(actor.Id);
            long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
            for (int tick = 1; tick <= 5; tick++)
            {
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-phoenix:" + attackerId + ":" + tick, tick * .2, () => {
                        if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                sourceStoryLane, sourceLaneRevision)) return;
                        var closest = NearestSourceModelTarget(actor, spawn.X, spawn.Y, 800);
                        if (closest == null) return;
                        ApplySourceScriptHits(actor, closest, script, new[] {
                            SourceModelNextAttackCatalog.BonClayPhoenixFireTick() });
                    });
            }
        }

        private SpatialCombatEnemy NearestSourceModelTarget(CharacterInstance actor,
            double x, double y, double radius)
        {
            SpatialCombatEnemy closest = null;
            double nearest = radius * radius;
            foreach (var candidate in enemies.Values)
            {
                if (!candidate.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, candidate)) continue;
                double dx = candidate.X - x, dy = candidate.Y - y;
                double squared = dx * dx + dy * dy;
                if (squared > nearest || (squared == nearest && closest != null &&
                    StringComparer.Ordinal.Compare(candidate.Id, closest.Id) >= 0)) continue;
                closest = candidate;
                nearest = squared;
            }
            return closest;
        }

        private void ApplySourceScriptHits(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec script,
            IReadOnlyList<SourceScriptHit> hits,
            double? pinnedX = null, double? pinnedY = null,
            long sourceBasicDamage = 0,
            double? rankyakuFacingDegrees = null)
        {
            string attackerId = InstanceKey(actor.Id);
            foreach (var hit in hits)
            {
                var targets = SourceScriptTargets(actor, primary, hit,
                    pinnedX, pinnedY, rankyakuFacingDegrees);
                foreach (var target in targets)
                {
                    // Bq9's story-only bonus must not splash to a normal
                    // enemy that happens to stand inside its 500-radius AoE.
                    if (hit.StoryOnly && target.Kind != CharacterEnemyKind.Story)
                        continue;
                    SourceModelScriptDamageFactors factors;
                    string damageBlocker;
                    if (!SourceModelScriptDamageTypeRules.TryPrepare(
                            hit, target.Enemy, out factors, out damageBlocker))
                    {
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EffectDeferred,
                            attackerId, target.Id, actor.Catalog.Name + " 공격 기술",
                            "MODEL_ONLY 스킬 피해 보류: " + damageBlocker, 0));
                        continue;
                    }
                    double uniform = hit.Minimum == hit.Maximum ? 0 :
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0;
                    // CCE selects ONE damage operand by the enemy marker,
                    // it does not add the boss/enraged percentage to 35000.
                    double raw = hit.Damage +
                        hit.BasicHitDamageMultiplier * sourceBasicDamage +
                        hit.TargetMaxHealthMultiplier * target.Enemy.MaxHealth +
                        hit.TargetCurrentHealthMultiplier * target.Enemy.Health;
                    if (target.Kind == CharacterEnemyKind.Line &&
                        hit.LineMaxHealthMultiplier > 0)
                        raw = target.Enemy.MaxHealth * hit.LineMaxHealthMultiplier;
                    else if (target.Kind == CharacterEnemyKind.Line &&
                             hit.LineCurrentHealthMultiplier > 0)
                        raw = target.Enemy.Health * hit.LineCurrentHealthMultiplier;
                    if (hit.RandomIntegerMaximum > 0)
                        raw = hit.RandomIntegerMinimum +
                            sourceModelRules.NextScriptRoll(
                                hit.RandomIntegerMaximum - hit.RandomIntegerMinimum + 1);
                    if (target.Kind == CharacterEnemyKind.Story &&
                        hit.StoryDamage > 0)
                        raw = hit.StoryDamage;
                    if (target.Kind == CharacterEnemyKind.Boss &&
                        hit.BossCurrentHealthPercent > 0)
                        raw = target.Enemy.Health *
                            hit.BossCurrentHealthPercent / 100.0;
                    else if (target.Kind == CharacterEnemyKind.Enraged &&
                             hit.EnragedMaxHealthPercent > 0)
                        raw = target.Enemy.MaxHealth *
                            hit.EnragedMaxHealthPercent / 100.0;
                    double sourceDamage = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                        raw, hit.Minimum, hit.Maximum, uniform);
                    // Attack type chooses the original DamageBonus* row;
                    // damage type independently chooses physical armor,
                    // A04Q spell resistance, or Universal bypass.
                    // Unpinned JASS packets remain on the legacy model path.
                    double modeled = factors.Apply(
                        sourceDamage,
                        target.Kind == CharacterEnemyKind.Line &&
                        hit.LineAttackOverride.HasValue
                            ? hit.LineAttackOverride.Value : hit.Attack);
                    if (modeled >= long.MaxValue)
                        throw new OverflowException("모델 공격 스킬 피해 범위 초과");
                    long planned = (long)Math.Floor(modeled);
                    long actual;
                    if (!resolution.TryApplySkillDamage(attackerId,
                            target.Id, planned, out actual)) continue;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage, attackerId,
                        target.Id, actor.Catalog.Name + " 공격 기술",
                        "MODEL_ONLY " + script.JassFunction +
                        (script.JassFunction == "tooltip-clone-proxy"
                            ? " 도감 분신 파동 / 원본 연속 공격·네이티브 동등성 미검증"
                            : (script.CatalogId == "S-08" || script.CatalogId == "S-12" ||
                         script.CatalogId == "S-18" || script.CatalogId == "S-19"
                            ? " 네이티브 객체 수치 기반 모바일 근사, 실제 타격 미검증"
                            : " JASS 기본 피해·범위, 최종 엔진 면역 미검증")),
                        actual));
                    if (!target.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated,
                            attackerId, target.Id, actor.Catalog.Name + " 공격 기술",
                            "연습 공격 선언 스킬로 적 처치", actual));
                }
            }
        }
    }
}
