using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class CharacterResourceState
    {
        public double MaximumHealth;
        public double Health;
        public double MaximumMana;
        public double Mana;
        public bool AllowsZeroHealth;
    }

    [Serializable]
    public sealed class CharacterSkillCooldownState
    {
        public string SkillId;
        public double DueAt;
    }

    [Serializable]
    public sealed class CharacterActiveAbilityState
    {
        public int Version = 1;
        public double Time;
        public CharacterResourceState Resources;
        public List<CharacterSkillCooldownState> Cooldowns =
            new List<CharacterSkillCooldownState>();
    }

    // 체력·마나 총량과 현재량은 원본 확인 후 외부 게임 시스템에서 주입한다.
    // 기본 생성 수치나 자연 재생 수치를 여기서 만들어내지 않는다.
    public sealed class CharacterResources
    {
        public double MaximumHealth { get; private set; }
        public double Health { get; private set; }
        public double MaximumMana { get; private set; }
        public double Mana { get; private set; }
        private readonly bool sourceModelAllowsZeroHealth;
        public bool AllowsZeroHealth => sourceModelAllowsZeroHealth;

        public CharacterResourceState CaptureState() => new CharacterResourceState {
            MaximumHealth = MaximumHealth, Health = Health,
            MaximumMana = MaximumMana, Mana = Mana,
            AllowsZeroHealth = sourceModelAllowsZeroHealth
        };

        public static CharacterResources Restore(CharacterResourceState state)
        {
            if (state == null) return null;
            try
            {
                return new CharacterResources(state.MaximumHealth, state.Health,
                    state.MaximumMana, state.Mana, state.AllowsZeroHealth);
            }
            catch (ArgumentException)
            {
                throw new FormatException("원본 유닛 HP/MP 저장 상태가 잘못됐습니다.");
            }
        }

        public CharacterResources(double maxHealth, double health, double maxMana,
            double mana, bool sourceModelAllowsZeroHealth = false)
        {
            RequireNonNegativeFinite(maxHealth, nameof(maxHealth));
            RequireNonNegativeFinite(health, nameof(health));
            RequireNonNegativeFinite(maxMana, nameof(maxMana));
            RequireNonNegativeFinite(mana, nameof(mana));
            if (maxHealth <= 0 || (sourceModelAllowsZeroHealth ? health < 0 : health <= 0) ||
                health > maxHealth)
                throw new ArgumentOutOfRangeException(nameof(health));
            if (mana > maxMana)
                throw new ArgumentOutOfRangeException(nameof(mana));
            MaximumHealth = maxHealth; Health = health;
            MaximumMana = maxMana; Mana = mana;
            this.sourceModelAllowsZeroHealth = sourceModelAllowsZeroHealth;
        }

        public void SetMana(double value)
        {
            RequireNonNegativeFinite(value, nameof(value));
            if (value > MaximumMana) throw new ArgumentOutOfRangeException(nameof(value));
            Mana = value;
        }

        // A source-model shape-change may have a different ORIGINAL SLK
        // mana pool. This cannot be used to rewrite external verified pools.
        internal void SetSourceModelFormManaPool(double maximum, double current)
        {
            if (!sourceModelAllowsZeroHealth)
                throw new InvalidOperationException("원본 객체의 연습용 변신 자원만 변경할 수 있습니다.");
            RequireNonNegativeFinite(maximum, nameof(maximum));
            RequireNonNegativeFinite(current, nameof(current));
            if (current > maximum)
                throw new ArgumentOutOfRangeException(nameof(current));
            MaximumMana = maximum;
            Mana = current;
        }

        public void SetHealth(double value)
        {
            RequireNonNegativeFinite(value, nameof(value));
            if ((sourceModelAllowsZeroHealth ? value < 0 : value <= 0) ||
                value > MaximumHealth)
                throw new ArgumentOutOfRangeException(nameof(value));
            Health = value;
        }

        public bool CanSpend(double healthCost, double manaCost)
        {
            RequireNonNegativeFinite(healthCost, nameof(healthCost));
            RequireNonNegativeFinite(manaCost, nameof(manaCost));
            // Caller-verified survival HP retains the conservative strict
            // boundary. Source-mode ability HP can reach 0 without asserting
            // Warcraft unit death; the SLK maximum equals several spell costs.
            return (sourceModelAllowsZeroHealth ? healthCost <= Health :
                healthCost < Health) && manaCost <= Mana;
        }

        public void Spend(double healthCost, double manaCost)
        {
            if (!CanSpend(healthCost, manaCost))
                throw new InvalidOperationException("스킬에 필요한 체력·마나가 부족합니다.");
            Health -= healthCost;
            Mana -= manaCost;
        }

        // Only source-backed practice regeneration uses this method. No
        // wall-clock catch-up or resources regenerated on UI redraws.
        public void RegenerateSourceModel(double healthPerSecond,
            double manaPerSecond, double elapsed)
        {
            RequireNonNegativeFinite(healthPerSecond, nameof(healthPerSecond));
            RequireNonNegativeFinite(manaPerSecond, nameof(manaPerSecond));
            RequireNonNegativeFinite(elapsed, nameof(elapsed));
            if (!sourceModelAllowsZeroHealth)
                throw new InvalidOperationException("검증된 외부 자원은 개발용 자동 재생 대상이 아닙니다.");
            Health = Math.Min(MaximumHealth, Health + healthPerSecond * elapsed);
            Mana = Math.Min(MaximumMana, Mana + manaPerSecond * elapsed);
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

        public CharacterActiveAbilityState CaptureState()
        {
            var state = new CharacterActiveAbilityState {
                Time = Time, Resources = resources?.CaptureState()
            };
            foreach (var pair in cooldownReadyAt)
                state.Cooldowns.Add(new CharacterSkillCooldownState {
                    SkillId = pair.Key, DueAt = pair.Value
                });
            state.Cooldowns.Sort((a,b) =>
                string.CompareOrdinal(a.SkillId, b.SkillId));
            return state;
        }

        public void RestoreState(CharacterActiveAbilityState state)
        {
            if (state == null || state.Version != 1 ||
                state.Cooldowns == null || double.IsNaN(state.Time) ||
                double.IsInfinity(state.Time) || state.Time < 0)
                throw new FormatException("캐릭터 자원/스킬 저장 형식이 잘못됐습니다.");
            if (Time != 0 || cooldownReadyAt.Count != 0)
                throw new InvalidOperationException(
                    "새 캐릭터 액티브 엔진에서만 복원할 수 있습니다.");
            var valid = new Dictionary<string,double>(StringComparer.Ordinal);
            foreach (var entry in state.Cooldowns)
                if (entry == null || string.IsNullOrWhiteSpace(entry.SkillId) ||
                    !skills.ContainsKey(entry.SkillId) ||
                    double.IsNaN(entry.DueAt) || double.IsInfinity(entry.DueAt) ||
                    entry.DueAt < 0 || !valid.TryAdd(entry.SkillId, entry.DueAt))
                    throw new FormatException(
                        "캐릭터 고유 기술 쿨타임/식별자가 잘못됐습니다.");
            var restored = CharacterResources.Restore(state.Resources);
            if (resources != null && restored == null)
                throw new FormatException(
                    "현재 자원이 있는 유닛의 저장 HP/MP가 없습니다.");
            // No cooldown or resource mutation until every field validates.
            foreach (var pair in valid) cooldownReadyAt.Add(pair.Key, pair.Value);
            resources = restored;
            Time = state.Time;
        }

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
                skill.Trigger != "manual" &&
                !(profile.UnitId == SpecialGradeMobilePolicy.DockingFive &&
                  skill.Trigger == "onSkillUse"))
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
                (skill.Trigger == "manual" || skill.Trigger == "onSkillUse") &&
                    !manaCost.HasValue && !healthCost.HasValue)
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
            // A source-approved form may include separate status/stack packets.
            // Authorization remains per-component, not a blanket permission
            // for arbitrary self-targeted tooltip skills.
            bool supportedSelfForm = (skill.Target == null || skill.Target == "self") &&
                skill.Components.Count > 0 &&
                skill.Components[0].Type == "transform" &&
                specialAllowed != null && specialAllowed(skill.Components[0]);
            // D-02's original target is omitted but radius=625 explicitly
            // defines an area packet. Only its exact reviewed skill may use
            // that source-backed omission; do not broaden the generic gate.
            bool approvedImplicitArea = (skill.Id == "D-02:skill:0" &&
                SourceModelSixteenthSkillCatalog.CanModelExactSpecial(skill)) ||
                (skill.Id == "D-04:skill:0" &&
                 SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill) &&
                 specialAllowed != null);
            bool approvedSengokuExplosion = skill.Id == "I-07:skill:0" &&
                SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill) &&
                specialAllowed != null;
            bool approvedTwentyFourth =
                SourceModelTwentyFourthSkillCatalog.CanModelExactActive(skill);
            if (!supportedSelfForm && !approvedImplicitArea &&
                !approvedSengokuExplosion &&
                !(approvedTwentyFourth && skill.Target == "line") &&
                skill.Target != "single" &&
                skill.Target != "area" && skill.Target != "targetCircle" &&
                skill.Target != "casterArea")
                return false;
            if (!supportedSelfForm && skill.Target != "single" && (!skill.Radius.HasValue ||
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
                    int count;
                    int? limit;
                    if (!CommonSkillStatusEffects.TryReadArmorBreak(component, out count, out limit) &&
                        (specialAllowed == null || !specialAllowed(component)))
                        return false;
                }
                else if (CommonSkillStatusEffects.IsTimedStatus(component.Type))
                {
                    CombatEffectType kind;
                    double amount, duration;
                    if (!CommonSkillStatusEffects.TryRead(component, out kind, out amount, out duration) &&
                        (specialAllowed == null || !specialAllowed(component)))
                        return false;
                }
                else if (!(approvedTwentyFourth &&
                           (component.Type == "magicDamageMultiplier" ||
                            component.Type == "removeTargetBuffs")) &&
                         (specialAllowed == null || !specialAllowed(component)))
                    return false;
            }
            return skill.Components.Count > 0;
        }
    }
}

