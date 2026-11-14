using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // S02 combat world CAPTURE contract. This draft has no safe live restore
    // entry point until all source-native deferred triggers and auxiliary
    // worlds are fully described by serializable effects and source RNG.
    [Serializable]
    public sealed class SourceCombatActorCheckpoint
    {
        public long InstanceId;
        public string CatalogId;
        public bool AttackReady;
        public bool AttackBlocked;
        public bool StoryDeployed;
        public bool PoneglyphChanneling;
        public string SharedObjectiveEnemyId;
        public CharacterActiveAbilityState Abilities;
        public CombatStatusEngineState Status;
    }

    [Serializable]
    public sealed class SourceCombatDefenseCheckpoint
    {
        public bool HasKind;
        public CharacterEnemyKind Kind;
        public bool HasArmor;
        public double Armor;
        public bool HasMagicResistance;
        public double MagicResistance;
        public bool HasMagicAbilityLevel;
        public int MagicAbilityLevel;
        public string WarcraftArmorType;
        public string Source;

        public static SourceCombatDefenseCheckpoint Capture(EnemyDefenseProfile defense)
        {
            if (defense == null)
                throw new InvalidOperationException("적 원본 방어 프로필이 없습니다.");
            return new SourceCombatDefenseCheckpoint {
                HasKind = defense.Kind.HasValue,
                Kind = defense.Kind.GetValueOrDefault(),
                HasArmor = defense.BaseArmor.HasValue,
                Armor = defense.BaseArmor.GetValueOrDefault(),
                HasMagicResistance = defense.MagicResistancePercent.HasValue,
                MagicResistance = defense.MagicResistancePercent.GetValueOrDefault(),
                HasMagicAbilityLevel = defense.MagicAbilityLevel.HasValue,
                MagicAbilityLevel = defense.MagicAbilityLevel.GetValueOrDefault(),
                WarcraftArmorType = defense.WarcraftArmorType,
                Source = defense.Source
            };
        }

        public EnemyDefenseProfile ValidateAndRecreate()
        {
            try
            {
                return new EnemyDefenseProfile(
                    HasKind ? (CharacterEnemyKind?)Kind : null,
                    HasArmor ? (double?)Armor : null,
                    HasMagicResistance ? (double?)MagicResistance : null,
                    WarcraftArmorType, Source,
                    HasMagicAbilityLevel ? (int?)MagicAbilityLevel : null);
            }
            catch (ArgumentException)
            {
                throw new FormatException(
                    "원본 몬스터 방어/저항/능력레벨 저장 상태가 잘못됐습니다.");
            }
        }
    }

    [Serializable]
    public sealed class SourceCombatEnemyCheckpoint
    {
        public string EnemyId;
        public CharacterEnemyKind Kind;
        public long Health;
        public long MaximumHealth;
        public double X;
        public double Y;
        public double PathProgress;
        public int ArmorBreakStacks;
        public int ProvenA04MStacks;
        public int ProvenA04PTotals;
        public CombatStatusEngineState Status;
        public SourceCombatDefenseCheckpoint Defenses;
        public bool HasOriginalA04RLevel;
        public int OriginalA04RLevel;
        public string OriginalA04RProvenance;
        public string ObservedHealthSource;
        public Original323NativePassiveTargetState NativePassiveTarget;
        public long NativePassiveTargetRevision;
    }

    [Serializable]
    public sealed class SourceCombatRuntimeCheckpoint
    {
        // Version 2 includes the exact native-target and A04R provenance.
        // Older world drafts cannot silently discard those runtime modifiers.
        public int Version = 2;
        public double Time;
        public bool HasSourceModelBasicAttacks;
        public bool MobileMainLaneEnabled;
        public CombatAttackPipelineState Attacks;
        public CombatTimedEffectQueueState Timed;
        public List<SourceCombatActorCheckpoint> Actors =
            new List<SourceCombatActorCheckpoint>();
        public List<SourceCombatEnemyCheckpoint> Enemies =
            new List<SourceCombatEnemyCheckpoint>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        // Restored GameSession/UnitRoster are constructed by the core loader.
        // This method adds only the existing enemy objects; it does not
        // re-run wave spawns, damage events, native observers or rewards.
        // Invoke on a NEW unpublished runtime, then restore actors/attacks,
        // wave ledger and all missing owner systems before exposing gameplay.
        public void RestoreEnemyWorldDraft(SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 2 || state.Enemies == null ||
                game.Phase != SessionPhase.Running || enemies.Count != 0 ||
                resolution == null || processingStep || pendingStepSeconds != 0 ||
                scheduler.Time != 0 || effectExecutor.Time != 0)
                throw new InvalidOperationException("새 전투 월드에서만 적의 실물 상태를 복구할 수 있습니다.");
            var staged = new List<SpatialCombatEnemy>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int waveEnemies = 0;
            foreach (var entry in state.Enemies)
            {
                if (entry == null || !Enum.IsDefined(typeof(CharacterEnemyKind), entry.Kind) ||
                    !seen.Add(entry.EnemyId) || double.IsNaN(entry.X) ||
                    double.IsInfinity(entry.X) || double.IsNaN(entry.Y) ||
                    double.IsInfinity(entry.Y) || double.IsNaN(entry.PathProgress) ||
                    double.IsInfinity(entry.PathProgress))
                    throw new FormatException("적 위치·신원이 중복되거나 잘못됐습니다.");
                var restored = CombatEnemy.RestoreSnapshot(entry);
                staged.Add(new SpatialCombatEnemy(restored, entry.Kind,
                    entry.X, entry.Y, entry.PathProgress));
                if (entry.Kind == CharacterEnemyKind.Line ||
                    entry.Kind == CharacterEnemyKind.Boss ||
                    entry.Kind == CharacterEnemyKind.Enraged)
                    waveEnemies++;
            }
            if (waveEnemies != game.AliveEnemies)
                throw new FormatException("적 숫자와 복원된 라운드 장부가 다릅니다.");
            // No mutating call occurs until every enemy/status/profile is valid.
            foreach (var actor in staged)
            {
                bool wave = actor.Kind == CharacterEnemyKind.Line ||
                    actor.Kind == CharacterEnemyKind.Boss ||
                    actor.Kind == CharacterEnemyKind.Enraged;
                resolution.AttachRestoredEnemy(actor.Enemy, wave);
                enemies.Add(actor.Id, actor);
                statuses.Add(actor.Id, actor.Enemy.Status);
            }
            foreach (var entry in state.Enemies)
            {
                armorBreakStacks.Add(entry.EnemyId, entry.ArmorBreakStacks);
                provenA04MStacks.Add(entry.EnemyId, entry.ProvenA04MStacks);
                provenA04PTotals.Add(entry.EnemyId, entry.ProvenA04PTotals);
            }
        }

        // Captures real owner identities, resources, status clocks and all
        // registered basic/skill timers. Unknown delayed lambda state must
        // fail before anyone can accidentally publish a partial checkpoint.
        public SourceCombatRuntimeCheckpoint CaptureRuntimeDraft()
        {
            if (game.Phase != SessionPhase.Running ||
                activeOriginalFusionSwap != null || processingStep ||
                pendingStepSeconds != 0 ||
                scheduler.Time != triggers.Time ||
                scheduler.Time != effectExecutor.Time)
                throw new InvalidOperationException(
                    "불완전한 전투 스텝·조합·불일치 시계는 저장할 수 없습니다.");
            var draft = new SourceCombatRuntimeCheckpoint {
                Time = scheduler.Time,
                HasSourceModelBasicAttacks = UsesSourceModelBasicAttacks,
                MobileMainLaneEnabled = sourceModelMainLane != null,
                Attacks = attackPipeline.CaptureState(),
                Timed = effectExecutor.CaptureTimedState()
            };
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                var actor = characters[id];
                var key = InstanceKey(id);
                CombatStatusEngine status;
                if (roster.Get(id).UnitId != actor.Catalog.Id ||
                    !unitStatuses.TryGetValue(key, out status))
                    throw new InvalidOperationException(
                        "전투 유닛과 공유 로스터/상태효과의 연결이 다릅니다.");
                draft.Actors.Add(new SourceCombatActorCheckpoint {
                    InstanceId = id, CatalogId = actor.Catalog.Id,
                    AttackReady = actor.AttackReady,
                    AttackBlocked = actor.AttackBlockedByMissingFormula,
                    StoryDeployed = IsStoryDeployed(id),
                    PoneglyphChanneling = IsOriginalPoneglyphChanneling(id),
                    SharedObjectiveEnemyId = SharedObjectiveTargetOf(id),
                    Abilities = actor.Abilities.CaptureState(),
                    Status = status.CaptureState()
                });
            }
            var enemyIds = new List<string>(enemies.Keys);
            enemyIds.Sort(StringComparer.Ordinal);
            foreach (string id in enemyIds)
            {
                var live = enemies[id];
                CombatStatusEngine status;
                if (!live.Enemy.IsAlive || resolution.GetEnemy(id) != live.Enemy ||
                    !statuses.TryGetValue(id, out status) ||
                    !ReferenceEquals(status, live.Enemy.Status))
                    throw new InvalidOperationException(
                        "전투 대상과 체력/효과 장부가 서로 다릅니다.");
                int stacks, magic, armor;
                armorBreakStacks.TryGetValue(id, out stacks);
                provenA04MStacks.TryGetValue(id, out armor);
                provenA04PTotals.TryGetValue(id, out magic);
                draft.Enemies.Add(new SourceCombatEnemyCheckpoint {
                    EnemyId = id, Kind = live.Kind,
                    Health = live.Enemy.Health,
                    MaximumHealth = live.Enemy.MaxHealth,
                    X = live.X, Y = live.Y, PathProgress = live.PathProgress,
                    ArmorBreakStacks = stacks, ProvenA04MStacks = armor,
                    ProvenA04PTotals = magic, Status = status.CaptureState(),
                    Defenses = SourceCombatDefenseCheckpoint.Capture(
                        live.Enemy.Defenses),
                    HasOriginalA04RLevel = live.Enemy.OriginalA04RLevel.HasValue,
                    OriginalA04RLevel = live.Enemy.OriginalA04RLevel.GetValueOrDefault(),
                    OriginalA04RProvenance = live.Enemy.OriginalA04RProvenance,
                    ObservedHealthSource = live.Enemy.ObservedHealthSource,
                    NativePassiveTarget = live.Enemy.NativePassiveTargetProfile?.CaptureState(),
                    NativePassiveTargetRevision = live.Enemy.NativePassiveTargetRevision
                });
            }
            // The core roster can contain only real actors, never detached
            // phantom material IDs, when taking a consistent checkpoint.
            if (draft.Actors.Count != roster.Count)
                throw new InvalidOperationException(
                    "공통 유닛 명단에 전투 엔진에 없는 실물이 존재합니다.");
            return draft;
        }
    }
}
