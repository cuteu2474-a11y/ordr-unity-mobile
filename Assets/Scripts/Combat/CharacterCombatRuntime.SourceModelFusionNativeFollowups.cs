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

        // JASS B6m swaps the active/pending song groups once every 60s.
        // The original h04O song objects have not been mapped as playable
        // combat characters: expose the *rotation*, not imaginary soldiers.
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
                        "MODEL_ONLY B6m: 60초 그룹 회전; 원본 h04O 음표 병사 객체 미연결",
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
        }
    }
}
