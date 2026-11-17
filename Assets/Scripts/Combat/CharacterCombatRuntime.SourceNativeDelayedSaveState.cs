using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    // Serialized identities for source-backed delayed native attacks.
    // Unlike anonymous Action closures these can be rebound against the
    // rebuilt actor, enemy and logical clock without repeating the cast.
    [Serializable]
    public sealed class SourceNativeLiquidFireRevision
    {
        public string EnemyId;
        public long Revision;
    }

    [Serializable]
    public sealed class SourceNativeDelayedOwnerCheckpoint
    {
        public int Version = 1;
        public List<SourceNativeLiquidFireRevision> LiquidFire =
            new List<SourceNativeLiquidFireRevision>();
    }

    [Serializable]
    public sealed class SourceNativeDelayedEffectPayload
    {
        public long OwnerId;
        public string EnemyId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public int Tick;
        public long Revision;
        public double CenterX;
        public double CenterY;
        public string OriginalAbilityRawcode;
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string NativeLiquidFireKind = "source-liquid-fire-v1";
        private const string NativeInfernoKind = "source-inferno-v1";
        private const string NativeSentinelFaerieKind = "source-sentinel-faerie-v1";

        private static string SerializeNativeDelayed(
            SourceNativeDelayedEffectPayload record) =>
            JsonUtility.ToJson(record);

        private static SourceNativeDelayedEffectPayload ReadNativeDelayed(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                return null;
            try
            {
                var payload =
                    JsonUtility.FromJson<SourceNativeDelayedEffectPayload>(json);
                if (payload == null || SerializeNativeDelayed(payload) != json)
                    return null;
                return payload;
            }
            catch (ArgumentException) { return null; }
        }

        private SourceNativeDelayedOwnerCheckpoint CaptureSourceNativeDelayedOwners()
        {
            var result = new SourceNativeDelayedOwnerCheckpoint();
            var ids = new List<string>(sourceLiquidFireRevision.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
                result.LiquidFire.Add(new SourceNativeLiquidFireRevision {
                    EnemyId = id, Revision = sourceLiquidFireRevision[id]
                });
            return result;
        }

        private void RestoreSourceNativeDelayedOwners(
            SourceNativeDelayedOwnerCheckpoint snapshot)
        {
            if (snapshot == null || sourceLiquidFireRevision.Count != 0)
                throw new InvalidOperationException(
                    "예약 기술의 효과 소유권은 새 전투에만 복원합니다.");
            foreach (var row in snapshot.LiquidFire)
                sourceLiquidFireRevision.Add(row.EnemyId, row.Revision);
        }

        // Fail closed on missing/wrong owners, forged timestamps, altered
        // native ability IDs, and a queued burn from the future generation.
        public static void ValidateSourceNativeDelayedState(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.NativeDelayed == null ||
                combat.NativeDelayed.Version != 1 ||
                combat.NativeDelayed.LiquidFire == null ||
                combat.NativeDelayed.LiquidFire.Count > 1000000 ||
                combat.Timed == null || combat.Timed.Pending == null ||
                combat.Actors == null || combat.Enemies == null ||
                double.IsNaN(combat.Time) || double.IsInfinity(combat.Time) ||
                combat.Time < 0)
                throw new FormatException(
                    "원본 지연 공격 소유권 장부가 불완전합니다.");
            var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
            string previous = null;
            foreach (var item in combat.NativeDelayed.LiquidFire)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.EnemyId) ||
                    item.EnemyId.Length > 256 || item.Revision <= 0 ||
                    (previous != null &&
                     StringComparer.Ordinal.Compare(previous, item.EnemyId) >= 0))
                    throw new FormatException("리퀴드 파이어 갱신 순번이 잘못됐습니다.");
                revisions.Add(item.EnemyId, item.Revision);
                previous = item.EnemyId;
            }
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var item in combat.Actors)
                if (item == null || item.InstanceId <= 0 ||
                    actors.ContainsKey(item.InstanceId))
                    throw new FormatException("지연 공격 유닛 신원이 중복됐습니다.");
                else actors.Add(item.InstanceId, item);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in combat.Enemies)
                if (item == null || string.IsNullOrWhiteSpace(item.EnemyId) ||
                    !enemies.Add(item.EnemyId))
                    throw new FormatException("지연 공격 대상 신원이 중복됐습니다.");
            foreach (var pending in combat.Timed.Pending)
            {
                if (pending == null)
                    throw new FormatException("예약 공격 항목이 없습니다.");
                bool burn = pending.Kind == NativeLiquidFireKind;
                bool inferno = pending.Kind == NativeInfernoKind;
                bool faerie = pending.Kind == NativeSentinelFaerieKind;
                if (!burn && !inferno && !faerie) continue;
                var payload = ReadNativeDelayed(pending.Payload);
                SourceCombatActorCheckpoint actor;
                if (!combat.HasSourceModelBasicAttacks || payload == null ||
                    payload.OwnerId <= 0 ||
                    !actors.TryGetValue(payload.OwnerId, out actor) ||
                    pending.CasterId != payload.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    payload.LaneRevision > actor.SourceLaneRevision ||
                    (payload.LaneRevision == actor.SourceLaneRevision &&
                     payload.StoryLane != actor.StoryDeployed) ||
                    payload.LaneRevision < 0 ||
                    double.IsNaN(payload.CastAt) ||
                    double.IsInfinity(payload.CastAt) ||
                    payload.CastAt < 0 || payload.CastAt > combat.Time ||
                    double.IsNaN(pending.At) || double.IsInfinity(pending.At) ||
                    pending.At < combat.Time ||
                    double.IsNaN(payload.CenterX) ||
                    double.IsInfinity(payload.CenterX) ||
                    double.IsNaN(payload.CenterY) ||
                    double.IsInfinity(payload.CenterY))
                    throw new FormatException(
                        "예약 공격의 시전자·전장·시계가 일치하지 않습니다.");
                string key = pending.CasterId;
                if (burn)
                {
                    long latest;
                    if (actor.CatalogId != "S-20" ||
                        string.IsNullOrWhiteSpace(payload.EnemyId) ||
                        pending.TargetId != payload.EnemyId ||
                        !enemies.Contains(payload.EnemyId) ||
                        !revisions.TryGetValue(payload.EnemyId, out latest) ||
                        payload.Revision <= 0 || payload.Revision > latest ||
                        payload.Tick < 1 || payload.Tick > 3 ||
                        payload.OriginalAbilityRawcode != null ||
                        payload.CenterX != 0 || payload.CenterY != 0 ||
                        pending.At != payload.CastAt + payload.Tick)
                        throw new FormatException(
                            "리퀴드 파이어 틱/소유 갱신 순번이 잘못됐습니다.");
                }
                else if (inferno)
                {
                    SourceModelInfernoFollowup spec;
                    if (!SourceModelLegacyNativeFollowupCatalog.TryGet(
                            actor.CatalogId, out spec) ||
                        payload.OriginalAbilityRawcode != spec.AbilityRawcode ||
                        payload.EnemyId != null || payload.Tick != 0 ||
                        payload.Revision != 0 ||
                        pending.TargetId != "practice-inferno:" + key +
                            ":" + spec.AbilityRawcode ||
                        pending.At != payload.CastAt + spec.ImpactDelaySeconds)
                        throw new FormatException(
                            "원본 인페르노 좌표/시전 예약이 잘못됐습니다.");
                }
                else
                {
                    if (actor.CatalogId != "RN-04" ||
                        payload.OriginalAbilityRawcode !=
                            SourceModelLegacyNativeFollowupCatalog.SentinelFaerieAbility ||
                        string.IsNullOrWhiteSpace(payload.EnemyId) ||
                        pending.TargetId != payload.EnemyId ||
                        !enemies.Contains(payload.EnemyId) ||
                        payload.Tick != 0 || payload.Revision != 0 ||
                        payload.CenterX != 0 || payload.CenterY != 0 ||
                        pending.At != payload.CastAt + 0.01)
                        throw new FormatException(
                            "원본 페어리 파이어의 시전 대상·시각이 잘못됐습니다.");
                }
            }
            if (!combat.HasSourceModelBasicAttacks &&
                combat.NativeDelayed.LiquidFire.Count != 0)
                throw new FormatException(
                    "원본 평타 모드가 아닌 전투에 화염 기록이 있습니다.");
        }

        private bool CanFinishNativeDelayed(SourceNativeDelayedEffectPayload state,
            CharacterInstance caster)
        {
            return caster != null &&
                CanFinishSourceModelDelayedAttack(state.OwnerId,
                    state.StoryLane, state.LaneRevision);
        }

        private Action CreateNativeLiquidFirePulse(
            SourceNativeDelayedEffectPayload state)
        {
            return () => {
                CharacterInstance actor;
                SpatialCombatEnemy target;
                long current;
                if (!characters.TryGetValue(state.OwnerId, out actor) ||
                    actor.Catalog.Id != "S-20" ||
                    !CanFinishNativeDelayed(state, actor) ||
                    !enemies.TryGetValue(state.EnemyId, out target) ||
                    !target.Enemy.IsAlive ||
                    !sourceLiquidFireRevision.TryGetValue(
                        state.EnemyId, out current) ||
                    current != state.Revision) return;
                ApplySourceScriptHits(actor, target, LiquidFireModel,
                    LiquidFireModel.OnProc);
            };
        }

        private Action CreateNativeInfernoPulse(
            SourceNativeDelayedEffectPayload state)
        {
            return () => {
                CharacterInstance actor;
                SourceModelInfernoFollowup spec;
                if (!characters.TryGetValue(state.OwnerId, out actor) ||
                    !CanFinishNativeDelayed(state, actor) ||
                    !SourceModelLegacyNativeFollowupCatalog.TryGet(
                        actor.Catalog.Id, out spec) ||
                    spec.AbilityRawcode != state.OriginalAbilityRawcode) return;
                var actorId = InstanceKey(state.OwnerId);
                foreach (var enemy in enemies.Values)
                {
                    if (!enemy.Enemy.IsAlive ||
                        !CanAttackInThisLane(actor, enemy)) continue;
                    double dx = enemy.X - state.CenterX;
                    double dy = enemy.Y - state.CenterY;
                    if (dx * dx + dy * dy > spec.Radius * spec.Radius) continue;
                    if (enemy.Kind != CharacterEnemyKind.Line)
                    {
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EffectDeferred, actorId,
                            enemy.Id, actor.Catalog.Name + " 인페르노",
                            "MODEL_ONLY " + spec.AbilityRawcode +
                            " 보스/특수 적 네이티브 기절 저항 미검증", 0));
                        continue;
                    }
                    string source = "practice-inferno:" +
                        spec.AbilityRawcode + ":" + actorId;
                    enemy.Enemy.Status.RemoveSource(source);
                    enemy.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                        1, spec.NormalStunSeconds));
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated,
                        actorId, enemy.Id, actor.Catalog.Name + " 인페르노",
                        "MODEL_ONLY " + spec.AbilityRawcode +
                        " 원본 객체 기절 " + spec.NormalStunSeconds +
                        "초 / " + spec.Radius +
                        " 범위 · 네이티브 면역 미검증", 0));
                }
            };
        }

        private Action CreateNativeSentinelFaeriePulse(
            SourceNativeDelayedEffectPayload state)
        {
            return () => {
                CharacterInstance actor;
                SpatialCombatEnemy target;
                if (!characters.TryGetValue(state.OwnerId, out actor) ||
                    actor.Catalog.Id != "RN-04" ||
                    !CanFinishNativeDelayed(state, actor) ||
                    !enemies.TryGetValue(state.EnemyId, out target) ||
                    !target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target)) return;
                string source = "practice-native:A10I";
                target.Enemy.Status.RemoveSource(source);
                target.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.ArmorReduction,
                    CombatEffectOrigin.TriggeredSkill,
                    SourceModelLegacyNativeFollowupCatalog.SentinelArmorReduction,
                    SourceModelLegacyNativeFollowupCatalog.SentinelFaerieDuration));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(state.OwnerId),
                    target.Id, "센토 이스즈 · 페어리 파이어",
                    "MODEL_ONLY e0LE/A10I: 대상 방어 -50 / 60초, 원본 시전 지연·면역 미검증", 0));
            };
        }

        private Action RestoreSourceNativeDelayed(
            string kind, string json, string casterId, string targetId)
        {
            if (kind != NativeLiquidFireKind &&
                kind != NativeInfernoKind &&
                kind != NativeSentinelFaerieKind) return null;
            var state = ReadNativeDelayed(json);
            CharacterInstance actor;
            if (state == null ||
                !characters.TryGetValue(state.OwnerId, out actor) ||
                casterId != InstanceKey(state.OwnerId))
                return null;
            // A previous-lane native point order may survive an owner move.
            // Its old callback would have become inert; retain that no-op
            // until the same due clock rather than rejecting the checkpoint.
            if (state.LaneRevision < SourceModelLaneRevision(state.OwnerId))
                return () => { };
            if (!CanFinishNativeDelayed(state, actor))
                return null;
            if (kind == NativeLiquidFireKind)
            {
                if (actor.Catalog.Id != "S-20" || targetId != state.EnemyId ||
                    !enemies.ContainsKey(targetId))
                    return null;
                return CreateNativeLiquidFirePulse(state);
            }
            if (kind == NativeInfernoKind)
            {
                SourceModelInfernoFollowup spec;
                if (!SourceModelLegacyNativeFollowupCatalog.TryGet(
                    actor.Catalog.Id, out spec) ||
                    spec.AbilityRawcode != state.OriginalAbilityRawcode ||
                    targetId != "practice-inferno:" + casterId +
                        ":" + spec.AbilityRawcode)
                    return null;
                return CreateNativeInfernoPulse(state);
            }
            if (actor.Catalog.Id != "RN-04" ||
                targetId != state.EnemyId ||
                !enemies.ContainsKey(targetId))
                return null;
            return CreateNativeSentinelFaeriePulse(state);
        }
    }
}
