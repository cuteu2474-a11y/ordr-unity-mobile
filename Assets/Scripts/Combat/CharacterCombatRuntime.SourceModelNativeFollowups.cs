using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // S-20 A0EG / Aliq: the source object gives DataA1=150 and Dur1=3.
        // The mobile model deals 150 each full second, refreshing (rather
        // than stacking) the same enemy's burn on subsequent basic hits.
        // Exact Warcraft tick phase/armor/immunity and engine refresh rules
        // remain unobserved; no original-runtime hook is changed.
        private static readonly SourceScriptAttackSpec LiquidFireModel =
            new SourceScriptAttackSpec("S-20", "h00Z", "Aliq", 1, 1,
                null, new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 150, 1, 1, Ordr323AttackType.Spells) },
                "Aliq 틱 위상·원본 방어 처리 미검증");

        // Native A0EA/Apxf is an independent auto-missile, NOT a proc from
        // the h017 weapon. Original AbilityData.slk has DataA=150,
        // DataB=0, cooldown .2, area 600, duration .01; no invented burn.
        private static readonly Original323NativeAutoMissile KumaNative =
            Original323KumaPhoenixFireRules.PhoenixFire;
        private const string KumaAutoMissileQueueKind = "source-kuma-autofire-v1";
        private const string KumaAutoMissileQueuePayload = "S-28:h017:A0EA";

        private static readonly SourceScriptAttackSpec KumaPhoenixModel =
            new SourceScriptAttackSpec("S-28", "h017", "A0EA", 1, 1,
                null, new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, KumaNative.InitialDamage, 1, 1,
                    Ordr323AttackType.Spells) },
                "Apxf 미사일 비행·타깃 판정의 워크래프트 엔진 동등성 미검증");

        private void StartSourceModelKumaPhoenixFire(long actorId)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "S-28") return;
            ScheduleSourceModelKumaPhoenixFire(actor);
        }

        // The native autonomous cadence is serialized by kind and owner ID.
        // Unlike an anonymous repeating Action, save/restore cannot silently
        // lose the next A0EA missile or reset its timer to 0.2 seconds.
        private Action RestoreSourceModelKumaPhoenixFire(string kind,
            string payload, string casterId, string targetId)
        {
            if (kind != KumaAutoMissileQueueKind ||
                payload != KumaAutoMissileQueuePayload ||
                targetId != "practice-kuma-phoenix:" + casterId ||
                !UsesSourceModelBasicAttacks) return null;
            long actorId;
            CharacterInstance actor;
            if (!long.TryParse(casterId, out actorId) ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "S-28") return null;
            return () => FireSourceModelKumaPhoenixFire(actorId);
        }

        private void ScheduleSourceModelKumaPhoenixFire(CharacterInstance actor)
        {
            string key = InstanceKey(actor.Id);
            effectExecutor.ScheduleRestorableSourceAttack(key,
                "practice-kuma-phoenix:" + key,
                KumaNative.CooldownSeconds,
                KumaAutoMissileQueueKind, KumaAutoMissileQueuePayload,
                () => FireSourceModelKumaPhoenixFire(actor.Id));
        }

        private void FireSourceModelKumaPhoenixFire(long actorId)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "S-28") return;
            if (game.Phase == SessionPhase.Running)
            {
                var position = roster.Get(actor.Id);
                var target = NearestSourceModelTarget(actor,
                    position.X, position.Y, KumaNative.AcquisitionRadius);
                if (target != null &&
                    KumaNative.Contains(position.X, position.Y, target.X, target.Y))
                {
                    // One automatically acquired enemy per cooldown.
                    // The projectile presentation is a receipt; this mobile
                    // damage model resolves at acquisition until native
                    // missile impact timing is supported by the renderer.
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), target.Id,
                        "쿠마 · 피닉스 파이어",
                        "A0EA/Apxf 미사일 발사: 150 최초 마법 피해, 0.2초 발사, 반경 600, 지속피해 0", 0));
                    ApplySourceScriptHits(actor, target, KumaPhoenixModel,
                        KumaPhoenixModel.OnProc);
                }
            }
            ScheduleSourceModelKumaPhoenixFire(actor);
        }

        private void ScheduleSourceModelLiquidFire(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            string victimId = primary.Id;
            long current;
            sourceLiquidFireRevision.TryGetValue(victimId, out current);
            long revision = checked(current + 1);
            sourceLiquidFireRevision[victimId] = revision;
            string attackerId = InstanceKey(actor.Id);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attackerId, victimId,
                "우솝 화염탄 · 리퀴드 파이어",
                "MODEL_ONLY A0EG/Aliq: 150/초, 3초 화상 갱신 · 원본 틱·면역 미검증", 0));
            bool story = IsStoryDeployed(actor.Id);
            long lane = SourceModelLaneRevision(actor.Id);
            for (int second = 1; second <= 3; second++)
            {
                var pulse = new SourceNativeDelayedEffectPayload {
                    OwnerId = actor.Id, EnemyId = victimId,
                    StoryLane = story, LaneRevision = lane,
                    CastAt = effectExecutor.Time, Tick = second,
                    Revision = revision
                };
                effectExecutor.ScheduleRestorableSourceAttack(attackerId,
                    victimId, second, NativeLiquidFireKind,
                    SerializeNativeDelayed(pulse),
                    CreateNativeLiquidFirePulse(pulse));
            }
        }

        // The source-model reward is now driven by MoriaParticipationLedger:
        // a qualifying positive hit survives until a line enemy dies/leaks,
        // rather than requiring ANba's 0.05-second native death mark.
        // h00H remains a timed, non-roster projectile attack effect.

        private void SpawnSourceModelMoriaZombie(CharacterInstance actor,
            SpatialCombatEnemy defeated)
        {
            // S-09 A087/ANba: UnitID1=h00H, DataC1=10 seconds. This is
            // Moria's transient on-kill attack, NOT U-14 as a permanent
            // inventory/fusion unit. Absalom's separately acquired U-14
            // remains a real roster instance under its own original rules.
            //
            // The 10s mobile proxy uses the original h00H attack object,
            // native dice and cadence through the shared attack pipeline.
            // No free material, roster slot, wisp or U-14 spawn is created.
            // Original ANba object data supplies h00H and 10 seconds;
            // the user-approved contribution rule replaces the 0.05-second
            // death window. Native attack phase/corpse flags remain modeled.
            if (sourceModelRules == null ||
                sourceModelRules.SourceObjectAttack("h00H") == null)
            {
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                    defeated.Id, "모리아 · 블랙 애로우",
                    "MODEL_ONLY A087/ANba: h00H 공격 주사위 미확인, 무료 좀비 생성 금지", 0));
                return;
            }
            var effect = SpawnSourceTemporaryActor(actor,
                "모리아 · 블랙 애로우", "모리아 일시 좀비 공격 효과",
                10, "h00H", spawnX: defeated.X, spawnY: defeated.Y,
                modeledCombat: true, virtualAttackEffect: true);
            SourceIndependentAttacker projectile;
            if (sourceIndependentAttackers.TryGetValue(
                    SummonAttackKey(effect.Id), out projectile))
                projectile.ModelBlackArrowEffect = true;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                defeated.Id, "모리아 · 블랙 애로우",
                "MODEL_ONLY A087/ANba: 10초 가상 공격 효과 " + effect.Id +
                " / 막타 무관 처치 기여 / U-14 영구 보유·조합 미변경 / 첫 공격 위상 미검증", 0));
        }
    }
}
