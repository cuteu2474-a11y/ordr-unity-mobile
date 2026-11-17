using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // An ORIGINAL-EVIDENCE authority, not a mobility-class or tier heuristic.
    // A production implementation must prove the actor can reach and damage
    // this EXACT live encounter, including any form, skill cost and cooldown.
    // The query is read-only and never spends resources or changes the form.
    // Until such an authority is installed, no ordinary owned unit is eligible.
    public interface IOriginal323CreepDispatchAuthority
    {
        bool TryResolveAttackPlacement(long instanceId,
            Original323CreepEncounter encounter, out double x, out double y);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private sealed class CreepDeployment
        {
            public int Stage;
            public string TargetId;
            public double ReturnX;
            public double ReturnY;
        }

        private readonly Dictionary<long, CreepDeployment> creepDeployments =
            new Dictionary<long, CreepDeployment>();
        private Original323CreepPracticeWorld creepWorld;
        private IOriginal323CreepDispatchAuthority creepAuthority;

        public bool HasVerifiedCreepDispatchAuthority => creepAuthority != null;
        public bool IsCreepDeployed(long id) => creepDeployments.ContainsKey(id);
        public int CreepDeployedCount => creepDeployments.Count;

        public int? CreepStageOf(long id)
        {
            CreepDeployment assignment;
            return creepDeployments.TryGetValue(id, out assignment)
                ? (int?)assignment.Stage : null;
        }

        // Defeated/escaped targets never remain attackable; the deployment
        // itself stays until an EXPLICIT recall or consumed-unit transaction.
        public string CreepTargetOf(long id)
        {
            CreepDeployment assignment;
            if (!creepDeployments.TryGetValue(id, out assignment) ||
                creepWorld == null) return null;
            var encounter = creepWorld.FindEncounter(assignment.TargetId);
            SpatialCombatEnemy enemy;
            return encounter != null &&
                encounter.State == Original323CreepEncounterState.Alive &&
                enemies.TryGetValue(assignment.TargetId, out enemy) &&
                enemy.Kind == CharacterEnemyKind.Creep &&
                enemy.Enemy.IsAlive ? assignment.TargetId : null;
        }

        public void ConfigureVerifiedCreepDispatch(
            Original323CreepPracticeWorld sourceWorld,
            IOriginal323CreepDispatchAuthority verifiedAuthority)
        {
            if (sourceWorld == null) throw new ArgumentNullException(nameof(sourceWorld));
            if (verifiedAuthority == null)
                throw new ArgumentNullException(nameof(verifiedAuthority));
            if (creepWorld != null || creepDeployments.Count != 0 ||
                processingStep || activeOriginalFusionSwap != null)
                throw new InvalidOperationException(
                    "크립 출전 공급자는 새 세션에 한 번만 연결합니다.");
            creepWorld = sourceWorld;
            creepAuthority = verifiedAuthority;
            creepWorld.EncounterSpawned += OnCreepEncounterSpawned;
        }

        private bool TryCreepPlacement(long id, Original323CreepEncounter encounter,
            out double x, out double y)
        {
            x = y = 0;
            if (creepAuthority == null || creepWorld == null ||
                encounter == null || encounter.State != Original323CreepEncounterState.Alive ||
                !characters.ContainsKey(id) ||
                creepWorld.FindEncounter(encounter.EnemyId) != encounter)
                return false;
            SpatialCombatEnemy real;
            if (!enemies.TryGetValue(encounter.EnemyId, out real) ||
                !real.Enemy.IsAlive || real.Kind != CharacterEnemyKind.Creep)
                return false;
            if (!creepAuthority.TryResolveAttackPlacement(id, encounter, out x, out y))
                return false;
            return !double.IsNaN(x) && !double.IsInfinity(x) &&
                   !double.IsNaN(y) && !double.IsInfinity(y);
        }

        private Original323CreepEncounter FindAliveCreepStage(int stage)
        {
            if (creepWorld == null || stage < 1 || stage > 3) return null;
            foreach (var encounter in creepWorld.Encounters)
                if (encounter.OriginalObject.Stage == stage &&
                    encounter.State == Original323CreepEncounterState.Alive)
                    return encounter;
            return null;
        }

        public bool CanDispatchToCreep(long id, int stage)
        {
            if (Game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 || activeOriginalFusionSwap != null ||
                IsStoryDeployed(id) || sharedObjectiveDeployed.ContainsKey(id) ||
                originalPoneglyphChanneling.Contains(id)) return false;
            var target = FindAliveCreepStage(stage);
            CreepDeployment previous;
            if (creepDeployments.TryGetValue(id, out previous) &&
                previous.TargetId == (target == null ? null : target.EnemyId))
                return false;
            double x, y;
            return TryCreepPlacement(id, target, out x, out y);
        }

        // One manual tap either changes the target of ONE owned actor or sends
        // it from the main lane. Failed eligibility never moves/recalls it.
        public bool TryDispatchToCreep(long id, int stage)
        {
            if (!CanDispatchToCreep(id, stage)) return false;
            var target = FindAliveCreepStage(stage);
            double x, y;
            if (!TryCreepPlacement(id, target, out x, out y)) return false;
            CreepDeployment previous;
            bool redirected = creepDeployments.TryGetValue(id, out previous);
            var at = roster.Get(id);
            if (!redirected)
                previous = new CreepDeployment {
                    ReturnX = at.X, ReturnY = at.Y
                };
            // No external source effect occurs before this point. The unit's
            // canonical roster identity, not a duplicate actor, changes lane.
            roster.Move(id, x, y);
            previous.Stage = target.OriginalObject.Stage;
            previous.TargetId = target.EnemyId;
            creepDeployments[id] = previous;
            RefreshAfterCommittedCombatLaneChange(id);
            return true;
        }

        public bool TryRecallFromCreep(long id)
        {
            if (activeOriginalFusionSwap != null || processingStep ||
                pendingStepSeconds != 0) return false;
            CreepDeployment previous;
            if (!creepDeployments.TryGetValue(id, out previous) ||
                !characters.ContainsKey(id)) return false;
            creepDeployments.Remove(id);
            roster.Move(id, previous.ReturnX, previous.ReturnY);
            RefreshAfterCommittedCombatLaneChange(id);
            return true;
        }

        // The original CTp/CTq chain may introduce a new physical monster.
        // Only the newly spawned, immediately next stage can inherit a lane;
        // no new creep, reward or timer is invented by this callback.
        private void OnCreepEncounterSpawned(Original323CreepEncounter next)
        {
            var ids = new List<long>(creepDeployments.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CreepDeployment previous;
                if (!creepDeployments.TryGetValue(id, out previous) ||
                    previous.Stage + 1 != next.OriginalObject.Stage) continue;
                var former = creepWorld.FindEncounter(previous.TargetId);
                if (former == null ||
                    former.State != Original323CreepEncounterState.Defeated) continue;
                double x, y;
                if (!TryCreepPlacement(id, next, out x, out y)) continue;
                roster.Move(id, x, y);
                previous.Stage = next.OriginalObject.Stage;
                previous.TargetId = next.EnemyId;
                RefreshAfterCommittedCombatLaneChange(id);
            }
        }

        private void ForgetCreepDeployment(long id)
        {
            creepDeployments.Remove(id);
        }
    }
}
