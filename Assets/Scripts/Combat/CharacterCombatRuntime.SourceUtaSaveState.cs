using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceUtaClockCheckpoint
    {
        public long OwnerId;
        public double At;
    }

    [Serializable]
    public sealed class SourceUtaOwnerCheckpoint
    {
        public int Version = 1;
        public long NextPulseId;
        public double SongElapsed;
        public long SongRotations;
        public List<long> IntroOwners = new List<long>();
        public List<long> TransformedOwners = new List<long>();
        public List<SourceUtaClockCheckpoint> CycleClocks =
            new List<SourceUtaClockCheckpoint>();
        public List<SourceUtaClockCheckpoint> SleepClocks =
            new List<SourceUtaClockCheckpoint>();
        public List<SourceUtaClockCheckpoint> ManaGates =
            new List<SourceUtaClockCheckpoint>();
        public List<long> ActiveSongIds = new List<long>();
        public List<long> PendingSongIds = new List<long>();
    }

    [Serializable]
    public sealed class SourceUtaPulse
    {
        public string Kind;
        public long Serial;
        public long OwnerId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public double DelaySeconds;
        public string OriginalPrimaryId;
        public double CenterX;
        public double CenterY;
        public int SongCount;
        public List<long> SongIds = new List<long>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string UtaIntroSoldierKind = "source-uta-b6x-intro-v1";
        private const string UtaAttackOrderKind = "source-uta-b6o-order-v1";
        private const string UtaHarmonyImpactKind = "source-uta-b6r-impact-v1";
        private long sourceUtaPulseSerial;

        private static bool TryReadUtaSummonKey(string key, out long summonId)
        {
            summonId = 0;
            const string prefix = "practice-summon:";
            return !string.IsNullOrWhiteSpace(key) &&
                key.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(key.Substring(prefix.Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out summonId) &&
                summonId > 0 && SummonAttackKey(summonId) == key;
        }

        private static string UtaPulseKey(long serial) =>
            "uta-native:" + serial.ToString(CultureInfo.InvariantCulture);

        private static string EncodeUtaPulse(SourceUtaPulse value) =>
            JsonUtility.ToJson(value);

        private static SourceUtaPulse ReadUtaPulse(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 65536)
                return null;
            try
            {
                var value = JsonUtility.FromJson<SourceUtaPulse>(payload);
                return value != null && EncodeUtaPulse(value) == payload ?
                    value : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static List<SourceUtaClockCheckpoint> CaptureUtaClock(
            Dictionary<long, double> owners)
        {
            var result = new List<SourceUtaClockCheckpoint>();
            foreach (var pair in owners)
                result.Add(new SourceUtaClockCheckpoint {
                    OwnerId = pair.Key, At = pair.Value
                });
            result.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            return result;
        }

        private static List<long> CaptureUtaSongIds(HashSet<string> keys)
        {
            var ids = new List<long>();
            foreach (string key in keys)
            {
                long id;
                if (!TryReadUtaSummonKey(key, out id))
                    throw new InvalidOperationException(
                        "우타 음표 병사의 소환 실물 ID가 잘못됐습니다.");
                ids.Add(id);
            }
            ids.Sort();
            return ids;
        }

        private SourceUtaOwnerCheckpoint CaptureSourceUtaOwners()
        {
            // The source group ledger must cover EVERY extant h04O visual
            // attacker, including soldiers abandoned by B6m GroupClear.
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var summoned in sourceTemporarySummons)
                if (summoned.SourceUnitRawcode == "h04O")
                {
                    string key = SummonAttackKey(summoned.Id);
                    List<string> ownerKeys;
                    SourceIndependentAttacker independent;
                    if (!expected.Add(key) ||
                        !sourceModelUtaSongSoldierKeys.TryGetValue(
                            summoned.OwnerId, out ownerKeys) ||
                        !ownerKeys.Contains(key) ||
                        !sourceIndependentAttackers.TryGetValue(
                            key, out independent) ||
                        independent.OwnerInstanceId != summoned.OwnerId ||
                        independent.Rawcode != "h04O")
                        throw new InvalidOperationException(
                            "우타 음표 병사의 실제 공격자·소환 원장이 불일치합니다.");
                }
            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in sourceModelUtaSongSoldierKeys)
                foreach (string key in pair.Value)
                    if (!registered.Add(key) || !expected.Contains(key))
                        throw new InvalidOperationException(
                            "우타 B6m 소환 원장에 삭제/중복된 병사가 있습니다.");
            if (registered.Count != expected.Count)
                throw new InvalidOperationException(
                    "우타 B6m 소환 원장의 병사 수가 실제 공격자와 다릅니다.");
            var state = new SourceUtaOwnerCheckpoint {
                NextPulseId = sourceUtaPulseSerial,
                SongElapsed = sourceModelUtaSongElapsed,
                SongRotations = sourceModelUtaSongRotations,
                CycleClocks = CaptureUtaClock(sourceModelUtaCycleReadyAt),
                SleepClocks = CaptureUtaClock(sourceModelUtaSleepUntil),
                ManaGates = CaptureUtaClock(sourceModelUtaManaGateUntil),
                ActiveSongIds = CaptureUtaSongIds(sourceModelUtaSongActive),
                PendingSongIds = CaptureUtaSongIds(sourceModelUtaSongPending)
            };
            state.IntroOwners.AddRange(sourceModelUtaIntroStarted);
            state.IntroOwners.Sort();
            foreach (var actor in characters.Values)
                if (sourceModelRules != null &&
                    sourceModelRules.SourceFormRawcodeOf(
                        InstanceKey(actor.Id)) == "h0AF")
                    state.TransformedOwners.Add(actor.Id);
            state.TransformedOwners.Sort();
            return state;
        }

        private Action CreateSourceUtaPulse(SourceUtaPulse pulse)
        {
            CharacterInstance actor;
            if (pulse == null || !characters.TryGetValue(pulse.OwnerId, out actor))
                return null;
            var ownerId = pulse.OwnerId;
            string caster = InstanceKey(ownerId);
            bool lane = pulse.StoryLane;
            long revision = pulse.LaneRevision;
            if (pulse.Kind == UtaIntroSoldierKind)
                return () => {
                    if (!CanFinishSourceModelDelayedAttack(ownerId, lane, revision))
                        return;
                    double radius = 100 +
                        sourceModelRules.NextScriptRoll(500001) / 10000.0;
                    double angle = sourceModelRules.NextScriptRoll(360000) *
                        Math.PI / 180000.0;
                    AddSourceModelUtaSongSoldier(actor, null, 7,
                        "우타 · 세계의 가희",
                        pulse.CenterX + radius * Math.Cos(angle),
                        pulse.CenterY + radius * Math.Sin(angle));
                };
            if (pulse.Kind == UtaAttackOrderKind)
                return () => {
                    if (!CanFinishSourceModelDelayedAttack(ownerId, lane, revision))
                        return;
                    foreach (long summonId in pulse.SongIds)
                        OrderSourceIndependentAttackOnce(
                            SummonAttackKey(summonId), pulse.OriginalPrimaryId);
                };
            if (pulse.Kind == UtaHarmonyImpactKind)
                return () => {
                    if (!CanFinishSourceModelDelayedAttack(ownerId, lane, revision))
                        return;
                    SpatialCombatEnemy center;
                    enemies.TryGetValue(pulse.OriginalPrimaryId, out center);
                    // The B6r packet is centered on the snapshotted position,
                    // even if the original enemy was already defeated.
                    ApplySourceModelAreaPacketAt(caster, lane, center,
                        pulse.CenterX, pulse.CenterY,
                        SourceModelUtaB6rRules.AreaRadius,
                        SourceModelUtaB6rRules.AreaDamage(pulse.SongCount),
                        Ordr323AttackType.Spells, "우타 · 음표 병사 합창");
                    SpatialCombatEnemy living;
                    if (!enemies.TryGetValue(pulse.OriginalPrimaryId, out living) ||
                        !living.Enemy.IsAlive ||
                        !CanAttackInThisLane(actor, living)) return;
                    if (living.Kind == CharacterEnemyKind.Line)
                        ApplySourceModelAreaPacket(caster, lane, living, 0,
                            SourceModelUtaB6rRules.ConditionalPrimaryDamage(
                                pulse.SongCount, living.Enemy.Health,
                                SourceModelNativeTesoroCorrection(living), true),
                            Ordr323AttackType.Melee,
                            "우타 · 음표 병사 대상 체력 추가 타격");
                    if (living.Enemy.IsAlive)
                        ApplySourceModelAreaPacket(caster, lane, living, 0,
                            SourceModelUtaB6rRules.FinalPrimaryDamage,
                            Ordr323AttackType.Spells,
                            "우타 · 음표 병사 마무리 타격");
                };
            return null;
        }

        private void ScheduleSourceUtaPulse(SourceUtaPulse pulse, double seconds)
        {
            if (pulse == null || sourceUtaPulseSerial == long.MaxValue)
                throw new OverflowException("우타 B6r 예약 번호 초과");
            pulse.Serial = ++sourceUtaPulseSerial;
            pulse.CastAt = effectExecutor.Time;
            pulse.DelaySeconds = seconds;
            var callback = CreateSourceUtaPulse(pulse);
            if (callback == null)
                throw new NotSupportedException("우타 원본 예약 타격의 주인이 없습니다.");
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(pulse.OwnerId), UtaPulseKey(pulse.Serial),
                seconds, pulse.Kind, EncodeUtaPulse(pulse), callback);
        }

        private Action RestoreSourceUtaTimedEffect(
            string kind, string payload, string casterId, string targetId)
        {
            if (kind != UtaIntroSoldierKind && kind != UtaAttackOrderKind &&
                kind != UtaHarmonyImpactKind) return null;
            var pulse = ReadUtaPulse(payload);
            if (pulse == null || pulse.Kind != kind ||
                casterId != InstanceKey(pulse.OwnerId) ||
                targetId != UtaPulseKey(pulse.Serial))
                return null;
            CharacterInstance actor;
            if (!characters.TryGetValue(pulse.OwnerId, out actor) ||
                actor.Catalog.Id != "E-09") return null;
            // A transferred unit keeps its old queue entry for deterministic
            // ordering, but a previous-lane cast cannot damage a new lane.
            if (pulse.LaneRevision < SourceModelLaneRevision(pulse.OwnerId))
                return () => { };
            if (pulse.LaneRevision != SourceModelLaneRevision(pulse.OwnerId) ||
                pulse.StoryLane != IsStoryDeployed(pulse.OwnerId))
                return null;
            return CreateSourceUtaPulse(pulse);
        }
    }
}
