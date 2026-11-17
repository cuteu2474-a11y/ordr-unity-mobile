using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class PracticeNamiZone
        {
            public string Source;
            public double X;
            public double Y;
            public bool Story;
            public double ExpiresAt;
        }

        private readonly List<PracticeNamiZone> sourceNamiZones = new List<PracticeNamiZone>();
        private readonly HashSet<string> sourceNamiAuraKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> sourcePracticeA04RLevels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private long sourceFollowupSequence;
        // UnitData.slk points: all registered original o*** wave rows are
        // 100 except these 200-point rows. Never infer points for story/creeps.
        private readonly Dictionary<string, int> sourcePracticePointValues =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> Source200PointWaveRawcodes =
            new HashSet<string>(StringComparer.Ordinal) {
                "o001", "o00J", "o01V", "o01W", "o01X", "o01Y", "o01Z",
                "o02B", "o02C", "o02D", "o02E", "o02F", "o02J", "o02K",
                "o02L", "o02M", "o02N", "o02Q", "o02R", "o02S", "o031", "o032"
            };

        // The 68 pinned ORDR line/boss object rawcodes ALL list A04R in
        // Units/UnitAbilities.slk (initial native level 1). We register only
        // those names supplied by SourceModelPracticeBattle, never a guessed
        // starting level for Story/Challenge/unknown enemies. This is kept
        // distinct from CombatEnemy.OriginalA04RLevel (live-only witness).
        internal void RegisterSourceModelWaveA04R(string enemyId, string originalRawcode)
        {
            if (!UsesSourceModelBasicAttacks || string.IsNullOrWhiteSpace(enemyId) ||
                originalRawcode == null || originalRawcode.Length != 4 ||
                originalRawcode[0] != 'o' || sourcePracticeA04RLevels.ContainsKey(enemyId))
                throw new InvalidOperationException("연습 원본 웨이브 A04R 객체 자료 불일치");
            sourcePracticeA04RLevels.Add(enemyId, 1);
            sourcePracticePointValues.Add(enemyId,
                Source200PointWaveRawcodes.Contains(originalRawcode) ? 200 : 100);
        }

        internal int? PracticeSourcePointValue(string enemyId)
        {
            int points;
            return sourcePracticePointValues.TryGetValue(enemyId ?? "", out points)
                ? (int?)points : null;
        }

        internal int? PracticeSourceA04RLevel(string enemyId)
        {
            int level;
            return sourcePracticeA04RLevels.TryGetValue(enemyId ?? "", out level)
                ? (int?)level : null;
        }

        private void BeforeSourceModelLegacyProc(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec spec)
        {
            if (spec.CatalogId == "S-03")
            {
                // Bnt creates the e00K/A089 helper at the target location.
                // The S-03 character tooltip independently specifies a 3s
                // aftershock; Bnt alone does not describe timed-life removal.
                // Keep this temporary area distinct from any innate A088 buff.
                if (sourceFollowupSequence == long.MaxValue)
                    throw new OverflowException("연습 소환 순번 초과");
                sourceNamiZones.Add(new PracticeNamiZone {
                    Source = "practice-nami:A089:" + (++sourceFollowupSequence),
                    X = primary.X, Y = primary.Y, Story = IsStoryDeployed(actor.Id),
                    ExpiresAt = effectExecutor.Time +
                        SourceModelLegacyNativeFollowupCatalog.NamiAftershockDurationSeconds
                });
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    primary.Id, "나미 · 크리마텍트 오라",
                    "Bnt→e00K/A089: 이동속도 -5%, 반경 450, 여진 3초(도감 기준) · 원본 버프 중첩/네이티브 면역 미확정", 0));
                RefreshSourceModelNamiZones();
            }
            if (spec.CatalogId == "RN-04")
            {
                int current, next;
                if (sourcePracticeA04RLevels.TryGetValue(primary.Id, out current) &&
                    Ordr323OriginalA04R.TryPlanIncrease(current,
                        SourceModelLegacyNativeFollowupCatalog.SentinelA04RLevelIncrease,
                        out next))
                {
                    sourcePracticeA04RLevels[primary.Id] = next;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                        primary.Id, "센토 이스즈 · A04R 레벨 누적",
                        "MODEL_ONLY Bd8(A04R): " + current + "→" + next +
                        " / isr2 원본 필드, 최종 네이티브 피해 환산 미검증", next));
                }
                else
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                        primary.Id, "센토 이스즈 · A04R 레벨 누적",
                        "Bd8(A04R)+15: 원본 기본 레벨 없음 또는 26단계 초과, 효과 보류", 0));
            }
        }

        private void AfterSourceModelLegacyProc(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec spec)
        {
            SourceModelInfernoFollowup inferno;
            if (SourceModelLegacyNativeFollowupCatalog.TryGet(spec.CatalogId,
                    out inferno))
                ScheduleSourceModelInferno(actor, primary, inferno);
            if (spec.CatalogId == "RN-04")
                ScheduleSourceModelSentinelFaerie(actor, primary);
        }

        private void ScheduleSourceModelInferno(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceModelInfernoFollowup spec)
        {
            // JASS fixes the cast POINT at the originally attacked location.
            // AUin DataB1=.01 (native impact delay), DataA1=0 extra damage,
            // Dur1 is normal stun; h0C5 has no combat attack or spells cast.
            double centerX = primary.X, centerY = primary.Y;
            string actorId = InstanceKey(actor.Id);
            bool sourceStoryLane = IsStoryDeployed(actor.Id);
            long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
            var pulse = new SourceNativeDelayedEffectPayload {
                OwnerId = actor.Id, StoryLane = sourceStoryLane,
                LaneRevision = sourceLaneRevision,
                CastAt = effectExecutor.Time,
                CenterX = centerX, CenterY = centerY,
                OriginalAbilityRawcode = spec.AbilityRawcode
            };
            effectExecutor.ScheduleRestorableSourceAttack(actorId,
                "practice-inferno:" + actorId + ":" + spec.AbilityRawcode,
                spec.ImpactDelaySeconds, NativeInfernoKind,
                SerializeNativeDelayed(pulse),
                CreateNativeInfernoPulse(pulse));
        }

        private void ScheduleSourceModelSentinelFaerie(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            string actorId = InstanceKey(actor.Id);
            bool sourceStoryLane = IsStoryDeployed(actor.Id);
            long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
            // CCg creates e0LE/A10I and issues faeriefire to RN. Afae has
            // DataA1=50 flat armor loss, Dur1=60; native cast execution can
            // follow the synchronous JASS Bcb, so use the map's .01 model
            // event interval rather than altering the same instant's AoE.
            var pulse = new SourceNativeDelayedEffectPayload {
                OwnerId = actor.Id, StoryLane = sourceStoryLane,
                LaneRevision = sourceLaneRevision,
                CastAt = effectExecutor.Time, EnemyId = target.Id,
                OriginalAbilityRawcode =
                    SourceModelLegacyNativeFollowupCatalog.SentinelFaerieAbility
            };
            effectExecutor.ScheduleRestorableSourceAttack(actorId, target.Id,
                .01, NativeSentinelFaerieKind,
                SerializeNativeDelayed(pulse),
                CreateNativeSentinelFaeriePulse(pulse));
        }

        private bool HasUnexpiredSourceNamiZones()
        {
            foreach (var zone in sourceNamiZones)
                if (zone.ExpiresAt > effectExecutor.Time) return true;
            return false;
        }

        private void RefreshSourceModelNamiZones()
        {
            if (!UsesSourceModelBasicAttacks) return;
            // Remove prior aura leases FIRST. Expired zones must not leave
            // permanent 0-duration enemy statuses behind.
            foreach (var status in statuses.Values)
                foreach (var key in sourceNamiAuraKeys)
                    status.RemoveSource(key);
            sourceNamiAuraKeys.Clear();
            sourceNamiZones.RemoveAll(zone =>
                effectExecutor.Time >= zone.ExpiresAt);
            foreach (var zone in sourceNamiZones)
            {
                sourceNamiAuraKeys.Add(zone.Source);
                foreach (var target in enemies.Values)
                {
                    if (!target.Enemy.IsAlive ||
                        (zone.Story ? target.Kind != CharacterEnemyKind.Story :
                            target.Kind != CharacterEnemyKind.Line &&
                            target.Kind != CharacterEnemyKind.Boss &&
                            target.Kind != CharacterEnemyKind.Enraged)) continue;
                    double dx = target.X - zone.X, dy = target.Y - zone.Y;
                    if (dx * dx + dy * dy >
                        SourceModelLegacyNativeFollowupCatalog.NamiZoneRadius *
                        SourceModelLegacyNativeFollowupCatalog.NamiZoneRadius) continue;
                    target.Enemy.Status.Apply(new CombatEffect(zone.Source,
                        CombatEffectType.MovementSlowPercent,
                        CombatEffectOrigin.Aura,
                        SourceModelLegacyNativeFollowupCatalog.NamiMovementSlowPercent, 0));
                }
            }
        }
    }
}
