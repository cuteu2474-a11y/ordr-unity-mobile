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

        public BasicDamageRequest(string unitInstanceId, UnitCombatSpec unit,
            string targetInstanceId)
            : this(unitInstanceId, unit == null ? null : unit.UnitRawCode,
                unit == null ? null : unit.BasicAttack, targetInstanceId) { }

        public BasicDamageRequest(string unitInstanceId, string unitRawCode,
            BasicAttackSpec effectiveAttack, string targetInstanceId)
        {
            if (string.IsNullOrWhiteSpace(unitInstanceId) ||
                string.IsNullOrWhiteSpace(targetInstanceId))
                throw new ArgumentException("공격자와 대상 ID가 필요합니다.");
            if (effectiveAttack == null)
                throw new ArgumentNullException(nameof(effectiveAttack));
            UnitInstanceId = unitInstanceId;
            UnitRawCode = unitRawCode;
            TargetInstanceId = targetInstanceId;
            NominalAttackPower = effectiveAttack.NominalAttackPower;
            School = effectiveAttack.School;
            Traits = effectiveAttack.Traits;
        }
    }

    // 원작 버프/변신이 평타 공격력·속도·사거리를 바꾸면 외부 검증 정책이 계산한다.
    public interface IOriginalUnitAttackModifiers
    {
        BasicAttackSpec Resolve(string attackerId, BasicAttackSpec originalStats,
            IReadOnlyList<CombatEffect> attackerEffects);
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
        private IOriginalUnitAttackModifiers originalModifierRules;
        private Func<string, IReadOnlyList<CombatEffect>> getAttackerEffects;
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

        public void ConfigureVerifiedModifiers(IOriginalUnitAttackModifiers rules,
            Func<string, IReadOnlyList<CombatEffect>> effectLookup)
        {
            originalModifierRules = rules ?? throw new ArgumentNullException(nameof(rules));
            getAttackerEffects = effectLookup ??
                throw new ArgumentNullException(nameof(effectLookup));
        }

        public BasicAttackSpec EffectiveAttack(string instanceId)
        {
            RegisteredUnit unit;
            if (!units.TryGetValue(instanceId, out unit))
                throw new InvalidOperationException("미등록 공격자");
            if (originalModifierRules == null) return unit.Spec.BasicAttack;
            return originalModifierRules.Resolve(instanceId, unit.Spec.BasicAttack,
                getAttackerEffects(instanceId)) ??
                throw new InvalidOperationException("공격력 보정 수치 미확인");
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

        // 원작 조건이 게임 이벤트에서 충족되었음을 별도 검증한 후에만 호출.
        public bool TryActivateVerifiedCondition(string instanceId, string skillId)
        {
            RegisteredUnit unit;
            if (!units.TryGetValue(instanceId, out unit))
                throw new InvalidOperationException("미등록 캐릭터 이벤트");
            bool found = false;
            foreach (var skill in unit.Spec.Skills)
                if (skill.RawCode == skillId &&
                    skill.Trigger == SkillTriggerKind.VerifiedCondition)
                { found = true; break; }
            if (!found) return false;
            if (unit.NextEventSequence == long.MaxValue)
                throw new OverflowException("스킬 이벤트 순번 초과");
            return skillTriggers.TryActivate(instanceId, skillId,
                SkillTriggerKind.VerifiedCondition, ++unit.NextEventSequence);
        }

        public double? NextPeriodicSkillAt(string instanceId, string skillId)
        {
            RegisteredUnit unit;
            if (!units.TryGetValue(instanceId, out unit)) return null;
            foreach (var skill in unit.Spec.Skills)
                if (skill.RawCode == skillId &&
                    skill.Trigger == SkillTriggerKind.Periodic)
                    return skillTriggers.NextReadyAt(instanceId, skillId);
            return null;
        }

        // 평타의 쿨타임을 소모하지 않는 주기 스킬 발동.
        // 확률 판정에 실패한 경우에도 주기 시각은 다음으로 이동한다.
        public bool TryActivatePeriodicSkill(string instanceId, string skillId)
        {
            RegisteredUnit unit;
            if (!units.TryGetValue(instanceId, out unit))
                throw new InvalidOperationException("미등록 유닛의 주기 스킬입니다.");
            double? at = NextPeriodicSkillAt(instanceId, skillId);
            if (!at.HasValue || at.Value > scheduler.Time) return false;
            if (unit.NextEventSequence == long.MaxValue)
                throw new OverflowException("유닛 스킬 이벤트 순번 초과");
            return skillTriggers.TryActivate(instanceId, skillId,
                SkillTriggerKind.Periodic, ++unit.NextEventSequence);
        }

        public double? NextAttackAt(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("공격자 ID가 필요합니다.", nameof(instanceId));
            return units.ContainsKey(instanceId) ? scheduler.NextReadyAt(instanceId) : null;
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
            return TryBasicAttack(attackerInstanceId, candidates, null, out result);
        }

        // 선호 대상은 원작 타깃 규칙이 검증한 경우에만 전달한다.
        // 선호 대상이 죽었거나 사거리 밖이면 무시하고 기존 가까운 적을 공격한다.
        public bool TryBasicAttack(string attackerInstanceId,
            IEnumerable<TargetCandidate> candidates, string verifiedPreferredTargetId,
            out BasicAttackResult result)
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
            BasicAttackSpec effective = EffectiveAttack(attackerInstanceId);
            string targetId = CombatTargeting.Select(validCandidates,
                effective.Range, TargetPriority.Nearest);
            if (!string.IsNullOrWhiteSpace(verifiedPreferredTargetId))
            {
                double maximumSquared = effective.Range * effective.Range;
                foreach (var candidate in validCandidates)
                    if (candidate.Id == verifiedPreferredTargetId &&
                        candidate.DistanceSquared <= maximumSquared)
                    {
                        targetId = candidate.Id;
                        break;
                    }
            }
            if (targetId == null) return false;
            var enemy = resolution.GetEnemy(targetId);
            if (enemy == null || !enemy.IsAlive) return false;

            // 피해 계산 오류가 발생하면 공격 쿨타임은 소비하지 않는다.
            long resolved = damageResolver.ResolveBasicDamage(
                new BasicDamageRequest(attackerInstanceId, unit.Spec.UnitRawCode,
                    effective, targetId), enemy);
            if (resolved < 0) throw new InvalidOperationException("최종 피해량이 음수입니다.");
            if (unit.NextEventSequence == long.MaxValue)
                throw new OverflowException("전투 이벤트 순번을 더 늘릴 수 없습니다.");
            long actual;
            if (!resolution.TryAttack(attackerInstanceId, targetId,
                effective.IntervalSeconds, resolved, out actual)) return false;
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
