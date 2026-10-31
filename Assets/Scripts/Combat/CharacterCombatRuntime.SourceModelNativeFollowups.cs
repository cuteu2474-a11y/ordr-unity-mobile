using System;
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

        private void SpawnSourceModelMoriaZombie(CharacterInstance actor,
            SpatialCombatEnemy defeated)
        {
            // S-09 A087/ANba has UnitID1=h00H, DataA1=0, DataB1=1,
            // DataC1=10, Dur1=.05. The mobile outcome approximation applies
            // the killing blow's Black Arrow to an ordinary living enemy and
            // makes ONE h00H zombie for the source-listed ten-second lifetime.
            // Original on-kill/debuff timing and special target flags are not
            // yet certified by an actual Warcraft execution trace.
            long summoned = SpawnCharacter("U-14", defeated.X, defeated.Y);
            string zombieId = InstanceKey(summoned);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                defeated.Id, "모리아 · 블랙 애로우",
                "MODEL_ONLY A087/ANba: 처치 시 h00H 좀비 1기, 10초 소환 · 원본 조건 미검증", 1));
            effectExecutor.SchedulePracticeSourceAttack(zombieId,
                "practice-zombie-life:" + zombieId, 10, () => {
                    if (characters.ContainsKey(summoned))
                        RemoveCharacter(summoned);
                });
        }
    }
}
