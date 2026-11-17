using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    // No serialized Action closures. Timed attack pauses, pending form
    // changes and virtual Sugar projectiles rebind existing owner IDs.
    [Serializable]
    public sealed class SourceTemporaryOwnerDeadline
    {
        public long OwnerId;
        public double FinishesAt;
        public long Token;
    }

    [Serializable]
    public sealed class SourceTemporaryFormName
    {
        public long OwnerId;
        public string Name;
    }

    [Serializable]
    public sealed class SourceTemporaryOwnerCheckpoint
    {
        public int Version = 1;
        public long AttackPauseSerial;
        public bool FormCastWake;
        public List<SourceTemporaryOwnerDeadline> FormWindups =
            new List<SourceTemporaryOwnerDeadline>();
        public List<SourceTemporaryOwnerDeadline> AttackPauses =
            new List<SourceTemporaryOwnerDeadline>();
        public List<SourceTemporaryFormName> FormNames =
            new List<SourceTemporaryFormName>();
        public List<long> YamatoOpeningReady = new List<long>();
    }

    [Serializable]
    public sealed class SourceTemporaryDeferredPayload
    {
        public long OwnerId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public double DelaySeconds;
        public long Token;
        public long SummonId;
        public string EnemyId;
        public string SkillId;
        public string SkillName;
        public string OriginalAbilityRawcode;
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string TemporaryFormWindupKind = "source-form-windup-v1";
        private const string TemporaryAttackPauseKind = "source-attack-pause-v1";
        private const string TemporarySugarDollKind = "source-sugar-doll-v1";

        private static string EncodeTemporaryDeferred(
            SourceTemporaryDeferredPayload payload) =>
            JsonUtility.ToJson(payload);

        private static SourceTemporaryDeferredPayload ReadTemporaryDeferred(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                return null;
            try
            {
                var state = JsonUtility.FromJson<SourceTemporaryDeferredPayload>(json);
                return state != null && EncodeTemporaryDeferred(state) == json ?
                    state : null;
            }
            catch (ArgumentException) { return null; }
        }

        private SourceTemporaryOwnerCheckpoint CaptureSourceTemporaryOwners()
        {
            var state = new SourceTemporaryOwnerCheckpoint {
                AttackPauseSerial = sourceAttackPauseSerial,
                FormCastWake = sourceFormCastWake
            };
            var ids = new List<long>(sourceFormCastFinishes.Keys);
            ids.Sort();
            foreach (long id in ids)
                state.FormWindups.Add(new SourceTemporaryOwnerDeadline {
                    OwnerId = id, FinishesAt = sourceFormCastFinishes[id]
                });
            ids = new List<long>(sourceAttackPauseFinishes.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                long token;
                if (!sourceAttackPauseTokens.TryGetValue(id, out token))
                    throw new InvalidOperationException(
                        "원본 EXPauseUnit 해제 토큰이 없습니다.");
                state.AttackPauses.Add(new SourceTemporaryOwnerDeadline {
                    OwnerId = id, FinishesAt = sourceAttackPauseFinishes[id],
                    Token = token
                });
            }
            if (sourceAttackPauseTokens.Count != state.AttackPauses.Count)
                throw new InvalidOperationException(
                    "원본 EXPauseUnit 토큰과 해제 시각이 다릅니다.");
            ids = new List<long>(sourceFormNames.Keys);
            ids.Sort();
            foreach (long id in ids)
                state.FormNames.Add(new SourceTemporaryFormName {
                    OwnerId = id, Name = sourceFormNames[id]
                });
            state.YamatoOpeningReady.AddRange(sourceModelYamatoOpeningReady);
            state.YamatoOpeningReady.Sort();
            return state;
        }

        private void RestoreSourceTemporaryOwners(
            SourceTemporaryOwnerCheckpoint saved)
        {
            if (saved == null || sourceFormCastFinishes.Count != 0 ||
                sourceAttackPauseFinishes.Count != 0 ||
                sourceAttackPauseTokens.Count != 0 ||
                sourceFormNames.Count != 0 ||
                sourceModelYamatoOpeningReady.Count != 0 ||
                sourceAttackPauseSerial != 0 || sourceFormCastWake)
                throw new InvalidOperationException(
                    "임시 스킬 소유권은 새로운 전투 월드에만 복원합니다.");
            sourceAttackPauseSerial = saved.AttackPauseSerial;
            sourceFormCastWake = saved.FormCastWake;
            foreach (var row in saved.FormWindups)
                sourceFormCastFinishes.Add(row.OwnerId, row.FinishesAt);
            foreach (var row in saved.AttackPauses)
            {
                sourceAttackPauseFinishes.Add(row.OwnerId, row.FinishesAt);
                sourceAttackPauseTokens.Add(row.OwnerId, row.Token);
            }
            foreach (var row in saved.FormNames)
            {
                CharacterInstance actor = characters[row.OwnerId];
                ImportedCombatSkill skill = actor.Catalog.Id == "TR-14" ?
                    FindSourceSkill(actor, "블랙 맘바 각성") :
                    FindSourceTemporarySkill(actor, "TR-18:skill:0");
                SourceModelFormSpec form;
                if (!SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                        skill, out form) || form.Name != row.Name)
                    throw new FormatException(
                        "저장된 원본 변신 이름과 기술 원본이 불일치합니다.");
                sourceFormNames.Add(row.OwnerId, row.Name);
            }
            foreach (long id in saved.YamatoOpeningReady)
                sourceModelYamatoOpeningReady.Add(id);
        }

        private static ImportedCombatSkill FindSourceTemporarySkill(
            CharacterInstance actor, string skillId)
        {
            if (actor?.Profile == null) return null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Id == skillId) return skill;
            return null;
        }

        private Action CreateTemporaryFormWindupFinish(
            SourceTemporaryDeferredPayload payload)
        {
            return () => {
                CharacterInstance actor;
                if (!characters.TryGetValue(payload.OwnerId, out actor) ||
                    !CanFinishSourceModelDelayedAttack(payload.OwnerId,
                        payload.StoryLane, payload.LaneRevision)) return;
                double until;
                if (!sourceFormCastFinishes.TryGetValue(payload.OwnerId,
                        out until) || until !=
                    payload.CastAt + payload.DelaySeconds) return;
                var skill = FindSourceSkill(actor, "블랙 맘바 각성");
                SourceModelFormSpec form;
                if (skill == null || skill.Id != payload.SkillId ||
                    !SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                        skill, out form) ||
                    skill.Number("castTimeSeconds") != payload.DelaySeconds)
                    return;
                sourceFormCastFinishes.Remove(payload.OwnerId);
                sourceFormCastWake = true;
                ApplySourceForm(payload.OwnerId, skill, form);
            };
        }

        private Action CreateTemporaryAttackPauseRelease(
            SourceTemporaryDeferredPayload payload)
        {
            return () => {
                long activeToken;
                double until;
                if (!sourceAttackPauseTokens.TryGetValue(payload.OwnerId,
                        out activeToken) || activeToken != payload.Token ||
                    !sourceAttackPauseFinishes.TryGetValue(payload.OwnerId,
                        out until) ||
                    until != payload.CastAt + payload.DelaySeconds) return;
                sourceAttackPauseTokens.Remove(payload.OwnerId);
                sourceAttackPauseFinishes.Remove(payload.OwnerId);
                sourceFormCastWake = true;
                if (CanFinishSourceModelDelayedAttack(payload.OwnerId,
                        payload.StoryLane, payload.LaneRevision))
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated,
                        InstanceKey(payload.OwnerId), payload.EnemyId,
                        payload.SkillName,
                        "MODEL_ONLY 원본 EXPauseUnit 해제 / 평타 재개", 0));
            };
        }

        private Action CreateTemporarySugarDollImpact(
            SourceTemporaryDeferredPayload payload)
        {
            return () => {
                CharacterInstance actor;
                SpatialCombatEnemy target;
                if (!characters.TryGetValue(payload.OwnerId, out actor) ||
                    !CanFinishSourceModelDelayedAttack(payload.OwnerId,
                        payload.StoryLane, payload.LaneRevision) ||
                    !enemies.TryGetValue(payload.EnemyId, out target) ||
                    !target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target)) return;
                SourceRemainingCallback spec;
                if (!SourceRemainingCallbackCatalog.TryGet("h027", out spec) ||
                    spec.CatalogId != actor.Catalog.Id ||
                    spec.Jass != "BpT") return;
                if (!RetireSourceTemporaryActor(payload.SummonId,
                        target.Id, target.X, target.Y)) return;
                var hit = RemainingHitSpec(spec.CatalogId, spec.Rawcode,
                    spec.Jass, RemainingDirect(500));
                ApplySourceScriptHits(actor, target, hit, hit.OnProc);
            };
        }

        private Action RestoreSourceTemporaryTimedEffect(
            string kind, string data, string casterId, string targetId)
        {
            if (kind != TemporaryFormWindupKind &&
                kind != TemporaryAttackPauseKind &&
                kind != TemporarySugarDollKind) return null;
            var payload = ReadTemporaryDeferred(data);
            CharacterInstance actor;
            if (payload == null ||
                casterId != payload.OwnerId.ToString(
                    CultureInfo.InvariantCulture) ||
                !characters.TryGetValue(payload.OwnerId, out actor) ||
                payload.LaneRevision > SourceModelLaneRevision(payload.OwnerId))
                return null;
            // Already transferred out of the casting lane: preserve its
            // due timestamp but never revive a stale former-lane action.
            if (payload.LaneRevision < SourceModelLaneRevision(payload.OwnerId))
                return () => { };
            if (kind == TemporaryFormWindupKind)
            {
                var skill = FindSourceSkill(actor, "블랙 맘바 각성");
                SourceModelFormSpec form;
                if (actor.Catalog.Id != "TR-14" ||
                    skill == null || payload.SkillId != skill.Id ||
                    !SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                        skill, out form) ||
                    skill.Number("castTimeSeconds") != payload.DelaySeconds ||
                    targetId != "practice-form-cast:" + casterId)
                    return null;
                return CreateTemporaryFormWindupFinish(payload);
            }
            if (kind == TemporaryAttackPauseKind)
                return targetId == "practice-attack-pause:" + casterId +
                    ":" + payload.Token ?
                    CreateTemporaryAttackPauseRelease(payload) : null;
            return targetId == "remaining-doll:" + casterId +
                ":" + payload.SummonId ?
                CreateTemporarySugarDollImpact(payload) : null;
        }

        private static Dictionary<long, SourceCombatActorCheckpoint>
            TemporarySavedActors(SourceCombatRuntimeCheckpoint state)
        {
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in state.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("임시 기술의 소유자 ID가 중복됐습니다.");
                actors.Add(actor.InstanceId, actor);
            }
            return actors;
        }

        private static void ValidateTemporaryDeadlines(
            List<SourceTemporaryOwnerDeadline> rows,
            Dictionary<long, SourceCombatActorCheckpoint> actors,
            double time, bool pause, long maximumToken)
        {
            if (rows == null || rows.Count > 1000000)
                throw new FormatException("임시 기술의 대기 목록이 잘못됐습니다.");
            long previous = 0;
            foreach (var row in rows)
            {
                SourceCombatActorCheckpoint actor;
                if (row == null || row.OwnerId <= previous ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    (pause && (row.Token <= 0 || row.Token > maximumToken)) ||
                    (!pause && (row.Token != 0 ||
                                actor.CatalogId != "TR-14")) ||
                    double.IsNaN(row.FinishesAt) ||
                    double.IsInfinity(row.FinishesAt) ||
                    row.FinishesAt <= time)
                    throw new FormatException("원본 예약 해제의 시각·소유자가 잘못됐습니다.");
                previous = row.OwnerId;
            }
        }

        public static void ValidateSourceTemporaryOwners(
            SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.TemporaryOwners == null ||
                state.TemporaryOwners.Version != 1 ||
                state.TemporaryOwners.AttackPauseSerial < 0 ||
                state.TemporaryOwners.FormWindups == null ||
                state.TemporaryOwners.AttackPauses == null ||
                state.TemporaryOwners.FormNames == null ||
                state.TemporaryOwners.YamatoOpeningReady == null ||
                state.TemporaryOwners.FormNames.Count > 1000000 ||
                state.TemporaryOwners.YamatoOpeningReady.Count > 1000000 ||
                state.Actors == null || state.Summons == null ||
                state.Timed == null || state.Timed.Pending == null ||
                double.IsNaN(state.Time) || double.IsInfinity(state.Time) ||
                state.Time < 0)
                throw new FormatException("원본 임시 기술 저장 상태가 불완전합니다.");
            var owners = state.TemporaryOwners;
            var actors = TemporarySavedActors(state);
            ValidateTemporaryDeadlines(owners.FormWindups, actors,
                state.Time, false, owners.AttackPauseSerial);
            ValidateTemporaryDeadlines(owners.AttackPauses, actors,
                state.Time, true, owners.AttackPauseSerial);
            long previous = 0;
            foreach (var row in owners.FormNames)
            {
                SourceCombatActorCheckpoint actor;
                if (row == null || row.OwnerId <= previous ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    (actor.CatalogId != "TR-14" &&
                     actor.CatalogId != "TR-18") ||
                    string.IsNullOrWhiteSpace(row.Name) ||
                    row.Name.Length > 128)
                    throw new FormatException("복원할 폼의 소유자·이름이 잘못됐습니다.");
                previous = row.OwnerId;
            }
            previous = 0;
            foreach (long id in owners.YamatoOpeningReady)
            {
                SourceCombatActorCheckpoint actor;
                if (id <= previous || !actors.TryGetValue(id, out actor) ||
                    actor.CatalogId != "TR-18")
                    throw new FormatException("야마토 각성 후속 소유권이 잘못됐습니다.");
                previous = id;
            }
            var formDeadline = new Dictionary<long, double>();
            var pauseDeadline = new Dictionary<long, SourceTemporaryOwnerDeadline>();
            foreach (var row in owners.FormWindups)
                formDeadline.Add(row.OwnerId, row.FinishesAt);
            foreach (var row in owners.AttackPauses)
                pauseDeadline.Add(row.OwnerId, row);
            var currentForms = new HashSet<long>();
            var currentPauses = new HashSet<long>();
            var seenPauseTokens = new HashSet<string>(StringComparer.Ordinal);
            var seenDollImpactIds = new HashSet<long>();
            var summons = new Dictionary<long, SourceCombatSummonCheckpoint>();
            foreach (var summon in state.Summons)
            {
                if (summon == null || summon.Id <= 0 ||
                    summons.ContainsKey(summon.Id))
                    throw new FormatException("원본 슈가 투사체 ID가 잘못됐습니다.");
                summons.Add(summon.Id, summon);
            }
            foreach (var pending in state.Timed.Pending)
            {
                if (pending == null)
                    throw new FormatException("원본 임시 기술 예약이 없습니다.");
                bool form = pending.Kind == TemporaryFormWindupKind;
                bool pause = pending.Kind == TemporaryAttackPauseKind;
                bool doll = pending.Kind == TemporarySugarDollKind;
                if (!form && !pause && !doll) continue;
                var row = ReadTemporaryDeferred(pending.Payload);
                SourceCombatActorCheckpoint actor;
                if (!state.HasSourceModelBasicAttacks || row == null ||
                    row.OwnerId <= 0 ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    pending.CasterId != row.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    row.LaneRevision < 0 ||
                    row.LaneRevision > actor.SourceLaneRevision ||
                    (row.LaneRevision == actor.SourceLaneRevision &&
                     row.StoryLane != actor.StoryDeployed) ||
                    double.IsNaN(row.CastAt) ||
                    double.IsInfinity(row.CastAt) || row.CastAt < 0 ||
                    row.CastAt > state.Time ||
                    double.IsNaN(row.DelaySeconds) ||
                    double.IsInfinity(row.DelaySeconds) ||
                    row.DelaySeconds <= 0 ||
                    pending.At != row.CastAt + row.DelaySeconds ||
                    pending.At < state.Time)
                    throw new FormatException("원본 임시 기술의 시전·전장·시간이 잘못됐습니다.");
                bool activeLane = row.LaneRevision == actor.SourceLaneRevision;
                if (form)
                {
                    double until;
                    if (actor.CatalogId != "TR-14" ||
                        row.Token != 0 || row.SummonId != 0 ||
                        row.EnemyId != null || row.SkillName != null ||
                        row.OriginalAbilityRawcode != null ||
                        string.IsNullOrWhiteSpace(row.SkillId) ||
                        !row.SkillId.StartsWith("TR-14:skill:",
                            StringComparison.Ordinal) ||
                        pending.TargetId != "practice-form-cast:" +
                            pending.CasterId)
                        throw new FormatException("변신 예약의 기술 ID가 잘못됐습니다.");
                    if (activeLane &&
                        (!formDeadline.TryGetValue(row.OwnerId, out until) ||
                         until != pending.At ||
                         !currentForms.Add(row.OwnerId)))
                        throw new FormatException("변신 완료 예약과 유닛 상태가 다릅니다.");
                }
                else if (pause)
                {
                    SourceTemporaryOwnerDeadline active;
                    if (row.Token <= 0 ||
                        row.Token > owners.AttackPauseSerial ||
                        row.SummonId != 0 || row.SkillId != null ||
                        row.OriginalAbilityRawcode != null ||
                        string.IsNullOrWhiteSpace(row.EnemyId) ||
                        row.EnemyId.Length > 256 ||
                        string.IsNullOrWhiteSpace(row.SkillName) ||
                        row.SkillName.Length > 128 ||
                        pending.TargetId != "practice-attack-pause:" +
                            pending.CasterId + ":" + row.Token ||
                        !seenPauseTokens.Add(pending.CasterId + ":" +
                            row.Token.ToString(CultureInfo.InvariantCulture)))
                        throw new FormatException("평타 일시정지의 주문·토큰이 잘못됐습니다.");
                    if (activeLane &&
                        pauseDeadline.TryGetValue(row.OwnerId, out active) &&
                        row.Token == active.Token &&
                        (active.FinishesAt != pending.At ||
                         !currentPauses.Add(row.OwnerId)))
                        throw new FormatException("평타 재개 예약이 중복됐습니다.");
                }
                else
                {
                    SourceCombatSummonCheckpoint summoned;
                    if (actor.CatalogId != "R-24" ||
                        row.Token != 0 || row.SkillId != null ||
                        row.SkillName != null ||
                        row.OriginalAbilityRawcode != "A03A" ||
                        row.SummonId <= 0 ||
                        !seenDollImpactIds.Add(row.SummonId) ||
                        string.IsNullOrWhiteSpace(row.EnemyId) ||
                        row.EnemyId.Length > 256 ||
                        pending.TargetId != "remaining-doll:" +
                            pending.CasterId + ":" + row.SummonId ||
                        row.DelaySeconds != .35 ||
                        (summons.TryGetValue(row.SummonId, out summoned) ?
                            summoned.OwnerId != row.OwnerId ||
                            summoned.SourceUnitRawcode != "h06O" ||
                            !summoned.IsVirtualAttackEffect ||
                            summoned.HasIndependentCombat ||
                            summoned.ExpiresAt <= pending.At ||
                            summoned.LaneRevision != row.LaneRevision ||
                            summoned.StoryLane != row.StoryLane :
                            activeLane))
                        throw new FormatException("슈가 투사체 예약의 실물·수명이 잘못됐습니다.");
                }
            }
            if (currentForms.Count != owners.FormWindups.Count ||
                currentPauses.Count != owners.AttackPauses.Count)
                throw new FormatException("원본 임시 기술 완료 예약이 누락됐습니다.");
            if (!state.HasSourceModelBasicAttacks &&
                (owners.AttackPauseSerial != 0 || owners.FormCastWake ||
                 owners.FormWindups.Count != 0 ||
                 owners.AttackPauses.Count != 0 ||
                 owners.FormNames.Count != 0 ||
                 owners.YamatoOpeningReady.Count != 0))
                throw new FormatException("비원본 전투에 임시 변신이 저장됐습니다.");
        }
    }
}
