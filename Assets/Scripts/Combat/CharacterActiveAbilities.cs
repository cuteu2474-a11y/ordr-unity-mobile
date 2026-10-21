using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 체력·마나 총량과 현재량은 원본 확인 후 외부 게임 시스템에서 주입한다.
    // 기본 생성 수치나 자연 재생 수치를 여기서 만들어내지 않는다.
    public sealed class CharacterResources
    {
        public double MaximumHealth { get; private set; }
        public double Health { get; private set; }
        public double MaximumMana { get; private set; }
        public double Mana { get; private set; }

        public CharacterResources(double maxHealth, double health, double maxMana, double mana)
        {
            RequireNonNegativeFinite(maxHealth, nameof(maxHealth));
            RequireNonNegativeFinite(health, nameof(health));
            RequireNonNegativeFinite(maxMana, nameof(maxMana));
            RequireNonNegativeFinite(mana, nameof(mana));
            if (maxHealth <= 0 || health <= 0 || health > maxHealth)
                throw new ArgumentOutOfRangeException(nameof(health));
            if (mana > maxMana)
                throw new ArgumentOutOfRangeException(nameof(mana));
            MaximumHealth = maxHealth; Health = health;
            MaximumMana = maxMana; Mana = mana;
        }

        public void SetMana(double value)
        {
            RequireNonNegativeFinite(value, nameof(value));
            if (value > MaximumMana) throw new ArgumentOutOfRangeException(nameof(value));
            Mana = value;
        }

        public void SetHealth(double value)
        {
            RequireNonNegativeFinite(value, nameof(value));
            if (value <= 0 || value > MaximumHealth)
                throw new ArgumentOutOfRangeException(nameof(value));
            Health = value;
        }

        public bool CanSpend(double healthCost, double manaCost)
        {
            RequireNonNegativeFinite(healthCost, nameof(healthCost));
            RequireNonNegativeFinite(manaCost, nameof(manaCost));
            // HP 소모로 사망 가능한지 원작 수치가 확인되지 않아 사망 시전을 임의 허용하지 않는다.
            return healthCost < Health && manaCost <= Mana;
        }

        public void Spend(double healthCost, double manaCost)
        {
            if (!CanSpend(healthCost, manaCost))
                throw new InvalidOperationException("스킬에 필요한 체력·마나가 부족합니다.");
            Health -= healthCost;
            Mana -= manaCost;
        }

        private static void RequireNonNegativeFinite(double value, string argument)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(argument);
        }
    }

    public enum AbilityCastResult
    {
        Cast,
        NoResourceState,
        SkillNotFound,
        TriggerRequiresBattleEvent,
        MissingOrUnsupportedRequirement,
        InsufficientResources,
        Cooldown,
        MissingTarget,
        UnsupportedEffect
    }

    // '사용형' 스킬의 자원소모·절대쿨타임은 스킬 JSON에서 가져온다.
    // 평타확률형 스킬/도트/미검증 조건은 사용자 액티브 스킬로 강제 실행하지 않는다.
    public sealed class CharacterActiveAbilities
    {
        private readonly CharacterSkillProfile profile;
        private readonly Dictionary<string, double> cooldownReadyAt =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, ImportedCombatSkill> skills =
            new Dictionary<string, ImportedCombatSkill>(StringComparer.Ordinal);
        private CharacterResources resources;

        public double Time { get; private set; }
        public CharacterResources Resources { get { return resources; } }

        public CharacterActiveAbilities(CharacterSkillProfile profile)
        {
            this.profile = profile;
            if (profile == null) return;
            foreach (var skill in profile.Skills) skills.Add(skill.Id, skill);
        }

        public void ConfigureResources(CharacterResources verifiedResources)
        {
            resources = verifiedResources ?? throw new ArgumentNullException(nameof(verifiedResources));
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (double.IsInfinity(Time + seconds))
                throw new OverflowException("캐릭터 액티브 스킬 시간이 초과되었습니다.");
            Time += seconds;
        }

        public AbilityCastResult TryCast(string skillId, bool hasLivingTarget,
            out ImportedCombatSkill skill)
        {
            return TryCast(skillId, hasLivingTarget, false, null, out skill);
        }

        public AbilityCastResult TryCast(string skillId, bool hasLivingTarget,
            bool hasVerifiedTimedDamage, out ImportedCombatSkill skill)
        {
            return TryCast(skillId, hasLivingTarget, hasVerifiedTimedDamage,
                null, out skill);
        }

        public AbilityCastResult TryCast(string skillId, bool hasLivingTarget,
            bool hasVerifiedTimedDamage, Func<ImportedCombatEffect, bool> specialAllowed,
            out ImportedCombatSkill skill)
        {
            skill = null;
            if (resources == null) return AbilityCastResult.NoResourceState;
            if (string.IsNullOrWhiteSpace(skillId) || !skills.TryGetValue(skillId, out skill))
                return AbilityCastResult.SkillNotFound;

            if (skill.Get("requiresOriginalVerification") is bool &&
                (bool)skill.Get("requiresOriginalVerification"))
                return AbilityCastResult.MissingOrUnsupportedRequirement;

            if (skill.Trigger != "activeMana" && skill.Trigger != "activeHealth" &&
                skill.Trigger != "manual")
                return AbilityCastResult.TriggerRequiresBattleEvent;

            if (!hasLivingTarget) return AbilityCastResult.MissingTarget;

            // 특정 폼, 아이템, 라운드, 스택, 변신 상태가 필요한 스킬은
            // 아직 그 조건을 확인하는 시스템이 없어 발동하지 않는다.
            foreach (var key in new[] {
                "requires", "requiresForm", "requiresHealthAtLeast", "conditions",
                "requiresNearbyUnit", "requiredTargetArmorBreak", "onlyTargetKinds",
                "exceptTargetKinds", "uses", "requiredSoulStacks", "requiresFormChange"
            })
                if (skill.OriginalFields.ContainsKey(key))
                    return AbilityCastResult.MissingOrUnsupportedRequirement;

            double? manaCost = skill.Number("manaCost");
            double? healthCost = skill.Number("healthCost");
            if (skill.Trigger == "activeMana" && !manaCost.HasValue ||
                skill.Trigger == "activeHealth" && !healthCost.HasValue ||
                skill.Trigger == "manual" && !manaCost.HasValue && !healthCost.HasValue)
                return AbilityCastResult.MissingOrUnsupportedRequirement;
            double mana = manaCost ?? 0;
            double health = healthCost ?? 0;
            if (mana < 0 || health < 0 || !resources.CanSpend(health, mana))
                return AbilityCastResult.InsufficientResources;

            double readyAt;
            if (cooldownReadyAt.TryGetValue(skillId, out readyAt) && Time < readyAt)
                return AbilityCastResult.Cooldown;
            double? cooldown = skill.Number("absoluteCooldownSeconds") ?? skill.CooldownSeconds;
            if (cooldown.HasValue && (double.IsNaN(cooldown.Value) ||
                double.IsInfinity(cooldown.Value) || cooldown.Value < 0))
                return AbilityCastResult.MissingOrUnsupportedRequirement;

            // 원본 실행기의 지원 범위를 넘는 일부 효과를 발동하고
            // 자원만 차감하는 오류를 방지한다.
            if (!CanExecuteAllComponents(skill, hasVerifiedTimedDamage, specialAllowed))
                return AbilityCastResult.UnsupportedEffect;

            double next = Time + (cooldown ?? 0);
            if (double.IsInfinity(next))
                throw new OverflowException("액티브 스킬 쿨타임 초과.");
            resources.Spend(health, mana);
            cooldownReadyAt[skillId] = next;
            return AbilityCastResult.Cast;
        }

        private static bool CanExecuteAllComponents(ImportedCombatSkill skill,
            bool verifiedTiming, Func<ImportedCombatEffect, bool> specialAllowed)
        {
            if (skill.Target != "single" && skill.Target != "area" &&
                skill.Target != "targetCircle" && skill.Target != "casterArea")
                return false;
            if (skill.Target != "single" && (!skill.Radius.HasValue ||
                skill.Radius.Value <= 0)) return false;
            foreach (var component in skill.Components)
            {
                if (component.Type == "physical" || component.Type == "magical" ||
                    component.Type == "fixed" || component.Type == "explosive")
                {
                    if ((component.Get("hits") != null ||
                         component.Get("amountPerHit") != null ||
                         component.Get("amountPerTick") != null ||
                         component.Get("durationSeconds") != null) && !verifiedTiming)
                        return false;
                }
                else if (component.Type == "armorBreak")
                {
                    if (!component.Number("stacks").HasValue) return false;
                }
                else if (component.Type == "stun" ||
                         component.Type == "armorReduction" ||
                         component.Type == "movementSlow" ||
                         component.Type == "movementSlowPercent" ||
                         component.Type == "magicResistanceReduction" ||
                         component.Type == "magicDamageAmplification" ||
                         component.Type == "explosiveDamageAmplification")
                {
                    if (!component.Number("durationSeconds").HasValue) return false;
                }
                else if (specialAllowed == null || !specialAllowed(component))
                    return false;
            }
            return skill.Components.Count > 0;
        }
    }
}
