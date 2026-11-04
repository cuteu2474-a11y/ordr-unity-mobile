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
        private readonly HashSet<long> sourceModelFusionSharkScheduled =
            new HashSet<long>();
        private double sourceModelUtaSongElapsed;
        private long sourceModelUtaSongRotations;
        private long sourceModelUtaSongSoldierSequence;
        private readonly Dictionary<long, List<string>> sourceModelUtaSongSoldierKeys =
            new Dictionary<long, List<string>>();

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
            return SourceModelFusionNativeBalance(
                Original323FusionNativeFollowupLedger.DragonLineExplosionBonusId);
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
            CharacterInstance actor, string rawcode)
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
        }

        // B6s creates physical h04O with source-object 38001 base damage,
        // 0.33s cooldown and 0.32s attack point. Unlike a roster character,
        // its autonomous attacks use the common attack/death/armor pipeline.
        private bool RegisterSourceModelUtaSongSoldier(
            CharacterInstance actor, SpatialCombatEnemy primary, double lifetime)
        {
            if (actor.Catalog.Id != "E-09" ||
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.UtaSongId) <= 0)
                return false;
            long sequence = checked(++sourceModelUtaSongSoldierSequence);
            string key = "practice-uta-song:" + sequence;
            var position = roster.Get(actor.Id);
            RegisterSourceIndependentAttack(key, "h04O", IsStoryDeployed(actor.Id),
                position.X, position.Y, Time + lifetime);
            List<string> keys;
            if (!sourceModelUtaSongSoldierKeys.TryGetValue(actor.Id, out keys))
            {
                keys = new List<string>();
                sourceModelUtaSongSoldierKeys.Add(actor.Id, keys);
            }
            keys.Add(key);
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id), key,
                lifetime, () =>
                {
                    UnregisterSourceIndependentAttack(key);
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
                primary.Id, "우타 · 음표 병사",
                "MODEL_ONLY B6s/h04O: 원본 공격 주사위·0.33초 주기 적용, 7초 생존/캐스터 근처 배치 모바일 모델", 0));
            return true;
        }

        // JASS B6m swaps the active/pending song groups once every 60s.
        // h04O is an independent attacker, NOT a playable roster character.
        // Original active/pending group mutation remains unmodeled; this
        // counter records the proven native timer only.
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
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, null, null,
                        "우타 · 노래 그룹 교대",
                        "MODEL_ONLY B6m: 60초 그룹 회전; h04O 공격 객체 연결 / 원본 그룹 소속 교체 미검증",
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
                // CEN/CEP: .21s warmup, then two .1s innerfire target orders.
                // B02D lasts 20.5s; CEP restarts 19.7s after each cycle.
                ScheduleSourceModelFusionSharkInnerFire(actor.Id, .41);
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

                    // CEN casts on the shark itself and selects up to two
                    // other allies in a 450-radius group. UnitPointValue/CEO
                    // restrictions need an original point-value witness;
                    // this practice target policy accepts deployed allies.
                    ApplySourceModelFusionSharkInnerFire(actorId, actorId);
                    var candidates = new List<long>();
                    var position = roster.Get(actorId);
                    foreach (var ally in characters.Values)
                    {
                        if (ally.Id == actorId || !ally.AttackReady ||
                            IsStoryDeployed(ally.Id) != IsStoryDeployed(actorId))
                            continue;
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
                        ApplySourceModelFusionSharkInnerFire(actorId, targetId);
                    }
                    ScheduleSourceModelFusionSharkInnerFire(actorId, 19.7);
                });
        }

        private void ApplySourceModelFusionSharkInnerFire(long casterId,
            long allyId)
        {
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(allyId), out status)) return;
            string source = "practice-native:SPH3:A0N1:" + casterId;
            status.RemoveSource(source);
            // A0N1/Ainf: damage bonus DataA1=.55, B02D duration 20.5s.
            // DataD1=1.75 is kept out of damage/armor math until the native
            // Inner Fire field mapping and ally armor handling are proved.
            status.Apply(new CombatEffect(source,
                CombatEffectType.AttackPowerBuff,
                CombatEffectOrigin.TriggeredSkill, 55, 20.5));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, InstanceKey(casterId),
                InstanceKey(allyId), "S-샤크 · 내면의 불꽃",
                "MODEL_ONLY CEN/CEP + A0N1: 20.5초 공격력 +55%, 최대 아군 2기/19.7초",
                0));
        }

        private void ForgetSourceModelFusionNativeActor(long actorId)
        {
            sourceModelFusionSharkScheduled.Remove(actorId);
            List<string> keys;
            if (sourceModelUtaSongSoldierKeys.TryGetValue(actorId, out keys))
            {
                foreach (string key in keys)
                    UnregisterSourceIndependentAttack(key);
                sourceModelUtaSongSoldierKeys.Remove(actorId);
            }
            sourceSongSoldierExpiry.Remove(actorId);
        }
    }
}
