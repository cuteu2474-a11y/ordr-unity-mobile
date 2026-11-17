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

    // Stable, already-committed dispatch only. Pending source-native
    // mobility/form casts have a separate irreversible ticket and cannot
    // be reconstructed from this record.
    [Serializable]
    public sealed class SourceCombatCreepDeploymentCheckpoint
    {
        public long OwnerId;
        public int Stage;
        public string TargetId;
        public double ReturnX, ReturnY;
        public double PositionX, PositionY;
        // The auto-attempt receipt must survive load or the source ability
        // could be cast again and charged a second time.
        public string AutoAttemptedEnemyId;
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
            if (!sourceWorld.IsAttachedTo(this))
                throw new InvalidOperationException(
                    "다른 전투에 연결된 원본 크립 세계를 사용할 수 없습니다.");
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

        // Cross-check saved actor/roster identity, destination history, and
        // non-main lane exclusivity. This does NOT call any native ability
        // or infer attack eligibility from mere unit movement classes.
        public static void ValidateCreepDeploymentState(
            SourceCombatRuntimeCheckpoint combat,
            Original323CreepPracticeState creeps,
            UnitRosterState roster)
        {
            if (combat == null || combat.Version != 4 ||
                combat.CreepDeployments == null)
                throw new FormatException("크립 파견 저장 형식이 잘못됐습니다.");
            if (combat.CreepDeployments.Count == 0) return;
            if (creeps == null || roster == null || roster.Units == null ||
                combat.Actors == null)
                throw new FormatException(
                    "크립 출전의 실물 크립/공통 유닛 상태가 누락됐습니다.");
            Original323CreepPracticeWorld.ValidateState(creeps);
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("크립 출전 유닛 ID가 잘못됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var rosterOwners = new Dictionary<long, UnitRosterEntryState>();
            foreach (var unit in roster.Units)
                if (unit == null || unit.InstanceId <= 0 ||
                    rosterOwners.ContainsKey(unit.InstanceId))
                    throw new FormatException(
                        "크립 저장의 공통 유닛 장부가 잘못됐습니다.");
                else rosterOwners.Add(unit.InstanceId, unit);
            var encounters = new Dictionary<string,
                Original323CreepEncounterCheckpoint>(StringComparer.Ordinal);
            foreach (var entry in creeps.Encounters)
                encounters.Add(entry.EnemyId, entry);
            // A historical target may be defeated/escaped, but a currently
            // alive encounter must have exactly one matching live combat
            // entity. Do not restore a phantom creep lane.
            var physical = new HashSet<string>(StringComparer.Ordinal);
            if (combat.Enemies == null)
                throw new FormatException("크립의 실제 전투 대상 목록이 없습니다.");
            foreach (var enemy in combat.Enemies)
                if (enemy != null && enemy.Kind == CharacterEnemyKind.Creep &&
                    !physical.Add(enemy.EnemyId))
                    throw new FormatException("중복된 크립 실물 적입니다.");
            foreach (var encounter in creeps.Encounters)
            {
                bool alive = physical.Remove(encounter.EnemyId);
                if ((encounter.State == Original323CreepEncounterState.Alive) != alive)
                    throw new FormatException(
                        "크립 실물과 출현/처치 기록이 일치하지 않습니다.");
            }
            if (physical.Count != 0)
                throw new FormatException("출현 기록에 없는 크립이 존재합니다.");
            var unique = new HashSet<long>();
            foreach (var row in combat.CreepDeployments)
            {
                SourceCombatActorCheckpoint actor;
                UnitRosterEntryState owned;
                Original323CreepEncounterCheckpoint encounter;
                if (row == null || row.OwnerId <= 0 ||
                    !unique.Add(row.OwnerId) ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    !rosterOwners.TryGetValue(row.OwnerId, out owned) ||
                    owned.UnitId != actor.CatalogId ||
                    actor.StoryDeployed || actor.PoneglyphChanneling ||
                    !string.IsNullOrEmpty(actor.SharedObjectiveEnemyId) ||
                    row.Stage < 1 || row.Stage > 3 ||
                    string.IsNullOrWhiteSpace(row.TargetId) ||
                    !encounters.TryGetValue(row.TargetId, out encounter) ||
                    encounter.Stage != row.Stage ||
                    double.IsNaN(row.ReturnX) || double.IsInfinity(row.ReturnX) ||
                    double.IsNaN(row.ReturnY) || double.IsInfinity(row.ReturnY) ||
                    double.IsNaN(row.PositionX) || double.IsInfinity(row.PositionX) ||
                    double.IsNaN(row.PositionY) || double.IsInfinity(row.PositionY) ||
                    owned.X != row.PositionX || owned.Y != row.PositionY)
                    throw new FormatException(
                        "크립 파견 소유자·전장·복귀 좌표가 잘못됐습니다.");
                if (row.AutoAttemptedEnemyId != null)
                {
                    Original323CreepEncounterCheckpoint attempted;
                    if (string.IsNullOrWhiteSpace(row.AutoAttemptedEnemyId) ||
                        !encounters.TryGetValue(row.AutoAttemptedEnemyId,
                            out attempted) ||
                        (attempted.Stage != row.Stage &&
                         attempted.Stage != row.Stage + 1))
                        throw new FormatException(
                            "원본 크립 이동기 재시전 방지 장부가 잘못됐습니다.");
                }
            }
        }

        // No pending source cast can be saved here: its native completion,
        // paid resources and one-use authorization are not serializable.
        private void CaptureCreepDeployments(SourceCombatRuntimeCheckpoint draft)
        {
            if (creepPreparations.Count != 0)
                throw new NotSupportedException(
                    "진행 중인 원본 크립 이동기·폼 시전은 저장할 수 없습니다.");
            if (creepDeployments.Count == 0)
            {
                if (autoCreepPreparationAttempted.Count != 0)
                    throw new InvalidOperationException(
                        "출전 유닛 없는 크립 시전 이력이 남았습니다.");
                return;
            }
            if (creepWorld == null || creepAuthority == null)
                throw new InvalidOperationException(
                    "크립 실물 전장과 원본 파견 증거가 함께 필요합니다.");
            var ids = new List<long>(creepDeployments.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                var assignment = creepDeployments[id];
                var owned = roster.Get(id);
                string attempt;
                draft.CreepDeployments.Add(
                    new SourceCombatCreepDeploymentCheckpoint {
                        OwnerId = id, Stage = assignment.Stage,
                        TargetId = assignment.TargetId,
                        ReturnX = assignment.ReturnX,
                        ReturnY = assignment.ReturnY,
                        PositionX = owned.X, PositionY = owned.Y,
                        AutoAttemptedEnemyId =
                            autoCreepPreparationAttempted.TryGetValue(
                                id, out attempt) ? attempt : null
                    });
            }
            foreach (long owner in autoCreepPreparationAttempted.Keys)
                if (!creepDeployments.ContainsKey(owner))
                    throw new InvalidOperationException(
                        "출전하지 않은 유닛의 크립 이동기 이력이 있습니다.");
            if (autoCreepPreparationAttempted.Count >
                creepDeployments.Count)
                throw new InvalidOperationException(
                    "크립 이동기 시도 기록의 유닛 ID가 중복됩니다.");
            ValidateCreepDeploymentState(draft, creepWorld.CaptureState(),
                roster.CaptureState());
        }

        // Called after the already-saved CreepPracticeWorld was restored
        // and all combat actors recovered, but before any combat tick.
        // Placement is not recomputed and no cast, reward or movement fires.
        public void RestoreCreepDeploymentsDraft(
            SourceCombatRuntimeCheckpoint combat,
            Original323CreepPracticeState savedCreeps,
            Original323CreepPracticeWorld restoredCreeps,
            IOriginal323CreepDispatchAuthority authority)
        {
            ValidateCreepDeploymentState(combat, savedCreeps,
                roster.CaptureState());
            if (combat.CreepDeployments.Count == 0) return;
            if (restoredCreeps == null || authority == null)
                throw new NotSupportedException(
                    "파견된 크립 유닛은 원본 전장·출전 증거 포트 없이 복원할 수 없습니다.");
            if (!restoredCreeps.IsAttachedTo(this))
                throw new InvalidOperationException(
                    "다른 전투/종료된 크립 월드의 출전은 복원할 수 없습니다.");
            if (game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 || activeOriginalFusionSwap != null ||
                creepWorld != null || creepDeployments.Count != 0 ||
                creepPreparations.Count != 0 ||
                autoCreepPreparationAttempted.Count != 0 ||
                characters.Count != combat.Actors.Count ||
                scheduler.Time != combat.Time)
                throw new InvalidOperationException(
                    "크립 출전은 준비된 비공개 전투에만 연결합니다.");
            var current = restoredCreeps.CaptureState();
            if (current.Encounters.Count != savedCreeps.Encounters.Count)
                throw new FormatException(
                    "복원된 크립 실물 개체 수가 원본 기록과 다릅니다.");
            foreach (var saved in savedCreeps.Encounters)
            {
                var present = restoredCreeps.FindEncounter(saved.EnemyId);
                if (present == null ||
                    present.OriginalObject.Stage != saved.Stage ||
                    present.OriginalObject.Rawcode != saved.Rawcode ||
                    present.State != saved.State ||
                    present.X != saved.X || present.Y != saved.Y)
                    throw new FormatException(
                        "크립 출전 목적지의 복원된 실물이 다릅니다.");
            }
            foreach (var row in combat.CreepDeployments)
                if (!characters.ContainsKey(row.OwnerId))
                    throw new FormatException(
                        "복원된 전투에 크립 출전 유닛이 없습니다.");
            // Attach only after all checks. A failed unpublished restore
            // is discarded; nothing touches the current live scene.
            ConfigureVerifiedCreepDispatch(restoredCreeps, authority);
            foreach (var row in combat.CreepDeployments)
            {
                creepDeployments.Add(row.OwnerId, new CreepDeployment {
                    Stage = row.Stage, TargetId = row.TargetId,
                    ReturnX = row.ReturnX, ReturnY = row.ReturnY
                });
                if (row.AutoAttemptedEnemyId != null)
                    autoCreepPreparationAttempted.Add(
                        row.OwnerId, row.AutoAttemptedEnemyId);
            }
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
