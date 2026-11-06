using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Skill-local auto commands, separate from the attack-proc callback.
        // No command here may spend a second source-script RNG roll.
        public int AutoCastSourceModelManualUtility(int maximumPerFrame = 16)
        {
            if (maximumPerFrame < 1 || maximumPerFrame > 128)
                throw new ArgumentOutOfRangeException(nameof(maximumPerFrame));
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0) return 0;

            var ids = new List<long>(characters.Keys);
            ids.Sort();
            int completed = 0;
            foreach (long id in ids)
            {
                if (completed >= maximumPerFrame) break;
                CharacterInstance actor;
                if (!characters.TryGetValue(id, out actor) ||
                    actor.Profile == null || actor.Mapping == null ||
                    !actor.AttackReady || IsOriginalUnitCasting(id) ||
                    SourceFormIsCasting(id)) continue;
                double ready;
                if (sourceManualReadyAt.TryGetValue(id, out ready) && Time < ready)
                    continue;
                SpatialCombatEnemy target = null;
                foreach (var candidate in EligibleSkillTargets(actor))
                {
                    // Delete/forced movement are not allowed to bypass
                    // boss, story, or enraged native immunity flags.
                    if (candidate.Kind != CharacterEnemyKind.Line) continue;
                    if (target == null || candidate.PathProgress > target.PathProgress ||
                        (candidate.PathProgress == target.PathProgress &&
                         string.CompareOrdinal(candidate.Id, target.Id) < 0))
                        target = candidate;
                }
                if (target == null) continue;
                if (actor.Catalog.Id == "L-19" &&
                    actor.Mapping.AttackSpec.UnitRawCode == "h037" &&
                    TryAutoCastSugarToy(actor, target)) completed++;
                else if (actor.Catalog.Id == "L-32" &&
                    actor.Mapping.AttackSpec.UnitRawCode == "h030" &&
                    TryAutoCastKumaTransfer(actor, target)) completed++;
            }
            return completed;
        }

        private bool TryAutoCastSugarToy(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            var skill = SourceManualSkill(actor, "장난감화(R)");
            if (skill == null || skill.Id != "L-19:skill:2" ||
                skill.Trigger != "manual" || skill.Target != "enemy" ||
                skill.Number("cooldownSeconds") != 85 ||
                skill.Components.Count != 2 ||
                skill.Components[0].Type != "delete" ||
                skill.Components[0].Text("target") != "enemy" ||
                skill.Components[1].Type != "summon" ||
                skill.Components[1].Text("unit") != "장난감 No.31" ||
                skill.Components[1].Number("durationSeconds") != 30)
                return false;
            // Delete without damage rather than fake a lethal spell hit.
            // The wave ledger receives a defeat event so the round can end;
            // original native loot/immunity parity remains to be verified.
            if (!resolution.TryResolveNonDamageDefeat(target.Id)) return false;
            RemoveEnemyRuntimeState(target.Id);
            var location = roster.Get(actor.Id);
            SpawnSourceTemporaryActor(actor, skill.Name, "장난감 No.31", 30,
                "h06N", true, location.X, location.Y);
            sourceManualReadyAt[actor.Id] = Time + 85;
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                target.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY 일반 라인 적 무피해 제거 + 30초 장난감 생성. 만료 판매 보상 미연결", 0));
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                target.Id, skill.Name,
                "장난감 만료 판매 보상·원본 적 삭제 면역 조건은 추가 근거 필요", 0));
            return true;
        }

        private bool TryAutoCastKumaTransfer(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = SourceManualSkill(actor, "전이");
            if (skill == null || skill.Id != "L-32:skill:2" ||
                skill.Trigger != "manual" ||
                skill.Target != "enemiesOwnLine" ||
                skill.Number("cooldownSeconds") != 60 ||
                skill.Radius != 275 || skill.Components.Count != 2 ||
                skill.Components[0].Type != "pull" ||
                skill.Components[0].Text("to") != "inFrontOfCaster" ||
                skill.Components[1].Type != "castOwnedStunSkill")
                return false;
            // Move the owning lane's line enemies near the selected target
            // towards the caster. This models the visible 'pull' result;
            // its native cast sequence/immune targets remain unverified.
            var pos = roster.Get(actor.Id);
            double dx = primary.X - pos.X, dy = primary.Y - pos.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= .00001) { dx = 1; dy = 0; length = 1; }
            double destinationX = pos.X + 80 * dx / length;
            double destinationY = pos.Y + 80 * dy / length;
            var targets = SourceScriptTargets(actor, primary,
                new SourceScriptHit(SourceScriptHitShape.Circle, 275, 0, 0,
                    1, 1, Ordr323AttackType.Spells));
            int moved = 0;
            foreach (var enemy in targets)
            {
                if (enemy.Kind != CharacterEnemyKind.Line ||
                    !enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                    continue;
                ForceMoveEnemy(enemy.Id, destinationX, destinationY,
                    enemy.PathProgress);
                moved++;
            }
            if (moved == 0) return false;
            sourceManualReadyAt[actor.Id] = Time + 60;
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY 라인 적 " + moved +
                "기 전방 이동. 원본 보유 스턴 스킬 재시전은 미연결", 0));
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                primary.Id, skill.Name,
                "보유 스턴 기술의 원본 주문/면역·시점 미확정", 0));
            return true;
        }
    }
}
