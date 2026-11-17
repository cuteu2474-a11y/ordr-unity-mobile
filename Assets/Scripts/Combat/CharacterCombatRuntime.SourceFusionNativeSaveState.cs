using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceFusionClockReceipt
    {
        public long OwnerId;
        public double Until;
    }

    [Serializable]
    public sealed class SourceFusionCountReceipt
    {
        public long OwnerId;
        public int Count;
    }

    [Serializable]
    public sealed class SourceFusionEnemyLevelReceipt
    {
        public string EnemyId;
        public int Level;
    }

    [Serializable]
    public sealed class SourceFusionEnemyFactorReceipt
    {
        public string EnemyId;
        public double Factor;
    }

    [Serializable]
    public sealed class SourceFusionNativeWorldCheckpoint
    {
        public int Version = 1;
        public long NextPulseId;
        public List<SourceFusionClockReceipt> RogerCooldowns =
            new List<SourceFusionClockReceipt>();
        public List<SourceFusionCountReceipt> TesoroCounts =
            new List<SourceFusionCountReceipt>();
        public List<SourceFusionEnemyLevelReceipt> EnemyLevels =
            new List<SourceFusionEnemyLevelReceipt>();
        public List<SourceFusionEnemyFactorReceipt> EnemyMagicFactors =
            new List<SourceFusionEnemyFactorReceipt>();
        public List<long> ActiveSharkOwners = new List<long>();
    }

    [Serializable]
    public sealed class SourceFusionNativePulse
    {
        public string Kind;
        public long Serial;
        public long OwnerId;
        public string TargetId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public double DelaySeconds;
        public double CenterX;
        public double CenterY;
        public long GoldCap;
        public bool ThirdImpact;
        public long AllyId;
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string FusionRogerStunKind = "fusion-roger-stun-v1";
        private const string FusionRogerImpactKind = "fusion-roger-impact-v1";
        private const string FusionTesoroImpactKind = "fusion-tesoro-impact-v1";
        private const string FusionSharkCycleKind = "fusion-shark-cycle-v1";
        private const string FusionSharkAllyKind = "fusion-shark-ally-v1";
        private long sourceFusionNativePulseSerial;

        private static bool FusionFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static string FusionPulseTarget(long id) =>
            "fusion-native:" + id.ToString(CultureInfo.InvariantCulture);

        private static string EncodeFusionPulse(SourceFusionNativePulse p) =>
            JsonUtility.ToJson(p);

        private static SourceFusionNativePulse DecodeFusionPulse(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 4096)
                return null;
            try
            {
                var p = JsonUtility.FromJson<SourceFusionNativePulse>(payload);
                return p != null && EncodeFusionPulse(p) == payload ? p : null;
            }
            catch (ArgumentException) { return null; }
        }

        private void ScheduleSourceFusionNativePulse(SourceFusionNativePulse pulse,
            double delay, SpatialCombatEnemy originalTarget = null)
        {
            if (pulse == null || sourceFusionNativePulseSerial == long.MaxValue)
                throw new OverflowException("원본 Fusion 예약 ID 한도를 초과했습니다.");
            pulse.Serial = ++sourceFusionNativePulseSerial;
            pulse.CastAt = effectExecutor.Time;
            pulse.DelaySeconds = delay;
            var callback = CreateSourceFusionNativePulse(pulse, originalTarget);
            if (callback == null)
                throw new NotSupportedException(
                    "Fusion 예약 시점의 원본 시전자/목표를 연결할 수 없습니다.");
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(pulse.OwnerId), FusionPulseTarget(pulse.Serial),
                delay, pulse.Kind, EncodeFusionPulse(pulse), callback);
        }

        private Action CreateSourceFusionNativePulse(SourceFusionNativePulse p,
            SpatialCombatEnemy originalTarget = null)
        {
            CharacterInstance actor;
            if (p == null || !characters.TryGetValue(p.OwnerId, out actor))
                return null;
            long actorId = p.OwnerId;
            string actorKey = InstanceKey(actorId);
            string caster = actorKey;
            bool lane = p.StoryLane;
            bool story = lane;
            long laneRevision = p.LaneRevision;
            double centerX = p.CenterX, centerY = p.CenterY;
            string targetId = p.TargetId;
            bool third = p.ThirdImpact;
            double goldPower = (double)p.GoldCap * p.GoldCap / 250000.0;
            if (p.Kind == FusionRogerStunKind)
                return () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actorId, lane, laneRevision))
                        return;
                    foreach (var enemy in enemies.Values)
                    {
                        if (!enemy.Enemy.IsAlive ||
                            !CanAttackInThisLane(actor, enemy)) continue;
                        double dx = enemy.X - centerX, dy = enemy.Y - centerY;
                        if (dx * dx + dy * dy > 800 * 800) continue;
                        double duration = enemy.Kind == CharacterEnemyKind.Boss ||
                            enemy.Kind == CharacterEnemyKind.Story ? 1 : 2;
                        string source = "practice-native:IM01:A14V:" + actorId;
                        enemy.Enemy.Status.RemoveSource(source);
                        enemy.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, duration));
                    }
                    // A202's own AHtb primary effect lasts 3 seconds.
                    SpatialCombatEnemy primary;
                    if (enemies.TryGetValue(targetId, out primary) &&
                        primary.Enemy.IsAlive && CanAttackInThisLane(actor, primary))
                    {
                        string source = "practice-native:IM01:A202:" + actorId;
                        primary.Enemy.Status.RemoveSource(source);
                        primary.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, 3));
                    }
                };
            if (p.Kind == FusionRogerImpactKind)
            {
                SpatialCombatEnemy target = originalTarget;
                if (target == null &&
                    !enemies.TryGetValue(targetId, out target)) return null;
                return () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actorId, lane, laneRevision))
                        return;
                    // Bcc samples a real random multiplier in [1,2] once,
                    // then sends one CHAOS/NORMAL 800-area packet.
                    double multiplier = 1 +
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0;
                    ApplySourceModelAreaPacket(actorKey, lane, target, 800,
                        4000000 * multiplier, Ordr323AttackType.Chaos,
                        "로저 · 인페르노 광역");
                    SpatialCombatEnemy primary;
                    if (!enemies.TryGetValue(targetId, out primary) ||
                        !primary.Enemy.IsAlive ||
                        !CanAttackInThisLane(actor, primary)) return;
                    if (primary.Kind != CharacterEnemyKind.Boss &&
                        primary.Kind != CharacterEnemyKind.Story) return;
                    // Native B8c: boss = 40% CURRENT HP; story = 25% MAX HP.
                    // Apply through existing damage/death accounting; final
                    // native universal mitigation is still unverified.
                    double percent = primary.Kind == CharacterEnemyKind.Boss ?
                        primary.Enemy.Health * .40 : primary.Enemy.MaxHealth * .25;
                    long amount = (long)Math.Min(long.MaxValue - 1,
                        Math.Floor(Math.Max(0, percent)));
                    ApplySourceModelFixedPrimaryPacket(actorKey, primary, amount,
                        "로저 · 인페르노 추가 타격 (CHAOS/UNIVERSAL 모델)");
                };
            }
            if (p.Kind == FusionTesoroImpactKind)
                return () =>
                {
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, story, laneRevision))
                        return;
                    double radius = third ? 325 : 425;
                    var victims = new List<SpatialCombatEnemy>();
                    foreach (var enemy in enemies.Values)
                    {
                        if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                            continue;
                        double dx = enemy.X - centerX, dy = enemy.Y - centerY;
                        if (dx * dx + dy * dy <= radius * radius)
                            victims.Add(enemy);
                    }
                    victims.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
                    string name = "테조로 · 황금 충격";
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, caster,
                        targetId, name,
                        "MODEL_ONLY B8S/B8L: 공격 시 골드 상한250000, 0.45초 지연·" +
                        radius + " 범위, 재화 차감 없음", 0));
                    foreach (var victim in victims)
                    {
                        // The ordinary line/boss spawn writes the current
                        // LL level into JD=A11S; snapshot that factor per
                        // victim, not once at the center or a hard-coded 0.
                        double nativeFactor =
                            SourceModelNativeTesoroCorrection(victim);
                        double normalMultiplier = 1 +
                            sourceModelRules.NextScriptRoll(500001) / 1000000.0;
                        ApplySourceModelAreaPacket(caster, story, victim, 0,
                            (third ? 160000 : 400000) * normalMultiplier,
                            Ordr323AttackType.Spells, name);
                        if (!victim.Enemy.IsAlive) continue;
                        ApplySourceModelAreaPacket(caster, story, victim, 0,
                            goldPower * nativeFactor * 1.5,
                            Ordr323AttackType.Siege, name);
                        if (!third && victim.Enemy.IsAlive)
                        {
                            double missing = Math.Max(0,
                                victim.Enemy.MaxHealth - victim.Enemy.Health);
                            ApplySourceModelAreaPacket(caster, story, victim, 0,
                                missing * .01 * .45 * goldPower / 25000.0 *
                                    nativeFactor,
                                Ordr323AttackType.Siege, name);
                        }
                    }
                    SpatialCombatEnemy center;
                    if (enemies.TryGetValue(targetId, out center) &&
                        center.Enemy.IsAlive && CanAttackInThisLane(actor, center))
                        ApplySourceModelAreaPacket(caster, story, center, 0,
                            center.Enemy.MaxHealth * (third ? .14 : .18) *
                                SourceModelNativeTesoroCorrection(center),
                            Ordr323AttackType.Siege, name + " · 주 대상");
                };
            if (p.Kind == FusionSharkCycleKind)
            {
                string casterId = actorKey;
                return () =>
                {
                    CharacterInstance cycleActor;
                    if (!UsesSourceModelBasicAttacks ||
                        game.Phase != SessionPhase.Running ||
                        SourceModelFusionNativeBalance(
                            Original323FusionNativeFollowupLedger.SharkCenId) <= 0 ||
                        !characters.TryGetValue(actorId, out cycleActor) ||
                        !cycleActor.AttackReady ||
                        cycleActor.Catalog.Id != "SR-03")
                        return;

                    // CEO's native filter is Ji[owner] &&
                    // GetUnitPointValue(ally)>100. The mobile player owns
                    // its active roster; unknown UnitData points fail closed.
                    ApplySourceModelFusionSharkInnerFire(actorId, actorId);
                    var candidates = new List<long>();
                    var position = roster.Get(actorId);
                    foreach (var ally in characters.Values)
                    {
                        if (ally.Id == actorId || !ally.AttackReady ||
                            !CanShareAllyCombatLane(ally.Id, actorId))
                            continue;
                        var point = sourceModelNativePointValues == null ? null :
                            sourceModelNativePointValues.Find(ally.Catalog.Id);
                        if (point == null || !point.UnitPointValue.HasValue ||
                            point.UnitPointValue.Value <= 100) continue;
                        var other = roster.Get(ally.Id);
                        double dx = position.X - other.X;
                        double dy = position.Y - other.Y;
                        if (dx * dx + dy * dy <= 450 * 450)
                            candidates.Add(ally.Id);
                    }
                    for (int selected = 0; selected < 2 && candidates.Count > 0;
                         selected++)
                    {
                        int index = sourceModelRules.NextScriptRoll(candidates.Count);
                        long chosenAllyId = candidates[index];
                        candidates.RemoveAt(index);
                        double orderDelay = (selected + 1) * .1;
                        bool isStory = IsStoryDeployed(actorId);
                        long revision = SourceModelLaneRevision(actorId);
                        ScheduleSourceFusionNativePulse(new SourceFusionNativePulse {
                            Kind = FusionSharkAllyKind, OwnerId = actorId,
                            AllyId = chosenAllyId, StoryLane = isStory,
                            LaneRevision = revision
                        }, orderDelay);
                    }
                    ScheduleSourceModelFusionSharkInnerFire(actorId, 19.9);
                };
            }
            if (p.Kind == FusionSharkAllyKind)
                return () =>
                {
                    CharacterInstance owner;
                    CharacterInstance target;
                    if (!CanFinishSourceModelDelayedAttack(actorId,
                            p.StoryLane, p.LaneRevision) ||
                        !characters.TryGetValue(actorId, out owner) ||
                        !owner.AttackReady ||
                        !characters.TryGetValue(p.AllyId, out target) ||
                        !target.AttackReady ||
                        !CanShareAllyCombatLane(actorId, p.AllyId)) return;
                    ApplySourceModelFusionSharkInnerFire(actorId, p.AllyId);
                };
            return null;
        }

        private Action RestoreSourceFusionNativeTimedEffect(
            string kind, string payload, string casterId, string targetId)
        {
            if (kind != FusionRogerStunKind &&
                kind != FusionRogerImpactKind &&
                kind != FusionTesoroImpactKind &&
                kind != FusionSharkCycleKind &&
                kind != FusionSharkAllyKind) return null;
            var p = DecodeFusionPulse(payload);
            CharacterInstance actor;
            if (p == null || p.Kind != kind ||
                !characters.TryGetValue(p.OwnerId, out actor) ||
                casterId != InstanceKey(p.OwnerId) ||
                targetId != FusionPulseTarget(p.Serial) ||
                (kind == FusionTesoroImpactKind ? actor.Catalog.Id != "E-11"
                 : kind == FusionSharkCycleKind || kind == FusionSharkAllyKind
                    ? actor.Catalog.Id != "SR-03"
                    : actor.Catalog.Id != "I-04"))
                return null;
            // CEN/CEP's self-renewing tick follows its living owner even
            // after lane changes; unlike an in-flight ally order, it does
            // not carry the old-lane cancellation contract.
            if (kind == FusionSharkCycleKind)
                return CreateSourceFusionNativePulse(p);
            if (p.LaneRevision < SourceModelLaneRevision(p.OwnerId))
                return () => { };
            if (p.LaneRevision != SourceModelLaneRevision(p.OwnerId) ||
                p.StoryLane != IsStoryDeployed(p.OwnerId))
                return null;
            return CreateSourceFusionNativePulse(p);
        }
    }
}
