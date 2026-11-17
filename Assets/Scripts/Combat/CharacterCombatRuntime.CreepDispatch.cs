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

    public enum Original323CreepPreparationKind
    {
        MovementSkill,
        InPlaceForm
    }

    // Immutable, read-only source authorization for a SPECIFIC owned unit,
    // creep life and original ability. It is not an approximate movement
    // class, a resource exemption or a free teleport.
    public sealed class Original323CreepPreparationPlan
    {
        public long InstanceId { get; private set; }
        public string CatalogId { get; private set; }
        public string EnemyId { get; private set; }
        public int Stage { get; private set; }
        public string OriginalAbilityRawcode { get; private set; }
        public string SourceEvidence { get; private set; }
        public Original323CreepPreparationKind Kind { get; private set; }
        public bool HasApprovedLanding { get; private set; }
        public double ApprovedLandingX { get; private set; }
        public double ApprovedLandingY { get; private set; }

        public Original323CreepPreparationPlan(long instanceId, string catalogId,
            string enemyId, int stage, string abilityRawcode,
            Original323CreepPreparationKind kind, string evidence,
            double? approvedLandingX = null, double? approvedLandingY = null)
        {
            if (instanceId <= 0 || string.IsNullOrWhiteSpace(catalogId) ||
                string.IsNullOrWhiteSpace(enemyId) || stage < 1 || stage > 3 ||
                string.IsNullOrWhiteSpace(abilityRawcode) ||
                !Enum.IsDefined(typeof(Original323CreepPreparationKind), kind) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                approvedLandingX.HasValue != approvedLandingY.HasValue ||
                approvedLandingX.HasValue &&
                    (double.IsNaN(approvedLandingX.Value) ||
                     double.IsInfinity(approvedLandingX.Value) ||
                     double.IsNaN(approvedLandingY.Value) ||
                     double.IsInfinity(approvedLandingY.Value)))
                throw new ArgumentException("원본 크립 준비의 유닛·기술·증빙이 필요합니다.");
            HasApprovedLanding = approvedLandingX.HasValue;
            ApprovedLandingX = approvedLandingX.GetValueOrDefault();
            ApprovedLandingY = approvedLandingY.GetValueOrDefault();
            InstanceId = instanceId;
            CatalogId = catalogId;
            EnemyId = enemyId;
            Stage = stage;
            OriginalAbilityRawcode = abilityRawcode;
            Kind = kind;
            SourceEvidence = evidence;
        }
    }

    public enum Original323CreepPreparationProgress
    {
        Pending,
        Completed,
        Failed
    }

    // Optional original bridge. Preview is pure/read-only. Begin invokes
    // the existing original skill/form engine EXACTLY ONCE, including all
    // mana, food, points, cooldown and cast-time rules. A false Begin MUST
    // have made no irreversible change. Observe only reads original progress.
    // Physical actor-replacement transformations require a separate roster
    // swap witness; this bridge supports the SAME owned instance only.
    public interface IOriginal323CreepConditionalDispatchAuthority :
        IOriginal323CreepDispatchAuthority
    {
        bool TryPreviewPreparation(long instanceId, string catalogId,
            Original323CreepEncounter target,
            out Original323CreepPreparationPlan plan);
        bool TryBeginPreparation(Original323CreepPreparationPlan plan);
        Original323CreepPreparationProgress ObservePreparation(
            Original323CreepPreparationPlan plan);
    }

    // A completed original mobility ticket is one-use state. The runtime
    // informs the source bridge once its single placement is settled/aborted,
    // or the real owned actor is removed.
    public interface IOriginal323CreepAbilityTicketLifecycle
    {
        void ForgetIssuedOriginalAbility(long instanceId);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private void ForgetIssuedCreepAbility(long id)
        {
            var lifecycle = creepAuthority as IOriginal323CreepAbilityTicketLifecycle;
            if (lifecycle != null)
                lifecycle.ForgetIssuedOriginalAbility(id);
        }

        private sealed class CreepDeployment
        {
            public int Stage;
            public string TargetId;
            public double ReturnX;
            public double ReturnY;
        }

        private readonly Dictionary<long, CreepDeployment> creepDeployments =
            new Dictionary<long, CreepDeployment>();
        private sealed class PendingCreepPreparation
        {
            public Original323CreepPreparationPlan Plan;
        }

        private readonly Dictionary<long, PendingCreepPreparation> creepPreparations =
            new Dictionary<long, PendingCreepPreparation>();
        // A native cast that failed or finished without a valid landing is
        // not automatically repeated every frame, which would double-charge
        // points/mana. A fresh player action remains possible.
        private readonly Dictionary<long, string> autoCreepPreparationAttempted =
            new Dictionary<long, string>();
        public event Action<Exception> CreepPreparationObserverFailed;

        private void ReportCreepPreparationObserverFailure(Exception error)
        {
            var listeners = CreepPreparationObserverFailed;
            if (listeners == null) return;
            foreach (Action<Exception> listener in listeners.GetInvocationList())
                try { listener(error); } catch { }
        }
        public int CreepPreparationCount => creepPreparations.Count;
        public bool IsPreparingCreepDispatch(long id) =>
            creepPreparations.ContainsKey(id);

        public IReadOnlyList<Original323CreepPreparationPlan>
            PendingCreepPreparationSnapshot()
        {
            var result = new List<Original323CreepPreparationPlan>();
            foreach (var entry in creepPreparations)
                result.Add(entry.Value.Plan);
            result.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            return result.AsReadOnly();
        }
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

        private bool CanStartCreepRequest(long id, Original323CreepEncounter target)
        {
            if (Game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 || activeOriginalFusionSwap != null ||
                target == null || !characters.ContainsKey(id) ||
                IsStoryDeployed(id) || sharedObjectiveDeployed.ContainsKey(id) ||
                originalPoneglyphChanneling.Contains(id) ||
                creepPreparations.ContainsKey(id) || IsOriginalUnitCasting(id))
                return false;
            CreepDeployment previous;
            return !creepDeployments.TryGetValue(id, out previous) ||
                previous.TargetId != target.EnemyId;
        }

        private bool PreviewCreepPreparation(long id,
            Original323CreepEncounter target,
            out Original323CreepPreparationPlan plan)
        {
            plan = null;
            var conditional = creepAuthority as
                IOriginal323CreepConditionalDispatchAuthority;
            CharacterInstance actor;
            if (conditional == null || target == null ||
                !characters.TryGetValue(id, out actor) ||
                !conditional.TryPreviewPreparation(id, actor.Catalog.Id,
                    target, out plan) || plan == null)
                return false;
            return plan.InstanceId == id &&
                plan.CatalogId == actor.Catalog.Id &&
                plan.EnemyId == target.EnemyId &&
                plan.Stage == target.OriginalObject.Stage &&
                Original323BossRoundRules.TrustedRuntimeEvidence(
                    plan.SourceEvidence);
        }

        public bool CanDispatchToCreep(long id, int stage)
        {
            var target = FindAliveCreepStage(stage);
            if (!CanStartCreepRequest(id, target)) return false;
            double x, y;
            if (TryCreepPlacement(id, target, out x, out y))
                return true;
            Original323CreepPreparationPlan plan;
            return PreviewCreepPreparation(id, target, out plan);
        }

        private void CommitCreepPlacement(long id,
            Original323CreepEncounter target, double x, double y)
        {
            CreepDeployment previous;
            if (!creepDeployments.TryGetValue(id, out previous))
            {
                var origin = roster.Get(id);
                previous = new CreepDeployment {
                    ReturnX = origin.X, ReturnY = origin.Y
                };
            }
            roster.Move(id, x, y);
            previous.Stage = target.OriginalObject.Stage;
            previous.TargetId = target.EnemyId;
            creepDeployments[id] = previous;
            RefreshAfterCommittedCombatLaneChange(id);
        }

        private bool BeginCreepPreparation(long id,
            Original323CreepEncounter target)
        {
            Original323CreepPreparationPlan plan;
            if (!PreviewCreepPreparation(id, target, out plan))
                return false;
            var conditional = (IOriginal323CreepConditionalDispatchAuthority)creepAuthority;
            // Reserve before calling original code. Reentrant UI, later
            // callbacks and duplicate taps cannot spend the same cost twice.
            if (creepPreparations.ContainsKey(id) ||
                !CanStartCreepRequest(id, target))
                return false;
            creepPreparations.Add(id, new PendingCreepPreparation { Plan = plan });
            try
            {
                bool started = conditional.TryBeginPreparation(plan);
                if (!started) creepPreparations.Remove(id);
                return started;
            }
            catch
            {
                // A native source exception is not a durable cast receipt.
                // Release the transaction lock, preserve the old placement,
                // and let the combat controller report the source failure.
                creepPreparations.Remove(id);
                throw;
            }
        }

        // One tap: directly move when already eligible; otherwise invoke the
        // ORIGINAL approved movement/form cast once and await its own clock.
        public bool TryDispatchToCreep(long id, int stage)
        {
            var target = FindAliveCreepStage(stage);
            if (!CanStartCreepRequest(id, target)) return false;
            double x, y;
            if (TryCreepPlacement(id, target, out x, out y))
            {
                CommitCreepPlacement(id, target, x, y);
                return true;
            }
            return BeginCreepPreparation(id, target);
        }

        // Called after the combat/source cast step, NOT from OnGUI. A source
        // skill that finished but can no longer reach an alive target never
        // grants a free relocation or rolls back a legitimate permanent form.
        public void AdvancePendingCreepPreparations()
        {
            if (processingStep || activeOriginalFusionSwap != null ||
                pendingStepSeconds != 0 || Game.Phase != SessionPhase.Running)
                return;
            var conditional = creepAuthority as
                IOriginal323CreepConditionalDispatchAuthority;
            var ids = new List<long>(creepPreparations.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                PendingCreepPreparation request;
                if (!creepPreparations.TryGetValue(id, out request) ||
                    conditional == null)
                    continue;
                Original323CreepPreparationProgress progress;
                try { progress = conditional.ObservePreparation(request.Plan); }
                catch (Exception error)
                {
                    // A cast may already have debited its own resources.
                    // Never silently reissue it after a witness exception.
                    creepPreparations.Remove(id);
                    ForgetIssuedCreepAbility(id);
                    ReportCreepPreparationObserverFailure(error);
                    continue;
                }
                if (progress == Original323CreepPreparationProgress.Pending)
                    continue;
                if (progress != Original323CreepPreparationProgress.Completed)
                {
                    creepPreparations.Remove(id);
                    ForgetIssuedCreepAbility(id);
                    continue;
                }
                // Keep the ticket live ONLY during this exact completion.
                // The mobility source can now prove a finished native AEbl
                // landing independently of any direct walking/pathing route.
                try
                {
                    CharacterInstance actor;
                    if (!characters.TryGetValue(id, out actor) ||
                        actor.Catalog.Id != request.Plan.CatalogId ||
                        IsStoryDeployed(id) ||
                        sharedObjectiveDeployed.ContainsKey(id) ||
                        originalPoneglyphChanneling.Contains(id))
                        continue;
                    var target = FindAliveCreepStage(request.Plan.Stage);
                    if (target == null ||
                        target.EnemyId != request.Plan.EnemyId)
                        continue;
                    double x, y;
                    if (TryCreepPlacement(id, target, out x, out y))
                        CommitCreepPlacement(id, target, x, y);
                }
                finally
                {
                    // A completed cast may never authorize a second free
                    // return to this location after recall or re-deploy.
                    creepPreparations.Remove(id);
                    ForgetIssuedCreepAbility(id);
                }
            }
            TryAutoRetargetInactiveCreepActors();
        }

        private void TryAutoRetargetInactiveCreepActors()
        {
            if (creepWorld == null || processingStep ||
                activeOriginalFusionSwap != null || pendingStepSeconds != 0)
                return;
            var ids = new List<long>(creepDeployments.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CreepDeployment assignment;
                if (!creepDeployments.TryGetValue(id, out assignment) ||
                    assignment.Stage >= 3 || CreepTargetOf(id) != null)
                    continue;
                var former = creepWorld.FindEncounter(assignment.TargetId);
                if (former == null ||
                    former.State != Original323CreepEncounterState.Defeated)
                    continue;
                var next = FindAliveCreepStage(assignment.Stage + 1);
                if (!CanStartCreepRequest(id, next)) continue;
                double x, y;
                if (TryCreepPlacement(id, next, out x, out y))
                {
                    CommitCreepPlacement(id, next, x, y);
                    continue;
                }
                string prior;
                if (autoCreepPreparationAttempted.TryGetValue(id, out prior) &&
                    prior == next.EnemyId)
                    continue;
                // Reserve this original target BEFORE running external
                // source code. A throwing cast must never be auto-reissued
                // every frame with a second mana/cooldown payment.
                autoCreepPreparationAttempted[id] = next.EnemyId;
                try
                {
                    if (!BeginCreepPreparation(id, next))
                        autoCreepPreparationAttempted.Remove(id);
                }
                catch (Exception error)
                {
                    ReportCreepPreparationObserverFailure(error);
                }
            }
        }

        public bool TryRecallFromCreep(long id)
        {
            if (activeOriginalFusionSwap != null || processingStep ||
                pendingStepSeconds != 0 ||
                creepPreparations.ContainsKey(id)) return false;
            CreepDeployment previous;
            if (!creepDeployments.TryGetValue(id, out previous) ||
                !characters.ContainsKey(id)) return false;
            creepDeployments.Remove(id);
            autoCreepPreparationAttempted.Remove(id);
            roster.Move(id, previous.ReturnX, previous.ReturnY);
            RefreshAfterCommittedCombatLaneChange(id);
            return true;
        }

        // The original CTp/CTq chain may introduce a new physical monster.
        // Only the newly spawned, immediately next stage can inherit a lane;
        // no new creep, reward or timer is invented by this callback.
        private void OnCreepEncounterSpawned(Original323CreepEncounter next)
        {
            // This may fire while another original effect is still casting.
            // A deferred retry happens after the next combat step without
            // charging any successful preparation a second time.
            TryAutoRetargetInactiveCreepActors();
        }

        private void ForgetCreepDeployment(long id)
        {
            creepDeployments.Remove(id);
            creepPreparations.Remove(id);
            autoCreepPreparationAttempted.Remove(id);
            ForgetIssuedCreepAbility(id);
        }
    }
}
