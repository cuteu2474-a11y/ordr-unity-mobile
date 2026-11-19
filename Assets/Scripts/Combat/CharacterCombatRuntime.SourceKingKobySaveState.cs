using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceKingBasicOwnerRow
    {
        public long OwnerId, LaneRevision;
        public string EnemyId;
        public bool StoryLane;
        public double ExpiresAt;
    }

    [Serializable]
    public sealed class SourceKatakuriOwnerRow
    {
        public long OwnerId, LaneRevision;
        public bool StoryLane;
        public double ExpiresAt;
    }

    [Serializable]
    public sealed class SourceKingKobyOwnerCheckpoint
    {
        public int Version = 1;
        public List<SourceKingBasicOwnerRow> KingBasic =
            new List<SourceKingBasicOwnerRow>();
        public List<SourceKatakuriOwnerRow> KatakuriSpeed =
            new List<SourceKatakuriOwnerRow>();
    }

    [Serializable]
    public sealed class SourceKingKobyTimedPayload
    {
        public long OwnerId, LaneRevision, SummonId;
        public bool StoryLane;
        public string EnemyId, SkillId;
        public double CastAt, DelaySeconds, IntervalSeconds, DurationSeconds;
        public int Hit;
    }

    // King: a refresh extends the owner lease, not the already queued tick.
    // Koby: a visual, non-roster summon has independent scheduled effects,
    // but never claims a source-native basic attack or an invented rawcode.
    public sealed partial class CharacterCombatRuntime
    {
        private const string KingBasicTickKind = "source-king-basic-tick-v1";
        private const string KobyCloneHitKind = "source-koby-clone-hit-v1";
        private static bool KingKobyFinite(double x) =>
            !double.IsNaN(x) && !double.IsInfinity(x);
        private static string SerializeKingKoby(SourceKingKobyTimedPayload p) =>
            JsonUtility.ToJson(p);

        private static SourceKingKobyTimedPayload ReadKingKoby(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                return null;
            try
            {
                var row = JsonUtility.FromJson<SourceKingKobyTimedPayload>(json);
                return row != null && SerializeKingKoby(row) == json ?
                    row : null;
            }
            catch (ArgumentException) { return null; }
        }

        private SourceKingKobyOwnerCheckpoint CaptureKingKobyOwners()
        {
            var record = new SourceKingKobyOwnerCheckpoint();
            var keys = new List<string>(thirtiethKingBasic.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                var row = thirtiethKingBasic[key];
                record.KingBasic.Add(new SourceKingBasicOwnerRow {
                    OwnerId = row.OwnerId, EnemyId = row.Victim.Id,
                    StoryLane = row.StoryLane, LaneRevision = row.LaneRevision,
                    ExpiresAt = row.ExpiresAt
                });
            }
            var ids = new List<long>(thirtiethKatakuriSpeed.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                var row = thirtiethKatakuriSpeed[id];
                record.KatakuriSpeed.Add(new SourceKatakuriOwnerRow {
                    OwnerId = id, StoryLane = row.StoryLane,
                    LaneRevision = row.LaneRevision, ExpiresAt = row.ExpiresAt
                });
            }
            return record;
        }

        private void RestoreKingKobyOwners(SourceKingKobyOwnerCheckpoint record)
        {
            if (record == null || thirtiethKingBasic.Count != 0 ||
                thirtiethKatakuriSpeed.Count != 0)
                throw new InvalidOperationException(
                    "킹/카타쿠리 상태는 빈 전투에서만 복원합니다.");
            foreach (var row in record.KingBasic)
            {
                SpatialCombatEnemy victim = enemies[row.EnemyId];
                string key = InstanceKey(row.OwnerId) + ":" + row.EnemyId;
                thirtiethKingBasic.Add(key, new KingBasicDebuffState {
                    OwnerId = row.OwnerId, Victim = victim,
                    StoryLane = row.StoryLane, LaneRevision = row.LaneRevision,
                    ExpiresAt = row.ExpiresAt
                });
            }
            foreach (var row in record.KatakuriSpeed)
                thirtiethKatakuriSpeed.Add(row.OwnerId, new KatakuriSpeedState {
                    StoryLane = row.StoryLane, LaneRevision = row.LaneRevision,
                    ExpiresAt = row.ExpiresAt
                });
        }

        public static void ValidateKingKobyState(SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.Version != 4 ||
                combat.KingKoby == null || combat.KingKoby.Version != 1 ||
                combat.KingKoby.KingBasic == null ||
                combat.KingKoby.KatakuriSpeed == null ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Summons == null || combat.Timed == null ||
                combat.Timed.Pending == null ||
                !KingKobyFinite(combat.Time) || combat.Time < 0)
                throw new FormatException("킹/코비 예약 저장 장부가 없습니다.");
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("킹/코비 소유자가 중복됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    !enemies.Add(enemy.EnemyId))
                    throw new FormatException("킹 예약 목표가 중복됐습니다.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in combat.KingKoby.KingBasic)
            {
                SourceCombatActorCheckpoint owner;
                string key = row == null ? null :
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                    ":" + row.EnemyId;
                if (row == null || !actors.TryGetValue(row.OwnerId, out owner) ||
                    owner.CatalogId != "L-34" ||
                    !enemies.Contains(row.EnemyId) || !keys.Add(key) ||
                    row.LaneRevision < 0 ||
                    row.LaneRevision != owner.SourceLaneRevision ||
                    row.StoryLane != owner.StoryDeployed ||
                    !KingKobyFinite(row.ExpiresAt) ||
                    row.ExpiresAt < combat.Time - .000001 ||
                    row.ExpiresAt > combat.Time + 3.000001)
                    throw new FormatException(
                        "킹 평타 지속 피해 소유권이 손상됐습니다.");
            }
            var katakuri = new HashSet<long>();
            foreach (var row in combat.KingKoby.KatakuriSpeed)
            {
                SourceCombatActorCheckpoint owner;
                if (row == null || !actors.TryGetValue(row.OwnerId, out owner) ||
                    owner.CatalogId != "LI-08" ||
                    !katakuri.Add(row.OwnerId) ||
                    row.LaneRevision != owner.SourceLaneRevision ||
                    row.StoryLane != owner.StoryDeployed ||
                    !KingKobyFinite(row.ExpiresAt) ||
                    row.ExpiresAt < combat.Time ||
                    row.ExpiresAt > combat.Time + 1.500001)
                    throw new FormatException(
                        "카타쿠리 공격속도 상태가 잘못됐습니다.");
            }
            var clones = new Dictionary<long, SourceCombatSummonCheckpoint>();
            foreach (var summon in combat.Summons)
            {
                if (summon == null)
                    throw new FormatException("임시 소환 장부가 없습니다.");
                if (summon.SourceUnitRawcode != null ||
                    summon.SkillName != "콤비네이션 어택") continue;
                SourceCombatActorCheckpoint owner;
                if (summon.Id <= 0 || clones.ContainsKey(summon.Id) ||
                    !actors.TryGetValue(summon.OwnerId, out owner) ||
                    owner.CatalogId != "TR-24" ||
                    summon.HasIndependentCombat || summon.IsVirtualAttackEffect ||
                    summon.LaneRevision != owner.SourceLaneRevision ||
                    summon.StoryLane != owner.StoryDeployed ||
                    !KingKobyFinite(summon.ExpiresAt) ||
                    summon.ExpiresAt <= combat.Time)
                    throw new FormatException(
                        "코비 분신이 네이티브 소환으로 변조됐습니다.");
                clones.Add(summon.Id, summon);
            }
            var kingPending = new HashSet<string>(StringComparer.Ordinal);
            var clonePending = new Dictionary<long, HashSet<int>>();
            foreach (var queued in combat.Timed.Pending)
            {
                if (queued == null)
                    throw new FormatException("킹/코비 예약 목록이 없습니다.");
                bool king = queued.Kind == KingBasicTickKind;
                if (!king && queued.Kind != KobyCloneHitKind) continue;
                var p = ReadKingKoby(queued.Payload);
                SourceCombatActorCheckpoint owner;
                if (!combat.HasSourceModelBasicAttacks || p == null ||
                    !actors.TryGetValue(p.OwnerId, out owner) ||
                    queued.CasterId != p.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    !KingKobyFinite(p.CastAt) || p.CastAt < 0 ||
                    p.CastAt > combat.Time ||
                    !KingKobyFinite(p.DelaySeconds) || p.DelaySeconds <= 0 ||
                    !KingKobyFinite(queued.At) || queued.At < combat.Time ||
                    queued.At != p.CastAt + p.DelaySeconds ||
                    p.LaneRevision > owner.SourceLaneRevision ||
                    p.LaneRevision < 0 ||
                    (p.LaneRevision == owner.SourceLaneRevision &&
                     p.StoryLane != owner.StoryDeployed))
                    throw new FormatException(
                        "킹/코비 예약 효과의 시계·전장이 잘못됐습니다.");
                if (king)
                {
                    string id = p.OwnerId.ToString(CultureInfo.InvariantCulture) +
                        ":" + p.EnemyId;
                    if (owner.CatalogId != "L-34" ||
                        !enemies.Contains(p.EnemyId) ||
                        queued.TargetId != p.EnemyId ||
                        p.SummonId != 0 || p.SkillId != null ||
                        p.Hit != 0 || p.DelaySeconds != 1 ||
                        p.IntervalSeconds != 0 || p.DurationSeconds != 0 ||
                        !keys.Contains(id) || !kingPending.Add(id))
                        throw new FormatException(
                            "킹의 다음 1초 예약 피해가 잘못됐습니다.");
                }
                else
                {
                    SourceCombatSummonCheckpoint summon;
                    HashSet<int> hits;
                    if (owner.CatalogId != "TR-24" ||
                        p.EnemyId != null ||
                        p.SkillId != "TR-24:skill:2" ||
                        p.SummonId <= 0 ||
                        !clones.TryGetValue(p.SummonId, out summon) ||
                        summon.OwnerId != p.OwnerId ||
                        p.StoryLane != summon.StoryLane ||
                        p.LaneRevision != summon.LaneRevision ||
                        queued.TargetId != "practice-koby-clone:" + p.SummonId ||
                        p.Hit <= 0 || p.Hit > 120 ||
                        !KingKobyFinite(p.IntervalSeconds) ||
                        p.IntervalSeconds <= 0 ||
                        !KingKobyFinite(p.DurationSeconds) ||
                        p.DurationSeconds <= 0 ||
                        p.DelaySeconds != p.Hit * p.IntervalSeconds ||
                        p.Hit > Math.Floor(
                            p.DurationSeconds / p.IntervalSeconds + .000001) ||
                        p.CastAt + p.DurationSeconds != summon.ExpiresAt)
                        throw new FormatException(
                            "코비 분신의 출생·타격 예약이 일치하지 않습니다.");
                    if (!clonePending.TryGetValue(p.SummonId, out hits))
                        clonePending.Add(p.SummonId, hits = new HashSet<int>());
                    if (!hits.Add(p.Hit))
                        throw new FormatException("코비 분신이 같은 타격을 중복 예약했습니다.");
                }
            }
            if (kingPending.Count != keys.Count ||
                (!combat.HasSourceModelBasicAttacks &&
                 (keys.Count != 0 || katakuri.Count != 0 ||
                  clones.Count != 0)))
                throw new FormatException("킹/코비 소유권과 후속 예약이 누락됐습니다.");
        }

        private Action CreateKingBasicTick(CharacterInstance actor,
            KingBasicDebuffState state, string key)
        {
            return () => {
                KingBasicDebuffState currentState;
                SpatialCombatEnemy current;
                if (!thirtiethKingBasic.TryGetValue(key, out currentState) ||
                    !ReferenceEquals(state, currentState) ||
                    !enemies.TryGetValue(state.Victim.Id, out current) ||
                    !ReferenceEquals(state.Victim, current) ||
                    !state.Victim.Enemy.IsAlive ||
                    !CanFinishSourceModelDelayedAttack(actor.Id,
                        state.StoryLane, state.LaneRevision) ||
                    Time > state.ExpiresAt + .000001) return;
                ApplySourceModelSeventhTimedHit(actor, state.Victim,
                    "킹 평타 지속 피해",
                    actor.Profile.Passives[2].Number("magicalDamagePerSecond").Value,
                    "magical");
                if (state.Victim.Enemy.IsAlive &&
                    Time + 1 <= state.ExpiresAt + .000001)
                    ScheduleSourceModelThirtiethKingBasicTick(actor, state, key);
                else
                    thirtiethKingBasic.Remove(key);
            };
        }

        private void ScheduleKingBasicTick(CharacterInstance actor,
            KingBasicDebuffState state, string key)
        {
            var p = new SourceKingKobyTimedPayload {
                OwnerId = actor.Id, EnemyId = state.Victim.Id,
                StoryLane = state.StoryLane, LaneRevision = state.LaneRevision,
                CastAt = effectExecutor.Time, DelaySeconds = 1
            };
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(actor.Id), state.Victim.Id, 1,
                KingBasicTickKind, SerializeKingKoby(p),
                CreateKingBasicTick(actor, state, key));
        }

        private Action CreateKobyCloneHit(
            SourceKingKobyTimedPayload p, CharacterInstance actor,
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            return () => {
                if (!CanFinishSourceModelDelayedAttack(actor.Id,
                        p.StoryLane, p.LaneRevision)) return;
                bool exists = false;
                foreach (var live in PracticeSummonsOf(actor.Id))
                    if (live.Id == p.SummonId) { exists = true; break; }
                if (!exists) return;
                var candidates = BuildTargetCandidates(actor);
                if (candidates.Count == 0) return;
                string chosen = ChoosePreviewTarget(actor, candidates);
                SpatialCombatEnemy victim;
                if (chosen == null || !enemies.TryGetValue(chosen, out victim) ||
                    !victim.Enemy.IsAlive) return;
                var hitTargets = SourceScriptTargets(actor, victim,
                    new SourceScriptHit(SourceScriptHitShape.Circle,
                        component.Number("explosiveRadius").Value,
                        0, 0, 1, 1, Ordr323AttackType.Spells));
                double attack = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
                foreach (var target in hitTargets)
                    ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                        SourceModelSeventhKnownAttackBuff(actor.Id,
                            (attack + SourceModelUpgradeAttackBonus(actor.Id)) *
                            component.Number("basicAttackExplosivePercent").Value / 100),
                        "explosive");
                ApplySourceModelSeventhTimedHit(actor, victim, skill.Name,
                    component.Number("singleMagicalDamagePerHit").Value,
                    "magical");
            };
        }

        private void ScheduleKobyCloneHit(
            CharacterInstance actor, SourceModelSummonState clone,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            double interval, double duration, int hit)
        {
            var p = new SourceKingKobyTimedPayload {
                OwnerId = actor.Id, SummonId = clone.Id,
                StoryLane = clone.StoryLane, LaneRevision = clone.LaneRevision,
                SkillId = skill.Id, CastAt = effectExecutor.Time,
                DelaySeconds = hit * interval, Hit = hit,
                IntervalSeconds = interval, DurationSeconds = duration
            };
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(actor.Id), "practice-koby-clone:" + clone.Id,
                p.DelaySeconds, KobyCloneHitKind, SerializeKingKoby(p),
                CreateKobyCloneHit(p, actor, skill, component));
        }

        private Action RestoreKingKobyTimed(
            string kind, string json, string casterId, string targetId)
        {
            bool king = kind == KingBasicTickKind;
            if (!king && kind != KobyCloneHitKind) return null;
            var p = ReadKingKoby(json);
            CharacterInstance actor;
            if (p == null || !characters.TryGetValue(p.OwnerId, out actor) ||
                casterId != InstanceKey(p.OwnerId)) return null;
            if (king)
            {
                var key = casterId + ":" + p.EnemyId;
                KingBasicDebuffState row;
                if (actor.Catalog.Id != "L-34" ||
                    targetId != p.EnemyId ||
                    !thirtiethKingBasic.TryGetValue(key, out row) ||
                    row.LaneRevision != p.LaneRevision ||
                    row.StoryLane != p.StoryLane ||
                    !enemies.ContainsKey(p.EnemyId)) return null;
                return CreateKingBasicTick(actor, row, key);
            }
            if (actor.Catalog.Id != "TR-24" ||
                targetId != "practice-koby-clone:" + p.SummonId)
                return null;
            bool found = false;
            foreach (var summon in sourceTemporarySummons)
                if (summon.Id == p.SummonId && summon.OwnerId == p.OwnerId &&
                    !summon.HasIndependentCombat &&
                    summon.ExpiresAt == p.CastAt + p.DurationSeconds &&
                    summon.StoryLane == p.StoryLane &&
                    summon.LaneRevision == p.LaneRevision)
                    found = true;
            if (!found) return null;
            var skill = AdditionalAutoSkill(actor, p.SkillId);
            if (skill == null || skill.Components.Count == 0) return null;
            ImportedCombatEffect component = null;
            foreach (var part in skill.Components)
                if (part.Type == "summon") { component = part; break; }
            if (component == null ||
                component.Number("durationSeconds") != p.DurationSeconds ||
                actor.Mapping.AttackSpec.BasicAttack.IntervalSeconds !=
                    p.IntervalSeconds)
                return null;
            return CreateKobyCloneHit(p, actor, skill, component);
        }
    }
}
