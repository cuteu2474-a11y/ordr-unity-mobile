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
        }

        private readonly List<PracticeNamiZone> sourceNamiZones = new List<PracticeNamiZone>();
        private readonly HashSet<string> sourceNamiAuraKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> sourcePracticeA04RLevels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private long sourceFollowupSequence;

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
                if (sourceFollowupSequence == long.MaxValue)
                    throw new OverflowException("연습 소환 순번 초과");
                sourceNamiZones.Add(new PracticeNamiZone {
                    Source = "practice-nami:A089:" + (++sourceFollowupSequence),
                    X = primary.X, Y = primary.Y, Story = IsStoryDeployed(actor.Id)
                });
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    primary.Id, "나미 · 크리마텍트 오라",
                    "MODEL_ONLY e00K/A089: 위치형 적 이동속도 -5%, 범위 450 · 지속/중첩 원본 미검증", 0));
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
            effectExecutor.SchedulePracticeSourceAttack(actorId,
                "practice-inferno:" + actorId + ":" + spec.AbilityRawcode,
                spec.ImpactDelaySeconds, () => {
                    if (!characters.ContainsKey(actor.Id)) return;
                    foreach (var enemy in enemies.Values)
                    {
                        if (!enemy.Enemy.IsAlive ||
                            !CanAttackInThisLane(actor, enemy)) continue;
                        double dx = enemy.X - centerX, dy = enemy.Y - centerY;
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
                        string source = "practice-inferno:" + spec.AbilityRawcode + ":" + actorId;
                        enemy.Enemy.Status.RemoveSource(source);
                        enemy.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, spec.NormalStunSeconds));
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.SkillActivated, actorId,
                            enemy.Id, actor.Catalog.Name + " 인페르노",
                            "MODEL_ONLY " + spec.AbilityRawcode + " 원본 객체 기절 " +
                            spec.NormalStunSeconds + "초 / " + spec.Radius +
                            " 범위 · 네이티브 면역 미검증", 0));
                    }
                });
        }

        private void ScheduleSourceModelSentinelFaerie(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            string actorId = InstanceKey(actor.Id);
            // CCg creates e0LE/A10I and issues faeriefire to RN. Afae has
            // DataA1=50 flat armor loss, Dur1=60; native cast execution can
            // follow the synchronous JASS Bcb, so use the map's .01 model
            // event interval rather than altering the same instant's AoE.
            effectExecutor.SchedulePracticeSourceAttack(actorId, target.Id,
                .01, () => {
                    if (!characters.ContainsKey(actor.Id) ||
                        !target.Enemy.IsAlive) return;
                    string source = "practice-native:A10I";
                    target.Enemy.Status.RemoveSource(source);
                    target.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.ArmorReduction,
                        CombatEffectOrigin.TriggeredSkill,
                        SourceModelLegacyNativeFollowupCatalog.SentinelArmorReduction,
                        SourceModelLegacyNativeFollowupCatalog.SentinelFaerieDuration));
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, actorId,
                        target.Id, "센토 이스즈 · 페어리 파이어",
                        "MODEL_ONLY e0LE/A10I: 대상 방어 -50 / 60초, 원본 시전 지연·면역 미검증", 0));
                });
        }

        private void RefreshSourceModelNamiZones()
        {
            if (!UsesSourceModelBasicAttacks) return;
            foreach (var status in statuses.Values)
                foreach (var key in sourceNamiAuraKeys)
                    status.RemoveSource(key);
            sourceNamiAuraKeys.Clear();
            foreach (var zone in sourceNamiZones)
            {
                sourceNamiAuraKeys.Add(zone.Source);
                foreach (var target in enemies.Values)
                {
                    if (!target.Enemy.IsAlive ||
                        (zone.Story ? target.Kind != CharacterEnemyKind.Story :
                            target.Kind != CharacterEnemyKind.Line &&
                            target.Kind != CharacterEnemyKind.Boss)) continue;
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
