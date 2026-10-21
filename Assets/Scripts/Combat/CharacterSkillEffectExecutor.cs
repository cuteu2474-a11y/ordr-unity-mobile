using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum CharacterEnemyKind
    {
        Line, Boss, Enraged, Story, Creep, SeaKing, Challenge
    }

    public sealed class SpatialCombatEnemy
    {
        public string Id { get; private set; }
        public CombatEnemy Enemy { get; private set; }
        public CharacterEnemyKind Kind { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double PathProgress { get; private set; }

        public SpatialCombatEnemy(CombatEnemy enemy, CharacterEnemyKind kind,
            double x, double y, double progress)
        {
            Enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            Id = enemy.Id;
            Kind = kind;
            Move(x, y, progress);
        }

        public void Move(double x, double y, double progress)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(progress) || double.IsInfinity(progress))
                throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Y = y; PathProgress = progress;
        }

        public string KindId
        {
            get
            {
                switch (Kind)
                {
                    case CharacterEnemyKind.Line: return "line";
                    case CharacterEnemyKind.Boss: return "boss";
                    case CharacterEnemyKind.Enraged: return "enraged";
                    case CharacterEnemyKind.Story: return "story";
                    case CharacterEnemyKind.Creep: return "creep";
                    case CharacterEnemyKind.SeaKing: return "seaKing";
                    case CharacterEnemyKind.Challenge: return "challenge";
                    default: throw new ArgumentOutOfRangeException(nameof(Kind));
                }
            }
        }
    }

    // 원작 공식과 버프·체력 비례·마방 계산을 확인한 계산기만 주입한다.
    // 지원하지 않는 조합식은 NotSupportedException을 던져 기록만 남겨야 한다.
    public interface IVerifiedCharacterSkillDamageResolver
    {
        long ResolveDamage(CharacterSkillDamageRequest request);
    }

    public sealed class CharacterSkillDamageRequest
    {
        public string CasterInstanceId { get; private set; }
        public CharacterSkillProfile CasterProfile { get; private set; }
        public ImportedCombatSkill Skill { get; private set; }
        public ImportedCombatEffect Effect { get; private set; }
        public SpatialCombatEnemy Target { get; private set; }
        public bool IsPrimaryTarget { get; private set; }
        public int TargetArmorBreakStacks { get; private set; }
        public int HitIndex { get; private set; }
        public int HitCount { get; private set; }
        public double ElapsedFromActivationSeconds { get; private set; }

        internal CharacterSkillDamageRequest(string caster, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect, SpatialCombatEnemy target,
            bool primary, int armorBreakStacks,
            int hitIndex = 0, int hitCount = 1, double elapsedSeconds = 0)
        {
            if (hitIndex < 0 || hitCount <= 0 || hitIndex >= hitCount ||
                double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(hitIndex));
            HitIndex = hitIndex;
            HitCount = hitCount;
            ElapsedFromActivationSeconds = elapsedSeconds;
            CasterInstanceId = caster;
            CasterProfile = profile;
            Skill = skill;
            Effect = effect;
            Target = target;
            IsPrimaryTarget = primary;
            TargetArmorBreakStacks = armorBreakStacks;
        }
    }

    public enum CharacterCombatEventType
    {
        UnitSpawned, UnitPlaced, Attack, SkillActivated, SkillDamage, StatusApplied,
        ArmorBreakApplied, EnemyDefeated, EffectDeferred, EnemySpawned
    }

    public sealed class CharacterCombatEvent
    {
        public CharacterCombatEventType Type { get; private set; }
        public string ActorId { get; private set; }
        public string EnemyId { get; private set; }
        public string SkillName { get; private set; }
        public string Detail { get; private set; }
        public long Amount { get; private set; }

        public CharacterCombatEvent(CharacterCombatEventType type, string actor,
            string enemyId, string skillName, string detail, long amount)
        {
            Type = type; ActorId = actor; EnemyId = enemyId;
            SkillName = skillName; Detail = detail; Amount = amount;
        }
    }

    // 소환·변신·아이템·보상·특성 스택은 검증된 외부 기능 모듈로 라우팅한다.
    // 등록되지 않은 효과는 아무것도 소비/변경하지 않고 미구현 상태로 남긴다.
    public interface IOriginalSpecialCombatEffectHandler
    {
        bool Supports(CharacterSkillProfile profile, ImportedCombatSkill skill,
            ImportedCombatEffect effect);
        bool ExecuteOncePerCast(CharacterSkillProfile profile, ImportedCombatSkill skill,
            ImportedCombatEffect effect);
        bool TryApply(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect,
            SpatialCombatEnemy target, bool isPrimary, out string reason);
    }

    // 지원된 효과만 실제 적용한다. 해석되지 않은 복잡한 수식은 계산하지 않고 이벤트로 기록.
    public sealed class CharacterSkillEffectExecutor
    {
        private readonly CombatResolution resolution;
        private readonly IVerifiedCharacterSkillDamageResolver damageFormula;
        private readonly Dictionary<string, CombatStatusEngine> statuses;
        private readonly Dictionary<string, int> armorBreakStacks;
        private readonly List<CharacterCombatEvent> events;
        private readonly CombatTimedEffectQueue timedEffects;
        private IOriginalSkillHitTimingProvider hitTiming;
        private IOriginalSpecialCombatEffectHandler specialEffects;

        public double Time { get { return timedEffects.Time; } }

        public void ConfigureVerifiedSpecialEffects(IOriginalSpecialCombatEffectHandler handler)
        {
            specialEffects = handler;
        }

        public bool CanHandleSpecialComponent(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect)
        {
            return specialEffects != null &&
                specialEffects.Supports(profile, skill, effect);
        }

        private static bool IsStandardEffect(string type)
        {
            return type == "stun" || type == "armorReduction" ||
                type == "movementSlow" || type == "movementSlowPercent" ||
                type == "magicResistanceReduction" ||
                type == "magicDamageAmplification" ||
                type == "explosiveDamageAmplification" || type == "armorBreak";
        }

        // 원작의 각 타격 시각을 확인한 공급자만 허용.
        public void ConfigureVerifiedHitTiming(IOriginalSkillHitTimingProvider provider)
        {
            hitTiming = provider;
        }

        public void Advance(double seconds, Action<double> advanceOtherClocks)
        {
            timedEffects.Advance(seconds, advanceOtherClocks);
        }

        public void CancelCaster(string id) { timedEffects.CancelCaster(id); }
        public void CancelTarget(string id) { timedEffects.CancelTarget(id); }

        public CharacterSkillEffectExecutor(CombatResolution resolution,
            IVerifiedCharacterSkillDamageResolver damageFormula,
            Dictionary<string, CombatStatusEngine> statuses,
            Dictionary<string, int> armorBreakStacks, List<CharacterCombatEvent> events)
            : this(resolution, damageFormula, statuses, armorBreakStacks, events,
                new CombatTimedEffectQueue()) { }

        public CharacterSkillEffectExecutor(CombatResolution resolution,
            IVerifiedCharacterSkillDamageResolver damageFormula,
            Dictionary<string, CombatStatusEngine> statuses,
            Dictionary<string, int> armorBreakStacks, List<CharacterCombatEvent> events,
            CombatTimedEffectQueue timedEffects)
        {
            this.timedEffects = timedEffects ??
                throw new ArgumentNullException(nameof(timedEffects));
            this.resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            this.damageFormula = damageFormula ?? throw new ArgumentNullException(nameof(damageFormula));
            this.statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
            this.armorBreakStacks = armorBreakStacks ?? throw new ArgumentNullException(nameof(armorBreakStacks));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        // 명시적 '미구현 공식' 정책을 쓰는 월드에서는 피해 스킬을 시전해
        // 체력·마나만 소모하지 않도록 자원 차감 이전에 가로막는다.
        public bool CanAttemptWithConfiguredFormula(ImportedCombatSkill skill)
        {
            return CanAttemptWithConfiguredFormula(null, skill);
        }

        public bool CanAttemptWithConfiguredFormula(
            CharacterSkillProfile profile, ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            foreach (var component in skill.Components)
            {
                if (IsDamage(component.Type))
                {
                    if (damageFormula is RejectUnverifiedDamageFormula) return false;
                    if (IsTimedDamage(component) &&
                        !HasVerifiedTiming(profile, skill, component)) return false;
                }
                else if (!IsStandardEffect(component.Type) &&
                    !CanHandleSpecialComponent(profile, skill, component))
                    return false;
            }
            return true;
        }

        public bool HasVerifiedTimings(CharacterSkillProfile profile, ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            foreach (var component in skill.Components)
                if (IsDamage(component.Type) && IsTimedDamage(component) &&
                    !HasVerifiedTiming(profile, skill, component)) return false;
            return true;
        }

        private bool HasVerifiedTiming(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            if (hitTiming == null || profile == null) return false;
            IReadOnlyList<double> offsets;
            return hitTiming.TryGetHitOffsets(profile, skill, component, out offsets) &&
                CombatTimedEffectQueue.AreValidOffsets(component, offsets);
        }

        private static bool IsTimedDamage(ImportedCombatEffect component)
        {
            return component.Get("hits") != null ||
                   component.Get("amountPerHit") != null ||
                   component.Get("amountPerTick") != null ||
                   component.Get("durationSeconds") != null;
        }

        public void Execute(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, SpatialCombatEnemy primary, double casterX, double casterY,
            IEnumerable<SpatialCombatEnemy> enemies)
        {
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                casterId, primary.Id, skill.Name, skill.Trigger, 0));
            if (skill.Target != "single" && skill.Target != "area" &&
                skill.Target != "targetCircle" && skill.Target != "casterArea")
            {
                Defer(casterId, primary.Id, skill.Name, "대상 형태 미지원: " + skill.Target);
                return;
            }

            var targets = new List<SpatialCombatEnemy>();
            bool circle = skill.Target != "single";
            double centerX = skill.Target == "casterArea" ? casterX : primary.X;
            double centerY = skill.Target == "casterArea" ? casterY : primary.Y;
            double r2 = circle && skill.Radius.HasValue
                ? skill.Radius.Value * skill.Radius.Value : 0;

            foreach (var enemy in enemies)
            {
                if (!enemy.Enemy.IsAlive) continue;
                if (!circle && enemy.Id != primary.Id) continue;
                if (circle)
                {
                    double dx = enemy.X - centerX;
                    double dy = enemy.Y - centerY;
                    if (dx * dx + dy * dy > r2) continue;
                }
                targets.Add(enemy);
            }
            // 사전 등록 순서와 무관하게 같은 타깃 집합에는 안정된 ID 순서로 처리한다.
            targets.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            // 이미 평타에 사망한 대상은 광역 중심 좌표로 사용 가능하지만 재차 피해를 받지 않음.
            foreach (var component in skill.Components)
            {
                if (!IsDamage(component.Type) && !IsStandardEffect(component.Type) &&
                    specialEffects != null &&
                    specialEffects.Supports(profile, skill, component) &&
                    specialEffects.ExecuteOncePerCast(profile, skill, component))
                {
                    string failure;
                    if (!specialEffects.TryApply(casterId, profile, skill, component,
                        primary, true, out failure))
                        Defer(casterId, primary.Id, skill.Name,
                            failure ?? "검증된 특수효과 실행 거부");
                    continue;
                }
                foreach (var target in targets)
                {
                    if (!target.Enemy.IsAlive) continue;
                    if (!TargetMatches(component, target, primary.Id)) continue;
                    ApplyComponent(casterId, profile, skill, component,
                        target, target.Id == primary.Id);
                }
            }
        }

        private void ApplyComponent(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, bool isPrimary)
        {
            if (IsDamage(component.Type))
            {
                if (!IsTimedDamage(component))
                {
                    ApplyDamagePulse(casterId, profile, skill, component, target,
                        isPrimary, 0, 1, 0);
                    return;
                }

                IReadOnlyList<double> offsets;
                if (hitTiming == null || !hitTiming.TryGetHitOffsets(
                        profile, skill, component, out offsets) ||
                    !CombatTimedEffectQueue.AreValidOffsets(component, offsets))
                {
                    Defer(casterId, target.Id, skill.Name,
                        "타격 횟수/시각 미확인: " + component.Type);
                    return;
                }
                for (int index = 0; index < offsets.Count; index++)
                {
                    int hitIndex = index;
                    double elapsed = offsets[index];
                    if (elapsed == 0)
                        ApplyDamagePulse(casterId, profile, skill, component, target,
                            isPrimary, hitIndex, offsets.Count, elapsed);
                    else
                        timedEffects.Schedule(casterId, target.Id, elapsed,
                            () => ApplyDamagePulse(casterId, profile, skill,
                                component, target, isPrimary, hitIndex, offsets.Count,
                                elapsed));
                }
                return;
            }

            CombatEffectType kind;
            double? amount = null;
            switch (component.Type)
            {
                case "stun":
                    kind = CombatEffectType.Stun;
                    amount = 1;
                    break;
                case "armorReduction":
                    kind = CombatEffectType.ArmorReduction;
                    amount = component.Number("amount");
                    break;
                case "movementSlow":
                case "movementSlowPercent":
                    kind = CombatEffectType.MovementSlowPercent;
                    amount = component.Number("percent");
                    break;
                case "magicResistanceReduction":
                    kind = CombatEffectType.MagicResistanceReduction;
                    amount = component.Number("percent");
                    break;
                case "magicDamageAmplification":
                    kind = CombatEffectType.MagicDamageAmplification;
                    amount = component.Number("percent");
                    break;
                case "explosiveDamageAmplification":
                    kind = CombatEffectType.ExplosiveDamageAmplification;
                    amount = component.Number("percent");
                    break;
                case "armorBreak":
                    double? count = component.Number("stacks");
                    if (!count.HasValue || count.Value <= 0 ||
                        count.Value > int.MaxValue || Math.Floor(count.Value) != count.Value)
                    {
                        Defer(casterId, target.Id, skill.Name, "아머브레이크 수량 미확인.");
                        return;
                    }
                    int current;
                    armorBreakStacks.TryGetValue(target.Id, out current);
                    int next = checked(current + (int)count.Value);
                    double? limit = component.Number("maxStacks");
                    if (limit.HasValue && limit.Value >= 0 && limit.Value <= int.MaxValue)
                        next = Math.Min(next, (int)limit.Value);
                    armorBreakStacks[target.Id] = next;
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.ArmorBreakApplied,
                        casterId, target.Id, skill.Name, "stack", next));
                    return;
                default:
                    if (specialEffects != null &&
                        specialEffects.Supports(profile, skill, component))
                    {
                        string failure;
                        if (!specialEffects.TryApply(casterId, profile, skill,
                            component, target, isPrimary, out failure))
                            Defer(casterId, target.Id, skill.Name,
                                failure ?? "특수효과 적용 조건 불일치");
                        return;
                    }
                    Defer(casterId, target.Id, skill.Name,
                        "별도 효과 실행기 미구현: " + component.Type);
                    return;
            }

            double? duration = component.Number("durationSeconds");
            if (!amount.HasValue || !duration.HasValue || duration.Value <= 0)
            {
                // 효과 지속시간이 원본에서 불분명하면 영구/즉시 만료로 임의 변환하지 않음.
                Defer(casterId, target.Id, skill.Name,
                    "효과 수치 또는 지속시간 미확인: " + component.Type);
                return;
            }
            CombatStatusEngine status;
            if (!statuses.TryGetValue(target.Id, out status))
            {
                Defer(casterId, target.Id, skill.Name, "적 상태 관리 정보를 찾을 수 없습니다.");
                return;
            }
            status.Apply(new CombatEffect(casterId + ":" + skill.Id, kind,
                CombatEffectOrigin.TriggeredSkill, amount.Value, duration.Value));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                casterId, target.Id, skill.Name, component.Type, 0));
        }

        private void ApplyDamagePulse(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, bool isPrimary, int hitIndex, int hitCount,
            double elapsed)
        {
            if (!target.Enemy.IsAlive) return;
            try
            {
                int stacks;
                armorBreakStacks.TryGetValue(target.Id, out stacks);
                var request = new CharacterSkillDamageRequest(casterId, profile,
                    skill, component, target, isPrimary, stacks, hitIndex, hitCount,
                    elapsed);
                long damage = damageFormula.ResolveDamage(request);
                if (damage < 0)
                    throw new InvalidOperationException("스킬 피해 계산 결과가 음수입니다.");
                long dealt;
                if (resolution.TryApplySkillDamage(target.Id, damage, out dealt))
                {
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage, casterId, target.Id,
                        skill.Name, component.Type, dealt));
                    if (!target.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated, casterId,
                            target.Id, skill.Name, "skill", 0));
                }
            }
            catch (NotSupportedException ex)
            {
                Defer(casterId, target.Id, skill.Name,
                    "검증된 피해 공식 미구현: " + ex.Message);
            }
        }

        private static bool IsDamage(string type)
        {
            return type == "physical" || type == "magical" ||
                   type == "fixed" || type == "explosive";
        }

        private static bool TargetMatches(ImportedCombatEffect component,
            SpatialCombatEnemy target, string primaryId)
        {
            if (component.Text("scope") == "singlePrimaryTarget" && target.Id != primaryId)
                return false;
            var only = component.Get("onlyTargetKinds") as List<object>;
            if (only != null)
            {
                bool match = false;
                foreach (var item in only)
                    if (item as string == target.KindId) { match = true; break; }
                if (!match) return false;
            }
            var except = component.Get("exceptTargetKinds") as List<object>;
            if (except != null)
                foreach (var item in except)
                    if (item as string == target.KindId) return false;
            return true;
        }

        private void Defer(string actor, string enemy, string skill, string reason)
        {
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                actor, enemy, skill, reason, 0));
        }
    }
}
