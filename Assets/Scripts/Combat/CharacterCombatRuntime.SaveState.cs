using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Mobile;
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
        public string AttackEvidence;
        public string DeferredAttackReason;
        public bool SourceManagedResources;
        public long SourceLaneRevision;
        public CharacterActiveAbilityState Abilities;
        public CombatStatusEngineState Status;
    }

    // S03 manual owner state; active CDh is represented by one typed queue pulse.
    [Serializable]
    public sealed class SourceCombatManualOwnerCheckpoint
    {
        public long InstanceId;
        public int MariaStage = -1; // -1 = never used; 0..2 = last mode
        public double MariaReadyAt;
        public long MariaToken; // 0=not active; unique CDh loop identity
        public string MarcoForm; // null = default human, or human/phoenix
        public double MarcoReadyAt;
    }

    [Serializable]
    public sealed class SourceCombatSummonCheckpoint
    {
        public long Id, OwnerId, LaneRevision;
        public string SkillName, Name, SourceUnitRawcode;
        public double ExpiresAt, SpawnX, SpawnY;
        public bool StoryLane, HasIndependentCombat, IsVirtualAttackEffect;
        public bool HasUnresolvedOriginalLootAbility, AttackBlocked;
        public CombatStatusEngineState AttackStatus;
    }

    [Serializable]
    public sealed class SourceCombatFormCheckpoint
    {
        public long OwnerId, LaneRevision;
        public string Form;
        public double ExpiresAt;
        public bool StoryLane;
    }

    [Serializable]
    public sealed class SourceCombatForestCheckpoint
    {
        public long OwnerId;
        public int Nutrients;
    }

    // Active Bullet I-05 Rokushiki stance: absolute logical expiry and
    // the owner's original-lane generation, not a replayed skill cast.
    [Serializable]
    public sealed class SourceCombatBulletStanceCheckpoint
    {
        public long OwnerId;
        public long LaneRevision;
        public double ExpiresAt;
    }

    // e00K/A089 temporary zone has no owned summon/attack timer.
    [Serializable]
    public sealed class SourceCombatNamiZoneCheckpoint
    {
        public string Source;
        public double X, Y, ExpiresAt;
        public bool Story;
    }

    [Serializable]
    public sealed class SourceCombatSharedDeploymentCheckpoint
    {
        public long OwnerId;
        public string EnemyId;
        public double ReturnX, ReturnY;
        public double PositionX, PositionY;
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
        // V4: per-owner source lane revisions, restorable native summon
        // attackers and the supported timed-form/growth state. V2/V3 drafts
        // cannot be upgraded without risking lost combat side effects.
        public int Version = 4;
        public double Time;
        public bool HasSourceModelBasicAttacks;
        public bool MobileMainLaneEnabled;
        public CombatAttackPipelineState Attacks;
        public CombatTimedEffectQueueState Timed;
        // Native e0KD delayed casts, per-actor attacked-event sequences, and
        // completed cast tombstones; no projectiles or rewards are replayed.
        public SourceOriginalDummyLedgerCheckpoint OriginalNativeDummy;
        // Source-backed, queue-owned native delayed hits and liquid-fire
        // per-enemy generation counters (no lambda serialization).
        public SourceNativeDelayedOwnerCheckpoint NativeDelayed;
        public SourceTemporaryOwnerCheckpoint TemporaryOwners;
        public List<SourceCombatActorCheckpoint> Actors =
            new List<SourceCombatActorCheckpoint>();
        public List<SourceCombatManualOwnerCheckpoint> ManualOwners =
            new List<SourceCombatManualOwnerCheckpoint>();
        public long NextSummonId;
        public List<SourceCombatSummonCheckpoint> Summons =
            new List<SourceCombatSummonCheckpoint>();
        public List<SourceCombatFormCheckpoint> Forms =
            new List<SourceCombatFormCheckpoint>();
        public List<SourceCombatForestCheckpoint> Forest =
            new List<SourceCombatForestCheckpoint>();
        public List<SourceCombatBulletStanceCheckpoint> BulletStances =
            new List<SourceCombatBulletStanceCheckpoint>();
        public long MariaNextToken;
        public long NamiNextSequence;
        public List<SourceCombatNamiZoneCheckpoint> NamiZones =
            new List<SourceCombatNamiZoneCheckpoint>();
        public MoriaParticipationLedgerState MoriaParticipation;
        public List<SourceCombatCreepDeploymentCheckpoint> CreepDeployments =
            new List<SourceCombatCreepDeploymentCheckpoint>();
        public List<SourceCombatSharedDeploymentCheckpoint> SharedDeployments =
            new List<SourceCombatSharedDeploymentCheckpoint>();
        // Null means the equipment source was not configured in this world.
        public OriginalEquippedItemLedgerState EquippedItems;
        public List<SourceCombatEnemyCheckpoint> Enemies =
            new List<SourceCombatEnemyCheckpoint>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        // A single cross-reference validator is shared by core checkpoint
        // parsing and unpublished combat reconstruction. Every real roster
        // actor gets exactly one manual-owner row; no orphan may be replayed.
        public static void ValidateManualOwnerState(SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 ||
                state.Actors == null || state.ManualOwners == null ||
                state.ManualOwners.Count != state.Actors.Count ||
                state.MariaNextToken < 0 ||
                (!state.HasSourceModelBasicAttacks && state.MariaNextToken != 0))
                throw new FormatException("수동 스킬 보존 상태가 누락되거나 이전 버전입니다.");
            var actorCatalogs = new Dictionary<long, string>();
            foreach (var actor in state.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    string.IsNullOrWhiteSpace(actor.CatalogId) ||
                    actorCatalogs.ContainsKey(actor.InstanceId))
                    throw new FormatException("수동 스킬 유닛의 신원이 중복되거나 불완전합니다.");
                actorCatalogs.Add(actor.InstanceId, actor.CatalogId);
            }
            var seen = new HashSet<long>();
            foreach (var row in state.ManualOwners)
            {
                string catalog;
                if (row == null || row.InstanceId <= 0 ||
                    !seen.Add(row.InstanceId) ||
                    !actorCatalogs.TryGetValue(row.InstanceId, out catalog) ||
                    row.MariaStage < -1 || row.MariaStage > 2 ||
                    row.MariaToken < 0 || row.MariaToken > state.MariaNextToken ||
                    (row.MariaToken > 0 && row.MariaStage < 0) ||
                    double.IsNaN(row.MariaReadyAt) ||
                    double.IsInfinity(row.MariaReadyAt) || row.MariaReadyAt < 0 ||
                    double.IsNaN(row.MarcoReadyAt) ||
                    double.IsInfinity(row.MarcoReadyAt) || row.MarcoReadyAt < 0 ||
                    (row.MariaStage >= 0 && catalog != "D-01") ||
                    (row.MariaStage == -1 && row.MariaReadyAt != 0) ||
                    (row.MarcoForm != null &&
                     (catalog != "LI-03" ||
                      (row.MarcoForm != "human" &&
                       row.MarcoForm != "phoenix"))) ||
                    (row.MarcoReadyAt != 0 && row.MarcoForm == null) ||
                    (!state.HasSourceModelBasicAttacks &&
                     (row.MariaStage != -1 || row.MarcoForm != null ||
                      row.MariaToken != 0 || state.MariaNextToken != 0)))
                    throw new FormatException("캐릭터별 수동 스킬/폼 상태가 잘못됐습니다.");
            }
        }

        public static void ValidateSourceSidecars(SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 || state.Actors == null ||
                state.Summons == null || state.Forms == null || state.Forest == null ||
                state.BulletStances == null ||
                state.NextSummonId < 0 || double.IsNaN(state.Time) ||
                double.IsInfinity(state.Time) || state.Time < 0)
                throw new FormatException("소환/변신 저장 구성이 불완전합니다.");
            var owners = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var a in state.Actors)
            {
                if (a == null || a.InstanceId <= 0 || a.SourceLaneRevision < 0 ||
                    string.IsNullOrWhiteSpace(a.CatalogId) ||
                    owners.ContainsKey(a.InstanceId))
                    throw new FormatException("전투 소유 유닛 상태가 잘못됐습니다.");
                owners.Add(a.InstanceId, a);
            }
            var seen = new HashSet<long>();
            foreach (var s in state.Summons)
            {
                SourceCombatActorCheckpoint owner;
                if (s == null || s.Id <= 0 || s.Id > state.NextSummonId ||
                    !seen.Add(s.Id) || !owners.TryGetValue(s.OwnerId, out owner) ||
                    string.IsNullOrWhiteSpace(s.Name) ||
                    string.IsNullOrWhiteSpace(s.SkillName) ||
                    s.LaneRevision != owner.SourceLaneRevision ||
                    s.StoryLane != owner.StoryDeployed ||
                    double.IsNaN(s.ExpiresAt) || double.IsInfinity(s.ExpiresAt) ||
                    s.ExpiresAt <= state.Time || s.ExpiresAt - state.Time > 3600 ||
                    double.IsNaN(s.SpawnX) || double.IsInfinity(s.SpawnX) ||
                    double.IsNaN(s.SpawnY) || double.IsInfinity(s.SpawnY) ||
                    (s.SourceUnitRawcode != null && s.SourceUnitRawcode.Length != 4) ||
                    (s.HasIndependentCombat &&
                     (s.AttackStatus == null ||
                      s.AttackStatus.Time != state.Time ||
                      !((s.SourceUnitRawcode == "h085" &&
                         owner.CatalogId == "L-21") ||
                        (s.SourceUnitRawcode == "h06N" &&
                         owner.CatalogId == "L-19")))) ||
                    (!s.HasIndependentCombat && (s.AttackStatus != null ||
                                                s.AttackBlocked)))
                    throw new FormatException("원본 소환 실물/공격 ID/수명/전장이 잘못됐습니다.");
                if (s.HasIndependentCombat) CombatStatusEngine.Restore(s.AttackStatus);
            }
            seen.Clear();
            foreach (var f in state.Forms)
            {
                SourceCombatActorCheckpoint owner;
                if (f == null || !seen.Add(f.OwnerId) ||
                    !owners.TryGetValue(f.OwnerId, out owner) ||
                    f.LaneRevision != owner.SourceLaneRevision ||
                    f.StoryLane != owner.StoryDeployed ||
                    double.IsNaN(f.ExpiresAt) || double.IsInfinity(f.ExpiresAt) ||
                    f.ExpiresAt <= state.Time ||
                    !((f.Form == "awakening" && owner.CatalogId == "TR-03") ||
                      (f.Form == "forest" && owner.CatalogId == "TR-06")))
                    throw new FormatException("원본 임시 변신 상태가 잘못됐습니다.");
            }
            seen.Clear();
            foreach (var f in state.Forest)
            {
                SourceCombatActorCheckpoint owner;
                if (f == null || !seen.Add(f.OwnerId) || f.Nutrients <= 0 ||
                    !owners.TryGetValue(f.OwnerId, out owner) ||
                    owner.CatalogId != "TR-06")
                    throw new FormatException("숲인간 양분 성장 장부가 잘못됐습니다.");
            }
            seen.Clear();
            foreach (var stance in state.BulletStances)
            {
                SourceCombatActorCheckpoint owner;
                if (stance == null || stance.OwnerId <= 0 ||
                    !seen.Add(stance.OwnerId) ||
                    !owners.TryGetValue(stance.OwnerId, out owner) ||
                    owner.CatalogId != "I-05" ||
                    stance.LaneRevision != owner.SourceLaneRevision ||
                    double.IsNaN(stance.ExpiresAt) ||
                    double.IsInfinity(stance.ExpiresAt) ||
                    stance.ExpiresAt <= state.Time ||
                    stance.ExpiresAt - state.Time > 3.000001)
                    throw new FormatException(
                        "불릿 육식 체의 소유자·전장·남은 지속시간이 잘못됐습니다.");
            }
            if (!state.HasSourceModelBasicAttacks &&
                (state.Summons.Count != 0 || state.Forms.Count != 0 ||
                 state.Forest.Count != 0 || state.BulletStances.Count != 0))
                throw new FormatException("일반 전투에 원본 소환·폼을 주입할 수 없습니다.");
        }


        public static void ValidateEquipmentState(SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 || state.Actors == null)
                throw new FormatException("원본 장비 복원 대상이 없습니다.");
            if (state.EquippedItems == null) return;
            if (!state.HasSourceModelBasicAttacks)
                throw new FormatException("원본 전투가 아닌 장부에 장비가 저장됐습니다.");
            OriginalEquippedItemLedger.ValidateState(state.EquippedItems);
            var actors = new HashSet<long>();
            foreach (var row in state.Actors)
                if (row == null || row.InstanceId <= 0 || !actors.Add(row.InstanceId))
                    throw new FormatException("장비 저장의 소유 유닛 신원이 잘못됐습니다.");
            foreach (var owner in state.EquippedItems.Owners)
                if (!actors.Contains(owner.OwnerId))
                    throw new FormatException(
                        "이미 제거된 유닛에 원본 장비 실물이 저장됐습니다.");
            // Historical receipt/revision owner IDs may legitimately refer
            // to a sold or fused character: those are replay tombstones.
        }


        // One in-flight CDh pulse per active Maria manual owner.
        // Unknown Action callbacks still fail closed in CombatTimedEffectQueue.
        public static void ValidateMariaPulseState(SourceCombatRuntimeCheckpoint state)
        {
            ValidateManualOwnerState(state);
            CombatTimedEffectQueue.ValidateState(state.Timed);
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in state.Actors)
                actors.Add(actor.InstanceId, actor);
            var rows = new Dictionary<long, SourceCombatManualOwnerCheckpoint>();
            foreach (var row in state.ManualOwners)
                rows.Add(row.InstanceId, row);
            var found = new HashSet<long>();
            var consumedTokens = new HashSet<long>();
            foreach (var row in state.ManualOwners)
                if (row.MariaToken > 0 &&
                    !consumedTokens.Add(row.MariaToken))
                    throw new FormatException(
                        "마리아 반복 기술 토큰이 다른 유닛과 중복됐습니다.");
            foreach (var pending in state.Timed.Pending)
            {
                if (pending.Kind != SourceMariaPulseKind) continue;
                long id, token, lane;
                int tick;
                bool story;
                SourceCombatManualOwnerCheckpoint row;
                SourceCombatActorCheckpoint actor;
                if (!state.HasSourceModelBasicAttacks ||
                    !TryReadSourceMariaPulse(pending.Payload, out id,
                        out token, out story, out lane, out tick) ||
                    pending.CasterId !=
                        id.ToString(CultureInfo.InvariantCulture) ||
                    pending.TargetId != SourceMariaPulseTarget(id, token, tick) ||
                    !rows.TryGetValue(id, out row) ||
                    !actors.TryGetValue(id, out actor) ||
                    row.MariaToken != token || row.MariaStage < 0 ||
                    actor.CatalogId != "D-01" ||
                    actor.SourceLaneRevision != lane ||
                    actor.StoryDeployed != story ||
                    !found.Add(id) || pending.At <= state.Time ||
                    pending.At - state.Time > 5.000001)
                    throw new FormatException(
                        "마리아 CDh 반복 타격의 소유자·시각·순번이 잘못됐습니다.");
            }
            foreach (var row in state.ManualOwners)
                if ((row.MariaToken > 0) != found.Contains(row.InstanceId))
                    throw new FormatException(
                        "마리아 반복 효과의 다음 예약이 누락됐습니다.");
        }

        // Typed A0EA autonomous attacks must have exactly one owning real
        // Kuma, an unexpired native 0.2s clock, and the original payload.
        // Unknown/anonymous kinds remain rejected by the timed queue itself.
        public static void ValidateKumaAutoMissileState(
            SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 ||
                state.Actors == null || state.Timed == null)
                throw new FormatException("쿠마 피닉스 파이어 저장 상태가 없습니다.");
            CombatTimedEffectQueue.ValidateState(state.Timed);
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in state.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("쿠마 예약 소유자 목록이 중복됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var scheduled = new HashSet<long>();
            foreach (var pending in state.Timed.Pending)
            {
                if (pending.Kind != KumaAutoMissileQueueKind) continue;
                long owner;
                SourceCombatActorCheckpoint actor;
                if (!state.HasSourceModelBasicAttacks ||
                    pending.Payload != KumaAutoMissileQueuePayload ||
                    !long.TryParse(pending.CasterId,
                        NumberStyles.None, CultureInfo.InvariantCulture,
                        out owner) ||
                    pending.CasterId != owner.ToString(CultureInfo.InvariantCulture) ||
                    !actors.TryGetValue(owner, out actor) ||
                    actor.CatalogId != "S-28" ||
                    pending.TargetId != "practice-kuma-phoenix:" +
                        pending.CasterId ||
                    !scheduled.Add(owner) ||
                    pending.At < state.Time ||
                    pending.At - state.Time > KumaNative.CooldownSeconds + 0.000001)
                    throw new FormatException(
                        "쿠마 A0EA 발사 예약이 중복되거나 원본 시계가 아닙니다.");
            }
        }

        // Source-model transient state has no cast/reward side effects.
        // A participating enemy must still be alive and in the line lane;
        // zero-duration Nami aura leases must be backed by a live zone.
        public static void ValidateSourceTransientState(
            SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 ||
                state.NamiZones == null || state.NamiNextSequence < 0 ||
                state.MoriaParticipation == null ||
                state.Enemies == null || state.Actors == null)
                throw new FormatException("전투 임시 효과 저장 형식이 불완전합니다.");
            MoriaParticipationLedger.ValidateState(state.MoriaParticipation);
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in state.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("전투 임시 효과 소유자 ID가 잘못됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var enemyKinds = new Dictionary<string, CharacterEnemyKind>(
                StringComparer.Ordinal);
            foreach (var enemy in state.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    enemyKinds.ContainsKey(enemy.EnemyId))
                    throw new FormatException("전투 임시 효과 대상이 잘못됐습니다.");
                else enemyKinds.Add(enemy.EnemyId, enemy.Kind);
            var contributed = new HashSet<string>(
                state.MoriaParticipation.Participated, StringComparer.Ordinal);
            foreach (string enemyId in contributed)
            {
                CharacterEnemyKind kind;
                if (!enemyKinds.TryGetValue(enemyId, out kind) ||
                    kind != CharacterEnemyKind.Line)
                    throw new FormatException("모리아 기여 대상 라인 적이 존재하지 않습니다.");
            }
            var withOwners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in state.MoriaParticipation.Owners)
            {
                withOwners.Add(row.EnemyId);
                foreach (long id in row.OwnerIds)
                {
                    SourceCombatActorCheckpoint actor;
                    if (!state.HasSourceModelBasicAttacks ||
                        !actors.TryGetValue(id, out actor) ||
                        actor.CatalogId != SpecialGradeMobilePolicy.Moria ||
                        actor.StoryDeployed || actor.PoneglyphChanneling ||
                        !string.IsNullOrEmpty(actor.SharedObjectiveEnemyId))
                        throw new FormatException(
                            "모리아 기여 기록에 유효하지 않은 소유 유닛이 있습니다.");
                }
            }
            if (state.HasSourceModelBasicAttacks &&
                !contributed.SetEquals(withOwners))
                throw new FormatException("모리아 처치 기여 소유자가 누락됐습니다.");
            var zoneSources = new HashSet<string>(StringComparer.Ordinal);
            const string prefix = "practice-nami:A089:";
            foreach (var zone in state.NamiZones)
            {
                long sequence;
                if (!state.HasSourceModelBasicAttacks || zone == null ||
                    string.IsNullOrEmpty(zone.Source) ||
                    !zone.Source.StartsWith(prefix, StringComparison.Ordinal) ||
                    !long.TryParse(zone.Source.Substring(prefix.Length),
                        NumberStyles.None, CultureInfo.InvariantCulture,
                        out sequence) || sequence <= 0 ||
                    sequence > state.NamiNextSequence ||
                    zone.Source != prefix + sequence.ToString(CultureInfo.InvariantCulture) ||
                    !zoneSources.Add(zone.Source) ||
                    double.IsNaN(zone.X) || double.IsInfinity(zone.X) ||
                    double.IsNaN(zone.Y) || double.IsInfinity(zone.Y) ||
                    double.IsNaN(zone.ExpiresAt) ||
                    double.IsInfinity(zone.ExpiresAt) ||
                    zone.ExpiresAt <= state.Time ||
                    zone.ExpiresAt - state.Time >
                        SourceModelLegacyNativeFollowupCatalog.NamiAftershockDurationSeconds
                        + 0.000001)
                    throw new FormatException(
                        "나미 여진의 위치·기원·남은 시간이 잘못됐습니다.");
            }
            if (!state.HasSourceModelBasicAttacks && state.NamiNextSequence != 0)
                throw new FormatException("원본 모드가 아닌 나미 여진 순번입니다.");
            foreach (var enemy in state.Enemies)
                if (enemy.Status?.Effects != null)
                    foreach (var effect in enemy.Status.Effects)
                        if (effect != null && effect.SourceId != null &&
                            effect.SourceId.StartsWith(prefix,
                                StringComparison.Ordinal) &&
                            !zoneSources.Contains(effect.SourceId))
                            throw new FormatException(
                                "원본 영역이 없는 나미 이동속도 오라가 저장됐습니다.");
        }

        private void RejectNonRestorableSourceSkillState()
        {
            if (!UsesSourceModelBasicAttacks) return;
            // Non-equipment summons and transform callbacks still require
            // independent reconstruction; never silently erase.
            if (sourceModelBonneyAge.Count != 0 ||
                sourceModelKingExecutionUsed.Count != 0 ||
                sourceModelMarcoVerifiedForm != null)
                throw new NotSupportedException(
                    "실물 소환/임시 변신/외부 형태 증거가 저장되지 않아 전투 초안을 거부합니다.");
            int covered = 0;
            foreach (var summon in sourceTemporarySummons)
                if (summon.HasIndependentCombat)
                {
                    SourceIndependentAttacker independent;
                    if (!sourceIndependentAttackers.TryGetValue(
                            SummonAttackKey(summon.Id), out independent) ||
                        independent.Rawcode != summon.SourceUnitRawcode ||
                        independent.OwnerInstanceId != summon.OwnerId ||
                        independent.ExpiresAt != summon.ExpiresAt ||
                        independent.StoryLane != summon.StoryLane ||
                        independent.X != summon.SpawnX || independent.Y != summon.SpawnY ||
                        independent.ModelShadowZombie || independent.ModelBlackArrowEffect ||
                        independent.RenderAsTransientProjectile ||
                        independent.AttackOnceTargetId != null ||
                        independent.SatelliteRegenBeginsAt != 0 ||
                        independent.SatelliteRegenEndsAt != 0 ||
                        independent.SatelliteRegenPerSecond != 0 ||
                        independent.MaximumMana != 0 || independent.Mana != 0 ||
                        independent.SourceManaRegenPerSecond != 0 ||
                        independent.ShadowInheritedSources.Count != 0)
                        throw new NotSupportedException(
                            "소환수의 원본 독립 공격자 부수상태를 복원할 수 없습니다.");
                    covered++;
                }
            if (covered != sourceIndependentAttackers.Count || sourceSatellites.Count != 0)
                throw new NotSupportedException(
                    "구매 위성·기타 독립 공격자는 별도의 복원 장부가 필요합니다.");
            foreach (var form in sourceModelTenthForms.Values)
                if (form.Form != "awakening" && form.Form != "forest")
                    throw new NotSupportedException(
                        "미확인 원본 네이티브 변신은 안전하게 복원할 수 없습니다.");
        }

        private void RestoreSourceSidecars(SourceCombatRuntimeCheckpoint state)
        {
            ValidateSourceSidecars(state);
            ValidateSourceTransientState(state);
            if (sourceTemporarySummons.Count != 0 || sourceNextSummonId != 0 ||
                sourceIndependentAttackers.Count != 0 ||
                sourceModelTenthForms.Count != 0 || sourceModelForestNutrients.Count != 0 ||
                sourceModelBulletRokushikiUntil.Count != 0 ||
                sourceModelBulletRokushikiLane.Count != 0)
                throw new InvalidOperationException("원본 소환·변신은 빈 그래프에만 복원합니다.");
            foreach (var a in state.Actors)
                if (a.SourceLaneRevision > 0)
                    sourceModelStoryLaneRevisions.Add(a.InstanceId, a.SourceLaneRevision);
            foreach (var f in state.Forms)
                sourceModelTenthForms.Add(f.OwnerId, new SourceModelTenthFormState {
                    Form = f.Form, ExpiresAt = f.ExpiresAt,
                    StoryLane = f.StoryLane, LaneRevision = f.LaneRevision
                });
            foreach (var f in state.Forest)
                sourceModelForestNutrients.Add(f.OwnerId, f.Nutrients);
            // Existing actor IDs and their lane revisions were validated
            // before any state was introduced. No proc or skill is re-fired.
            foreach (var stance in state.BulletStances)
            {
                sourceModelBulletRokushikiUntil.Add(
                    stance.OwnerId, stance.ExpiresAt);
                sourceModelBulletRokushikiLane.Add(
                    stance.OwnerId, stance.LaneRevision);
            }
            foreach (var s in state.Summons)
            {
                var summoned = new SourceModelSummonState(s.Id, s.OwnerId,
                    s.SkillName, s.Name, s.ExpiresAt, s.StoryLane,
                    s.HasIndependentCombat, s.SourceUnitRawcode,
                    s.HasUnresolvedOriginalLootAbility, s.SpawnX, s.SpawnY,
                    s.LaneRevision, s.IsVirtualAttackEffect);
                if (s.HasIndependentCombat)
                {
                    string key = SummonAttackKey(s.Id);
                    RegisterSourceIndependentAttack(key, s.SourceUnitRawcode,
                        s.StoryLane, s.SpawnX, s.SpawnY, s.OwnerId,
                        s.ExpiresAt, characters[s.OwnerId].Profile);
                    sourceIndependentAttackers[key].Blocked = s.AttackBlocked;
                    unitStatuses[key] = CombatStatusEngine.Restore(s.AttackStatus);
                }
                sourceTemporarySummons.Add(summoned);
            }
            sourceNextSummonId = state.NextSummonId;
            if (sourceNamiZones.Count != 0 || sourceNamiAuraKeys.Count != 0 ||
                sourceFollowupSequence != 0 || moriaParticipation.HasPending)
                throw new InvalidOperationException(
                    "나미·모리아 임시 상태는 비어 있는 월드에서만 복구합니다.");
            sourceFollowupSequence = state.NamiNextSequence;
            foreach (var zone in state.NamiZones)
            {
                sourceNamiZones.Add(new PracticeNamiZone {
                    Source = zone.Source, X = zone.X, Y = zone.Y,
                    Story = zone.Story, ExpiresAt = zone.ExpiresAt
                });
                // Restored enemy statuses already have these aura leases.
                // The first live refresh must REMOVE them before reapplying.
                sourceNamiAuraKeys.Add(zone.Source);
            }
            moriaParticipation.RestoreState(state.MoriaParticipation);
            RestoreSourceNativeDelayedOwners(state.NativeDelayed);
            RestoreSourceTemporaryOwners(state.TemporaryOwners);
        }

        private void RestoreManualOwners(SourceCombatRuntimeCheckpoint state)
        {
            ValidateManualOwnerState(state);
            if (manualMariaMode.Count != 0 || manualMariaReady.Count != 0 ||
                manualMariaToken.Count != 0 || manualMariaNextToken != 0 ||
                sourceModelMarcoManualForms.Count != 0 ||
                sourceModelMarcoTransformReadyAt.Count != 0)
                throw new InvalidOperationException(
                    "수동 스킬 상태는 초기화된 복원 전투에서만 재구성할 수 있습니다.");
            // Validation is complete before changing any manual state.
            manualMariaNextToken = state.MariaNextToken;
            foreach (var row in state.ManualOwners)
            {
                if (row.MariaStage >= 0)
                {
                    manualMariaMode.Add(row.InstanceId, row.MariaStage);
                    manualMariaReady.Add(row.InstanceId, row.MariaReadyAt);
                }
                if (row.MariaToken > 0)
                    manualMariaToken.Add(row.InstanceId, row.MariaToken);
                if (row.MarcoForm != null)
                {
                    sourceModelMarcoManualForms.Add(row.InstanceId, row.MarcoForm);
                    sourceModelMarcoTransformReadyAt.Add(row.InstanceId,
                        row.MarcoReadyAt);
                }
            }
        }

        // Restored GameSession/UnitRoster are constructed by the core loader.
        // This method adds only the existing enemy objects; it does not
        // re-run wave spawns, damage events, native observers or rewards.
        // Invoke on a NEW unpublished runtime, then restore actors/attacks,
        // wave ledger and all missing owner systems before exposing gameplay.
        public void RestoreEnemyWorldDraft(SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.Version != 4 || state.Enemies == null ||
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


        // The three external lanes each retain the SAME real owned actor.
        // Source story movement, active Poneglyph session and native shared
        // objective history are independent owners; a saved boolean alone
        // must never create new cast, movement, mission or reward events.
        public static void ValidateExternalLaneState(
            SourceCombatRuntimeCheckpoint state,
            UnitRosterState roster,
            Original323PoneglyphState poneglyph = null,
            Original323SharedObjectiveWorldState sharedWorld = null)
        {
            if (state == null || state.Version != 4 ||
                state.Actors == null || state.Enemies == null ||
                state.SharedDeployments == null ||
                state.CreepDeployments == null ||
                roster == null || roster.Units == null)
                throw new FormatException(
                    "스토리·해독·공유 출전 복원 자료가 불완전합니다.");
            if (sharedWorld != null)
                Original323SharedObjectiveWorld.ValidateState(
                    sharedWorld, state, poneglyph);
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var row in state.Actors)
                if (row == null || row.InstanceId <= 0 ||
                    actors.ContainsKey(row.InstanceId))
                    throw new FormatException("외부 전장 유닛 신원이 잘못됐습니다.");
                else actors.Add(row.InstanceId, row);
            var positions = new Dictionary<long, UnitRosterEntryState>();
            foreach (var unit in roster.Units)
                if (unit == null || unit.InstanceId <= 0 ||
                    positions.ContainsKey(unit.InstanceId))
                    throw new FormatException("외부 전장 로스터 ID가 중복됐습니다.");
                else positions.Add(unit.InstanceId, unit);
            var creeps = new HashSet<long>();
            foreach (var row in state.CreepDeployments)
                if (row == null || !creeps.Add(row.OwnerId))
                    throw new FormatException("다중 크립 출전 유닛입니다.");
            var targets = new Dictionary<string, SourceCombatEnemyCheckpoint>(
                StringComparer.Ordinal);
            foreach (var enemy in state.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    targets.ContainsKey(enemy.EnemyId))
                    throw new FormatException("외부 목표 적의 ID가 잘못됐습니다.");
                else targets.Add(enemy.EnemyId, enemy);
            var sharedRows = new HashSet<long>();
            foreach (var row in state.SharedDeployments)
            {
                SourceCombatActorCheckpoint actor;
                UnitRosterEntryState unit;
                SourceCombatEnemyCheckpoint enemy;
                bool aliveInSource = false;
                if (sharedWorld != null)
                    foreach (var record in sharedWorld.Encounters)
                        if (record.EnemyId == row?.EnemyId &&
                            record.State == Original323SharedObjectiveState.Alive)
                            aliveInSource = true;
                if (row == null || row.OwnerId <= 0 ||
                    !sharedRows.Add(row.OwnerId) ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    !positions.TryGetValue(row.OwnerId, out unit) ||
                    actor.StoryDeployed || actor.PoneglyphChanneling ||
                    creeps.Contains(row.OwnerId) ||
                    actor.SharedObjectiveEnemyId != row.EnemyId ||
                    !targets.TryGetValue(row.EnemyId ?? "", out enemy) ||
                    (enemy.Kind != CharacterEnemyKind.SeaKing &&
                     enemy.Kind != CharacterEnemyKind.Challenge) ||
                    !aliveInSource ||
                    double.IsNaN(row.ReturnX) || double.IsInfinity(row.ReturnX) ||
                    double.IsNaN(row.ReturnY) || double.IsInfinity(row.ReturnY) ||
                    double.IsNaN(row.PositionX) || double.IsInfinity(row.PositionX) ||
                    double.IsNaN(row.PositionY) || double.IsInfinity(row.PositionY) ||
                    unit.X != row.PositionX || unit.Y != row.PositionY ||
                    unit.X != enemy.X || unit.Y != enemy.Y)
                    throw new FormatException(
                        "공유 목표 출전 유닛의 원본 소유권·좌표가 잘못됐습니다.");
            }
            long decoder = 0;
            foreach (var actor in state.Actors)
            {
                UnitRosterEntryState owned;
                if (!positions.TryGetValue(actor.InstanceId, out owned) ||
                    (actor.StoryDeployed && (actor.PoneglyphChanneling ||
                        actor.SharedObjectiveEnemyId != null ||
                        creeps.Contains(actor.InstanceId))) ||
                    (actor.SharedObjectiveEnemyId != null &&
                        !sharedRows.Contains(actor.InstanceId)) ||
                    (actor.StoryDeployed &&
                        (!state.HasSourceModelBasicAttacks ||
                         owned.X !=
                            Ordr.Mobile.Mobile.Original323SoloStoryMovement.SoloStoryX ||
                         owned.Y !=
                            Ordr.Mobile.Mobile.Original323SoloStoryMovement.SoloStoryY)) ||
                    (actor.PoneglyphChanneling &&
                        (decoder != 0 || creeps.Contains(actor.InstanceId) ||
                         actor.SharedObjectiveEnemyId != null ||
                         actor.StoryDeployed)))
                    throw new FormatException("외부 전장에 유닛이 중복 배치됐습니다.");
                if (actor.PoneglyphChanneling) decoder = actor.InstanceId;
            }
            long savedDecoder = poneglyph != null && poneglyph.HasActiveUnit ?
                poneglyph.ActiveUnitId : 0;
            if (decoder != savedDecoder)
                throw new FormatException(
                    "포네그리프 해독 유닛과 전투 정지 장부가 다릅니다.");
        }

        private void RestoreExternalLaneOwners(SourceCombatRuntimeCheckpoint state)
        {
            var storyOwners = new List<long>();
            foreach (var actor in state.Actors)
            {
                if (actor.StoryDeployed) storyOwners.Add(actor.InstanceId);
                if (actor.PoneglyphChanneling)
                    originalPoneglyphChanneling.Add(actor.InstanceId);
            }
            storyOwners.Sort();
            if (storyOwners.Count > 0)
            {
                var story = storyDispatch as
                    Ordr.Mobile.Mobile.IOriginal323StoryDispatchRestorePort;
                if (story == null || !story.IsAttachedTo(this))
                    throw new NotSupportedException(
                        "현재 비공개 전투의 원본 스토리 이동 증빙이 없습니다.");
                story.RestoreCommittedDeployments(storyOwners);
                foreach (long id in storyOwners)
                    if (!IsStoryDeployed(id))
                        throw new FormatException(
                            "스토리 파견 소유권이 재연결되지 않았습니다.");
            }
            foreach (var row in state.SharedDeployments)
                sharedObjectiveDeployed.Add(row.OwnerId,
                    new SharedObjectiveDeployment {
                        EnemyId = row.EnemyId, ReturnX = row.ReturnX,
                        ReturnY = row.ReturnY
                    });
        }

        private sealed class StagedRestoredActor
        {
            public CharacterInstance Actor;
            public CombatStatusEngine Status;
            public OriginalVerifiedAttackBinding Attack;
            public bool SourceManagedResources;
        }

        // Rehydrate real roster IDs without invoking SpawnCharacter(): a
        // spawn would allocate another ID, refresh aura state, roll extra
        // source effects and potentially re-grant mission/skill rewards.
        // Only unassigned main-lane actors can be restored here until story,
        // Poneglyph and shared-objective owners have their own restore ports.
        // A rejected reconstruction must be discarded, never published.
        public void RestoreActorWorldDraft(SourceCombatRuntimeCheckpoint state,
            Original323PoneglyphState poneglyph = null,
            Original323SharedObjectiveWorldState sharedWorld = null)
        {
            if (state == null || state.Version != 4 || state.Actors == null ||
                state.Attacks == null || state.Timed == null ||
                game.Phase != SessionPhase.Running || characters.Count != 0 ||
                unitStatuses.Count != 0 ||
                roster.Count < state.Actors.Count ||
                roster.Count > state.Actors.Count + 1 ||
                (sourceModelEquippedItems != null) != (state.EquippedItems != null) ||
                processingStep || pendingStepSeconds != 0 ||
                scheduler.Time != 0 || effectExecutor.Time != 0 ||
                UsesSourceModelBasicAttacks != state.HasSourceModelBasicAttacks ||
                (sourceModelMainLane != null) != state.MobileMainLaneEnabled)
                throw new InvalidOperationException("복원된 로스터·전투 모드에 새 전투 엔진이 필요합니다.");
            ValidateManualOwnerState(state);
            ValidateSourceSidecars(state);
            ValidateEquipmentState(state);
            ValidateSourceTransientState(state);
            ValidateOriginalDummyState(state);
            ValidateSourceNativeDelayedState(state);
            ValidateSourceTemporaryOwners(state);
            ValidateMariaPulseState(state);
            ValidateKumaAutoMissileState(state);
            ValidateExternalLaneState(state, roster.CaptureState(),
                poneglyph, sharedWorld);
            if (state.Actors.Exists(actor => actor.StoryDeployed))
            {
                var storyPort = storyDispatch as
                    Ordr.Mobile.Mobile.IOriginal323StoryDispatchRestorePort;
                if (storyPort == null || !storyPort.IsAttachedTo(this))
                    throw new NotSupportedException(
                        "현재 비공개 전투의 원본 스토리 파견 공급자가 없습니다.");
            }
            var staged = new List<StagedRestoredActor>();
            var identifiers = new HashSet<long>();
            foreach (var record in state.Actors)
            {
                if (record == null || record.InstanceId <= 0 ||
                    !identifiers.Add(record.InstanceId) ||
                    string.IsNullOrWhiteSpace(record.CatalogId) ||
                    record.Abilities == null || record.Status == null)
                    throw new FormatException("저장된 실제 캐릭터의 ID/상태가 잘못됐습니다.");
                UnitState owned;
                try { owned = roster.Get(record.InstanceId); }
                catch (KeyNotFoundException)
                {
                    throw new FormatException("공유 로스터에 저장된 유닛 ID가 없습니다.");
                }
                if (owned.UnitId != record.CatalogId)
                    throw new FormatException("저장된 캐릭터 신원이 로스터와 다릅니다.");
                var catalog = units.Require(record.CatalogId);
                if (catalog.IsAlternateForm)
                    throw new FormatException("변신 폼을 독립 유닛으로 저장할 수 없습니다.");
                var profile = profiles.Find(record.CatalogId);
                var abilities = new CharacterActiveAbilities(profile);
                abilities.RestoreState(record.Abilities);
                var status = CombatStatusEngine.Restore(record.Status);
                var actor = new CharacterInstance {
                    Id = record.InstanceId, Catalog = catalog,
                    Profile = profile, Abilities = abilities,
                    AttackBlockedByMissingFormula = record.AttackBlocked,
                    DeferredAttackReason = record.DeferredAttackReason,
                    AttackEvidence = record.AttackEvidence
                };
                OriginalVerifiedAttackBinding attack = null;
                if (record.AttackReady)
                {
                    if (verifiedAttackProvider == null ||
                        !verifiedAttackProvider.TryResolve(catalog, profile, out attack) ||
                        attack == null ||
                        (record.AttackEvidence != null &&
                         record.AttackEvidence != attack.SourceEvidence))
                        throw new NotSupportedException(
                            "저장된 평타 공격 스펙을 원본에서 다시 검증할 수 없습니다.");
                    var mapping = CharacterCombatMapping.Build(catalog.Id,
                        UsesSourceModelBasicAttacks ? null : profile,
                        attack.Stats, attack.OriginalUnitRawcode,
                        UsesSourceModelBasicAttacks ? null : specialTriggerMapping,
                        UsesSourceModelBasicAttacks ? null : uncommonAbilities,
                        UsesSourceModelBasicAttacks ? null : uncommonProcWorld);
                    actor.Mapping = mapping;
                    actor.AttackReady = true;
                    actor.BasicTargetFilter = verifiedAttackProvider as
                        IOriginalVerifiedBasicAttackTargetFilter;
                }
                else if (record.SourceManagedResources)
                    throw new FormatException("원본 등록되지 않은 유닛의 자원 관리가 저장됐습니다.");
                if (record.SourceManagedResources &&
                    !CanRebindRestoredSourceModelResources(actor))
                    throw new FormatException("원본 HP/MP 자원 관리 정보를 재연결할 수 없습니다.");
                staged.Add(new StagedRestoredActor {
                    Actor = actor, Status = status, Attack = attack,
                    SourceManagedResources = record.SourceManagedResources
                });
            }
            // e0HP belongs to the evolution service, not the combat registry.
            // A valid world checkpoint also cross-checks its evolution receipt.
            int externalEvolution = 0;
            foreach (var unit in roster.CaptureState().Units)
                if (!identifiers.Contains(unit.InstanceId))
                {
                    if (unit.UnitId != Original323SoloStartProfile.EvolutionRawcode ||
                        ++externalEvolution > 1)
                        throw new FormatException(
                            "전투 명단에 없는 유닛은 진화체 1기만 허용합니다.");
                }
            if (staged.Count + externalEvolution != roster.Count)
                throw new FormatException("전투와 진화체의 소유자 수가 일치하지 않습니다.");
            // Every actor is valid before creating any registration entries.
            foreach (var item in staged)
            {
                var actor = item.Actor;
                characters.Add(actor.Id, actor);
                unitStatuses.Add(InstanceKey(actor.Id), item.Status);
                if (item.Attack != null)
                    attackPipeline.RegisterUnit(InstanceKey(actor.Id),
                        actor.Mapping.AttackSpec, item.Attack.FirstAttackDelaySeconds);
                if (item.SourceManagedResources)
                    RebindRestoredSourceModelResources(actor);
            }
            // Rebind the three exclusive external lanes from previously
            // committed source states, WITHOUT movement, cast, or reward.
            RestoreExternalLaneOwners(state);
            // Recreate exact summon attack identities before loading the
            // saved next-hit clocks and per-attacker event sequences.
            RestoreSourceSidecars(state);
            RestoreManualOwners(state);
            attackPipeline.RestoreState(state.Attacks);
            effectExecutor.RestoreTimedState(state.Timed,
                actorId => {
                    long id;
                    CharacterInstance actor;
                    return long.TryParse(actorId, out id) &&
                        characters.TryGetValue(id, out actor) ? actor.Profile : null;
                }, enemyId => {
                    SpatialCombatEnemy live;
                    return enemies.TryGetValue(enemyId, out live) ? live : null;
                }, RestoreSourceModelTimedEffect);
            if (scheduler.Time != state.Time || effectExecutor.Time != state.Time ||
                triggers.Time != state.Time)
                throw new FormatException("복원된 평타·스킬·효과 시계가 맞지 않습니다.");
            if (sourceModelEquippedItems != null)
                sourceModelEquippedItems.RestoreState(state.EquippedItems);
        }

        // Captures real owner identities, resources, status clocks and all
        // registered basic/skill timers. Unknown delayed lambda state must
        // fail before anyone can accidentally publish a partial checkpoint.
        public SourceCombatRuntimeCheckpoint CaptureRuntimeDraft()
        {
            // An in-progress native mobility/form cast is still opaque:
            // never replay a source payment after loading the save.
            if (creepPreparations.Count != 0)
                throw new NotSupportedException(
                    "원본 크립 이동·변신 시전 완료 전에는 저장할 수 없습니다.");
            if (game.Phase != SessionPhase.Running ||
                activeOriginalFusionSwap != null || processingStep ||
                pendingStepSeconds != 0 ||
                scheduler.Time != triggers.Time ||
                scheduler.Time != effectExecutor.Time)
                throw new InvalidOperationException(
                    "불완전한 전투 스텝·조합·불일치 시계는 저장할 수 없습니다.");
            // The pending outcome consumed its RNG already; losing the
            // uncommitted result during save/load would permit free rerolls.
            if (pendingViviUpgrades.Count != 0)
                throw new NotSupportedException(
                    "미확정 비비 강화 추첨은 완료 전 저장할 수 없습니다.");
            RejectNonRestorableSourceSkillState();
            var draft = new SourceCombatRuntimeCheckpoint {
                Time = scheduler.Time, NextSummonId = sourceNextSummonId,
                MariaNextToken = manualMariaNextToken,
                NamiNextSequence = sourceFollowupSequence,
                MoriaParticipation = moriaParticipation.CaptureState(),
                HasSourceModelBasicAttacks = UsesSourceModelBasicAttacks,
                MobileMainLaneEnabled = sourceModelMainLane != null,
                EquippedItems = sourceModelEquippedItems == null ? null :
                    sourceModelEquippedItems.CaptureState(),
                Attacks = attackPipeline.CaptureState(),
                Timed = effectExecutor.CaptureTimedState(),
                OriginalNativeDummy = CaptureOriginalDummyState(),
                NativeDelayed = CaptureSourceNativeDelayedOwners(),
                TemporaryOwners = CaptureSourceTemporaryOwners()
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
                    AttackEvidence = actor.AttackEvidence,
                    DeferredAttackReason = actor.DeferredAttackReason,
                    SourceManagedResources = sourceModelManagedResourceRows.ContainsKey(id),
                    SourceLaneRevision = SourceModelLaneRevision(id),
                    Abilities = actor.Abilities.CaptureState(),
                    Status = status.CaptureState()
                });
                if (IsStoryDeployed(id) &&
                    !(storyDispatch is
                        Ordr.Mobile.Mobile.IOriginal323StoryDispatchRestorePort))
                    throw new NotSupportedException(
                        "스토리 파견 복원 불가능한 외부 이동 엔진입니다.");
                int mariaStage;
                long mariaToken;
                double mariaReady, marcoReady;
                string marcoForm;
                draft.ManualOwners.Add(new SourceCombatManualOwnerCheckpoint {
                    InstanceId = id,
                    MariaStage = manualMariaMode.TryGetValue(id, out mariaStage)
                        ? mariaStage : -1,
                    MariaReadyAt = manualMariaReady.TryGetValue(id, out mariaReady)
                        ? mariaReady : 0,
                    MariaToken = manualMariaToken.TryGetValue(id, out mariaToken)
                        ? mariaToken : 0,
                    MarcoForm = sourceModelMarcoManualForms.TryGetValue(
                        id, out marcoForm) ? marcoForm : null,
                    MarcoReadyAt = sourceModelMarcoTransformReadyAt.TryGetValue(
                        id, out marcoReady) ? marcoReady : 0
                });
            }
            ValidateManualOwnerState(draft);
            ValidateMariaPulseState(draft);
            ValidateKumaAutoMissileState(draft);
            ValidateEquipmentState(draft);
            foreach (var row in sourceModelTenthForms)
                draft.Forms.Add(new SourceCombatFormCheckpoint {
                    OwnerId = row.Key, Form = row.Value.Form,
                    ExpiresAt = row.Value.ExpiresAt,
                    StoryLane = row.Value.StoryLane,
                    LaneRevision = row.Value.LaneRevision
                });
            draft.Forms.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            foreach (var row in sourceModelForestNutrients)
                if (row.Value > 0)
                    draft.Forest.Add(new SourceCombatForestCheckpoint {
                        OwnerId = row.Key, Nutrients = row.Value
                    });
            draft.Forest.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            if (sourceModelBulletRokushikiUntil.Count !=
                sourceModelBulletRokushikiLane.Count)
                throw new InvalidOperationException(
                    "불릿 육식 체의 지속시간·전장 장부가 일치하지 않습니다.");
            foreach (var row in sourceModelBulletRokushikiUntil)
            {
                long lane;
                if (!sourceModelBulletRokushikiLane.TryGetValue(row.Key, out lane))
                    throw new InvalidOperationException(
                        "불릿 육식 체의 원본 전장 리비전이 없습니다.");
                if (row.Value <= draft.Time) continue;
                draft.BulletStances.Add(new SourceCombatBulletStanceCheckpoint {
                    OwnerId = row.Key, LaneRevision = lane,
                    ExpiresAt = row.Value
                });
            }
            draft.BulletStances.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            foreach (var summon in sourceTemporarySummons)
            {
                SourceIndependentAttacker independent = null;
                if (summon.HasIndependentCombat &&
                    !sourceIndependentAttackers.TryGetValue(
                        SummonAttackKey(summon.Id), out independent))
                    throw new InvalidOperationException("독립 소환 공격자가 없습니다.");
                CombatStatusEngine status = null;
                if (independent != null &&
                    !unitStatuses.TryGetValue(independent.Key, out status))
                    throw new InvalidOperationException("소환 공격자 버프 원장이 없습니다.");
                draft.Summons.Add(new SourceCombatSummonCheckpoint {
                    Id = summon.Id, OwnerId = summon.OwnerId,
                    SkillName = summon.SkillName, Name = summon.Name,
                    ExpiresAt = summon.ExpiresAt, StoryLane = summon.StoryLane,
                    LaneRevision = summon.LaneRevision,
                    HasIndependentCombat = summon.HasIndependentCombat,
                    IsVirtualAttackEffect = summon.IsVirtualAttackEffect,
                    SourceUnitRawcode = summon.SourceUnitRawcode,
                    HasUnresolvedOriginalLootAbility =
                        summon.HasUnresolvedOriginalLootAbility,
                    SpawnX = summon.SpawnX, SpawnY = summon.SpawnY,
                    AttackBlocked = independent != null && independent.Blocked,
                    AttackStatus = status == null ? null : status.CaptureState()
                });
            }
            draft.Summons.Sort((a, b) => a.Id.CompareTo(b.Id));
            ValidateSourceSidecars(draft);
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
            foreach (var zone in sourceNamiZones)
                if (zone.ExpiresAt > draft.Time)
                    draft.NamiZones.Add(new SourceCombatNamiZoneCheckpoint {
                        Source = zone.Source, X = zone.X, Y = zone.Y,
                        Story = zone.Story, ExpiresAt = zone.ExpiresAt
                    });
            draft.NamiZones.Sort((a, b) =>
                string.CompareOrdinal(a.Source, b.Source));
            ValidateSourceTransientState(draft);
            ValidateOriginalDummyState(draft);
            ValidateSourceNativeDelayedState(draft);
            ValidateSourceTemporaryOwners(draft);
            foreach (var assignment in sharedObjectiveDeployed)
            {
                var position = roster.Get(assignment.Key);
                draft.SharedDeployments.Add(
                    new SourceCombatSharedDeploymentCheckpoint {
                        OwnerId = assignment.Key,
                        EnemyId = assignment.Value.EnemyId,
                        ReturnX = assignment.Value.ReturnX,
                        ReturnY = assignment.Value.ReturnY,
                        PositionX = position.X, PositionY = position.Y
                    });
            }
            draft.SharedDeployments.Sort((a, b) =>
                a.OwnerId.CompareTo(b.OwnerId));
            CaptureCreepDeployments(draft);
            // e0HP is the original evolution world's SINGLE non-combat
            // starter, registered in the shared roster but not characters.
            // Never conflate it with a playable attacker or a phantom fusion
            // material; the core checkpoint validates its evolution receipt.
            int externalEvolution = 0;
            foreach (var entry in roster.CaptureState().Units)
                if (!characters.ContainsKey(entry.InstanceId))
                {
                    if (entry.UnitId != Original323SoloStartProfile.EvolutionRawcode ||
                        ++externalEvolution > 1)
                        throw new InvalidOperationException(
                            "공통 유닛 명단에 전투 엔진에 없는 실물이 존재합니다.");
                }
            if (draft.Actors.Count + externalEvolution != roster.Count)
                throw new InvalidOperationException(
                    "공통 유닛 명단과 전투 엔진의 실물 수가 다릅니다.");
            return draft;
        }
    }
}
