using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    // Every hit is an already-decided, source-backed packet: never reroll
    // BFi, recast a native dummy or reconstruct an arbitrary Action closure.
    [Serializable]
    public sealed class SourceRemainingHitSnapshot
    {
        public SourceScriptHitShape Shape;
        public double Radius;
        public double Damage;
        public double Minimum;
        public double Maximum;
        public Ordr323AttackType Attack;
        public double TargetMaxHealthMultiplier;
        public double TargetCurrentHealthMultiplier;
        public double LineMaxHealthMultiplier;
        public double LineCurrentHealthMultiplier;
        public bool HasLineAttackOverride;
        public Ordr323AttackType LineAttackOverride;
    }

    [Serializable]
    public sealed class SourceRemainingDelayedPayload
    {
        public long OwnerId;
        public string EnemyId;
        public string CatalogId;
        public string UnitRawcode;
        public string JassFunction;
        public bool StoryLane;
        public long LaneRevision;
        public long Serial;
        public double CastAt;
        public double DelaySeconds;
        public double CastX;
        public double CastY;
        public List<SourceRemainingHitSnapshot> Hits =
            new List<SourceRemainingHitSnapshot>();
    }

    [Serializable]
    public sealed class SourceRemainingDelayedOwnerCheckpoint
    {
        public int Version = 1;
        public long NextSerial;
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string RemainingDelayedHitKind =
            "source-remaining-jass-hit-v1";

        private static bool RemainingFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static string EncodeRemainingDelayedPayload(
            SourceRemainingDelayedPayload payload) =>
            JsonUtility.ToJson(payload);

        private static SourceRemainingDelayedPayload ReadRemainingDelayedPayload(
            string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 8192)
                return null;
            try
            {
                var result = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                    payload);
                return result != null &&
                    EncodeRemainingDelayedPayload(result) == payload ?
                    result : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static SourceRemainingHitSnapshot SnapshotRemainingHit(
            SourceScriptHit hit)
        {
            if (hit == null ||
                hit.DamageType != SourceScriptDamageType.ModelDefault ||
                hit.BossCurrentHealthPercent != 0 ||
                hit.EnragedMaxHealthPercent != 0 ||
                hit.StoryOnly || hit.StoryDamage != 0 ||
                hit.RandomIntegerMinimum != 0 ||
                hit.RandomIntegerMaximum != 0 ||
                hit.BasicHitDamageMultiplier != 0 ||
                hit.Length != 0 || hit.CastRange != 0 ||
                hit.EndRadius != hit.Radius || hit.MaxTargets != 0)
                throw new NotSupportedException(
                    "아직 원본 근거가 없는 JASS 타격 필드를 저장할 수 없습니다.");
            return new SourceRemainingHitSnapshot {
                Shape = hit.Shape,
                Radius = hit.Radius,
                Damage = hit.Damage,
                Minimum = hit.Minimum,
                Maximum = hit.Maximum,
                Attack = hit.Attack,
                TargetMaxHealthMultiplier = hit.TargetMaxHealthMultiplier,
                TargetCurrentHealthMultiplier = hit.TargetCurrentHealthMultiplier,
                LineMaxHealthMultiplier = hit.LineMaxHealthMultiplier,
                LineCurrentHealthMultiplier = hit.LineCurrentHealthMultiplier,
                HasLineAttackOverride = hit.LineAttackOverride.HasValue,
                LineAttackOverride = hit.LineAttackOverride.GetValueOrDefault()
            };
        }

        private static bool ValidateRemainingHitSnapshot(
            SourceRemainingHitSnapshot hit)
        {
            if (hit == null || !Enum.IsDefined(typeof(SourceScriptHitShape),
                    hit.Shape) ||
                !Enum.IsDefined(typeof(Ordr323AttackType), hit.Attack) ||
                !Enum.IsDefined(typeof(Ordr323AttackType),
                    hit.LineAttackOverride) ||
                (hit.Shape != SourceScriptHitShape.Direct &&
                 hit.Shape != SourceScriptHitShape.Circle &&
                 hit.Shape != SourceScriptHitShape.AttackerCircle) ||
                !RemainingFinite(hit.Radius) || hit.Radius < 0 ||
                (hit.Shape == SourceScriptHitShape.Direct ?
                    hit.Radius != 0 : hit.Radius <= 0 || hit.Radius > 2500) ||
                !RemainingFinite(hit.Damage) ||
                hit.Damage < 0 || hit.Damage > 1000000000000 ||
                !RemainingFinite(hit.Minimum) ||
                !RemainingFinite(hit.Maximum) ||
                hit.Minimum <= 0 || hit.Maximum < hit.Minimum ||
                hit.Maximum > 10 ||
                !RemainingFinite(hit.TargetMaxHealthMultiplier) ||
                !RemainingFinite(hit.TargetCurrentHealthMultiplier) ||
                !RemainingFinite(hit.LineMaxHealthMultiplier) ||
                !RemainingFinite(hit.LineCurrentHealthMultiplier) ||
                hit.TargetMaxHealthMultiplier < 0 ||
                hit.TargetCurrentHealthMultiplier < 0 ||
                hit.LineMaxHealthMultiplier < 0 ||
                hit.LineCurrentHealthMultiplier < 0 ||
                hit.TargetMaxHealthMultiplier > 1 ||
                hit.TargetCurrentHealthMultiplier > 1 ||
                hit.LineMaxHealthMultiplier > 2 ||
                hit.LineCurrentHealthMultiplier > 2 ||
                (!hit.HasLineAttackOverride &&
                 hit.LineAttackOverride != default(Ordr323AttackType)))
                return false;
            return true;
        }


        // Restore only the native callback's *already decided* packet. A
        // structurally valid arbitrary damage number is not source evidence.
        private static bool SameRemainingHit(
            SourceRemainingHitSnapshot actual, SourceScriptHit expected)
        {
            if (actual == null || expected == null) return false;
            var pinned = SnapshotRemainingHit(expected);
            return actual.Shape == pinned.Shape &&
                actual.Radius == pinned.Radius &&
                actual.Damage == pinned.Damage &&
                actual.Minimum == pinned.Minimum &&
                actual.Maximum == pinned.Maximum &&
                actual.Attack == pinned.Attack &&
                actual.TargetMaxHealthMultiplier ==
                    pinned.TargetMaxHealthMultiplier &&
                actual.TargetCurrentHealthMultiplier ==
                    pinned.TargetCurrentHealthMultiplier &&
                actual.LineMaxHealthMultiplier ==
                    pinned.LineMaxHealthMultiplier &&
                actual.LineCurrentHealthMultiplier ==
                    pinned.LineCurrentHealthMultiplier &&
                actual.HasLineAttackOverride ==
                    pinned.HasLineAttackOverride &&
                actual.LineAttackOverride == pinned.LineAttackOverride;
        }

        private static bool ValidRemainingCallbackPacket(
            SourceRemainingDelayedPayload payload, CharacterEnemyKind kind)
        {
            if (payload == null || payload.Hits == null ||
                payload.Hits.Count == 0 || payload.Hits.Count > 8)
                return false;
            var hits = payload.Hits;
            switch (payload.CatalogId)
            {
                case "L-36": // BtV 10% Hancock: 2 packets, 3rd on line only.
                    if (hits.Count != 2 && hits.Count != 3 ||
                        hits.Count == 3 && kind != CharacterEnemyKind.Line ||
                        !SameRemainingHit(hits[0],
                            RemainingCircle(415, 150000, 1, 1.5)) ||
                        !SameRemainingHit(hits[1],
                            new SourceScriptHit(SourceScriptHitShape.Direct,
                                0, 0, 150000, 1, 1.5,
                                Ordr323AttackType.Spells)))
                        return false;
                    return hits.Count == 2 ||
                        SameRemainingHit(hits[2], RemainingDirect(0,
                            Ordr323AttackType.Hero,
                            lineMaxHealthMultiplier: .05));
                case "TR-01": // Bzq / Bzx: keep the witnessed STR operand.
                    if (hits.Count != 1) return false;
                    var hit = hits[0];
                    if (SameRemainingHit(hit,
                            RemainingDirect(4000000, Ordr323AttackType.Chaos)) ||
                        SameRemainingHit(hit, RemainingDirect(3000000)) ||
                        SameRemainingHit(hit, RemainingDirect(2000000)))
                        return true;
                    return hit.Damage >= 625000 &&
                        hit.Damage <= 1000000000000 &&
                        Math.Floor((hit.Damage - 625000) / 5000) ==
                            (hit.Damage - 625000) / 5000 &&
                        SameRemainingHit(hit, RemainingCircle(415, hit.Damage));
                case "MY-01": // CGY/CGZ/CGa/CGc.
                    if (hits.Count != 1) return false;
                    return SameRemainingHit(hits[0],
                        new SourceScriptHit(
                            SourceScriptHitShape.AttackerCircle, 1600, 0,
                            450000, 1, 1, Ordr323AttackType.Chaos,
                            targetMaxHealthMultiplier: .0045)) ||
                        SameRemainingHit(hits[0],
                            RemainingCircle(600, 300000)) ||
                        SameRemainingHit(hits[0],
                            RemainingCircle(600, 150000)) ||
                        SameRemainingHit(hits[0],
                            RemainingCircle(800, 1500000)) ||
                        SameRemainingHit(hits[0],
                            new SourceScriptHit(SourceScriptHitShape.Circle,
                                800, 0, 4500000, 1, 1,
                                Ordr323AttackType.Spells,
                                targetMaxHealthMultiplier: .04));
                case "L-12": // BsK 10% shadow pursuit.
                    return hits.Count == 1 &&
                        (SameRemainingHit(hits[0],
                            RemainingCircle(400, 250000)) ||
                         SameRemainingHit(hits[0], RemainingDirect(200000)));
                case "D-06": // CDz: native armor gate selects one pair.
                    if (hits.Count != 2) return false;
                    return SameRemainingHit(hits[0],
                            RemainingCircle(425, 100000)) &&
                        SameRemainingHit(hits[1],
                            RemainingDirect(200000,
                                lineCurrentHealthMultiplier: .10,
                                lineAttackOverride: Ordr323AttackType.Siege)) ||
                        SameRemainingHit(hits[0],
                            RemainingCircle(425, 360000)) &&
                        SameRemainingHit(hits[1],
                            RemainingDirect(720000,
                                lineCurrentHealthMultiplier: .36,
                                lineAttackOverride: Ordr323AttackType.Siege));
                default: return false;
            }
        }

        private static SourceScriptHit RestoreRemainingHit(
            SourceRemainingHitSnapshot hit)
        {
            return new SourceScriptHit(hit.Shape, hit.Radius, 0,
                hit.Damage, hit.Minimum, hit.Maximum, hit.Attack,
                targetMaxHealthMultiplier: hit.TargetMaxHealthMultiplier,
                targetCurrentHealthMultiplier: hit.TargetCurrentHealthMultiplier,
                lineMaxHealthMultiplier: hit.LineMaxHealthMultiplier,
                lineCurrentHealthMultiplier: hit.LineCurrentHealthMultiplier,
                lineAttackOverride: hit.HasLineAttackOverride ?
                    (Ordr323AttackType?)hit.LineAttackOverride : null);
        }

        private static bool RestorableRemainingCallback(string catalog) =>
            catalog == "L-36" || catalog == "TR-01" ||
            catalog == "MY-01" || catalog == "L-12" ||
            catalog == "D-06";

        private SourceRemainingDelayedPayload CreateRemainingDelayedPayload(
            long ownerId, string enemyId, SourceRemainingCallback callback,
            bool story, long revision, double castTime, double delay,
            long serial, double x, double y, SourceScriptHit[] hits)
        {
            if (callback == null ||
                !RestorableRemainingCallback(callback.CatalogId) ||
                hits == null || hits.Length == 0 || hits.Length > 8)
                throw new NotSupportedException(
                    "원본 예약 다단히트 명세가 지원되지 않습니다.");
            var result = new SourceRemainingDelayedPayload {
                OwnerId = ownerId, EnemyId = enemyId,
                CatalogId = callback.CatalogId,
                UnitRawcode = callback.Rawcode,
                JassFunction = callback.Jass,
                StoryLane = story, LaneRevision = revision,
                Serial = serial, CastAt = castTime,
                DelaySeconds = delay, CastX = x, CastY = y
            };
            foreach (var hit in hits)
            {
                var snapshot = SnapshotRemainingHit(hit);
                if (!ValidateRemainingHitSnapshot(snapshot))
                    throw new NotSupportedException(
                        "원본 예약 피해의 모양·수치가 재구성 불가능합니다.");
                result.Hits.Add(snapshot);
            }
            return result;
        }

        private SourceRemainingDelayedOwnerCheckpoint
            CaptureRemainingDelayedOwners() =>
            new SourceRemainingDelayedOwnerCheckpoint {
                NextSerial = remainingCallbackSerial
            };

        private void RestoreRemainingDelayedOwners(
            SourceRemainingDelayedOwnerCheckpoint state)
        {
            if (state == null || remainingCallbackSerial != 0)
                throw new InvalidOperationException(
                    "원본 JASS 예약의 순번은 새 월드에만 복원합니다.");
            remainingCallbackSerial = state.NextSerial;
        }

        private Action CreateRemainingDelayedHitCallback(
            SourceRemainingDelayedPayload payload, CharacterInstance actor,
            SpatialCombatEnemy originalTarget)
        {
            var hits = new SourceScriptHit[payload.Hits.Count];
            for (int i = 0; i < hits.Length; i++)
                hits[i] = RestoreRemainingHit(payload.Hits[i]);
            var script = RemainingHitSpec(payload.CatalogId,
                payload.UnitRawcode, payload.JassFunction, hits);
            return () => {
                // The original delayed JASS callback is cast-location based:
                // it can finish its area packet after its initial target
                // dies. Keep that behavior on the live side. Checkpoint
                // validation refuses an in-flight orphan whose old target
                // object cannot be reconstructed on resume.
                if (!CanFinishSourceModelDelayedAttack(payload.OwnerId,
                        payload.StoryLane, payload.LaneRevision)) return;
                ApplySourceScriptHits(actor, originalTarget,
                    script, script.OnProc, payload.CastX, payload.CastY);
            };
        }

        private Action RestoreRemainingDelayedHit(
            string kind, string json, string casterId, string targetId)
        {
            if (kind != RemainingDelayedHitKind) return null;
            var payload = ReadRemainingDelayedPayload(json);
            CharacterInstance actor;
            SpatialCombatEnemy target;
            SourceRemainingCallback callback;
            if (payload == null ||
                !characters.TryGetValue(payload.OwnerId, out actor) ||
                !enemies.TryGetValue(payload.EnemyId, out target) ||
                !SourceRemainingCallbackCatalog.TryGet(payload.UnitRawcode,
                    out callback) ||
                callback.CatalogId != actor.Catalog.Id ||
                callback.CatalogId != payload.CatalogId ||
                callback.Jass != payload.JassFunction ||
                casterId != InstanceKey(payload.OwnerId) ||
                targetId != "remaining-jass:" + payload.OwnerId + ":" +
                    payload.Serial)
                return null;
            if (payload.LaneRevision < SourceModelLaneRevision(payload.OwnerId))
                return () => { };
            if (payload.LaneRevision != SourceModelLaneRevision(payload.OwnerId) ||
                payload.StoryLane != IsStoryDeployed(payload.OwnerId))
                return null;
            return CreateRemainingDelayedHitCallback(payload, actor, target);
        }

        public static void ValidateRemainingDelayedState(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.RemainingDelayed == null ||
                combat.RemainingDelayed.Version != 1 ||
                combat.RemainingDelayed.NextSerial < 0 ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                !RemainingFinite(combat.Time) || combat.Time < 0)
                throw new FormatException("원본 JASS 지연 타격 장부가 없습니다.");
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("JASS 예약 시전자 신원이 중복됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var enemyKinds = new Dictionary<string, CharacterEnemyKind>(
                StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    enemyKinds.ContainsKey(enemy.EnemyId))
                    throw new FormatException("JASS 예약 대상 신원이 중복됐습니다.");
                else enemyKinds.Add(enemy.EnemyId, enemy.Kind);
            var ids = new HashSet<long>();
            foreach (var queued in combat.Timed.Pending)
            {
                if (queued == null)
                    throw new FormatException("원본 지연 타격 예약이 불완전합니다.");
                if (queued.Kind != RemainingDelayedHitKind) continue;
                var payload = ReadRemainingDelayedPayload(queued.Payload);
                SourceRemainingCallback callback;
                SourceCombatActorCheckpoint actor;
                if (!combat.HasSourceModelBasicAttacks ||
                    payload == null ||
                    !SourceRemainingCallbackCatalog.TryGet(
                        payload.UnitRawcode, out callback) ||
                    !RestorableRemainingCallback(callback.CatalogId) ||
                    callback.CatalogId != payload.CatalogId ||
                    callback.Jass != payload.JassFunction ||
                    !actors.TryGetValue(payload.OwnerId, out actor) ||
                    actor.CatalogId != callback.CatalogId ||
                    !enemyKinds.ContainsKey(payload.EnemyId) ||
                    queued.CasterId != payload.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    payload.Serial <= 0 ||
                    payload.Serial > combat.RemainingDelayed.NextSerial ||
                    queued.TargetId != "remaining-jass:" + payload.OwnerId +
                        ":" + payload.Serial ||
                    !ids.Add(payload.Serial) ||
                    payload.LaneRevision < 0 ||
                    payload.LaneRevision > actor.SourceLaneRevision ||
                    (payload.LaneRevision == actor.SourceLaneRevision &&
                     payload.StoryLane != actor.StoryDeployed) ||
                    !RemainingFinite(payload.CastAt) ||
                    payload.CastAt < 0 || payload.CastAt > combat.Time ||
                    !RemainingFinite(payload.DelaySeconds) ||
                    payload.DelaySeconds <= 0 ||
                    payload.DelaySeconds > 10 ||
                    queued.At != payload.CastAt + payload.DelaySeconds ||
                    queued.At < combat.Time ||
                    !RemainingFinite(payload.CastX) ||
                    !RemainingFinite(payload.CastY) ||
                    payload.Hits == null || payload.Hits.Count == 0 ||
                    payload.Hits.Count > 8)
                    throw new FormatException(
                        "원본 JASS 지연 타격의 예약/소유자/시간이 잘못됐습니다.");
                foreach (var hit in payload.Hits)
                    if (!ValidateRemainingHitSnapshot(hit))
                        throw new FormatException(
                            "원본 JASS 피해/범위의 직렬화 값이 잘못됐습니다.");
                CharacterEnemyKind originalKind;
                if (!enemyKinds.TryGetValue(payload.EnemyId, out originalKind) ||
                    !ValidRemainingCallbackPacket(payload, originalKind))
                    throw new FormatException(
                        "원본 JASS 콜백별 히트 수치·타격 구조가 다릅니다.");
            }
            if (!combat.HasSourceModelBasicAttacks &&
                combat.RemainingDelayed.NextSerial != 0)
                throw new FormatException(
                    "원본 모델 외의 공격에 JASS 예약 번호가 저장됐습니다.");
        }
    }
}
