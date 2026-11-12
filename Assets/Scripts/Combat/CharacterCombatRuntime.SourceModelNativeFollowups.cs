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

        private static readonly SourceScriptAttackSpec KumaPhoenixModel =
            new SourceScriptAttackSpec("S-28", "h017", "A0EA", 1, 1,
                null, new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 150, 1, 1, Ordr323AttackType.Spells) },
                "Apxf 자동 발사 첫 시점·발사체·면역 미검증");

        private void StartSourceModelKumaPhoenixFire(long actorId)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "S-28") return;
            ScheduleSourceModelKumaPhoenixFire(actor);
        }

        private void ScheduleSourceModelKumaPhoenixFire(CharacterInstance actor)
        {
            string actorKey = InstanceKey(actor.Id);
            effectExecutor.SchedulePracticeSourceAttack(actorKey,
                "practice-kuma-phoenix:" + actorKey, .2, () => {
                    if (!characters.ContainsKey(actor.Id) ||
                        game.Phase != SessionPhase.Running) return;
                    var position = roster.Get(actor.Id);
                    // A0EA/Apxf: 150 at every .2s, 600 acquisition radius.
                    // Target choice and initial tick remain mobile policy.
                    var target = NearestSourceModelTarget(actor,
                        position.X, position.Y, 600);
                    if (target != null)
                    {
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.SkillActivated, actorKey,
                            target.Id, "쿠마 · 피닉스 파이어",
                            "MODEL_ONLY A0EA: 150 / 0.2초, 사거리 600 자동 화염", 0));
                        ApplySourceScriptHits(actor, target, KumaPhoenixModel,
                            KumaPhoenixModel.OnProc);
                    }
                    ScheduleSourceModelKumaPhoenixFire(actor);
                });
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
            for (int second = 1; second <= 3; second++)
            {
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    victimId, second, () => {
                        long latest;
                        if (!sourceLiquidFireRevision.TryGetValue(victimId, out latest) ||
                            latest != revision || !characters.ContainsKey(actor.Id) ||
                            !primary.Enemy.IsAlive) return;
                        ApplySourceScriptHits(actor, primary, LiquidFireModel,
                            LiquidFireModel.OnProc);
                    });
            }
        }

        private sealed class SourceMoriaBlackArrowMark
        {
            public long OwnerId;
            public double ExpiresAt;
            public bool StoryLane;
            public long LaneRevision;
            public string StatusSource;
        }

        // ANba/A087 from the original W3A: Dur1/HeroDur1=.05,
        // DataA1=0 bonus damage, DataC1=10 timed h00H attack,
        // BuffID1=BNba,BNdm. The recorded target mask excludes Hero,
        // Ancient and Sapper. This model restricts to standard line units
        // until precise original corpse/organic flags are available.
        private const double SourceMoriaBlackArrowMarkSeconds = .05;
        private readonly Dictionary<string, SourceMoriaBlackArrowMark>
            sourceMoriaBlackArrowMarks =
                new Dictionary<string, SourceMoriaBlackArrowMark>(StringComparer.Ordinal);
        private readonly HashSet<string> sourceMoriaBlackArrowConverted =
            new HashSet<string>(StringComparer.Ordinal);

        private static string SourceMoriaBlackArrowStatusId(long ownerId) =>
            "practice-native:A087:BNba:" + ownerId;

        private void ApplySourceMoriaBlackArrowAfterBasicHit(
            CharacterInstance owner, SpatialCombatEnemy victim)
        {
            if (!UsesSourceModelBasicAttacks || owner == null ||
                owner.Catalog.Id != "S-09" || victim == null ||
                victim.Kind != CharacterEnemyKind.Line ||
                !characters.ContainsKey(owner.Id) ||
                sourceMoriaBlackArrowConverted.Contains(victim.Id) ||
                !CanAttackInThisLane(owner, victim))
                return;
            // A successful weapon hit is the only mark source. It adds no
            // fabricated damage, no extra RNG and no persistent corpse unit.
            SourceMoriaBlackArrowMark old;
            if (sourceMoriaBlackArrowMarks.TryGetValue(victim.Id, out old))
            {
                CombatStatusEngine prior;
                if (statuses.TryGetValue(victim.Id, out prior))
                    prior.RemoveSource(old.StatusSource);
            }
            var mark = new SourceMoriaBlackArrowMark {
                OwnerId = owner.Id,
                ExpiresAt = effectExecutor.Time + SourceMoriaBlackArrowMarkSeconds,
                StoryLane = IsStoryDeployed(owner.Id),
                LaneRevision = SourceModelLaneRevision(owner.Id),
                StatusSource = SourceMoriaBlackArrowStatusId(owner.Id)
            };
            sourceMoriaBlackArrowMarks[victim.Id] = mark;
            CombatStatusEngine status;
            if (statuses.TryGetValue(victim.Id, out status))
                status.Apply(new CombatEffect(mark.StatusSource,
                    CombatEffectType.SourceBlackArrowDeathMark,
                    CombatEffectOrigin.TriggeredSkill, 1,
                    SourceMoriaBlackArrowMarkSeconds));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied,
                InstanceKey(owner.Id), victim.Id, "모리아 · 블랙 애로우 표식",
                "MODEL_ONLY A087/ANba: BNba/BNdm 0.05초 / 물리 추가 피해 0", 0));
            // CombatResolution emits DamageApplied before the normal attack
            // caller receives BasicAttackResult. A lethal Black Arrow must
            // still convert immediately, even without a later damage event.
            if (!victim.Enemy.IsAlive)
                TryConsumeSourceMoriaBlackArrowOnDeath(victim.Id);
        }

        private void TryConsumeSourceMoriaBlackArrowOnDeath(string enemyId)
        {
            if (sourceMoriaBlackArrowConverted.Contains(enemyId)) return;
            SourceMoriaBlackArrowMark mark;
            if (!sourceMoriaBlackArrowMarks.TryGetValue(enemyId, out mark)) return;
            CharacterInstance owner = null;
            SpatialCombatEnemy victim = null;
            CombatStatusEngine status = null;
            bool valid = effectExecutor.Time < mark.ExpiresAt &&
                characters.TryGetValue(mark.OwnerId, out owner) &&
                enemies.TryGetValue(enemyId, out victim) &&
                victim.Kind == CharacterEnemyKind.Line &&
                IsStoryDeployed(mark.OwnerId) == mark.StoryLane &&
                SourceModelLaneRevision(mark.OwnerId) == mark.LaneRevision &&
                !IsSharedObjectiveDeployed(mark.OwnerId) &&
                !IsOriginalPoneglyphChanneling(mark.OwnerId) &&
                statuses.TryGetValue(enemyId, out status) &&
                HasSourceMoriaBlackArrowStatus(status, mark.StatusSource);
            sourceMoriaBlackArrowMarks.Remove(enemyId);
            if (statuses.TryGetValue(enemyId, out status))
                status.RemoveSource(mark.StatusSource);
            if (!valid) return;
            sourceMoriaBlackArrowConverted.Add(enemyId);
            SpawnSourceModelMoriaZombie(owner, victim);
        }

        private static bool HasSourceMoriaBlackArrowStatus(
            CombatStatusEngine status, string source)
        {
            foreach (var effect in status.Snapshot())
                if (effect.SourceId == source &&
                    effect.Type == CombatEffectType.SourceBlackArrowDeathMark)
                    return true;
            return false;
        }

        private void ForgetSourceMoriaBlackArrowEnemy(string enemyId)
        {
            SourceMoriaBlackArrowMark mark;
            if (sourceMoriaBlackArrowMarks.TryGetValue(enemyId, out mark))
            {
                CombatStatusEngine status;
                if (statuses.TryGetValue(enemyId, out status))
                    status.RemoveSource(mark.StatusSource);
                sourceMoriaBlackArrowMarks.Remove(enemyId);
            }
            sourceMoriaBlackArrowConverted.Remove(enemyId);
        }

        private void ForgetSourceMoriaBlackArrowOwner(long ownerId)
        {
            var removed = new List<string>();
            foreach (var entry in sourceMoriaBlackArrowMarks)
                if (entry.Value.OwnerId == ownerId)
                    removed.Add(entry.Key);
            foreach (string enemyId in removed)
                ForgetSourceMoriaBlackArrowEnemy(enemyId);
        }

        private void ExpireSourceMoriaBlackArrowMarks()
        {
            if (sourceMoriaBlackArrowMarks.Count == 0) return;
            var expired = new List<string>();
            foreach (var entry in sourceMoriaBlackArrowMarks)
                if (effectExecutor.Time >= entry.Value.ExpiresAt ||
                    !characters.ContainsKey(entry.Value.OwnerId) ||
                    SourceModelLaneRevision(entry.Value.OwnerId) !=
                        entry.Value.LaneRevision)
                    expired.Add(entry.Key);
            foreach (string enemyId in expired)
                ForgetSourceMoriaBlackArrowEnemy(enemyId);
        }

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
            // Native mark duration and death listener now have source-backed
            // windows. Initial h00H attack phase and corpse flags remain a
            // model approximation until verified by Warcraft execution.
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
                " / U-14 영구 보유·조합 미변경 / 원본 첫 공격 위상·시체 조건 미검증", 0));
        }
    }
}
