using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Bvg, B8Y, B8G and CEN create a non-attacking h04V support object.
        // The receipt is committed by the fusion transaction, NOT by this
        // combat observer. Never grant/consume source items in this layer.
        private Func<string, long> sourceModelFusionNativeReceipt;
        private OriginalMapBaseStatCatalog sourceModelNativePointValues;
        private readonly HashSet<long> sourceModelFusionSharkScheduled =
            new HashSet<long>();
        private double sourceModelUtaSongElapsed;
        private long sourceModelUtaSongRotations;
        private long sourceModelUtaSongSoldierSequence;
        private readonly Dictionary<long, List<string>> sourceModelUtaSongSoldierKeys =
            new Dictionary<long, List<string>>();
        // B6m rotates two per-player Group handles. GroupClear/transfer does
        // not destroy the underlying h04O units or extend their 7s lifetime.
        private readonly HashSet<string> sourceModelUtaSongActive =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> sourceModelUtaSongPending =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<long, double> sourceModelRogerInfernoReadyAt =
            new Dictionary<long, double>();
        // Native B8V increases the unit user-data only on a missing B07O
        // frost-armor buff. Keep that trigger separate from the 12% d7 roll.
        private readonly Dictionary<long, int> sourceModelTesoroFrostCounts =
            new Dictionary<long, int>();
        private readonly Dictionary<string, int> sourceModelNativeTesoroSpawnA11S =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<long> sourceModelUtaIntroStarted =
            new HashSet<long>();

        private bool TryResolveSourceModelRogerInfernoTarget(long actorId,
            out CharacterInstance actor, out SpatialCombatEnemy chosen)
        {
            actor = null;
            chosen = null;
            if (!UsesSourceModelBasicAttacks ||
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.RogerHelperId) < 1 ||
                SourceModelFusionNativeBalance(
                    Original323SourceNativeActionKeys.UnlockResource(actorId)) < 1 ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "I-04") return false;
            var ordered = new List<SpatialCombatEnemy>(enemies.Values);
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var enemy in ordered)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy) ||
                    (enemy.Kind != CharacterEnemyKind.Boss &&
                     enemy.Kind != CharacterEnemyKind.Story)) continue;
                // Boss first in the main lane; story buildings only in the
                // separate story lane. Both match native Bdi/A904 intent.
                chosen = enemy;
                return true;
            }
            return false;
        }

        public bool CanCastSourceModelRogerInferno(long actorId)
        {
            CharacterInstance actor;
            SpatialCombatEnemy target;
            if (game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 ||
                !TryResolveSourceModelRogerInfernoTarget(actorId, out actor, out target))
                return false;
            double until;
            return !sourceModelRogerInfernoReadyAt.TryGetValue(actorId, out until) ||
                CombatScheduler.HasReached(Time, until);
        }

        public bool TryCastSourceModelRogerInferno(long actorId)
        {
            if (!CanCastSourceModelRogerInferno(actorId)) return false;
            CharacterInstance actor;
            SpatialCombatEnemy target;
            if (!TryResolveSourceModelRogerInfernoTarget(actorId,
                out actor, out target)) return false;
            // A202 (AHtb) cooldown=150, zero mana. No automatic cast: this
            // separate UI action is the source-confirmed B8Z trait upgrade.
            sourceModelRogerInfernoReadyAt[actorId] = Time + 150;
            string actorKey = InstanceKey(actorId);
            bool lane = IsStoryDeployed(actorId);
            long laneRevision = SourceModelLaneRevision(actorId);
            double centerX = target.X, centerY = target.Y;
            string targetId = target.Id;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, actorKey, targetId,
                "로저 · 인페르노",
                "MODEL_ONLY B8c/A202/A14V: 원본 150초 쿨다운, 보스/스토리 한정, 2.2초 공격 준비", 0));
            effectExecutor.SchedulePracticeSourceAttack(actorKey,
                "practice-native:IM01:A14V:stun", .01, () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actorId, lane, laneRevision))
                        return;
                    foreach (var enemy in enemies.Values)
                    {
                        if (!enemy.Enemy.IsAlive ||
                            !CanAttackInThisLane(actor, enemy)) continue;
                        double dx = enemy.X - centerX, dy = enemy.Y - centerY;
                        if (dx * dx + dy * dy > 800 * 800) continue;
                        double duration = enemy.Kind == CharacterEnemyKind.Boss ||
                            enemy.Kind == CharacterEnemyKind.Story ? 1 : 2;
                        string source = "practice-native:IM01:A14V:" + actorId;
                        enemy.Enemy.Status.RemoveSource(source);
                        enemy.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, duration));
                    }
                    // A202's own AHtb primary effect lasts 3 seconds.
                    SpatialCombatEnemy primary;
                    if (enemies.TryGetValue(targetId, out primary) &&
                        primary.Enemy.IsAlive && CanAttackInThisLane(actor, primary))
                    {
                        string source = "practice-native:IM01:A202:" + actorId;
                        primary.Enemy.Status.RemoveSource(source);
                        primary.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, 3));
                    }
                });
            effectExecutor.SchedulePracticeSourceAttack(actorKey,
                "practice-native:IM01:A14V:impact", 2.2, () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actorId, lane, laneRevision))
                        return;
                    // Bcc samples a real random multiplier in [1,2] once,
                    // then sends one CHAOS/NORMAL 800-area packet.
                    double multiplier = 1 +
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0;
                    ApplySourceModelAreaPacket(actorKey, lane, target, 800,
                        4000000 * multiplier, Ordr323AttackType.Chaos,
                        "로저 · 인페르노 광역");
                    SpatialCombatEnemy primary;
                    if (!enemies.TryGetValue(targetId, out primary) ||
                        !primary.Enemy.IsAlive ||
                        !CanAttackInThisLane(actor, primary)) return;
                    if (primary.Kind != CharacterEnemyKind.Boss &&
                        primary.Kind != CharacterEnemyKind.Story) return;
                    // Native B8c: boss = 40% CURRENT HP; story = 25% MAX HP.
                    // Apply through existing damage/death accounting; final
                    // native universal mitigation is still unverified.
                    double percent = primary.Kind == CharacterEnemyKind.Boss ?
                        primary.Enemy.Health * .40 : primary.Enemy.MaxHealth * .25;
                    long amount = (long)Math.Min(long.MaxValue - 1,
                        Math.Floor(Math.Max(0, percent)));
                    ApplySourceModelFixedPrimaryPacket(actorKey, primary, amount,
                        "로저 · 인페르노 추가 타격 (CHAOS/UNIVERSAL 모델)");
                });
            return true;
        }

        public bool TryAttachSourceModelNativeResearchOnFusion(long resultId,
            string catalogId, string rawcode)
        {
            if (!UsesSourceModelBasicAttacks ||
                SourceModelFusionNativeBalance("source:native-research:R00R") < 1)
                return false;
            // Original B7z/BwB/B3R/B6Y add A11B only at fusion time.
            bool eligible =
                (catalogId == "E-03" && rawcode == "h0CJ") ||
                (catalogId == "LI-04" && rawcode == "h084") ||
                (catalogId == "E-08" && rawcode == "h08R") ||
                (catalogId == "TR-18" && rawcode == "H0BD"); // B3R Yamato, NOT TR-19 Usopp.
            CharacterInstance actor;
            if (!eligible || !characters.TryGetValue(resultId, out actor) ||
                actor.Catalog.Id != catalogId) return false;
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(resultId), out status)) return false;
            string source = "practice-native:R00R:A11B:" + resultId;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, CombatEffectType.MovementSpeedBuff,
                CombatEffectOrigin.Aura, 3, 0));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, InstanceKey(resultId),
                InstanceKey(resultId), "연구 · 인내의 오라",
                "MODEL_ONLY R00R/A11B: 원본 이동속도 +3%; 모바일 순간 배치에는 이동시간 환산 없음", 0));
            return true;
        }

        public void ConfigureSourceModelFusionNativeReceipts(Func<string, long> balance)
        {
            sourceModelFusionNativeReceipt = balance;
        }

        private long SourceModelFusionNativeBalance(string id)
        {
            return sourceModelFusionNativeReceipt == null ? 0 :
                Math.Max(0, sourceModelFusionNativeReceipt(id));
        }

        private double SourceModelFusionLineExplosionBonus()
        {
            // The native LL player variable is shared by Dragon (+20),
            // Lucci (+10) and Koby (-10). Every producer commits its own
            // durable positive receipt, then consumers derive the net value.
            return Original323FusionNativeFollowupLedger
                .SignedLineExplosionAdjustment(SourceModelFusionNativeBalance);
        }

        // LL[player] starts at 21 (JASS initialisation 4738), and fusion
        // follow-ups B99/B2F/B4J modify it. Bvi/CVX/CVY COPY LL into the
        // new unit's A11S, so a later fusion must NOT retroactively boost
        // or weaken an already spawned monster. CVZ sets enraged A11S to 1.
        private void RecordSourceModelNativeTesoroSpawnA11S(
            string enemyId, CharacterEnemyKind kind)
        {
            if (kind == CharacterEnemyKind.Enraged)
                sourceModelNativeTesoroSpawnA11S[enemyId] = 1;
            else if (kind == CharacterEnemyKind.Line ||
                kind == CharacterEnemyKind.Boss)
            {
                double initialLevel = 21 + SourceModelFusionLineExplosionBonus();
                sourceModelNativeTesoroSpawnA11S[enemyId] =
                    (int)Math.Min(int.MaxValue, Math.Max(0, initialLevel));
            }
        }

        private double SourceModelNativeTesoroCorrection(SpatialCombatEnemy enemy)
        {
            int nativeLevel;
            sourceModelNativeTesoroSpawnA11S.TryGetValue(enemy.Id,
                out nativeLevel);
            return Ordr323MapDamageMath.ScriptHealthProportionalCorrection(
                nativeLevel);
        }

        private void ApplySourceModelFusionNativeHelperSlow(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            // The native h04V does not attack. JASS Bvc orders Rebecca's
            // A0HZ slow after the 850-radius attack proc; B8e orders Roger's
            // A0MH after the 800-radius proc. Both already have a tooltip
            // damage packet: emit only the missing *native* slow here.
            string ability;
            string receipt;
            double percent, normalDuration, heroDuration;
            if (actor.Catalog.Id == "LI-02" && skill.Id == "LI-02:skill:1")
            {
                ability = "A0HZ";
                receipt = Original323FusionNativeFollowupLedger.RebeccaHelperId;
                percent = 60; normalDuration = 2.5; heroDuration = 1.25;
            }
            else if (actor.Catalog.Id == "I-04" && skill.Id == "I-04:skill:0")
            {
                ability = "A0MH";
                receipt = Original323FusionNativeFollowupLedger.RogerHelperId;
                percent = 200; normalDuration = 3.25; heroDuration = 1.62;
            }
            else return;
            if (SourceModelFusionNativeBalance(receipt) == 0) return;

            string source = "practice-native:fusion-helper:" +
                ability + ":" + actor.Id;
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive ||
                    !SourceModelNextThirtyComponentTargets(component, primary, target) ||
                    !CanAttackInThisLane(actor, target)) continue;
                // Aslo/war3map AbilityData.slk DataA1 is a speed-change
                // fraction (A0HZ=.6, A0MH=2), not direct damage. Warcraft
                // speed floors/hero target classifications are not certified.
                double duration = target.Kind == CharacterEnemyKind.Boss ||
                    target.Kind == CharacterEnemyKind.Story ?
                    heroDuration : normalDuration;
                target.Enemy.Status.RemoveSource(source);
                target.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.MovementSlowPercent,
                    CombatEffectOrigin.TriggeredSkill, percent, duration));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "MODEL_ONLY h04V/" + ability +
                    ": 원본 Aslo 시전 둔화; 원본 영웅 분류·속도 하한 미검증", 0));
            }
        }

        // B8g orders h04V/A0JS (Aprg) on the attacked unit once Roger's
        // 8.5% slash has occurred. Only beneficial states represented by
        // our shared status engine are dispellable. The 99%/1s primary slow
        // is already handled by I-04:skill:1's tooltip component.
        private void ApplySourceModelFusionRogerPurge(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill)
        {
            if (actor.Catalog.Id != "I-04" || skill.Id != "I-04:skill:1" ||
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.RogerHelperId) <= 0 ||
                !primary.Enemy.IsAlive) return;
            RemoveSourceModelPositiveEnemyBuffs(actor, primary, skill.Name,
                "MODEL_ONLY B8g/A0JS 원본 보조 퍼지");
        }

        // B8V: If the caster lacks B07O, Tezoro orders a helper to cast
        // A11L/AUfa *on Tezoro himself*. Native DataA1=+1.5 defense and
        // duration=0 (aura-like permanent state). This source-model status
        // is not a fabricated enemy->unit damage simulation.
        private void ApplySourceModelFusionTesoroFrostArmor(
            CharacterInstance actor, string rawcode, SpatialCombatEnemy primary)
        {
            if (rawcode != "h06A" || actor.Catalog.Id != "E-11" ||
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.TesoroHelperId) <= 0)
                return;
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(actor.Id), out status)) return;
            string source = "practice-native:ET12:A11L:" + actor.Id;
            foreach (var existing in status.Snapshot())
                if (existing.SourceId == source &&
                    existing.Type == CombatEffectType.ArmorBuff)
                    return;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, CombatEffectType.ArmorBuff,
                CombatEffectOrigin.Aura, 1.5, 0));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, InstanceKey(actor.Id),
                InstanceKey(actor.Id), "테조로 · 프로스트 아머",
                "MODEL_ONLY B8V/A11L/B07O: 자기 방어력 +1.5 영속 효과; 피격 방어 적용·원본 버프 표시는 추후 연결", 0));
            ScheduleSourceModelTesoroGoldImpact(actor, primary);
        }

        // B8V -> d8/B8S: gold is READ (not spent) from the one shared wallet
        // at the attack declaration. B8L(gold) = min(gold,250000)^2/250000.
        // Branch 1/2 is a 425-radius impact after .05+.4 seconds; branch 0
        // is selected only at native user data >=3 and has a 325-radius hit.
        private void ScheduleSourceModelTesoroGoldImpact(
            CharacterInstance actor, SpatialCombatEnemy primary)
        {
            if (sourceModelTwentiethPlayerGold == null) return;
            long gold = sourceModelTwentiethPlayerGold();
            if (gold < 0) throw new InvalidOperationException("공유 골드가 음수입니다.");
            double capped = Math.Min(gold, 250000);
            double goldPower = capped * capped / 250000.0;
            int used;
            sourceModelTesoroFrostCounts.TryGetValue(actor.Id, out used);
            used = Math.Min(3, used + 1);
            bool third = used >= 3;
            sourceModelTesoroFrostCounts[actor.Id] = third ? 0 : used;
            if (!third) sourceModelRules.NextScriptRoll(2); // B8S branch 1/2: same damage.

            string caster = InstanceKey(actor.Id);
            bool story = IsStoryDeployed(actor.Id);
            long laneRevision = SourceModelLaneRevision(actor.Id);
            string targetId = primary.Id;
            effectExecutor.SchedulePracticeSourceAttack(caster,
                "practice-native:ET12:B8S", .45, () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, story, laneRevision))
                        return;
                    SpatialCombatEnemy center;
                    if (!enemies.TryGetValue(targetId, out center) ||
                        !center.Enemy.IsAlive || !CanAttackInThisLane(actor, center))
                        return;
                    double radius = third ? 325 : 425;
                    var victims = new List<SpatialCombatEnemy>();
                    foreach (var enemy in enemies.Values)
                    {
                        if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                            continue;
                        double dx = enemy.X - center.X, dy = enemy.Y - center.Y;
                        if (dx * dx + dy * dy <= radius * radius)
                            victims.Add(enemy);
                    }
                    victims.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
                    string name = "테조로 · 황금 충격";
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, caster,
                        targetId, name,
                        "MODEL_ONLY B8S/B8L: 공격 시 골드 상한250000, 0.45초 지연·" +
                        radius + " 범위, 재화 차감 없음", 0));
                    foreach (var victim in victims)
                    {
                        // The ordinary line/boss spawn writes the current
                        // LL level into JD=A11S; snapshot that factor per
                        // victim, not once at the center or a hard-coded 0.
                        double nativeFactor =
                            SourceModelNativeTesoroCorrection(victim);
                        double normalMultiplier = 1 +
                            sourceModelRules.NextScriptRoll(500001) / 1000000.0;
                        ApplySourceModelAreaPacket(caster, story, victim, 0,
                            (third ? 160000 : 400000) * normalMultiplier,
                            Ordr323AttackType.Spells, name);
                        if (!victim.Enemy.IsAlive) continue;
                        ApplySourceModelAreaPacket(caster, story, victim, 0,
                            goldPower * nativeFactor * 1.5,
                            Ordr323AttackType.Siege, name);
                        if (!third && victim.Enemy.IsAlive)
                        {
                            double missing = Math.Max(0,
                                victim.Enemy.MaxHealth - victim.Enemy.Health);
                            ApplySourceModelAreaPacket(caster, story, victim, 0,
                                missing * .01 * .45 * goldPower / 25000.0 *
                                    nativeFactor,
                                Ordr323AttackType.Siege, name);
                        }
                    }
                    if (center.Enemy.IsAlive && CanAttackInThisLane(actor, center))
                        ApplySourceModelAreaPacket(caster, story, center, 0,
                            center.Enemy.MaxHealth * (third ? .14 : .18) *
                                SourceModelNativeTesoroCorrection(center),
                            Ordr323AttackType.Siege, name + " · 주 대상");
                });
        }

        // B6s creates physical h04O with source-object 38001 base damage,
        // 0.33s cooldown and 0.32s attack point. Unlike a roster character,
        // its autonomous attacks use the common attack/death/armor pipeline.
        private bool RegisterSourceModelUtaSongSoldier(
            CharacterInstance actor, SpatialCombatEnemy primary, double lifetime,
            double? spawnX = null, double? spawnY = null)
        {
            if (actor.Catalog.Id != "E-09" ||
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.UtaSongId) <= 0)
                return false;
            long sequence = checked(++sourceModelUtaSongSoldierSequence);
            string key = "practice-uta-song:" + sequence;
            var position = roster.Get(actor.Id);
            RegisterSourceIndependentAttack(key, "h04O", IsStoryDeployed(actor.Id),
                spawnX ?? position.X, spawnY ?? position.Y, Time + lifetime);
            List<string> keys;
            if (!sourceModelUtaSongSoldierKeys.TryGetValue(actor.Id, out keys))
            {
                keys = new List<string>();
                sourceModelUtaSongSoldierKeys.Add(actor.Id, keys);
            }
            keys.Add(key);
            sourceModelUtaSongPending.Add(key);
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id), key,
                lifetime, () =>
                {
                    UnregisterSourceIndependentAttack(key);
                    sourceModelUtaSongActive.Remove(key);
                    sourceModelUtaSongPending.Remove(key);
                    List<string> active;
                    if (sourceModelUtaSongSoldierKeys.TryGetValue(actor.Id, out active))
                    {
                        active.Remove(key);
                        if (active.Count == 0)
                            sourceModelUtaSongSoldierKeys.Remove(actor.Id);
                    }
                });
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                primary == null ? null : primary.Id, "우타 · 음표 병사",
                "MODEL_ONLY B6s/h04O: 원본 공격 주사위·0.33초 주기 적용, 7초 생존/캐스터 근처 배치 모바일 모델", 0));
            return true;
        }

        // B7W's "세계의가희" / "u-ta et" chat trigger starts B63 -> B61
        // -> B6y -> B6x. It is NOT a generic E-09 on-attack proc. Expose a
        // one-shot unit action so the touch UI can express this native event
        // without introducing a text-chat subsystem.
        public bool CanStartSourceModelUtaIntro(long actorId)
        {
            CharacterInstance actor;
            return UsesSourceModelBasicAttacks &&
                game.Phase == SessionPhase.Running && !processingStep &&
                pendingStepSeconds == 0 &&
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.UtaSongId) > 0 &&
                !sourceModelUtaIntroStarted.Contains(actorId) &&
                characters.TryGetValue(actorId, out actor) &&
                actor.AttackReady && actor.Catalog.Id == "E-09";
        }

        public bool TryStartSourceModelUtaIntro(long actorId)
        {
            if (!CanStartSourceModelUtaIntro(actorId)) return false;
            CharacterInstance actor = characters[actorId];
            var position = roster.Get(actorId);
            double centerX = position.X, centerY = position.Y;
            bool isStory = IsStoryDeployed(actorId);
            long laneRevision = SourceModelLaneRevision(actorId);
            sourceModelUtaIntroStarted.Add(actorId);
            string caster = InstanceKey(actorId);

            // B6x first stage runs BiZ within 800 units and B6w deals
            // 1,500,000 * random [1,1.5] NORMAL/UNIVERSAL to each enemy.
            var victims = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                    continue;
                double dx = enemy.X - centerX, dy = enemy.Y - centerY;
                if (dx * dx + dy * dy <= 800 * 800)
                    victims.Add(enemy);
            }
            victims.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var enemy in victims)
            {
                double multiplier = 1 +
                    sourceModelRules.NextScriptRoll(500001) / 1000000.0;
                ApplySourceModelAreaPacket(caster, isStory, enemy, 0,
                    1500000 * multiplier, Ordr323AttackType.Spells,
                    "우타 · 세계의 가희 (B6x)");
            }
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, caster, null,
                "우타 · 세계의 가희",
                "MODEL_ONLY B7W/B63/B61/B6x: 수동 1회 시작, 800 범위 첫 충격, 약 1초 뒤 음표 병사", 0));

            // Original .4 + (.2 * 3) spectacle before B6s. Preserve the
            // native 100..150 polar offset and existing song-soldier lifetime.
            effectExecutor.SchedulePracticeSourceAttack(caster,
                "practice-native:ET09:B6x:intro-soldier", 1, () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actorId,
                            isStory, laneRevision)) return;
                    double radius = 100 + sourceModelRules.NextScriptRoll(500001) /
                        10000.0;
                    double angle = sourceModelRules.NextScriptRoll(360000) *
                        Math.PI / 180000.0;
                    AddSourceModelUtaSongSoldier(actor, null, 7,
                        "우타 · 세계의 가희", centerX + radius * Math.Cos(angle),
                        centerY + radius * Math.Sin(angle));
                });
            return true;
        }

        // JASS B6m swaps the active/pending song groups once every 60s.
        // h04O is an independent attacker, NOT a playable roster character.
        // Both logical groups are now tracked; group membership does not
        // control attack or lifetime without a source-confirmed effect.
        public long PracticeUtaNativeSongRotations => sourceModelUtaSongRotations;

        private void AdvanceSourceModelFusionNativeFollowups(double elapsed)
        {
            if (sourceModelFusionNativeReceipt == null || elapsed <= 0) return;
            if (SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.UtaSongId) > 0)
            {
                sourceModelUtaSongElapsed += elapsed;
                if (sourceModelUtaSongElapsed >= 60)
                {
                    // Avoid a per-period loop when the combat clock advances
                    // by multiple rounds at once.
                    long fullCycles = checked((long)Math.Floor(
                        sourceModelUtaSongElapsed / 60));
                    sourceModelUtaSongElapsed -= fullCycles * 60;
                    sourceModelUtaSongRotations = checked(
                        sourceModelUtaSongRotations + fullCycles);
                    // Original B6m clears the old active group, transfers the
                    // pending group, then clears/swaps handle identities.
                    sourceModelUtaSongActive.Clear();
                    if (fullCycles == 1)
                        sourceModelUtaSongActive.UnionWith(sourceModelUtaSongPending);
                    sourceModelUtaSongPending.Clear();
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, null, null,
                        "우타 · 노래 그룹 교대",
                        "MODEL_ONLY B6m: 60초 활성/대기 그룹 교체; h04O 생성·공격·만료는 별도 유지",
                        fullCycles));
                }
            }
            if (SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.SharkCenId) <= 0)
                return;

            foreach (var actor in characters.Values)
            {
                if (!actor.AttackReady || actor.Catalog.Id != "SR-03" ||
                    !sourceModelFusionSharkScheduled.Add(actor.Id)) continue;
                // CEN/CEP: .21s first cast, .1s between ally casts.
                // The full cycle restarts after the last two orders +19.7s.
                ScheduleSourceModelFusionSharkInnerFire(actor.Id, .21);
            }
        }

        private void ScheduleSourceModelFusionSharkInnerFire(long actorId,
            double delaySeconds)
        {
            string casterId = InstanceKey(actorId);
            effectExecutor.SchedulePracticeSourceAttack(casterId,
                "practice-native:SPH3:A0N1", delaySeconds, () =>
                {
                    CharacterInstance actor;
                    if (!UsesSourceModelBasicAttacks ||
                        game.Phase != SessionPhase.Running ||
                        SourceModelFusionNativeBalance(
                            Original323FusionNativeFollowupLedger.SharkCenId) <= 0 ||
                        !characters.TryGetValue(actorId, out actor) ||
                        !actor.AttackReady || actor.Catalog.Id != "SR-03")
                        return;

                    // CEO's native filter is Ji[owner] &&
                    // GetUnitPointValue(ally)>100. The mobile player owns
                    // its active roster; unknown UnitData points fail closed.
                    ApplySourceModelFusionSharkInnerFire(actorId, actorId);
                    var candidates = new List<long>();
                    var position = roster.Get(actorId);
                    foreach (var ally in characters.Values)
                    {
                        if (ally.Id == actorId || !ally.AttackReady ||
                            IsStoryDeployed(ally.Id) != IsStoryDeployed(actorId))
                            continue;
                        var point = sourceModelNativePointValues == null ? null :
                            sourceModelNativePointValues.Find(ally.Catalog.Id);
                        if (point == null || !point.UnitPointValue.HasValue ||
                            point.UnitPointValue.Value <= 100) continue;
                        var other = roster.Get(ally.Id);
                        double dx = position.X - other.X;
                        double dy = position.Y - other.Y;
                        if (dx * dx + dy * dy <= 450 * 450)
                            candidates.Add(ally.Id);
                    }
                    for (int selected = 0; selected < 2 && candidates.Count > 0;
                         selected++)
                    {
                        int index = sourceModelRules.NextScriptRoll(candidates.Count);
                        long targetId = candidates[index];
                        candidates.RemoveAt(index);
                        double orderDelay = (selected + 1) * .1;
                        bool isStory = IsStoryDeployed(actorId);
                        long revision = SourceModelLaneRevision(actorId);
                        effectExecutor.SchedulePracticeSourceAttack(casterId,
                            "practice-native:SPH3:A0N1:ally", orderDelay, () =>
                            {
                                CharacterInstance caster;
                                CharacterInstance target;
                                if (!CanFinishSourceModelDelayedAttack(actorId,
                                        isStory, revision) ||
                                    !characters.TryGetValue(actorId, out caster) ||
                                    !caster.AttackReady ||
                                    !characters.TryGetValue(targetId, out target) ||
                                    !target.AttackReady ||
                                    IsStoryDeployed(targetId) != isStory) return;
                                ApplySourceModelFusionSharkInnerFire(actorId, targetId);
                            });
                    }
                    ScheduleSourceModelFusionSharkInnerFire(actorId, 19.9);
                });
        }

        private void ApplySourceModelFusionSharkInnerFire(long casterId,
            long allyId)
        {
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(allyId), out status)) return;
            string source = "practice-native:SPH3:A0N1:" + casterId;
            status.RemoveSource(source);
            // A0N1/Ainf: DataA1=.55 damage and DataD1=1.75 armor;
            // both use B02D's 20.5-second lifespan. The armor status is
            // recorded without fabricating an unmodeled ally-hit pipeline.
            status.Apply(new CombatEffect(source,
                CombatEffectType.AttackPowerBuff,
                CombatEffectOrigin.TriggeredSkill, 55, 20.5));
            status.Apply(new CombatEffect(source,
                CombatEffectType.ArmorBuff,
                CombatEffectOrigin.TriggeredSkill, 1.75, 20.5));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, InstanceKey(casterId),
                InstanceKey(allyId), "S-샤크 · 내면의 불꽃",
                "MODEL_ONLY CEN/CEP + A0N1: 20.5초 공격력 +55%·방어 +1.75, 원본 점수>100 아군 최대 2기",
                0));
        }

        private void ForgetSourceModelFusionNativeActor(long actorId)
        {
            sourceModelFusionSharkScheduled.Remove(actorId);
            sourceModelRogerInfernoReadyAt.Remove(actorId);
            sourceModelTesoroFrostCounts.Remove(actorId);
            sourceModelUtaIntroStarted.Remove(actorId);
            List<string> keys;
            if (sourceModelUtaSongSoldierKeys.TryGetValue(actorId, out keys))
            {
                foreach (string key in keys)
                {
                    sourceModelUtaSongActive.Remove(key);
                    sourceModelUtaSongPending.Remove(key);
                    UnregisterSourceIndependentAttack(key);
                }
                sourceModelUtaSongSoldierKeys.Remove(actorId);
            }
            sourceSongSoldierExpiry.Remove(actorId);
        }
    }
}
