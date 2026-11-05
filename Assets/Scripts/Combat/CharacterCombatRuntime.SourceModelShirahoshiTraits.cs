using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Source: original 2.323[C] JASS B0H/B0D/B0B/B0C/B0F/B0G and Bz6/Bz7.
    // Native Aimp A0L1/A0KZ are level markers; their actual outcomes are JASS.
    public static class Original323ShirahoshiCombatRules
    {
        public const int MaxPoseidonMana = 120;
        public const int FamiliarUnlockLevel = 16;
        public const int PoseidonEnhancementLevel = 24;
        public const double PoseidonRadius = 800;
        public const double FamiliarRadius = 500;
        public const double StrongFamiliarRadius = 575;

        public static double PoseidonDamage(double missingHealth, bool enhanced)
        {
            if (missingHealth < 0 || double.IsNaN(missingHealth) ||
                double.IsInfinity(missingHealth))
                throw new ArgumentOutOfRangeException(nameof(missingHealth));
            // B0B: 3350000 + missing HP * .06; B0C: 4500000 + * .08.
            return (enhanced ? 4500000 : 3350000) +
                missingHealth * (enhanced ? .08 : .06);
        }

        public static double FamiliarDamage(double missingHealth,
            int addedStrength, bool strong)
        {
            if (addedStrength < 0 || missingHealth < 0 ||
                double.IsNaN(missingHealth) || double.IsInfinity(missingHealth))
                throw new ArgumentOutOfRangeException();
            // B0G state 22/24 and B0F. Initial native hero STR is not
            // present in the pinned source rows: only the verified level-up
            // STR increment is applied; do not invent the missing base.
            return (strong ? 200000 : 150000) +
                addedStrength * 10000.0 +
                (strong ? missingHealth * .01 : 0);
        }
    }

    public sealed partial class CharacterCombatRuntime
    {
        private sealed class ShirahoshiAttackState
        {
            public int Mana;
            public double FamiliarReadyAt;
            public double FamiliarExpiresAt;
            public bool FamiliarStoryLane;
            public long FamiliarLaneRevision;
        }

        private readonly Dictionary<long, ShirahoshiAttackState>
            original323ShirahoshiAttacks = new Dictionary<long, ShirahoshiAttackState>();

        private void ForgetOriginal323Shirahoshi(long instanceId)
        {
            original323ShirahoshiAttacks.Remove(instanceId);
        }

        // B0H is registered by w5 for BOTH H08U and H0C1: attack declared,
        // not a second effect on successful hit. Keep the same mobile actor
        // identity across source forms and never restart the purchased trait.
        private void OnOriginal323ShirahoshiAttackDeclared(
            string attackerId, string rawcode, string targetId)
        {
            if (!UsesSourceModelBasicAttacks || !processingStep ||
                (rawcode != "H08U" && rawcode != "H0C1")) return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady ||
                !enemies.TryGetValue(targetId, out primary) ||
                !primary.Enemy.IsAlive) return;
            var growth = SourceHeroLevelStateOf(id);
            if (growth == null ||
                (growth.SourceRawcode != "H08U" &&
                 growth.SourceRawcode != "H0C1")) return;

            ShirahoshiAttackState state;
            if (!original323ShirahoshiAttacks.TryGetValue(id, out state))
            {
                state = new ShirahoshiAttackState();
                original323ShirahoshiAttacks.Add(id, state);
            }
            bool story = IsStoryDeployed(id);
            long revision = SourceModelLaneRevision(id);
            double x = primary.X, y = primary.Y, now = effectExecutor.Time;

            // B0H: native ManaN=120 with zero native mana regeneration; +1
            // on every attack until the next attack at exactly 120 casts B0D.
            if (state.Mana == Original323ShirahoshiCombatRules.MaxPoseidonMana)
            {
                state.Mana = 0;
                bool stronger = growth.ShirahoshiA0KZLevel == 2;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attackerId,
                    primary.Id, "시라호시 · 해왕류 다이브",
                    stronger ? "원본 B0D/B0C: 24레벨 강화 광역" :
                        "원본 B0D/B0B: 기본 광역", 0));
                ScheduleOriginalShirahoshiArea(id, story, revision, x, y,
                    .6, Original323ShirahoshiCombatRules.PoseidonRadius,
                    stronger ? 4500000 : 3350000, stronger ? .08 : .06,
                    "시라호시 · 해왕류 다이브");
            }
            else state.Mana++;

            // TR-15 12% '해류조정' and 4% '고대병기 포세이돈'
            // already run in SourceModelNextThirtyAttacks. Never roll
            // or damage them a second time from this native callback.

            // Bz7 upgrades A0L1 to level 2 only after the four-point Bz6
            // purchase AND reaching level 16. B0H's additional 5% branch
            // spawns one 15-second helper. Its first attack is 1.05s later,
            // then state 22 attacks can pulse (25% stronger).
            if (growth.ShirahoshiA0L1Level != 2) return;
            if (state.FamiliarExpiresAt <= now ||
                state.FamiliarStoryLane != story ||
                state.FamiliarLaneRevision != revision)
            {
                state.FamiliarReadyAt = 0;
                state.FamiliarExpiresAt = 0;
                if (SourceRoll1To(100) > 5) return;
                state.FamiliarReadyAt = now + 1.05;
                state.FamiliarExpiresAt = now + 15.05;
                state.FamiliarStoryLane = story;
                state.FamiliarLaneRevision = revision;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attackerId,
                    primary.Id, "시라호시 · 해왕류 소환",
                    "원본 B0G/A0L1 5%: 15초 해왕류 · 첫 공격 1.05초 후", 0));
                return;
            }
            if (state.FamiliarReadyAt > now) return;
            bool strong = SourceRoll1To(4) == 1;
            int addedStr = growth.AddedStrength;
            ScheduleOriginalShirahoshiFamiliar(id, story, revision,
                x, y, strong, addedStr);
        }

        private void ScheduleOriginalShirahoshiFamiliar(long actorId,
            bool storyLane, long laneRevision, double centerX, double centerY,
            bool strong, int addedStrength)
        {
            string actorKey = InstanceKey(actorId);
            effectExecutor.SchedulePracticeSourceAttack(actorKey,
                "shirahoshi-native:familiar-area", .05, () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(
                        actorId, storyLane, laneRevision)) return;
                    ApplyOriginalShirahoshiArea(actorId, storyLane,
                        centerX, centerY,
                        strong ? Original323ShirahoshiCombatRules.StrongFamiliarRadius :
                            Original323ShirahoshiCombatRules.FamiliarRadius,
                        strong ? 200000 : 150000,
                        strong ? .01 : 0, addedStrength * 10000.0,
                        strong ? "시라호시 · 해왕류 강화타" :
                            "시라호시 · 해왕류 공격");
                });
        }

        private void ScheduleOriginalShirahoshiArea(long actorId,
            bool storyLane, long laneRevision, double centerX, double centerY,
            double delay, double radius, double flat, double missingHpRatio,
            string name)
        {
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actorId),
                "shirahoshi-native:area", delay, () =>
                {
                    if (CanFinishSourceModelDelayedAttack(
                        actorId, storyLane, laneRevision))
                        ApplyOriginalShirahoshiArea(actorId, storyLane,
                            centerX, centerY, radius, flat, missingHpRatio, 0, name);
                });
        }

        // Bcb(... ATTACK_TYPE_NORMAL, DAMAGE_TYPE_UNIVERSAL) runs after the
        // JASS enemy filter, so the existing HP damage resolver receives the
        // universal packet directly, not a duplicated armor calculation.
        private void ApplyOriginalShirahoshiArea(long actorId, bool storyLane,
            double centerX, double centerY, double radius, double flat,
            double missingHpRatio, double addedStrengthDamage, string name)
        {
            string actorKey = InstanceKey(actorId);
            var nearby = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive ||
                    (storyLane ? target.Kind != CharacterEnemyKind.Story :
                    target.Kind != CharacterEnemyKind.Line &&
                    target.Kind != CharacterEnemyKind.Boss &&
                    target.Kind != CharacterEnemyKind.Enraged)) continue;
                double dx = target.X - centerX, dy = target.Y - centerY;
                if (dx * dx + dy * dy <= radius * radius)
                    nearby.Add(target);
            }
            nearby.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in nearby)
            {
                double missing = Math.Max(0,
                    target.Enemy.MaxHealth - target.Enemy.Health);
                double damage = flat + missing * missingHpRatio +
                    addedStrengthDamage;
                if (damage >= long.MaxValue)
                    throw new OverflowException("시라호시 원본 광역 피해가 표현 범위를 초과합니다.");
                long actual;
                if (!resolution.TryApplySkillDamage(actorKey,
                    target.Id, (long)Math.Floor(damage), out actual)) continue;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, actorKey, target.Id,
                    name, "원본 B0H/B0D/B0G · NORMAL/UNIVERSAL" +
                        (addedStrengthDamage > 0 ? " · 기초 STR 미확인 증가분 반영" : ""),
                    actual));
                if (!target.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated, actorKey,
                        target.Id, name, "시라호시 원본 광역 처치", actual));
            }
        }
    }
}
