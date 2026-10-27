using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // 272종 카탈로그를 소환하고, 200종 상세 정보가 존재하면 공격·발동 스킬과 연결.
    // 원작 평타 수치/피해 공식 미확인 유닛은 의도적으로 공격을 시작하지 않는다.
    public enum BasicAttackReadinessKind
    {
        UnverifiedSource, UnverifiedTarget, FormulaDeferred,
        NoBattlefieldTarget, Cooldown, Ready
    }

    public sealed class BasicAttackReadiness
    {
        public long InstanceId { get; private set; }
        public string CatalogId { get; private set; }
        public BasicAttackReadinessKind Kind { get; private set; }
        public string Detail { get; private set; }
        public double? NextAttackAtSeconds { get; private set; }
        internal BasicAttackReadiness(long id, string catalog, BasicAttackReadinessKind kind,
            string detail, double? nextAt)
        {
            InstanceId = id; CatalogId = catalog; Kind = kind;
            Detail = detail; NextAttackAtSeconds = nextAt;
        }
    }

    public sealed partial class CharacterCombatRuntime
    {
        private sealed class CharacterInstance
        {
            public long Id;
            public CatalogUnit Catalog;
            public CharacterSkillProfile Profile;
            public CharacterCombatMapping Mapping;
            public CharacterActiveAbilities Abilities;
            public bool AttackReady;
            public bool AttackBlockedByMissingFormula;
            public string DeferredAttackReason;
            public string AttackEvidence;
            public IOriginalVerifiedBasicAttackTargetFilter BasicTargetFilter;
        }

        // Native source-verified fusion: material actors keep their REAL
        // cooldowns, statuses and scheduled effects until final commit.
        // A rollback only removes the staged output, never recreates inputs.
        private OriginalFusionUnitSwap activeOriginalFusionSwap;
        private bool internalOriginalFusionMutation;

        public sealed class OriginalFusionUnitSwap : IDisposable
        {
            private readonly CharacterCombatRuntime world;
            private readonly List<long> materialIds;
            private readonly HashSet<long> hiddenIds;
            private readonly Dictionary<long, UnitState> sourcePositions =
                new Dictionary<long, UnitState>();
            private readonly Dictionary<long, long> sourceStatusRevisions =
                new Dictionary<long, long>();
            private readonly double atTime;
            private readonly int atWave;
            private readonly SessionPhase atPhase;
            private long resultInstanceId;
            private bool committed;
            private bool disposed;

            internal OriginalFusionUnitSwap(CharacterCombatRuntime owner,
                List<long> inputs)
            {
                world = owner;
                materialIds = inputs;
                hiddenIds = new HashSet<long>(inputs);
                atTime = owner.Time;
                atWave = owner.Game.CurrentWave;
                atPhase = owner.Game.Phase;
                foreach (long id in inputs)
                {
                    sourcePositions.Add(id, owner.roster.Get(id));
                    sourceStatusRevisions.Add(id,
                        owner.unitStatuses[InstanceKey(id)].Revision);
                }
            }

            public long ResultInstanceId { get { return resultInstanceId; } }
            internal bool Hides(long instanceId)
            {
                return hiddenIds.Contains(instanceId);
            }

            internal void StageResult(string resultCatalogId, double x, double y)
            {
                world.internalOriginalFusionMutation = true;
                try
                {
                    // Preserve exact original witnessed placement; the
                    // regular mobile auto-placement policy is NOT an
                    // authority to reposition a native fusion result.
                    resultInstanceId = world.SpawnStagedOriginalFusionResult(
                        resultCatalogId, x, y);
                }
                finally
                {
                    world.internalOriginalFusionMutation = false;
                }
            }

            public void Commit()
            {
                if (disposed || committed || resultInstanceId <= 0 ||
                    !Object.ReferenceEquals(world.activeOriginalFusionSwap, this) ||
                    world.Time != atTime ||
                    world.Game.Phase != atPhase ||
                    world.Game.CurrentWave != atWave)
                    throw new InvalidOperationException(
                        "원본 조합 유닛 교체의 실행 시점이 변경됐습니다.");
                if (!world.characters.ContainsKey(resultInstanceId))
                    throw new InvalidOperationException("검증한 결과 캐릭터가 사라졌습니다.");
                foreach (long id in materialIds)
                {
                    if (!world.characters.ContainsKey(id) ||
                        world.IsStoryDeployed(id))
                        throw new InvalidOperationException(
                            "원본 조합 재료가 사라지거나 파견됐습니다.");
                    UnitState oldLocation = sourcePositions[id];
                    UnitState now = world.roster.Get(id);
                    if (now.X != oldLocation.X || now.Y != oldLocation.Y ||
                        world.unitStatuses[InstanceKey(id)].Revision !=
                            sourceStatusRevisions[id])
                        throw new InvalidOperationException(
                            "원본 조합 검증 중 재료 위치·상태효과가 변경됐습니다.");
                }
                // The verified native effect world MUST hold an exclusive
                // rollback-capable lock across this final material teardown.
                // Nothing is deleted before the actual result has passed the
                // higher-level original BGH/rawcode and wallet audit.
                world.internalOriginalFusionMutation = true;
                try
                {
                    // Reserve the sole visible source-backed spawn cue BEFORE
                    // touching the original source actors.
                    var result = world.characters[resultInstanceId];
                    var publishedSpawn = new CharacterCombatEvent(
                        CharacterCombatEventType.UnitSpawned,
                        InstanceKey(resultInstanceId), null, null,
                        result.Catalog.Name, 0);
                    var publishedPlacement = new CharacterCombatEvent(
                        CharacterCombatEventType.UnitPlaced,
                        InstanceKey(resultInstanceId), null, null,
                        "verified-original-fusion-position", 0);
                    world.pendingCommandEvents.Capacity = Math.Max(
                        world.pendingCommandEvents.Capacity,
                        world.pendingCommandEvents.Count + 2);
                    foreach (long id in materialIds)
                        world.RemoveCharacter(id);
                    world.pendingCommandEvents.Add(publishedSpawn);
                    world.pendingCommandEvents.Add(publishedPlacement);
                    committed = true;
                    world.activeOriginalFusionSwap = null;
                    // Step() reconciles auras at the next actual combat
                    // frame. No external callback runs midway through commit.
                }
                finally
                {
                    world.internalOriginalFusionMutation = false;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed && resultInstanceId > 0 &&
                        world.characters.ContainsKey(resultInstanceId))
                    {
                        world.internalOriginalFusionMutation = true;
                        try { world.RollbackUncommittedSpawn(resultInstanceId); }
                        finally { world.internalOriginalFusionMutation = false; }
                    }
                }
                finally
                {
                    if (Object.ReferenceEquals(world.activeOriginalFusionSwap, this))
                        world.activeOriginalFusionSwap = null;
                }
            }
        }

        // No unit is deleted in Stage; final destruction occurs only after
        // original CHu/CHx witness, generated rawcode and wallet are audited.
        public OriginalFusionUnitSwap BeginOriginalFusionUnitSwap(
            IReadOnlyList<long> exactMaterials, string resultCatalogId,
            double verifiedSpawnX, double verifiedSpawnY)
        {
            if (activeOriginalFusionSwap != null || processingStep ||
                pendingStepSeconds != 0)
                throw new InvalidOperationException(
                    "전투 업데이트 또는 다른 원본 조합 중에는 유닛 교체 불가");
            if (exactMaterials == null || exactMaterials.Count == 0 ||
                string.IsNullOrWhiteSpace(resultCatalogId))
                throw new ArgumentException("원본 조합 결과 및 재료가 누락됐습니다.");
            if (double.IsNaN(verifiedSpawnX) || double.IsInfinity(verifiedSpawnX) ||
                double.IsNaN(verifiedSpawnY) || double.IsInfinity(verifiedSpawnY))
                throw new ArgumentOutOfRangeException(nameof(verifiedSpawnX));
            units.Require(resultCatalogId);
            var materials = new List<long>();
            var unique = new HashSet<long>();
            foreach (long id in exactMaterials)
            {
                if (id <= 0 || !unique.Add(id) ||
                    !characters.ContainsKey(id) || IsStoryDeployed(id))
                    throw new InvalidOperationException(
                        "존재하지 않거나 파견 중인 원본 재료 인스턴스");
                materials.Add(id);
            }
            var ticket = new OriginalFusionUnitSwap(this, materials);
            activeOriginalFusionSwap = ticket;
            try
            {
                ticket.StageResult(resultCatalogId, verifiedSpawnX, verifiedSpawnY);
                return ticket;
            }
            catch
            {
                ticket.Dispose();
                throw;
            }
        }

        private void BlockExternalMutationDuringOriginalFusion()
        {
            if (activeOriginalFusionSwap != null &&
                !internalOriginalFusionMutation)
                throw new InvalidOperationException(
                    "원본 조합 검증 중에는 전투 유닛·시간 변경 불가");
        }

        private readonly CharacterUnitCatalog units;
        private readonly CharacterSkillCatalog profiles;
        private readonly UnitRoster roster;
        private readonly GameSession game = new GameSession();
        private readonly CombatScheduler scheduler = new CombatScheduler();
        private readonly CombatSkillTriggerEngine triggers;
        private readonly CombatResolution resolution;
        private readonly CombatAttackPipeline attackPipeline;
        private readonly CharacterSkillEffectExecutor effectExecutor;
        private readonly Dictionary<long, CharacterInstance> characters =
            new Dictionary<long, CharacterInstance>();
        private readonly Dictionary<string, SpatialCombatEnemy> enemies =
            new Dictionary<string, SpatialCombatEnemy>(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatStatusEngine> statuses =
            new Dictionary<string, CombatStatusEngine>(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatStatusEngine> unitStatuses =
            new Dictionary<string, CombatStatusEngine>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> armorBreakStacks =
            new Dictionary<string, int>(StringComparer.Ordinal);
        // Bdq(A04M) 원본 함수와 연결된 기술로 발생했음이 확인된 스택만 기록.
        private readonly Dictionary<string, int> provenA04MStacks =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> provenA04PTotals =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private Ordr323BonneyDefenseController bonneyArmor;
        private IOriginalBonneyFormEvidence bonneyFormEvidence;
        private readonly List<CharacterCombatEvent> events = new List<CharacterCombatEvent>();
        private readonly CombatAuraSystem auras = new CombatAuraSystem();
        private readonly List<CharacterCombatEvent> pendingCommandEvents =
            new List<CharacterCombatEvent>();
        private bool processingStep;
        private double pendingStepSeconds;
        private IOriginalStoryDispatchPort storyDispatch;
        private IOriginalVerifiedUnitAttackProvider verifiedAttackProvider;
        private OriginalMapCommonAttackPreview commonAttackPreview;
        private Original323UncommonAbilityCatalog uncommonAbilities;
        private IOriginalUncommonVerifiedProcWorld uncommonProcWorld;
        private string executingUncommonProcId;
        private string executingUncommonActor;
        private string executingUncommonTarget;
        private Original323ScriptAttackEffectCoordinator originalScriptAttackEffects;
        private string executingOriginalDummyActor;
        private string executingOriginalDummyTarget;
        private string executingOriginalDummyRawcode;
        private readonly Dictionary<string, long> originalScriptAttackSequences =
            new Dictionary<string, long>(StringComparer.Ordinal);
        // Original carrion-swarm projectiles can strike AFTER their attack
        // declaration. Keep cast ownership and lane immutable across frames.
        private sealed class PendingOriginalDummyCast
        {
            public Original323ObservedDummyCast Snapshot;
            public bool IsStoryLane;
            public readonly HashSet<string> AlreadyHit =
                new HashSet<string>(StringComparer.Ordinal);
            // Includes original native targets whose magic immunity resulted
            // in zero HP loss. Forked lightning's 4-target cap uses THESE.
            public readonly HashSet<string> AlreadySelected =
                new HashSet<string>(StringComparer.Ordinal);
            public int ImmuneSelections;
            public readonly HashSet<string> ImpactIds =
                new HashSet<string>(StringComparer.Ordinal);
            public double LastImpactAt;
        }
        private readonly Dictionary<string, PendingOriginalDummyCast>
            pendingOriginalDummyCasts =
                new Dictionary<string, PendingOriginalDummyCast>(StringComparer.Ordinal);
        private readonly HashSet<string> finishedOriginalDummyCastIds =
            new HashSet<string>(StringComparer.Ordinal);
        private bool committingOriginalDummyImpact;
        public int PendingOriginalDummySpellCount
        {
            get { return pendingOriginalDummyCasts.Count; }
        }

        public bool TryGetOriginalDummyTargetSelectionProgress(
            string sourceCastId, out int selectedCount, out int immuneCount)
        {
            selectedCount = 0; immuneCount = 0;
            PendingOriginalDummyCast cast;
            if (string.IsNullOrWhiteSpace(sourceCastId) ||
                !pendingOriginalDummyCasts.TryGetValue(sourceCastId, out cast))
                return false;
            selectedCount = cast.AlreadySelected.Count;
            immuneCount = cast.ImmuneSelections;
            return true;
        }

        // Registers only original-attacked-event spell dispatch for the three
        // exact original rawcodes. Cannot replace the original basic attack,
        // native Critical Strike RNG, or actual WC3 spell damage formulas.
        public void ConfigureOriginal323ScriptAttackEffectWorld(
            Original323ScriptAttackProcRolls verifiedOriginalRolls,
            IOriginal323VerifiedAttackDummySpellWorld originalSpellWorld)
        {
            if (characters.Count != 0 || originalScriptAttackEffects != null)
                throw new InvalidOperationException(
                    "원본 공격 순간 기술은 첫 유닛 생성 전 한 번만 연결해야 합니다.");
            var coordinator = new Original323ScriptAttackEffectCoordinator(
                verifiedOriginalRolls, originalSpellWorld);
            attackPipeline.ConfigureOriginalScriptAttackDeclared(
                OnOriginal323AttackedScriptSpell);
            originalScriptAttackEffects = coordinator;
        }

        private void OnOriginal323AttackedScriptSpell(
            string attackerId, string originalRawcode, string targetId)
        {
            if (originalScriptAttackEffects == null ||
                !Original323ScriptAttackEffectCoordinator.Supports(originalRawcode))
                return;
            long sequence;
            originalScriptAttackSequences.TryGetValue(attackerId, out sequence);
            if (sequence == long.MaxValue)
                throw new OverflowException("원본 공격 선언 이벤트 순번 초과");
            sequence++;
            originalScriptAttackSequences[attackerId] = sequence;
            if (!processingStep || executingOriginalDummyActor != null)
                throw new InvalidOperationException(
                    "공격 선언 밖에서 원본 보조 주문을 중복 실행할 수 없습니다.");
            executingOriginalDummyActor = attackerId;
            executingOriginalDummyTarget = targetId;
            executingOriginalDummyRawcode = originalRawcode;
            try
            {
                Original323AttackDummySpellResult result;
                if (!originalScriptAttackEffects.TryDispatch(
                    originalRawcode, attackerId, targetId,
                    sequence, this, out result) ||
                    result == null || !result.Triggered)
                    return;
                events.Add(new CharacterCombatEvent(
                    result.Applied ? CharacterCombatEventType.SkillActivated :
                        CharacterCombatEventType.EffectDeferred,
                    attackerId, targetId, result.HumanSkillName,
                    result.Applied ? result.Evidence :
                        "원본 주문 전체 효과 검증/적용 보류: " +
                        (result.Evidence ?? result.HumanSkillName), 0));
            }
            finally
            {
                executingOriginalDummyActor = null;
                executingOriginalDummyTarget = null;
                executingOriginalDummyRawcode = null;
            }
        }

        // The original attack-declared world may commit an immediate native
        // impact. New observed-impact flows register a cast at declaration
        // and commit each separate projectile impact at its observed time.
        internal bool TryCommitOriginalDummySpellHits(
            Original323AttackDummySpellCommand command,
            string attackerId, string attackedEnemyId,
            IReadOnlyList<Original323NativeDummySpellHit> nativeHits,
            out long totalActualDamage)
        {
            totalActualDamage = 0;
            Original323NativeDummyAbilityFacts facts;
            if (!processingStep || game.Phase != SessionPhase.Running ||
                attackerId != executingOriginalDummyActor ||
                attackedEnemyId != executingOriginalDummyTarget ||
                command == null ||
                command.AttackerRawcode != executingOriginalDummyRawcode ||
                !Original323NativeDummyAbilityCatalog.TryGet(command, out facts))
                return false;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != command.AttackerRawcode ||
                !enemies.TryGetValue(attackedEnemyId, out primary) ||
                !primary.Enemy.IsAlive ||
                !CanAttackInThisLane(actor, primary))
                return false;
            return TryCommitOriginalDummyHitBatch(command, attackerId,
                nativeHits, IsStoryDeployed(id), 0,
                null, out totalActualDamage);
        }

        // Called ONLY during the actual attacked-event callback. A cast
        // identifier is supplied by a verified Warcraft observer; it is not
        // an arbitrary UI-generated "cast" or a made-up travel time.
        internal bool TryRegisterObservedOriginalDummyCast(
            Original323AttackDummySpellCommand command,
            string attackerId, string originalTargetId,
            string originalCastId, string originalCastEvidence)
        {
            Original323NativeDummyAbilityFacts facts;
            if (game.Phase != SessionPhase.Running || !processingStep ||
                command == null ||
                !Original323NativeDummyAbilityCatalog.TryGet(command, out facts) ||
                attackerId != executingOriginalDummyActor ||
                originalTargetId != executingOriginalDummyTarget ||
                command.AttackerRawcode != executingOriginalDummyRawcode ||
                string.IsNullOrWhiteSpace(originalCastId) ||
                originalCastId.Length > 256 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(originalCastId) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(originalCastEvidence) ||
                pendingOriginalDummyCasts.ContainsKey(originalCastId) ||
                finishedOriginalDummyCastIds.Contains(originalCastId) ||
                pendingOriginalDummyCasts.Count >= 4096)
                return false;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != command.AttackerRawcode ||
                !enemies.TryGetValue(originalTargetId, out primary) ||
                !primary.Enemy.IsAlive ||
                !CanAttackInThisLane(actor, primary))
                return false;
            var cast = new Original323ObservedDummyCast(
                originalCastId, command, attackerId, originalTargetId,
                scheduler.Time, originalCastEvidence);
            pendingOriginalDummyCasts.Add(originalCastId,
                new PendingOriginalDummyCast {
                    Snapshot = cast,
                    IsStoryLane = IsStoryDeployed(id),
                    LastImpactAt = scheduler.Time
                });
            return true;
        }

        internal bool TryGetPendingOriginalDummyCast(string castId,
            out Original323ObservedDummyCast source)
        {
            source = null;
            PendingOriginalDummyCast state;
            if (string.IsNullOrWhiteSpace(castId) ||
                !pendingOriginalDummyCasts.TryGetValue(castId, out state))
                return false;
            source = state.Snapshot;
            return true;
        }

        // An independently observed native dummy despawn/order rejection
        // can end a cast with ZERO additional damage. Without this explicit
        // terminal receipt, failed/immune e0KD casts remain pending forever
        // and eventually block later Enel Forked Lightning activations.
        // Never infer expiration from an arbitrary timeout or a dead target:
        // delayed projectiles can still reach different Warcraft enemies.
        internal bool TryFinalizeObservedOriginalDummyCastWithoutHit(
            Original323ObservedDummyCast witnessedCast,
            Original323ObservedDummyCastEnd sourceEnd)
        {
            BlockExternalMutationDuringOriginalFusion();
            PendingOriginalDummyCast current;
            if (game.Phase != SessionPhase.Running ||
                committingOriginalDummyImpact ||
                witnessedCast == null || sourceEnd == null ||
                !pendingOriginalDummyCasts.TryGetValue(
                    witnessedCast.CastId, out current) ||
                !Object.ReferenceEquals(current.Snapshot, witnessedCast) ||
                sourceEnd.CastId != witnessedCast.CastId ||
                sourceEnd.DummyRawcode != "e0KD" ||
                sourceEnd.AbilityRawcode !=
                    witnessedCast.OriginalOrder.OriginalAbilityRawcode ||
                sourceEnd.OriginalW3xSha256 !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                sourceEnd.NativeEndTimeSeconds < witnessedCast.StartTimeSeconds ||
                sourceEnd.NativeEndTimeSeconds < current.LastImpactAt ||
                Math.Abs(sourceEnd.NativeEndTimeSeconds - scheduler.Time) > 1e-6 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    sourceEnd.OriginalLifecycleEvidence) ||
                ((sourceEnd.Reason ==
                    Original323DummyNativeCastEndReason.OriginalOrderRejected ||
                  sourceEnd.Reason ==
                    Original323DummyNativeCastEndReason.OriginalCompletedWithoutDamage) &&
                    current.AlreadyHit.Count > 0) ||
                finishedOriginalDummyCastIds.Contains(witnessedCast.CastId))
                return false;
            // Canceled cast is irrevocably finished, but any damage from
            // legitimately committed earlier impact receipts stays intact.
            pendingOriginalDummyCasts.Remove(witnessedCast.CastId);
            finishedOriginalDummyCastIds.Add(witnessedCast.CastId);
            var eventSink = processingStep ? events : pendingCommandEvents;
            eventSink.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred,
                witnessedCast.AttackerInstanceId, witnessedCast.OriginalTargetId,
                witnessedCast.OriginalOrder.HumanSkillName,
                "원본 e0KD 주문 종료 (" + sourceEnd.Reason + ")" +
                " / " + sourceEnd.OriginalLifecycleEvidence, 0));
            return true;
        }

        // Only a witnessed native IMPACT at this combat clock time can
        // change HP. No fixed projectile speed, guessed path intersection,
        // arbitrary splash radius or automatic "cast equals damage".
        internal bool TryCommitObservedOriginalDummyImpact(
            string originalCastId, Original323NativeDummyImpact impact,
            out long totalActualDamage)
        {
            totalActualDamage = 0;
            PendingOriginalDummyCast cast;
            if (game.Phase != SessionPhase.Running ||
                committingOriginalDummyImpact ||
                impact == null ||
                !pendingOriginalDummyCasts.TryGetValue(originalCastId,
                    out cast) ||
                cast.ImpactIds.Contains(impact.ImpactId) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    impact.OriginalImpactEvidence) ||
                impact.ObservedTimeSeconds < cast.Snapshot.StartTimeSeconds ||
                impact.ObservedTimeSeconds < cast.LastImpactAt ||
                Math.Abs(impact.ObservedTimeSeconds - scheduler.Time) > 1e-6)
                return false;

            // Native FORK selection is not the same as a positive-damage
            // hit. Verify all source-observed choices (including immune)
            // before mutating a single HP value.
            Original323NativeDummyAbilityFacts nativeFacts;
            if (!Original323NativeDummyAbilityCatalog.TryGet(
                    cast.Snapshot.OriginalOrder, out nativeFacts))
                return false;
            if (nativeFacts.MaxNativeTargets.HasValue &&
                !impact.HasOriginalTargetSelection)
                return false;
            var selectedInThisImpact =
                new HashSet<string>(StringComparer.Ordinal);
            var immuneInThisImpact =
                new HashSet<string>(StringComparer.Ordinal);
            foreach (var selected in impact.SelectedTargets)
            {
                SpatialCombatEnemy enemy;
                if (selected == null ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        selected.OriginalSelectionEvidence) ||
                    !selectedInThisImpact.Add(selected.TargetId) ||
                    cast.AlreadySelected.Contains(selected.TargetId) ||
                    !enemies.TryGetValue(selected.TargetId, out enemy) ||
                    !enemy.Enemy.IsAlive ||
                    (cast.IsStoryLane ?
                        enemy.Kind != CharacterEnemyKind.Story :
                        (enemy.Kind != CharacterEnemyKind.Line &&
                         enemy.Kind != CharacterEnemyKind.Boss &&
                         enemy.Kind != CharacterEnemyKind.Enraged)))
                    return false;
                if (selected.OriginalMagicImmune)
                    immuneInThisImpact.Add(selected.TargetId);
            }
            if (nativeFacts.MaxNativeTargets.HasValue &&
                (long)cast.AlreadySelected.Count +
                    selectedInThisImpact.Count >
                    nativeFacts.MaxNativeTargets.Value)
                return false;
            if (impact.HasOriginalTargetSelection)
                foreach (var hit in impact.Hits)
                    if (hit == null ||
                        !selectedInThisImpact.Contains(hit.TargetId) ||
                        immuneInThisImpact.Contains(hit.TargetId))
                        return false;

            // Stage all checks before the first mutation. A native swarm can
            // report consecutive projectile contacts in different frames.
            committingOriginalDummyImpact = true;
            try
            {
                if (!TryCommitOriginalDummyHitBatch(
                    cast.Snapshot.OriginalOrder,
                    cast.Snapshot.AttackerInstanceId,
                    impact.Hits, cast.IsStoryLane,
                    cast.AlreadyHit.Count, cast.AlreadyHit,
                    out totalActualDamage))
                    return false;
                foreach (var hit in impact.Hits)
                    cast.AlreadyHit.Add(hit.TargetId);
                foreach (var selected in selectedInThisImpact)
                    cast.AlreadySelected.Add(selected);
                cast.ImmuneSelections += immuneInThisImpact.Count;
                cast.ImpactIds.Add(impact.ImpactId);
                cast.LastImpactAt = impact.ObservedTimeSeconds;
                if (impact.SpellCompleted)
                {
                    pendingOriginalDummyCasts.Remove(originalCastId);
                    finishedOriginalDummyCastIds.Add(originalCastId);
                }
                return true;
            }
            finally { committingOriginalDummyImpact = false; }
        }

        // Shared atomic preflight for both immediate and delayed verified
        // Warcraft impacts. For delayed projectiles, the original attacked
        // enemy can already be dead; use captured CAST lane, not the
        // caster's possibly changed/current dispatch assignment.
        private bool TryCommitOriginalDummyHitBatch(
            Original323AttackDummySpellCommand command,
            string attackerId, IReadOnlyList<Original323NativeDummySpellHit> hits,
            bool storyLaneAtCast, int previouslyHit,
            HashSet<string> priorTargets,
            out long totalActualDamage)
        {
            totalActualDamage = 0;
            Original323NativeDummyAbilityFacts facts;
            if (command == null ||
                !Original323NativeDummyAbilityCatalog.TryGet(command, out facts) ||
                hits == null || hits.Count > 512 ||
                (facts.MaxNativeTargets.HasValue &&
                    (long)previouslyHit + hits.Count >
                        facts.MaxNativeTargets.Value))
                return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            long totalPreflight = 0;
            foreach (var hit in hits)
            {
                SpatialCombatEnemy target;
                if (hit == null ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        hit.OriginalNativeHitEvidence) ||
                    hit.FinalHpDamage < 0 ||
                    !unique.Add(hit.TargetId) ||
                    (priorTargets != null &&
                        priorTargets.Contains(hit.TargetId)) ||
                    !enemies.TryGetValue(hit.TargetId, out target) ||
                    !target.Enemy.IsAlive ||
                    (storyLaneAtCast ?
                        target.Kind != CharacterEnemyKind.Story :
                        (target.Kind != CharacterEnemyKind.Line &&
                         target.Kind != CharacterEnemyKind.Boss &&
                         target.Kind != CharacterEnemyKind.Enraged)) ||
                    !ReferenceEquals(resolution.GetEnemy(hit.TargetId),
                        target.Enemy))
                    return false;
                try
                {
                    totalPreflight = checked(totalPreflight +
                        Math.Min(hit.FinalHpDamage, target.Enemy.Health));
                }
                catch (OverflowException) { return false; }
            }

            foreach (var hit in hits)
            {
                var target = enemies[hit.TargetId];
                long actual;
                if (!resolution.TryApplySkillDamage(attackerId,
                        hit.TargetId, hit.FinalHpDamage, out actual))
                    throw new InvalidOperationException(
                        "원본 주문 피해의 사전 검증 후 몬스터 상태가 변했습니다.");
                totalActualDamage = checked(totalActualDamage + actual);
                var detail = "원본 보조 주문 실제 타격: " +
                    hit.OriginalNativeHitEvidence;
                var destination = processingStep ? events : pendingCommandEvents;
                destination.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage,
                    attackerId, hit.TargetId, command.HumanSkillName,
                    detail, actual));
                if (!target.Enemy.IsAlive)
                    destination.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated,
                        attackerId, hit.TargetId, command.HumanSkillName,
                        "원본 보조 주문으로 처치", actual));
            }
            return true;
        }

        // All 14 uncommon units have W3X object/ability metadata, even when
        // only 7 have an individually named native attack ability.
        // Neither source loading nor a native ability name authorizes damage.
        public void ConfigureOriginalUncommonAbilities(
            Original323UncommonAbilityCatalog originalAbilities,
            IOriginalUncommonVerifiedProcWorld verifiedEffectWorld = null)
        {
            if (characters.Count != 0 || uncommonAbilities != null)
                throw new InvalidOperationException(
                    "안흔함 능력은 첫 유닛 소환 전에 한 번만 연결해야 합니다.");
            uncommonAbilities = originalAbilities ??
                throw new ArgumentNullException(nameof(originalAbilities));
            uncommonProcWorld = verifiedEffectWorld;
        }

        public IReadOnlyList<OriginalUncommonProc> OriginalUncommonSkillsOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 유닛 인스턴스.");
            return uncommonAbilities == null
                ? Array.Empty<OriginalUncommonProc>()
                : uncommonAbilities.ForUnit(unit.Catalog.Id);
        }

        public bool IsOriginalUncommonSkillConnected(long instanceId, string procId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit) || unit.Mapping == null ||
                uncommonAbilities == null ||
                uncommonAbilities.FindProc(unit.Catalog.Id, procId) == null)
                return false;
            var native = ConnectedNativePassiveOf(instanceId);
            if (native != null && uncommonAbilities.FindProc(unit.Catalog.Id, procId).AbilityRawcode ==
                native.AbilityRawcode &&
                ((native.Kind == OriginalNativePassiveKind.Critical &&
                  uncommonAbilities.FindProc(unit.Catalog.Id, procId).Kind == OriginalUncommonProcKind.Critical) ||
                 (native.Kind == OriginalNativePassiveKind.Stun &&
                  uncommonAbilities.FindProc(unit.Catalog.Id, procId).Kind == OriginalUncommonProcKind.BashStun)))
                return true;
            foreach (var spec in unit.Mapping.AttackSpec.Skills)
                if (spec.RawCode == procId) return true;
            return false;
        }

        private bool CanTriggerOriginalUncommonProc(string attacker,
            string targetId, string skillId)
        {
            long unitId;
            CharacterInstance caster;
            if (!long.TryParse(attacker, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out unitId) ||
                !characters.TryGetValue(unitId, out caster)) return false;
            var skill = uncommonAbilities == null ? null :
                uncommonAbilities.FindProc(caster.Catalog.Id, skillId);
            if (skill == null) return true; // Other independently verified skills.
            CombatEnemy target = resolution.GetEnemy(targetId);
            return uncommonProcWorld != null && target != null && target.IsAlive &&
                uncommonProcWorld.CanAffectTarget(skill, attacker, target);
        }

        private void ExecuteOriginalUncommonProcs(CharacterInstance caster,
            BasicAttackResult hit)
        {
            if (uncommonAbilities == null || uncommonProcWorld == null) return;
            foreach (var procId in hit.ActivatedSkillRawCodes)
            {
                var skill = uncommonAbilities.FindProc(caster.Catalog.Id, procId);
                if (skill == null) continue;
                string proof;
                bool executed;
                if (executingUncommonProcId != null)
                    throw new InvalidOperationException("중첩 안흔함 스킬 실행은 허용되지 않습니다.");
                executingUncommonProcId = skill.ProcId;
                executingUncommonActor = InstanceKey(caster.Id);
                executingUncommonTarget = hit.TargetId;
                try
                {
                    try
                    {
                        executed = uncommonProcWorld.TryApplyCompleteEffect(skill, this,
                            InstanceKey(caster.Id), hit.TargetId, out proof);
                    }
                    catch (NotSupportedException ex)
                    {
                        executed = false;
                        proof = ex.Message;
                    }
                }
                finally
                {
                    executingUncommonProcId = null;
                    executingUncommonActor = null;
                    executingUncommonTarget = null;
                }
                events.Add(new CharacterCombatEvent(executed &&
                    !string.IsNullOrWhiteSpace(proof)
                    ? CharacterCombatEventType.SkillActivated :
                    CharacterCombatEventType.EffectDeferred,
                    InstanceKey(caster.Id), hit.TargetId, skill.Name,
                    executed && !string.IsNullOrWhiteSpace(proof) ? proof :
                    "원본 스킬 전체 효과 적용 보류: " + (proof ?? skill.ProcId), 0));
            }
        }


        // Final native bonus HP can enter this runtime ONLY while its
        // registered chance proc is executing. This prevents a UI or another
        // script from manufacturing free damage with an arbitrary rawcode.
        internal bool TryCommitOriginalUncommonBonus(string attackerId,
            string targetId, string procId, long verifiedFinalHpDamage,
            out long actualDamage)
        {
            actualDamage = 0;
            if (verifiedFinalHpDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedFinalHpDamage));
            if (game.Phase != SessionPhase.Running ||
                procId == null || procId != executingUncommonProcId ||
                attackerId != executingUncommonActor ||
                targetId != executingUncommonTarget ||
                !processingStep)
                return false;
            long casterId;
            CharacterInstance caster;
            SpatialCombatEnemy target;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out casterId) ||
                !characters.TryGetValue(casterId, out caster) ||
                !enemies.TryGetValue(targetId, out target) ||
                !target.Enemy.IsAlive || !CanAttackInThisLane(caster, target) ||
                uncommonAbilities == null ||
                uncommonAbilities.FindProc(caster.Catalog.Id, procId) == null)
                return false;
            if (!resolution.TryApplySkillDamage(attackerId,
                targetId, verifiedFinalHpDamage, out actualDamage))
                return false;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillDamage, attackerId, targetId,
                procId, "W3X 검증된 AHbh 추가 피해", actualDamage));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EnemyDefeated, attackerId,
                    targetId, procId, "AHbh 추가 피해로 처치", actualDamage));
            return true;
        }

        // Source-supplied line geometry and *final HP* damage only. The
        // whole batch is checked before touching a single enemy: no partial
        // AoE is allowed when any target crosses a lane or lacks provenance.
        // This is callable only from an active, source-approved A07I proc.
        internal bool TryCommitOriginalUncommonLine(string attackerId,
            string primaryTargetId, string procId,
            IReadOnlyList<Original323RankyakuLineHit> hits,
            out long totalActualDamage)
        {
            totalActualDamage = 0;
            if (!processingStep || game.Phase != SessionPhase.Running ||
                attackerId != executingUncommonActor ||
                primaryTargetId != executingUncommonTarget ||
                procId != executingUncommonProcId ||
                procId != "A07I:rankyaku" ||
                hits == null || hits.Count == 0 || hits.Count > 512)
                return false;
            long actorId;
            CharacterInstance actor;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out actorId) ||
                !characters.TryGetValue(actorId, out actor) ||
                uncommonAbilities == null ||
                uncommonAbilities.FindProc(actor.Catalog.Id, procId) == null)
                return false;
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in hits)
            {
                SpatialCombatEnemy target;
                if (line == null || line.FinalHpDamage < 0 ||
                    string.IsNullOrWhiteSpace(line.SourceEvidence) ||
                    !touched.Add(line.TargetId) ||
                    !enemies.TryGetValue(line.TargetId, out target) ||
                    !target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target) ||
                    resolution.GetEnemy(line.TargetId) == null)
                    return false;
            }
            // Preflight guarantees that no expected miss/duplicate can occur
            // inside the commit. External combat hooks must not mutate the
            // roster during this loop (same rule as existing AoE effect path).
            foreach (var line in hits)
            {
                SpatialCombatEnemy target = enemies[line.TargetId];
                long actual;
                if (!resolution.TryApplySkillDamage(attackerId,
                        line.TargetId, line.FinalHpDamage, out actual))
                    throw new InvalidOperationException(
                        "원본 람각의 사전검증과 실제 피해 반영이 불일치합니다.");
                totalActualDamage = checked(totalActualDamage + actual);
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, attackerId,
                    line.TargetId, procId, line.SourceEvidence, actual));
                if (!target.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated,
                        attackerId, line.TargetId, procId,
                        "원본 람각으로 적 처치", actual));
            }
            return true;
        }

        // Native A07M is one committed primary basic attack plus at most two
        // independently resolved additional shots. The primary target is
        // excluded: it already took a full native hit in the attack pipeline.
        // This method may only run inside Robin's approved skill event.
        // Preflight ALL extra hits before mutating HP; no partial volley when
        // any target's identity, defense, lane or damage evidence is unknown.
        internal bool TryCommitOriginalRobinMultiTarget(string attackerId,
            string primaryTargetId, string procId,
            IReadOnlyList<Original323RobinSecondaryHit> additionalHits,
            out long totalActualDamage)
        {
            totalActualDamage = 0;
            if (!processingStep || game.Phase != SessionPhase.Running ||
                procId != "A07M:multi3" ||
                procId != executingUncommonProcId ||
                attackerId != executingUncommonActor ||
                primaryTargetId != executingUncommonTarget ||
                additionalHits == null || additionalHits.Count > 2)
                return false;
            long actorId;
            CharacterInstance actor;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out actorId) ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "U-01" ||
                uncommonAbilities == null ||
                uncommonAbilities.FindProc("U-01", procId) == null)
                return false;
            // Keep the validated volley stable throughout commit, even when
            // the provider returned a mutable list. Check the aggregate BEFORE
            // changing HP: checked addition during commit could otherwise
            // throw after one or both enemies have already taken damage.
            var prepared = new List<Original323RobinSecondaryHit>(additionalHits);
            if (prepared.Count > 2) return false;
            long plannedActualDamage = 0;
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shot in prepared)
            {
                SpatialCombatEnemy target;
                Ordr323ArmorType armorType;
                if (shot == null || shot.TargetId == primaryTargetId ||
                    shot.FinalHpDamage < 0 ||
                    string.IsNullOrWhiteSpace(shot.Evidence) ||
                    !touched.Add(shot.TargetId) ||
                    !enemies.TryGetValue(shot.TargetId, out target) ||
                    !target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target) ||
                    !target.Enemy.Defenses.HasVerifiedArmor ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        target.Enemy.Defenses.WarcraftArmorType, out armorType) ||
                    (actor.BasicTargetFilter != null &&
                     !actor.BasicTargetFilter.CanAttemptBasicAttack(
                         actor.Catalog, target.Enemy)) ||
                    resolution.GetEnemy(shot.TargetId) == null)
                    return false;
                long actual = Math.Min(target.Enemy.Health, shot.FinalHpDamage);
                if (actual > long.MaxValue - plannedActualDamage)
                    return false;
                plannedActualDamage += actual;
            }
            // Resolution is shared with regular attacks so defeat accounting,
            // participation, damage listeners, and wave transitions stay
            // consistent. A zero-secondary volley is valid when the source
            // confirms no other Warcraft-eligible enemies.
            foreach (var shot in prepared)
            {
                SpatialCombatEnemy enemy = enemies[shot.TargetId];
                long actual;
                if (!resolution.TryApplySkillDamage(attackerId,
                        shot.TargetId, shot.FinalHpDamage, out actual))
                    throw new InvalidOperationException(
                        "A07M 추가 타격 사전검증 이후 전장/적 상태가 변경되었습니다.");
                totalActualDamage = checked(totalActualDamage + actual);
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, attackerId,
                    shot.TargetId, procId, shot.Evidence, actual));
                if (!enemy.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated,
                        attackerId, shot.TargetId, procId,
                        "원본 A07M 추가 타격으로 적 처치", actual));
            }
            return true;
        }

        private IOriginalSpecialSkillTriggerMapping specialTriggerMapping;
        private IOriginalSpecialSkillEventRules specialEventRules;
        private IOriginalCombatTargetSelectionRules targetSelectionRules;
        private ISpecialGradeLawCastService lawCastService;
        private EconomyWallet otamaRewardWallet;
        private readonly Dictionary<long, double> lawReadyAt = new Dictionary<long, double>();
        private readonly MoriaParticipationLedger moriaParticipation = new MoriaParticipationLedger();
        public long MoriaZombieMaterials { get; private set; }
        public MobileCombatAutomation MobileAutomation { get; } = new MobileCombatAutomation();

        // 실제 경제/조합과 공유한 지갑만 주입한다. 별도 임시 지갑을 만들지 않는다.
        public void ConfigureOtamaWoodWallet(EconomyWallet sharedWallet)
        {
            if (sharedWallet == null) throw new ArgumentNullException(nameof(sharedWallet));
            if (otamaRewardWallet != null && !ReferenceEquals(otamaRewardWallet, sharedWallet))
                throw new InvalidOperationException("세션 중 공유 지갑 변경은 불가능합니다.");
            otamaRewardWallet = sharedWallet;
        }

        public bool TryUseOtamaDango(long instanceId)
        {
            CharacterInstance unit;
            if (game.Phase != SessionPhase.Running || otamaRewardWallet == null ||
                !characters.TryGetValue(instanceId, out unit) ||
                unit.Catalog.Id != SpecialGradeMobilePolicy.Otama) return false;
            bool paid;
            try
            {
                paid = otamaRewardWallet.TryGrantOnce(
                    SpecialGradeMobilePolicy.OtamaRewardId(instanceId),
                    new ResourceBundle().Add("wood", SpecialGradeMobilePolicy.OtamaWoodReward));
            }
            catch (OverflowException)
            {
                // 지급에 실패했으면 1회 사용권도 소모되지 않는다.
                return false;
            }
            if (!paid) return false;
            var evt = new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(instanceId), null, "경단먹이기",
                "wood +16", SpecialGradeMobilePolicy.OtamaWoodReward);
            if (processingStep) events.Add(evt); else pendingCommandEvents.Add(evt);
            return true;
        }

        public void ConfigureSpecialGradeLawCasting(ISpecialGradeLawCastService service)
        {
            lawCastService = service ?? throw new ArgumentNullException(nameof(service));
        }

        // 시전 가능한 시점마다 현재 전장의 전체 후보를 다시 평가한다.
        // 실제 가속 효과·자원/중복 검증 서비스 없이는 시전 성공으로 처리하지 않는다.
        public bool TryRunLawAcceleration(long lawId)
        {
            CharacterInstance law;
            if (game.Phase != SessionPhase.Running || lawCastService == null ||
                !characters.TryGetValue(lawId, out law) ||
                law.Catalog.Id != SpecialGradeMobilePolicy.Law) return false;
            double ready;
            if (lawReadyAt.TryGetValue(lawId, out ready) && Time < ready) return false;
            if (!lawCastService.CanCastNow(lawId, Time)) return false;
            bool inStory = IsStoryDeployed(lawId);
            var candidates = new List<LawDpsCandidate>();
            foreach (var target in MobileUnitSnapshot())
            {
                if (IsStoryDeployed(target.InstanceId) != inStory) continue;
                double dps;
                if (!lawCastService.TryEvaluateTarget(lawId, target.InstanceId, Time, out dps))
                    continue;
                candidates.Add(new LawDpsCandidate(target.InstanceId, inStory, true, dps));
            }
            long? chosen = SpecialGradeMobilePolicy.ChooseLawTarget(inStory, candidates);
            if (!chosen.HasValue || !characters.ContainsKey(chosen.Value) ||
                IsStoryDeployed(lawId) != IsStoryDeployed(chosen.Value)) return false;
            if (!lawCastService.TryCast(lawId, chosen.Value, Time)) return false;
            lawReadyAt[lawId] = Time + 50;
            return true;
        }

        // 평타의 보스/선두 선택 정책을 동일하게 사용한다. 별도 스킬 타깃 AI 없음.
        public string CurrentAttackTargetOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit) || !unit.AttackReady ||
                unit.AttackBlockedByMissingFormula || unit.Mapping == null) return null;
            var candidates = BuildBasicAttackCandidates(unit);
            return ChooseApprovedBossTarget(unit, candidates) ??
                attackPipeline.SelectDefaultTarget(InstanceKey(instanceId), candidates,
                    TargetPriority.Nearest);
        }

        private void OnActualDamage(string actor, string enemyId, long actualDamage, bool killed)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy)) return;
            long actorId;
            CharacterInstance unit;
            bool isMoria = long.TryParse(actor, NumberStyles.None, CultureInfo.InvariantCulture,
                out actorId) && characters.TryGetValue(actorId, out unit) &&
                unit.Catalog.Id == SpecialGradeMobilePolicy.Moria;
            moriaParticipation.Record(enemyId, isMoria, actualDamage, enemy.Kind);
            if (!killed || !moriaParticipation.Complete(enemyId)) return;
            MoriaZombieMaterials = checked(MoriaZombieMaterials + 1);
            var earned = new CharacterCombatEvent(CharacterCombatEventType.ZombieMaterialEarned,
                actor, enemyId, null, "모리아 처치 참여", 1);
            if (processingStep) events.Add(earned); else pendingCommandEvents.Add(earned);
        }

        private bool original323DefenseLinksConfigured;

        public void ConfigureOriginal323DefenseSkillLinks()
        {
            if (original323DefenseLinksConfigured || enemies.Count != 0 ||
                characters.Count != 0 || game.Phase == SessionPhase.Running)
                throw new InvalidOperationException(
                    "원본 방깎·마방깎 연결은 게임 시작 전에 한 번만 설정할 수 있습니다.");
            effectExecutor.ConfigureOriginal323DefenseSkillRouter(
                new Ordr323VerifiedDefenseEffectRouter(armorBreakStacks,
                    provenA04MStacks, statuses, VerifiedCasterRawcode));
            bonneyArmor = new Ordr323BonneyDefenseController(
                statuses, provenA04PTotals);
            attackPipeline.ConfigureVerifiedAttackDeclared(ApplyBonneyDeclaredAttack);
            original323DefenseLinksConfigured = true;
        }

        // Never infer an original W3X unit type from the catalog ID or tooltip.
        // Only a separately approved attack binding can attest the rawcode.
        private string VerifiedCasterRawcode(string casterId)
        {
            long id;
            CharacterInstance unit;
            if (!long.TryParse(casterId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out unit) ||
                !unit.AttackReady || unit.Mapping == null)
                return null;
            string rawcode = unit.Mapping.AttackSpec.UnitRawCode;
            return rawcode != null && rawcode.Length == 4 ? rawcode : null;
        }

        // Current original form has to come from an independent state/transform
        // tracker. We NEVER guess H0BX vs H0BY from the tooltips.
        public void ConfigureVerifiedBonneyFormEvidence(IOriginalBonneyFormEvidence provider)
        {
            if (!original323DefenseLinksConfigured)
                throw new InvalidOperationException("원본 2.323[C] 연결부터 구성하세요.");
            bonneyFormEvidence = provider ??
                throw new ArgumentNullException(nameof(provider));
        }

        public bool IsVerified323BonneyArmorTotal(string enemyId, int total)
        {
            int value;
            return !string.IsNullOrWhiteSpace(enemyId) && total > 0 &&
                provenA04PTotals.TryGetValue(enemyId, out value) &&
                value == total;
        }

        // Only a source-validated game combat event should call this for
        // conditional bonuses (+20/+2). RNG is not invented here.
        public bool TryApplyVerifiedBonneyArmorEvent(long casterId, string enemyId,
            Ordr323BonneyArmorEvent kind, out string reason,
            string areaCenterEnemyId = null)
        {
            reason = null;
            if (game.Phase != SessionPhase.Running ||
                bonneyArmor == null || bonneyFormEvidence == null)
            {
                reason = "원본 보니 전투/변신 상태 공급자 미연결";
                return false;
            }
            CharacterInstance caster;
            SpatialCombatEnemy target;
            if (!characters.TryGetValue(casterId, out caster) ||
                caster.Catalog.Id != "TR-21" ||
                !enemies.TryGetValue(enemyId, out target) ||
                !target.Enemy.IsAlive || !CanAttackInThisLane(caster, target))
            {
                reason = "보니 캐릭터/대상/전장 검증 실패";
                return false;
            }
            if (kind == Ordr323BonneyArmorEvent.AreaProc)
            {
                SpatialCombatEnemy center;
                if (string.IsNullOrWhiteSpace(areaCenterEnemyId) ||
                    !enemies.TryGetValue(areaCenterEnemyId, out center) ||
                    !CanAttackInThisLane(caster, center) ||
                    !Ordr323BonneyDefenseController.IsWithinVerifiedAreaProc(
                        center.X, center.Y, target.X, target.Y))
                {
                    reason = "원본 B39/B4B 320범위 대상 확인 실패";
                    return false;
                }
            }
            Ordr323BonneyForm form;
            if (!bonneyFormEvidence.TryGetVerifiedForm(casterId, out form))
            {
                reason = "보니 원본 형태 미확인";
                return false;
            }
            if (!bonneyArmor.TryApply(casterId, form, kind, target.Id, out reason))
                return false;
            var result = new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied,
                InstanceKey(casterId), target.Id, "보니 A04P",
                "jass:A04P:" + kind.ToString(), 0);
            if (processingStep) events.Add(result);
            else pendingCommandEvents.Add(result);
            return true;
        }

        private void ApplyBonneyDeclaredAttack(string attackerId, string enemyId)
        {
            if (bonneyFormEvidence == null) return;
            long casterId;
            if (!long.TryParse(attackerId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out casterId))
                return;
            CharacterInstance caster;
            if (!characters.TryGetValue(casterId, out caster) ||
                caster.Catalog.Id != "TR-21") return;
            string reason;
            if (!TryApplyVerifiedBonneyArmorEvent(casterId, enemyId,
                Ordr323BonneyArmorEvent.AttackDeclared, out reason))
            {
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred,
                    attackerId, enemyId, "보니 A04P",
                    reason ?? "원본 보니 능력 적용 보류", 0));
            }
        }

        public bool IsVerified323ArmorBreakStack(string enemyId, int stacks)
        {
            int verified;
            return !string.IsNullOrWhiteSpace(enemyId) && stacks >= 0 &&
                provenA04MStacks.TryGetValue(enemyId, out verified) &&
                verified == stacks;
        }

        public void ConfigureVerifiedSpecialEffects(
            IOriginalSpecialCombatEffectHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            effectExecutor.ConfigureVerifiedSpecialEffects(handler);
        }

        // Source-verified native AOae attack-speed timing is configured
        // independently of AttackPowerBuff. No guessed IAS stacking,
        // cap, phase reset or cooldown change is applied by default.
        public void ConfigureVerifiedOriginal323NativeAttackCadence(
            IOriginal323VerifiedNativeAttackCadence originalTiming)
        {
            if (characters.Count != 0)
                throw new InvalidOperationException(
                    "원본 공속 공식은 전투 유닛을 생성하기 전에 설정해야 합니다.");
            attackPipeline.ConfigureVerifiedNativeAttackCadence(
                originalTiming);
        }

        public void ConfigureVerifiedUnitAttackModifiers(IOriginalUnitAttackModifiers rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            attackPipeline.ConfigureVerifiedModifiers(rules, id =>
            {
                CombatStatusEngine state;
                if (!unitStatuses.TryGetValue(id, out state))
                    throw new KeyNotFoundException("공격자 상태 정보가 없습니다.");
                return state.Snapshot();
            });
        }

        public CombatStatusEngine UnitStatusOf(long id)
        {
            CombatStatusEngine value;
            if (!unitStatuses.TryGetValue(InstanceKey(id), out value))
                throw new KeyNotFoundException("유닛 효과 정보가 없습니다.");
            return value;
        }

        public void ApplyVerifiedUnitEffect(long id, CombatEffect effect)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            UnitStatusOf(id).Apply(effect);
        }

        // 기본 공격을 등록하기 전에 특수 이벤트 매핑을 제공한다.
        // 조건 미확인 스킬은 자동으로 일반 평타 스킬로 바꾸지 않는다.
        public void ConfigureVerifiedSpecialTriggers(
            IOriginalSpecialSkillTriggerMapping mapping,
            IOriginalSpecialSkillEventRules eventRules)
        {
            if (characters.Count != 0)
                throw new InvalidOperationException(
                    "기존 캐릭터 등록 전에 특수 스킬 규칙을 설정하세요.");
            specialTriggerMapping = mapping ??
                throw new ArgumentNullException(nameof(mapping));
            specialEventRules = eventRules ??
                throw new ArgumentNullException(nameof(eventRules));
        }

        public void ConfigureVerifiedTargetSelection(
            IOriginalCombatTargetSelectionRules verifiedRules)
        {
            targetSelectionRules = verifiedRules ??
                throw new ArgumentNullException(nameof(verifiedRules));
        }

        // 검증된 오라의 원본 효과/대상 분류를 등록한다. 적용 거리는 모바일에서 무제한.
        public void AddVerifiedEnemyAura(CombatAuraEmitter source)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!characters.ContainsKey(source.UnitInstanceId))
                throw new KeyNotFoundException("오라를 사용하는 캐릭터가 없습니다.");
            auras.Add(source);
            RefreshEnemyAuras();
        }

        public void RemoveVerifiedEnemyAura(long unitInstanceId, string auraId)
        {
            BlockExternalMutationDuringOriginalFusion();
            auras.Remove(unitInstanceId, auraId);
            RefreshEnemyAuras();
        }

        private void RefreshEnemyAuras()
        {
            auras.Refresh(statuses, enemies.Values,
                id => characters.ContainsKey(id)
                    ? (UnitState?)roster.Get(id) : null,
                (id, enemy) => characters.ContainsKey(id) &&
                    CanAttackInThisLane(characters[id], enemy));
            RefreshOriginalSpecialAuras();
        }

        // 다중 타격/지속 피해 실행 시각은 원작 출처를 가진 공급자만 연결한다.
        public void ConfigureVerifiedSkillHitTiming(IOriginalSkillHitTimingProvider rules)
        {
            effectExecutor.ConfigureVerifiedHitTiming(rules);
        }

        public void ConfigureStoryDeployment(IOriginalStoryDispatchPort verifiedStory)
        {
            storyDispatch = verifiedStory;
        }

        // Supports ALL 271 catalogue types, including the 71 without a detailed
        // skill profile. A provider must supply original-verified basic stats;
        // an unsupported unit remains spawnable but unable to attack.
        public void ConfigureVerifiedUnitAttackProvider(
            IOriginalVerifiedUnitAttackProvider originalProvider)
        {
            verifiedAttackProvider = originalProvider;
        }

        // Map object timings are useful for animation-less mobile attack cues,
        // but MUST NOT be interpreted as verified final damage or skill procs.
        public void ConfigureOriginalMapCommonAttackPreview(OriginalMapBaseStatCatalog map)
        {
            if (commonAttackPreview != null)
                throw new InvalidOperationException("흔함 공격 미리보기는 한 번만 연결할 수 있습니다.");
            commonAttackPreview = new OriginalMapCommonAttackPreview(map);
            foreach (var unit in characters.Values)
                if (!unit.AttackReady)
                    commonAttackPreview.TryTrack(unit.Id, unit.Catalog.Id, scheduler.Time);
        }

        public int BindExistingVerifiedAttacks()
        {
            if (verifiedAttackProvider == null) return 0;
            int bound = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance instance = characters[id];
                if (instance.AttackReady) continue;
                OriginalVerifiedAttackBinding found;
                if (!verifiedAttackProvider.TryResolve(instance.Catalog,
                    instance.Profile, out found)) continue;
                if (found == null)
                    throw new InvalidOperationException(
                        "원본 공격 스탯 공급자가 비어 있는 성공 결과를 반환했습니다.");
                BindVerifiedBasicAttack(id, found.Stats, found.FirstAttackDelaySeconds,
                    found.OriginalUnitRawcode);
                instance.AttackEvidence = found.SourceEvidence;
                instance.BasicTargetFilter = verifiedAttackProvider as
                    IOriginalVerifiedBasicAttackTargetFilter;
                bound++;
            }
            return bound;
        }

        public bool IsStoryDeployed(long instanceId)
        {
            return storyDispatch != null && storyDispatch.IsDeployed(instanceId);
        }

        // Independent observer for per-enemy defeat or escape. Driven by
        // CombatResolution after the original session counters are updated.
        public event Action<string, CombatEnemyExitReason> EnemyExited;
        public GameSession Game { get { return game; } }
        public UnitRoster Roster { get { return roster; } }
        public int CharacterCount { get { return characters.Count; } }
        public int EnemyCount { get { return enemies.Count; } }
        public int ReadyAttackers
        {
            get
            {
                int count = 0;
                foreach (var unit in characters.Values)
                    if (unit.AttackReady && !unit.AttackBlockedByMissingFormula) count++;
                return count;
            }
        }
        public int PreviewAttackers
        {
            get { return commonAttackPreview != null ? commonAttackPreview.Count : 0; }
        }
        public double Time { get { return scheduler.Time; } }
        public double PendingSimulationSeconds { get { return pendingStepSeconds; } }
        public IReadOnlyList<CharacterCombatEvent> LastEvents { get { return events.AsReadOnly(); } }

        // 프레임·HUD·모바일 행동 정책은 유닛과 적의 현재 상태만 조회할 수 있다.
        public IReadOnlyList<MobileUnitSnapshot> MobileUnitSnapshot()
        {
            var result = new List<MobileUnitSnapshot>();
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (activeOriginalFusionSwap != null &&
                    activeOriginalFusionSwap.Hides(id))
                    continue;
                CharacterInstance unit = characters[id];
                result.Add(new MobileUnitSnapshot(id, unit.Catalog, unit.Profile, roster.Get(id)));
            }
            return result.AsReadOnly();
        }

        public IReadOnlyList<MobileEnemySnapshot> MobileEnemySnapshot()
        {
            var result = new List<MobileEnemySnapshot>();
            foreach (var enemy in enemies.Values)
                if (enemy.Enemy.IsAlive)
                    result.Add(new MobileEnemySnapshot(enemy.Id, enemy.Kind,
                        enemy.Enemy.Health, enemy.Enemy.MaxHealth, enemy.X, enemy.Y));
            result.Sort((a,b) => string.CompareOrdinal(a.Id, b.Id));
            return result.AsReadOnly();
        }

        public CharacterCombatRuntime(CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver verifiedBasicDamage,
            IVerifiedCharacterSkillDamageResolver verifiedSkillDamage,
            ICombatProcRandom random)
            : this(units, profiles, verifiedBasicDamage, verifiedSkillDamage, random,
                new UnitRoster()) { }

        public CharacterCombatRuntime(CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver verifiedBasicDamage,
            IVerifiedCharacterSkillDamageResolver verifiedSkillDamage,
            ICombatProcRandom random, UnitRoster sharedRoster)
        {
            roster = sharedRoster ?? throw new ArgumentNullException(nameof(sharedRoster));
            this.units = units ?? throw new ArgumentNullException(nameof(units));
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            if (random == null) throw new ArgumentNullException(nameof(random));
            triggers = new CombatSkillTriggerEngine(random);
            resolution = new CombatResolution(game, scheduler);
            resolution.DamageApplied += OnActualDamage;
            resolution.EnemyExited += (enemyId, reason) =>
            {
                var handler = EnemyExited;
                if (handler != null) handler(enemyId, reason);
            };
            attackPipeline = new CombatAttackPipeline(scheduler, resolution, triggers,
                verifiedBasicDamage ?? throw new ArgumentNullException(nameof(verifiedBasicDamage)));
            attackPipeline.ConfigureAttackerEffectLookup(id => unitStatuses[id].Snapshot());
            attackPipeline.ConfigureVerifiedProcPreflight(CanTriggerOriginalUncommonProc);
            // 유닛 재배치가 어려운 모바일 플레이 기준: 같은 전장 안의 평타 사거리 무제한.
            attackPipeline.ConfigureReachMode(
                CombatAttackReachMode.UnlimitedWithinAssignedBattlefield);
            effectExecutor = new CharacterSkillEffectExecutor(resolution,
                verifiedSkillDamage ?? throw new ArgumentNullException(nameof(verifiedSkillDamage)),
                statuses, armorBreakStacks, events);
            foreach (var profile in profiles.All)
                if (units.Find(profile.UnitId) == null)
                    throw new FormatException("스킬 프로필에 존재하지 않는 유닛 ID: " + profile.UnitId);
        }

        public void Start()
        {
            BlockExternalMutationDuringOriginalFusion();
            game.Start();
        }

        public void BeginWave(int waveNumber)
        {
            BlockExternalMutationDuringOriginalFusion();
            game.BeginWave(waveNumber);
        }

        public long SpawnCharacter(string catalogUnitId, double x, double y)
        {
            BlockExternalMutationDuringOriginalFusion();
            return SpawnCharacterCore(catalogUnitId, x, y, false);
        }

        // JASS BIY already determines the complete original spawn placement.
        // The optional mobile auto-placement adapter must not replace those
        // source coordinates while constructing an e0IX common draw.
        internal long SpawnVerifiedOriginalPositionCharacter(string catalogUnitId,
            double originalX, double originalY)
        {
            BlockExternalMutationDuringOriginalFusion();
            return SpawnCharacterCore(catalogUnitId, originalX, originalY,
                false, true);
        }

        private long SpawnStagedOriginalFusionResult(string catalogUnitId,
            double x, double y)
        {
            if (!internalOriginalFusionMutation ||
                activeOriginalFusionSwap == null)
                throw new InvalidOperationException(
                    "원본 조합 결과 소환은 검증된 원자 거래에서만 허용합니다.");
            return SpawnCharacterCore(catalogUnitId, x, y, true);
        }

        private long SpawnCharacterCore(string catalogUnitId, double x,
            double y, bool stagedOriginalFusionResult,
            bool preserveOriginalSourcePosition = false)
        {
            CatalogUnit catalogUnit = units.Require(catalogUnitId);
            long id = roster.Add(catalogUnitId, x, y);
            var profile = profiles.Find(catalogUnitId);
            characters.Add(id, new CharacterInstance {
                Id = id, Catalog = catalogUnit, Profile = profile,
                Abilities = new CharacterActiveAbilities(profile)
            });
            unitStatuses.Add(InstanceKey(id), new CombatStatusEngine());
            bool placed;
            try
            {
                placed = !stagedOriginalFusionResult &&
                    !preserveOriginalSourcePosition &&
                    MobileAutomation.TryPlace(catalogUnit, roster, id);
                if (verifiedAttackProvider != null)
                {
                    OriginalVerifiedAttackBinding original;
                    if (verifiedAttackProvider.TryResolve(catalogUnit, profile, out original))
                    {
                        if (original == null)
                            throw new InvalidOperationException(
                                "원본 공격 스탯 공급자가 null로 등록 승인을 반환했습니다.");
                        BindVerifiedBasicAttack(id, original.Stats,
                            original.FirstAttackDelaySeconds, original.OriginalUnitRawcode);
                        characters[id].AttackEvidence = original.SourceEvidence;
                        characters[id].BasicTargetFilter = verifiedAttackProvider as
                            IOriginalVerifiedBasicAttackTargetFilter;
                    }
                }
                if (!characters[id].AttackReady && commonAttackPreview != null)
                    commonAttackPreview.TryTrack(id, catalogUnitId, scheduler.Time);
                // Stage is not a combat tick. Recomputing enemy auras here
                // can change status revision and native spell evidence even
                // if the fusion is subsequently rolled back.
                if (!stagedOriginalFusionResult) RefreshEnemyAuras();
            }
            catch
            {
                // Incomplete spawns never remain in the roster or skill engines.
                if (characters[id].AttackReady)
                    attackPipeline.RemoveUnit(InstanceKey(id));
                if (commonAttackPreview != null) commonAttackPreview.Remove(id);
                characters.Remove(id);
                unitStatuses.Remove(InstanceKey(id));
                roster.Remove(id);
                if (!stagedOriginalFusionResult) RefreshEnemyAuras();
                throw;
            }
            if (!stagedOriginalFusionResult)
            {
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.UnitSpawned,
                    InstanceKey(id), null, null, catalogUnit.Name, 0));
                if (placed)
                    pendingCommandEvents.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.UnitPlaced, InstanceKey(id), null,
                        null, "automatic-verified-position", 0));
            }
            return id;
        }

        public bool HasVerifiedAttack(long instanceId)
        {
            CharacterInstance instance;
            return characters.TryGetValue(instanceId, out instance) && instance.AttackReady;
        }

        // Read-only visual hint for the Unity presenter. Map weapon types alone
        // cannot establish a physical/magical damage school.
        public bool TryGetVerifiedAttackSchool(long instanceId, out DamageSchool school)
        {
            CharacterInstance instance;
            if (characters.TryGetValue(instanceId, out instance) &&
                instance.AttackReady && instance.Mapping != null)
            {
                school = instance.Mapping.AttackSpec.BasicAttack.School;
                return true;
            }
            school = DamageSchool.ScriptDefined;
            return false;
        }

        // RNG/공격 쿨타임을 움직이지 않는 읽기 전용 진단.
        public BasicAttackReadiness InspectBasicAttack(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            string id = unit.Catalog.Id;
            if (!unit.AttackReady || unit.Mapping == null)
            {
                var diagnostics = verifiedAttackProvider as IOriginalBasicAttackEvidenceDiagnostics;
                string detail = verifiedAttackProvider == null
                    ? "원본 평타 승인 공급자 미연결"
                    : diagnostics == null ? "원본 평타 공급자가 미승인"
                    : diagnostics.ExplainUnapprovedUnit(unit.Catalog);
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.UnverifiedSource, detail, null);
            }
            if (attackPipeline.HasUnresolvedNativeAttackSpeed(
                    InstanceKey(instanceId)))
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.FormulaDeferred,
                    "현재 공속 오라의 원본 네이티브 공격 간격 계산/중첩 근거 미확인",
                    attackPipeline.NextAttackAt(InstanceKey(instanceId)));
            if (unit.AttackBlockedByMissingFormula)
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.FormulaDeferred,
                    unit.DeferredAttackReason ?? "최종 피해 공식 미확인",
                    attackPipeline.NextAttackAt(InstanceKey(instanceId)));
            var visible = BuildTargetCandidates(unit);
            if (visible.Count == 0)
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.NoBattlefieldTarget,
                    "현재 소속 전장에 살아 있는 적 없음", null);
            var eligible = BuildBasicAttackCandidates(unit);
            if (eligible.Count == 0)
            {
                var diagnostics = unit.BasicTargetFilter as IOriginalBasicAttackEvidenceDiagnostics;
                string detail = diagnostics == null
                    ? "원본 대상/방어 검증 보류"
                    : diagnostics.ExplainRejectedTarget(unit.Catalog, enemies[visible[0].Id].Enemy);
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.UnverifiedTarget, detail,
                    attackPipeline.NextAttackAt(InstanceKey(instanceId)));
            }
            double? next = attackPipeline.NextAttackAt(InstanceKey(instanceId));
            return next.HasValue && !CombatScheduler.HasReached(scheduler.Time, next.Value)
                ? new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.Cooldown, "검증된 평타 공격 시점 대기", next)
                : new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.Ready, "평타 사전 검증을 통과한 대상 존재", next);
        }

        public IReadOnlyList<BasicAttackReadiness> CaptureBasicAttackDiagnostics()
        {
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            var report = new List<BasicAttackReadiness>();
            foreach (long id in ids) report.Add(InspectBasicAttack(id));
            return report.AsReadOnly();
        }

        public string AttackEvidenceOf(long instanceId)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 유닛 인스턴스.");
            return instance.AttackEvidence;
        }

        // 모바일에서는 평타의 사거리 수치가 없어도 등록할 수 있다.
        // 공격력·공속·피해계열/공식은 여전히 원작 근거가 필요하다.
        // 미등록 유닛은 선택·배치만 가능하고 공격하지 않는다.
        public IReadOnlyList<string> BindVerifiedBasicAttack(long instanceId,
            BasicAttackSpec verifiedStats, double firstAttackDelay)
        {
            return BindVerifiedBasicAttack(instanceId, verifiedStats,
                firstAttackDelay, null);
        }

        public IReadOnlyList<string> BindVerifiedBasicAttack(long instanceId,
            BasicAttackSpec verifiedStats, double firstAttackDelay,
            string originalUnitRawcode)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            if (instance.AttackReady)
                throw new InvalidOperationException("공격 스펙이 이미 등록되어 있습니다.");
            var mapping = CharacterCombatMapping.Build(instance.Catalog.Id,
                instance.Profile, verifiedStats, originalUnitRawcode,
                specialTriggerMapping, uncommonAbilities, uncommonProcWorld);
            attackPipeline.RegisterUnit(InstanceKey(instanceId), mapping.AttackSpec,
                firstAttackDelay);
            if (commonAttackPreview != null) commonAttackPreview.Remove(instanceId);
            instance.Mapping = mapping;
            instance.AttackReady = true;
            return mapping.DeferredReasons;
        }

        // 개별 스펙 수집이 끝나면 전체 캐릭터 데이터를 건드리지 않고 자동 등록할 수 있다.
        // JSON에 basicAttackVerificationStatus == "original-confirmed"가 없으면 적용하지 않는다.
        public bool TryBindCatalogVerifiedAttack(long instanceId, double firstAttackDelay)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            var profile = unit.Profile;
            if (profile == null || !profile.HasVerifiedBasicAttack) return false;
            object rawPower = profile.BasicAttack["nominalAttackPower"];
            object rawInterval = profile.BasicAttack["intervalSeconds"];
            object rawRange;
            double rangeValue = 0; // 모바일 무제한 사거리 내부 표식, 실제 원작 사거리 아님
            if (profile.BasicAttack.TryGetValue("range", out rawRange) &&
                rawRange != null)
            {
                if (!(rawRange is double)) return false;
                rangeValue = (double)rawRange;
            }
            else if (attackPipeline.ReachMode == CombatAttackReachMode.OriginalUnitRange)
                return false;
            object rawSchool = profile.BasicAttack["damageSchool"];
            double power = (double)rawPower;
            if (power < 0 || power > long.MaxValue || Math.Floor(power) != power)
                throw new FormatException("원본 공격력은 0 이상의 정수여야 합니다.");
            DamageSchool school;
            if (!Enum.TryParse((string)rawSchool, true, out school) ||
                !Enum.IsDefined(typeof(DamageSchool), school))
                throw new FormatException("검증된 기본 피해 계열 값 오류.");
            BindVerifiedBasicAttack(instanceId,
                new BasicAttackSpec((long)power, (double)rawInterval, rangeValue,
                    school, DamageTraits.None),
                firstAttackDelay);
            return true;
        }

        // 모바일 UI가 캐릭터 상세 스킬·현재 적 체력을 읽는 조회 경로.
        // 객체를 조회해도 공격 스펙이 자동으로 활성화되지는 않는다.
        public CharacterSkillProfile ProfileOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            return unit.Profile;
        }

        public CombatEnemy EnemyOf(string enemyId)
        {
            return string.IsNullOrWhiteSpace(enemyId) ? null : resolution.GetEnemy(enemyId);
        }

        // Existing-unit A04Q changes must be observed independently of LK/NJ
        // next-spawn state; the damage adapter reads this same CombatEnemy.
        public bool TryApplyObservedEnemyA04QLevel(string enemyId,
            int expectedPreviousLevel, int observedCurrentLevel,
            IOriginal323LiveMonsterA04QWitness verifiedEvent)
        {
            CombatEnemy live = EnemyOf(enemyId);
            return live != null && live.TryApplyObservedOriginalA04QLevel(
                expectedPreviousLevel, observedCurrentLevel, verifiedEvent);
        }

        // Several original scripted hits can additively increase the
        // SAME A04Q ability level. Source callback order and final native
        // level must both be witnessed. Existing pending spells resolve
        // against that level at IMPACT time.
        public bool TryApplyObservedEnemyMagicDefenseStack(string enemyId,
            int expectedPreviousLevel, int independentlyObservedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseStackCall> sourceCalls,
            IOriginal323MagicDefenseStackSequenceWitness orderedEvents,
            IOriginal323LiveMonsterA04QWitness finalNativeLevel)
        {
            return game.Phase == SessionPhase.Running &&
                Original323MagicDefenseStackRules.TryApplyObservedStack(
                    EnemyOf(enemyId), expectedPreviousLevel,
                    independentlyObservedFinalLevel, sourceCalls,
                    orderedEvents, finalNativeLevel);
        }

        // The pinned JASS also has Bd4 (set to LK/NJ) and Bd5
        // (increase); these ordered operations must not be confused.
        public bool TryApplyObservedEnemyMagicDefenseLevelMutations(
            string enemyId, int expectedPreviousLevel,
            int independentlyObservedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseLevelMutation>
                chronologicalSourceEvents,
            IOriginal323MagicDefenseLevelMutationWitness verifiedSequence,
            IOriginal323LiveMonsterA04QWitness nativeLevelWitness)
        {
            return game.Phase == SessionPhase.Running &&
                Original323MagicDefenseLevelMutationRules
                    .TryApplyObservedLevelMutations(
                        EnemyOf(enemyId), expectedPreviousLevel,
                        independentlyObservedFinalLevel,
                        chronologicalSourceEvents, verifiedSequence,
                        nativeLevelWitness);
        }

        // Same CombatEnemy instance feeds HUD, hit resolution and verified
        // wave tracking. An external native event must independently witness
        // maximum/current HP and physical armor changes.
        public bool TryApplyObservedEnemyMaximumHealthChange(
            string enemyId, long expectedCurrentHealth, long expectedMaxHealth,
            long observedCurrentHealth, long observedMaxHealth,
            IOriginal323LiveMonsterStatWitness verifiedEvent)
        {
            var live = EnemyOf(enemyId);
            return live != null && live.TryApplyObservedMaximumHealthChange(
                expectedCurrentHealth, expectedMaxHealth,
                observedCurrentHealth, observedMaxHealth, verifiedEvent);
        }

        public bool TryApplyObservedEnemyPhysicalArmorChange(
            string enemyId, double expectedArmor, string expectedArmorType,
            double observedArmor, string observedArmorType,
            IOriginal323LiveMonsterStatWitness verifiedEvent)
        {
            var live = EnemyOf(enemyId);
            return live != null && live.TryApplyObservedPhysicalArmorChange(
                expectedArmor, expectedArmorType,
                observedArmor, observedArmorType, verifiedEvent);
        }

        // Called only after the source-verified monster spawner resolves
        // each unit's existing A04R level. Unknown level is never zero.
        public void ConfigureVerifiedEnemyOriginalA04R(
            string enemyId, int originalLevel, string sourceEvidence)
        {
            var live = EnemyOf(enemyId);
            if (live == null)
                throw new KeyNotFoundException("A04R 설정할 실제 적이 없습니다.");
            live.SetVerifiedOriginalA04RBaseline(originalLevel, sourceEvidence);
        }

        // Concrete source-backed helper-cow damage / persistent A04R sink.
        // Uses CombatResolution for HP/defeats and CombatEnemy for the
        // authoritative per-enemy A04R level.
        // The external world MUST resolve CHAOS/UNIVERSAL defense, ability
        // range, mana cost and immunity before passing prepared final hits.
        public bool TryApplyOriginalHelperCowAreaEffects(string originalAbilityId,
            int level, IReadOnlyList<OriginalHelperCowResolvedHit> hits,
            IOriginalA04REffectEngine originalA04REffects = null)
        {
            if (game.Phase != SessionPhase.Running || hits == null ||
                hits.Count == 0) return false;
            // Reject unverified abilities/levels; do not treat every Q/W/R
            // as a helper-cow area spell.
            OriginalHelperCowAreaEvidence.JassDamage(originalAbilityId, level);
            // The original CJg/CJh apply Bd8 after dealing damage.
            // A04R is a persistent ability level, NOT a timed stun.
            int a04rIncrement = OriginalHelperCowAreaEvidence.JassA04RLevelIncrease(
                originalAbilityId, level);
            var preparedA04R = new Dictionary<string, int>(StringComparer.Ordinal);

            var distinct = new HashSet<string>(StringComparer.Ordinal);
            // Preflight the entire batch before mutating HP or statuses.
            foreach (var hit in hits)
            {
                if (hit == null || !distinct.Add(hit.EnemyId))
                    return false;
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(hit.EnemyId, out enemy) ||
                    !enemy.Enemy.IsAlive || resolution.GetEnemy(hit.EnemyId) == null ||
                    !Enum.IsDefined(typeof(CharacterEnemyKind), enemy.Kind))
                    return false;
                if (a04rIncrement > 0 && hit.FinalDamage < enemy.Enemy.Health)
                {
                    int next;
                    // The same verified physical enemy owns the A04R level.
                    // If starting level/max is unknown, do NOT cast at all.
                    if (!enemy.Enemy.TryPrepareOriginalA04RIncrease(
                        a04rIncrement, out next)) return false;
                    double sourceIsr2;
                    // The field table is verified, but its actual effect on
                    // Warcraft's outgoing/incoming attack pipeline is not.
                    // A genuine mechanics provider is required before ANY
                    // A07T damage/mana mutation can be committed.
                    if (originalA04REffects == null ||
                        !Ordr323OriginalA04R.TryReadIsr2(next, out sourceIsr2) ||
                        !originalA04REffects.CanApplyOriginalA04RLevel(
                            enemy.Enemy, next, sourceIsr2))
                        return false;
                    preparedA04R.Add(hit.EnemyId, next);
                }
            }

            const string actorId = "helper:h08A";
            var activated = new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, actorId, null,
                originalAbilityId, "원본 도움소 광역 시전", 0);
            if (processingStep) events.Add(activated);
            else pendingCommandEvents.Add(activated);

            foreach (var hit in hits)
            {
                long actualDamage;
                // The target is still present in CombatResolution until its
                // current hit kills it; never bypass the canonical kill count.
                if (!resolution.TryApplySkillDamage(actorId, hit.EnemyId,
                    hit.FinalDamage, out actualDamage))
                    throw new InvalidOperationException(
                        "도움소 사전 검증된 적이 타격 직전 사라졌습니다.");

                var dealt = new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, actorId, hit.EnemyId,
                    originalAbilityId, "원본 최종 피해량", actualDamage);
                if (processingStep) events.Add(dealt);
                else pendingCommandEvents.Add(dealt);

                // Bd8 runs after Bcb; a surviving enemy accumulates +5/+8
                // A04R levels on its canonical CombatEnemy instance.
                var survived = resolution.GetEnemy(hit.EnemyId);
                if (survived != null && survived.IsAlive && a04rIncrement > 0)
                {
                    int next;
                    if (!preparedA04R.TryGetValue(hit.EnemyId, out next) ||
                        !survived.OriginalA04RLevel.HasValue)
                        throw new InvalidOperationException("A04R 검증 시점이 변경되었습니다.");
                    int previous = survived.OriginalA04RLevel.Value;
                    double originalIsr2;
                    if (!Ordr323OriginalA04R.TryReadIsr2(next, out originalIsr2))
                        throw new InvalidOperationException("원본 A04R isr2 근거가 없습니다.");
                    survived.ApplyPreparedOriginalA04RIncrease(previous, next);
                    // Apply the actual original ability modifier in addition
                    // to storing the persistent enemy level. Never infer it.
                    originalA04REffects.ApplyOriginalA04RLevel(
                        survived, next, originalIsr2);
                    var leveled = new CharacterCombatEvent(
                        CharacterCombatEventType.StatusApplied, actorId, hit.EnemyId,
                        originalAbilityId, "A04R 영구 능력 레벨 증가", a04rIncrement);
                    if (processingStep) events.Add(leveled);
                    else pendingCommandEvents.Add(leveled);
                }

                if (resolution.GetEnemy(hit.EnemyId) == null)
                {
                    var killed = new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated, actorId, hit.EnemyId,
                        originalAbilityId, "도움소 타격으로 처치", actualDamage);
                    if (processingStep) events.Add(killed);
                    else pendingCommandEvents.Add(killed);
                }
            }

            RemoveDefeatedEnemies();
            return true;
        }

        public void ConfigureCharacterResources(long instanceId, CharacterResources verifiedResources)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            unit.Abilities.ConfigureResources(verifiedResources);
        }

        // UI의 스킬 버튼 또는 원본 조건 검증을 마친 자동 마나 스킬 호출 경로.
        // 소비 자원·쿨타임·효과 종류가 확인되지 않은 스킬은 시전 실패로 반환한다.
        public AbilityCastResult TryCastActiveSkill(long instanceId, string skillId,
            string primaryEnemyId)
        {
            if (game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("게임 진행 중에만 시전할 수 있습니다.");
            CharacterInstance caster;
            if (!characters.TryGetValue(instanceId, out caster))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");

            SpatialCombatEnemy enemy = null;
            bool hasTarget = !string.IsNullOrWhiteSpace(primaryEnemyId) &&
                enemies.TryGetValue(primaryEnemyId, out enemy) &&
                enemy.Enemy.IsAlive && CanAttackInThisLane(caster, enemy);
            // 자원을 소모하기 전에 등록된 피해 계산기의 지원 여부를 확인한다.
            if (caster.Profile != null)
                foreach (var candidate in caster.Profile.Skills)
                    if (candidate.Id == skillId &&
                        !effectExecutor.CanAttemptWithConfiguredFormula(
                            InstanceKey(instanceId), caster.Profile, candidate))
                        return AbilityCastResult.UnsupportedEffect;

            ImportedCombatSkill skill;
            bool verifiedTiming = false;
            ImportedCombatSkill selectedSkill = null;
            if (caster.Profile != null)
                foreach (var candidate in caster.Profile.Skills)
                    if (candidate.Id == skillId)
                    {
                        selectedSkill = candidate;
                        verifiedTiming = effectExecutor.HasVerifiedTimings(
                            caster.Profile, candidate);
                        break;
                    }
            AbilityCastResult result = caster.Abilities.TryCast(
                skillId, hasTarget, verifiedTiming,
                c => effectExecutor.CanHandleSpecialComponent(
                    caster.Profile, selectedSkill, c), out skill);
            if (result != AbilityCastResult.Cast) return result;

            UnitState position = roster.Get(instanceId);
            int before = events.Count;
            effectExecutor.Execute(InstanceKey(instanceId), caster.Profile, skill,
                enemy, position.X, position.Y, EligibleSkillTargets(caster));
            if (!processingStep)
                for (int index = before; index < events.Count; index++)
                    pendingCommandEvents.Add(events[index]);
            RemoveDefeatedEnemies();
            return result;
        }

        // 게임 이벤트(스택/변신/피격 등)가 원본 조건을 충족했다고 승인한 때만
        // 해당 캐릭터의 이벤트형 스킬을 실행한다.
        public bool TryActivateVerifiedSpecialEvent(long instanceId,
            string skillId, string primaryEnemyId)
        {
            if (game.Phase != SessionPhase.Running || specialEventRules == null)
                return false;
            if (string.IsNullOrWhiteSpace(skillId) ||
                string.IsNullOrWhiteSpace(primaryEnemyId)) return false;
            CharacterInstance unit;
            SpatialCombatEnemy enemy;
            if (!characters.TryGetValue(instanceId, out unit) ||
                !enemies.TryGetValue(primaryEnemyId, out enemy) ||
                !enemy.Enemy.IsAlive || unit.Mapping == null ||
                unit.Profile == null || !CanAttackInThisLane(unit, enemy))
                return false;
            ImportedCombatSkill skill;
            if (!unit.Mapping.ActivatedSkills.TryGetValue(skillId, out skill))
                return false;
            if (attackPipeline.NextAttackAt(InstanceKey(instanceId)) == null ||
                !effectExecutor.CanAttemptWithConfiguredFormula(
                    InstanceKey(instanceId), unit.Profile, skill))
                return false;
            var position = roster.Get(instanceId);
            double dx = enemy.X - position.X;
            double dy = enemy.Y - position.Y;
            double distanceSquared = dx * dx + dy * dy;
            if (!attackPipeline.CanReach(InstanceKey(unit.Id), distanceSquared) ||
                !specialEventRules.CanActivate(unit.Catalog, skill, enemy.Kind,
                    scheduler.Time))
                return false;
            if (!attackPipeline.TryActivateVerifiedCondition(
                InstanceKey(instanceId), skillId))
                return false;
            int before = events.Count;
            effectExecutor.Execute(InstanceKey(instanceId), unit.Profile,
                skill, enemy, position.X, position.Y, EligibleSkillTargets(unit));
            if (!processingStep)
                for (int i = before; i < events.Count; i++)
                    pendingCommandEvents.Add(events[i]);
            RemoveDefeatedEnemies();
            return true;
        }

        public CharacterResources ResourcesOf(long instanceId)
        {
            CharacterInstance caster;
            if (!characters.TryGetValue(instanceId, out caster))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            return caster.Abilities.Resources;
        }

        public void RemoveCharacter(long instanceId)
        {
            BlockExternalMutationDuringOriginalFusion();
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            if (instance.AttackReady) attackPipeline.RemoveUnit(InstanceKey(instanceId));
            if (commonAttackPreview != null) commonAttackPreview.Remove(instanceId);
            effectExecutor.CancelCaster(InstanceKey(instanceId));
            lawReadyAt.Remove(instanceId);
            auras.RemoveAll(instanceId);
            characters.Remove(instanceId);
            unitStatuses.Remove(InstanceKey(instanceId));
            // Native fusion removes every input as ONE logical batch. Do
            // not run external aura reconciliation with an intermediate,
            // half-consumed roster (or during rollback of the staged result).
            if (!internalOriginalFusionMutation) RefreshEnemyAuras();
            roster.Remove(instanceId);
        }

        // Used only by an uncommitted Wisp transaction. A failed reward must
        // not leave a phantom Spawned/Placed cue in the next mobile frame.
        public void RollbackUncommittedSpawn(long instanceId)
        {
            string key = InstanceKey(instanceId);
            RemoveCharacter(instanceId);
            pendingCommandEvents.RemoveAll(evt => evt.ActorId == key &&
                (evt.Type == CharacterCombatEventType.UnitSpawned ||
                 evt.Type == CharacterCombatEventType.UnitPlaced));
            events.RemoveAll(evt => evt.ActorId == key &&
                (evt.Type == CharacterCombatEventType.UnitSpawned ||
                 evt.Type == CharacterCombatEventType.UnitPlaced));
        }

        public void MoveCharacter(long instanceId, double x, double y)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (!characters.ContainsKey(instanceId))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            roster.Move(instanceId, x, y);
            RefreshEnemyAuras();
        }

        public void SpawnEnemy(string enemyId, long health, CharacterEnemyKind kind,
            double x, double y, double pathProgress)
        {
            SpawnEnemyWithDefenses(enemyId, health, kind, x, y, pathProgress,
                EnemyDefenseProfile.UnverifiedKind(kind));
        }

        // 2.323[C] 원본에서 확인한 A04Q/Def5 마법 저항값으로 생성.
        // LK[JG]와 NJ는 아이템/이벤트로 바뀔 수 있어 현재 레벨을 필수로 받는다.
        // 검증된 레벨 5..41 외에는 적을 아예 만들지 않는다.
        public void SpawnEnemyWithVerified323MagicDefense(string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            double? preAbilityArmor, string armorType,
            int currentA04QLevel, string evidence)
        {
            var defenses = Ordr323VerifiedDefenseAbilities
                .FromVerifiedMagicAbilityLevel(kind, preAbilityArmor,
                    armorType, currentA04QLevel, evidence);
            SpawnEnemyWithDefenses(enemyId, health, kind, x, y, pathProgress,
                defenses);
        }

        public void SpawnEnemyWithDefenses(string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            EnemyDefenseProfile verifiedDefenses)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (string.IsNullOrWhiteSpace(enemyId)) throw new ArgumentException("적 ID가 필요합니다.");
            if (enemies.ContainsKey(enemyId)) throw new InvalidOperationException("이미 존재하는 적 ID입니다.");
            if (verifiedDefenses == null) throw new ArgumentNullException(nameof(verifiedDefenses));
            if (verifiedDefenses.Kind.HasValue && verifiedDefenses.Kind.Value != kind)
                throw new ArgumentException("적 분류와 방어 프로필 분류가 다릅니다.");
            var enemy = new CombatEnemy(enemyId, health, verifiedDefenses);
            var spatial = new SpatialCombatEnemy(enemy, kind, x, y, pathProgress);
            resolution.Spawn(enemy);
            enemies.Add(enemyId, spatial);
            statuses.Add(enemyId, enemy.Status);
            armorBreakStacks.Add(enemyId, 0);
            provenA04MStacks.Add(enemyId, 0);
            provenA04PTotals.Add(enemyId, 0);
            RefreshEnemyAuras();
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EnemySpawned, enemyId, enemyId,
                null, kind.ToString(), health));
        }

        // 실제 게임의 적 이동 루프에서 사용. 스턴은 이동을 막고,
        // 이감 수치는 아직 원작 이동속도 공식이 없어 외부 경로 이동기가 확인해야 한다.
        public bool TryMoveEnemy(string enemyId, double x, double y, double progress)
        {
            BlockExternalMutationDuringOriginalFusion();
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            if (statuses[enemyId].IsStunned()) return false;
            enemy.Move(x, y, progress);
            RefreshEnemyAuras();
            return true;
        }

        // 스턴 무시 강제 이동(원본 맵에 확인된 넉백/이동만 호출해야 한다).
        public void ForceMoveEnemy(string enemyId, double x, double y, double progress)
        {
            BlockExternalMutationDuringOriginalFusion();
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            enemy.Move(x, y, progress);
            RefreshEnemyAuras();
        }

        public void ResumeAfterVerifiedFormulaUpdate(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            unit.AttackBlockedByMissingFormula = false;
            unit.DeferredAttackReason = null; // 재승인 후 이전의 실패 사유를 남기지 않는다.
        }

        public CombatStatusEngine StatusOf(string enemyId)
        {
            CombatStatusEngine value;
            return statuses.TryGetValue(enemyId, out value) ? value : null;
        }

        public int ArmorBreakStacksOf(string enemyId)
        {
            int count;
            return armorBreakStacks.TryGetValue(enemyId, out count) ? count : 0;
        }

        public bool LeakEnemy(string enemyId)
        {
            bool leaked = resolution.TryLeak(enemyId);
            if (leaked) RemoveEnemyRuntimeState(enemyId);
            return leaked;
        }

        // 공격·지속피해·상태효과의 시간을 같은 시계로 진행한다.
        // 큰 delta에서도 지나간 공격 시점을 순서대로 실행하고 누락하지 않는다.
        private void AdvanceCombatTime(double seconds)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            effectExecutor.Advance(seconds, elapsed =>
            {
                attackPipeline.Advance(elapsed);
                foreach (var unit in characters.Values)
                    unit.Abilities.Advance(elapsed);
                foreach (var status in statuses.Values)
                    status.Advance(elapsed);
                foreach (var status in unitStatuses.Values)
                    status.Advance(elapsed);
                RefreshEnemyAuras();
            });
        }

        private bool CanAttackInThisLane(CharacterInstance unit,
            SpatialCombatEnemy enemy)
        {
            // 크립/해왕류/도전은 별도 출전 허가가 없는 한 일반 전장에 섞이지 않는다.
            if (enemy.Kind == CharacterEnemyKind.Creep ||
                enemy.Kind == CharacterEnemyKind.SeaKing ||
                enemy.Kind == CharacterEnemyKind.Challenge)
                return false;
            bool dispatched = IsStoryDeployed(unit.Id);
            return dispatched
                ? enemy.Kind == CharacterEnemyKind.Story
                : enemy.Kind == CharacterEnemyKind.Line ||
                  enemy.Kind == CharacterEnemyKind.Boss ||
                  enemy.Kind == CharacterEnemyKind.Enraged;
        }

        // 범위 기술과 상태 효과도 기본 공격과 같은 전장의 적만 대상으로.
        private List<SpatialCombatEnemy> EligibleSkillTargets(CharacterInstance unit)
        {
            var eligible = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
                if (enemy.Enemy.IsAlive && CanAttackInThisLane(unit, enemy))
                    eligible.Add(enemy);
            return eligible;
        }

        private List<TargetCandidate> BuildTargetCandidates(CharacterInstance unit)
        {
            var position = roster.Get(unit.Id);
            var candidates = new List<TargetCandidate>();
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(unit, enemy)) continue;
                double dx = enemy.X - position.X;
                double dy = enemy.Y - position.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (double.IsInfinity(distanceSquared)) continue;
                candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                    enemy.PathProgress, true));
            }
            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return candidates;
        }

        private List<TargetCandidate> BuildBasicAttackCandidates(CharacterInstance unit)
        {
            var candidates = BuildTargetCandidates(unit);
            if (unit.BasicTargetFilter != null)
                candidates.RemoveAll(candidate => !unit.BasicTargetFilter
                    .CanAttemptBasicAttack(unit.Catalog, enemies[candidate.Id].Enemy));
            return candidates;
        }

        // 현재 표적을 가진 유닛 중 가장 빠른 공격 시점을 찾는다.
        private double? NextEligibleAttackAt()
        {
            double? next = null;
            foreach (var unit in characters.Values)
            {
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null) continue;
                double? at = attackPipeline.NextAttackAt(InstanceKey(unit.Id));
                var candidates = BuildBasicAttackCandidates(unit);
                if (!attackPipeline.HasUnresolvedNativeAttackSpeed(
                        InstanceKey(unit.Id)) &&
                    attackPipeline.SelectDefaultTarget(InstanceKey(unit.Id),
                        candidates, TargetPriority.Nearest) != null &&
                    at.HasValue && (!next.HasValue || at.Value < next.Value))
                    next = at;
                // A separately verified periodic skill may target an enemy
                // whose native basic-attack armor has not been established.
                if (BuildTargetCandidates(unit).Count == 0) continue;
                foreach (var entry in unit.Mapping.ActivatedSkills)
                {
                    if (entry.Value.Trigger != "periodic") continue;
                    double? scheduled = attackPipeline.NextPeriodicSkillAt(
                        InstanceKey(unit.Id), entry.Key);
                    if (scheduled.HasValue &&
                        (!next.HasValue || scheduled.Value < next.Value))
                        next = scheduled;
                }
            }
            if (commonAttackPreview != null)
                foreach (var unit in characters.Values)
                {
                    if (unit.AttackReady) continue;
                    double? scheduled = commonAttackPreview.NextAt(unit.Id);
                    if (!scheduled.HasValue || BuildTargetCandidates(unit).Count == 0)
                        continue;
                    if (!next.HasValue || scheduled.Value < next.Value)
                        next = scheduled;
                }
            return next;
        }

        private int ExecuteReadyCommonAttackPreviews(int budget)
        {
            if (budget <= 0 || commonAttackPreview == null) return 0;
            int shown = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (shown >= budget) break;
                var unit = characters[id];
                if (unit.AttackReady) continue;
                double? scheduled = commonAttackPreview.NextAt(id);
                if (!scheduled.HasValue ||
                    !CombatScheduler.HasReached(scheduler.Time, scheduled.Value)) continue;
                var candidates = BuildTargetCandidates(unit);
                if (candidates.Count == 0) continue;
                string selected = ChoosePreviewTarget(unit, candidates);
                if (selected == null) continue;

                OriginalMapCharacterBasics evidence;
                if (!commonAttackPreview.TryCommit(id, scheduler.Time, out evidence))
                    continue;
                shown++;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.AttackPreview,
                    InstanceKey(id), selected, null,
                    evidence.OriginalRawcode + " map-base " +
                    evidence.Attack1.MinimumBaseDamage.Value + "-" +
                    evidence.Attack1.MaximumBaseDamage.Value +
                    " (미검증 최종피해: 체력 감소 없음)", 0));
            }
            return shown;
        }

        private string ChoosePreviewTarget(CharacterInstance unit,
            List<TargetCandidate> candidates)
        {
            if (targetSelectionRules != null)
            {
                var contacts = new List<VerifiedTargetContact>();
                foreach (var candidate in candidates)
                    contacts.Add(new VerifiedTargetContact(candidate,
                        enemies[candidate.Id].Kind));
                string approved;
                if (targetSelectionRules.TryChoose(unit.Catalog,
                    contacts.AsReadOnly(), double.MaxValue, out approved))
                {
                    foreach (var candidate in candidates)
                        if (candidate.Id == approved) return approved;
                    throw new InvalidOperationException(
                        "미리보기 대상 정책이 다른 전장이나 존재하지 않는 적을 선택했습니다.");
                }
            }
            var bossRules = MobileAutomation.BossRules;
            if (bossRules != null)
            {
                foreach (var candidate in candidates)
                {
                    var enemy = enemies[candidate.Id];
                    if (enemy.Kind == CharacterEnemyKind.Boss &&
                        bossRules.CanPreferBoss(unit.Catalog, enemy.Enemy,
                            candidate.DistanceSquared, double.MaxValue))
                        return candidate.Id;
                }
            }
            return CombatTargeting.Select(candidates, 0,
                TargetPriority.Nearest, true);
        }

        private int ExecuteReadyAttacks(int budget)
        {
            int fired = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (fired >= budget) break;
                CharacterInstance unit = characters[id];
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null) continue;
                var at = attackPipeline.NextAttackAt(InstanceKey(id));
                if (!at.HasValue ||
                    !CombatScheduler.HasReached(scheduler.Time, at.Value)) continue;

                var candidates = BuildBasicAttackCandidates(unit);
                string approvedBoss = ChooseApprovedBossTarget(unit, candidates);
                BasicAttackResult hit;
                try
                {
                    if (!attackPipeline.TryBasicAttack(InstanceKey(id), candidates,
                        approvedBoss, out hit))
                        continue;
                }
                catch (NotSupportedException ex)
                {
                    unit.AttackBlockedByMissingFormula = true;
                    unit.DeferredAttackReason = "평타 피해 공식 미연결: " + ex.Message;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred,
                        InstanceKey(id), null, null,
                        "평타 피해 공식 미연결: " + ex.Message, 0));
                    continue;
                }
                fired++;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    InstanceKey(id), hit.TargetId, null, "basic", hit.ActualDamage));
                SpatialCombatEnemy primary;
                if (!enemies.TryGetValue(hit.TargetId, out primary))
                    throw new InvalidOperationException("타격 대상 정보 소실");
                if (!primary.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated,
                        InstanceKey(id), primary.Id, null, "basic", 0));
                // Native uncommon procs were rolled only after a verified hit
                // and only for fully approved effects. No web tooltip required.
                CompleteOriginalNativePassives(unit, hit, primary.Enemy);
                ExecuteOriginalUncommonProcs(unit, hit);
                if (unit.Profile == null || unit.Mapping == null) continue;
                var position = roster.Get(id);
                foreach (string skillId in hit.ActivatedSkillRawCodes)
                {
                    ImportedCombatSkill skill;
                    if (!unit.Mapping.ActivatedSkills.TryGetValue(skillId, out skill))
                        continue;
                    effectExecutor.Execute(InstanceKey(id), unit.Profile,
                        skill, primary, position.X, position.Y, EligibleSkillTargets(unit));
                }
            }
            RemoveDefeatedEnemies();
            return fired;
        }

        private int ExecuteReadyPeriodicSkills(int budget)
        {
            int processed = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (processed >= budget) break;
                var unit = characters[id];
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null || unit.Profile == null) continue;
                var candidates = BuildTargetCandidates(unit);
                var primaryId = attackPipeline.SelectDefaultTarget(
                    InstanceKey(unit.Id), candidates, TargetPriority.Nearest);
                if (primaryId == null) continue;
                var skills = new List<string>(unit.Mapping.ActivatedSkills.Keys);
                skills.Sort(StringComparer.Ordinal);
                foreach (string skillId in skills)
                {
                    if (processed >= budget) break;
                    ImportedCombatSkill skill = unit.Mapping.ActivatedSkills[skillId];
                    if (skill.Trigger != "periodic") continue;
                    double? due = attackPipeline.NextPeriodicSkillAt(
                        InstanceKey(id), skillId);
                    if (!due.HasValue ||
                        !CombatScheduler.HasReached(scheduler.Time, due.Value)) continue;
                    // 확률 실패도 한 번의 예정 발동으로 처리해야 같은 시각에 무한 재시도하지 않는다.
                    bool activated = attackPipeline.TryActivatePeriodicSkill(
                        InstanceKey(id), skillId);
                    processed++;
                    if (!activated) continue;
                    SpatialCombatEnemy primary;
                    if (!enemies.TryGetValue(primaryId, out primary) ||
                        !primary.Enemy.IsAlive) break;
                    var position = roster.Get(id);
                    effectExecutor.Execute(InstanceKey(id), unit.Profile,
                        skill, primary, position.X, position.Y, EligibleSkillTargets(unit));
                    if (!primary.Enemy.IsAlive)
                    {
                        primaryId = attackPipeline.SelectDefaultTarget(
                            InstanceKey(unit.Id), BuildTargetCandidates(unit),
                            TargetPriority.Nearest);
                        if (primaryId == null) break;
                    }
                }
            }
            RemoveDefeatedEnemies();
            return processed;
        }

        // API에 전달한 시간은 모두 처리하거나 다음 Step으로 이월한다.
        // 과도한 프레임 지연 시 전투 시뮬레이션을 조용히 건너뛰지 않는다.
        public IReadOnlyList<CharacterCombatEvent> Step(double deltaSeconds)
        {
            // Must fail BEFORE pendingStepSeconds, event buffers or any
            // aura/mana/attack clock can be mutated during native fusion.
            BlockExternalMutationDuringOriginalFusion();
            if (game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("게임이 진행 중이 아닙니다.");
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0 || double.IsInfinity(pendingStepSeconds + deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            pendingStepSeconds += deltaSeconds;
            events.Clear();
            if (pendingCommandEvents.Count != 0)
            {
                events.AddRange(pendingCommandEvents);
                pendingCommandEvents.Clear();
            }
            processingStep = true;
            try
            {
                RefreshEnemyAuras();
                const int maxAttacksPerStep = 8192; // 무한 연산 방지, 잔여 시간은 이월
                int completed = 0;
                bool checkCurrentTime = true;
                while ((checkCurrentTime || pendingStepSeconds > 0) &&
                       completed < maxAttacksPerStep)
                {
                    checkCurrentTime = false;
                    double? next = NextEligibleAttackAt();
                    if (!next.HasValue || !CombatScheduler.HasReached(
                        scheduler.Time + pendingStepSeconds, next.Value))
                    {
                        AdvanceCombatTime(pendingStepSeconds);
                        pendingStepSeconds = 0;
                        MobileAutomation.RunAutomaticAbilities(this);
                        break;
                    }
                    // Never deduct a rounding residual larger than pending time.
                    double wait = Math.Min(pendingStepSeconds,
                        Math.Max(0, next.Value - scheduler.Time));
                    if (wait > 0)
                    {
                        AdvanceCombatTime(wait);
                        pendingStepSeconds -= wait;
                    }
                    MobileAutomation.RunAutomaticAbilities(this);
                    int fired = ExecuteReadyAttacks(maxAttacksPerStep - completed);
                    completed += fired;
                    int previews = ExecuteReadyCommonAttackPreviews(
                        maxAttacksPerStep - completed);
                    completed += previews;
                    int periodic = ExecuteReadyPeriodicSkills(
                        maxAttacksPerStep - completed);
                    completed += periodic;
                    if (fired == 0 && previews == 0 && periodic == 0)
                    {
                        // 자동 시전으로 적이 사라지는 경우 등 다음 시간으로 진행.
                        AdvanceCombatTime(pendingStepSeconds);
                        pendingStepSeconds = 0;
                        break;
                    }
                    if (pendingStepSeconds == 0) break;
                }
                RemoveDefeatedEnemies();
                return events.AsReadOnly();
            }
            finally
            {
                processingStep = false;
            }
        }

        private string ChooseApprovedBossTarget(CharacterInstance unit,
            List<TargetCandidate> candidates)
        {
            if (targetSelectionRules != null)
            {
                var contacts = new List<VerifiedTargetContact>();
                foreach (var candidate in candidates)
                {
                    SpatialCombatEnemy enemy;
                    if (!enemies.TryGetValue(candidate.Id, out enemy) ||
                        !enemy.Enemy.IsAlive) continue;
                    contacts.Add(new VerifiedTargetContact(candidate, enemy.Kind));
                }
                string chosen;
                if (targetSelectionRules.TryChoose(unit.Catalog,
                    contacts.AsReadOnly(),
                    attackPipeline.ReachForRuleEvaluation(InstanceKey(unit.Id)),
                    out chosen))
                {
                    if (string.IsNullOrWhiteSpace(chosen))
                        throw new InvalidOperationException("검증된 타깃 정책이 빈 ID를 반환했습니다.");
                    bool eligible = false;
                    foreach (var contact in contacts)
                        if (contact.Candidate.Id == chosen &&
                            attackPipeline.CanReach(InstanceKey(unit.Id),
                                contact.Candidate.DistanceSquared))
                        { eligible = true; break; }
                    if (!eligible)
                        throw new InvalidOperationException(
                            "타깃 정책이 다른 전장 또는 공격 불가능한 적을 반환했습니다.");
                    return chosen;
                }
            }
            var rules = MobileAutomation.BossRules;
            if (rules == null || unit.Mapping == null) return null;
            string preferred = null;
            double nearestBossDistance = double.MaxValue;
            double effectiveReach = attackPipeline.ReachForRuleEvaluation(
                InstanceKey(unit.Id));
            foreach (var candidate in candidates)
            {
                if (!candidate.Alive ||
                    !attackPipeline.CanReach(InstanceKey(unit.Id),
                        candidate.DistanceSquared)) continue;
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(candidate.Id, out enemy) ||
                    enemy.Kind != CharacterEnemyKind.Boss || !enemy.Enemy.IsAlive) continue;
                if (!rules.CanPreferBoss(unit.Catalog, enemy.Enemy,
                    candidate.DistanceSquared, effectiveReach)) continue;
                if (candidate.DistanceSquared < nearestBossDistance ||
                    (candidate.DistanceSquared == nearestBossDistance &&
                     (preferred == null || string.CompareOrdinal(candidate.Id, preferred) < 0)))
                {
                    nearestBossDistance = candidate.DistanceSquared;
                    preferred = candidate.Id;
                }
            }
            return preferred;
        }

        private void RemoveDefeatedEnemies()
        {
            var removed = new List<string>();
            foreach (var pair in enemies)
                if (!pair.Value.Enemy.IsAlive || resolution.GetEnemy(pair.Key) == null)
                    removed.Add(pair.Key);
            foreach (string id in removed) RemoveEnemyRuntimeState(id);
        }

        private void RemoveEnemyRuntimeState(string id)
        {
            moriaParticipation.Forget(id);
            effectExecutor.CancelTarget(id);
            enemies.Remove(id);
            statuses.Remove(id);
            armorBreakStacks.Remove(id);
            provenA04MStacks.Remove(id);
            provenA04PTotals.Remove(id);
        }

        private static string InstanceKey(long id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}

