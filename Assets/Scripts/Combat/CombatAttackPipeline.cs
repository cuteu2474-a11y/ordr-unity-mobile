using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public sealed class BasicDamageRequest
    {
        public string UnitInstanceId { get; }
        public string UnitRawCode { get; }
        public string TargetInstanceId { get; }
        public long NominalAttackPower { get; }
        public DamageSchool School { get; }
        public DamageTraits Traits { get; }

        public BasicDamageRequest(string unitInstanceId, UnitCombatSpec unit, string targetInstanceId)
        {
            if (string.IsNullOrWhiteSpace(unitInstanceId) || string.IsNullOrWhiteSpace(targetInstanceId))
                throw new ArgumentException("공격자와 대상 ID가 필요합니다.");
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            UnitInstanceId = unitInstanceId;
            UnitRawCode = unit.UnitRawCode;
            TargetInstanceId = targetInstanceId;
            NominalAttackPower = unit.BasicAttack.NominalAttackPower;
            School = unit.BasicAttack.School;
            Traits = unit.BasicAttack.Traits;
        }
    }

    // 워크래프트 III 공격 타입/방어력·마법 저항·특성강화 및 반올림 규칙을
    // 원본에서 검증한 뒤 공급해야 하는 피해 계산기 인터페이스.
    public interface IOriginalDamageResolver
    {
        long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target);
    }

    public sealed class BasicAttackResult
    {
        public string AttackerId { get; }
        public string TargetId { get; }
        public long ActualDamage { get; }
        public IReadOnlyList<string> ActivatedSkillRawCodes { get; }

        internal BasicAttackResult(string attackerId, string targetId, long damage,
            List<string> activatedSkills)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            ActualDamage = damage;
            ActivatedSkillRawCodes = activatedSkills.AsReadOnly();
        }
    }

    // 기존 CombatScheduler + CombatResolution + 개인별 스킬 트리거를 연결한 실제 기본타격 경로.
    // 발동한 스킬의 AoE·오라·피해 효과 적용은 후속 엔진으로 연결해야 한다.
    public sealed class CombatAttackPipeline
    {
        private sealed class RegisteredUnit
        {
            public UnitCombatSpec Spec;
            public long NextEventSequence;
        }

        private readonly CombatScheduler scheduler;
        private readonly CombatResolution resolution;
        private readonly CombatSkillTriggerEngine skillTriggers;
        private readonly IOriginalDamageResolver damageResolver;
        private readonly Dictionary<string, RegisteredUnit> units =
            new Dictionary<string, RegisteredUnit>(StringComparer.Ordinal);

        public CombatAttackPipeline(CombatScheduler scheduler, CombatResolution resolution,
            CombatSkillTriggerEngine skillTriggers, IOriginalDamageResolver damageResolver)
        {
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            this.resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            this.skillTriggers = skillTriggers ?? throw new ArgumentNullException(nameof(skillTriggers));
            this.damageResolver = damageResolver ?? throw new ArgumentNullException(nameof(damageResolver));
            if (scheduler.Time != skillTriggers.Time)
                throw new ArgumentException("공격과 스킬 시계가 일치해야 합니다.");
        }

        public void RegisterUnit(string instanceId, UnitCombatSpec spec, double firstAttackDelay)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("유닛 인스턴스 ID가 필요합니다.");
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (units.ContainsKey(instanceId) || skillTriggers.HasUnit(instanceId))
                throw new InvalidOperationException("이미 등록된 인스턴스입니다.");
            scheduler.Register(instanceId, firstAttackDelay);
            bool triggerRegistered = false;
            try
            {
                skillTriggers.RegisterUnit(instanceId, spec);
                triggerRegistered = true;
                units.Add(instanceId, new RegisteredUnit { Spec = spec });
            }
            catch
            {
                scheduler.Remove(instanceId);
                if (triggerRegistered) skillTriggers.RemoveUnit(instanceId);
                throw;
            }
        }

        public void RemoveUnit(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("인스턴스 ID가 필요합니다.");
            units.Remove(instanceId);
            scheduler.Remove(instanceId);
            skillTriggers.RemoveUnit(instanceId);
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (scheduler.Time != skillTriggers.Time)
                throw new InvalidOperationException("공격 시계와 스킬 시계가 어긋났습니다.");
            if (double.IsInfinity(scheduler.Time + seconds) ||
                double.IsInfinity(skillTriggers.Time + seconds))
                throw new OverflowException("시간 진행이 불가능합니다.");
            scheduler.Advance(seconds);
            skillTriggers.Advance(seconds);
        }

        public bool TryBasicAttack(string attackerInstanceId,
            IEnumerable<TargetCandidate> candidates, out BasicAttackResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(attackerInstanceId))
                throw new ArgumentException("공격자 인스턴스 ID가 필요합니다.");
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            RegisteredUnit unit;
            if (!units.TryGetValue(attackerInstanceId, out unit))
                throw new InvalidOperationException("등록되지 않은 공격자입니다.");
            if (!scheduler.IsReady(attackerInstanceId)) return false;

            // 화면에 남아 있는 오래된 좌표 정보는 실제 살아 있는 적으로 취급하지 않는다.
            var validCandidates = new List<TargetCandidate>();
            foreach (var candidate in candidates)
            {
                if (candidate == null) throw new ArgumentException("null 대상은 허용하지 않습니다.");
                if (!candidate.Alive) continue;
                var tracked = resolution.GetEnemy(candidate.Id);
                if (tracked != null && tracked.IsAlive) validCandidates.Add(candidate);
            }
            string targetId = CombatTargeting.Select(validCandidates,
                unit.Spec.BasicAttack.Range, TargetPriority.Nearest);
            if (targetId == null) return false;
            var enemy = resolution.GetEnemy(targetId);
            if (enemy == null || !enemy.IsAlive) return false;

            // 피해 계산 오류가 발생하면 공격 쿨타임은 소비하지 않는다.
            long resolved = damageResolver.ResolveBasicDamage(
                new BasicDamageRequest(attackerInstanceId, unit.Spec, targetId), enemy);
            if (resolved < 0) throw new InvalidOperationException("최종 피해량이 음수입니다.");
            if (unit.NextEventSequence == long.MaxValue)
                throw new OverflowException("전투 이벤트 순번을 더 늘릴 수 없습니다.");
            long actual;
            if (!resolution.TryAttack(attackerInstanceId, targetId,
                unit.Spec.BasicAttack.IntervalSeconds, resolved, out actual)) return false;
            long eventSequence = ++unit.NextEventSequence;
            var activated = new List<string>();
            foreach (var skill in unit.Spec.Skills)
            {
                SkillTriggerKind trigger = skill.Trigger;
                if (trigger != SkillTriggerKind.OnAttack && trigger != SkillTriggerKind.OnHit)
                    continue;
                if (trigger == SkillTriggerKind.OnHit && actual <= 0)
                    continue;
                if (skillTriggers.TryActivate(attackerInstanceId, skill.RawCode,
                    trigger, eventSequence))
                    activated.Add(skill.RawCode);
            }
            result = new BasicAttackResult(attackerInstanceId, targetId, actual, activated);
            return true;
        }
    }
}
