using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // A temporary form is an overlay on the same owned character. It is not a
    // replacement/creation transaction, so the original instance, lane and
    // fusion material identity remain intact.
    public sealed class SourceModelFormSpec
    {
        public string Name { get; private set; }
        public double Duration { get; private set; }
        public double FlatAttackPower { get; private set; }
        public double AttackIntervalReduction { get; private set; }

        internal SourceModelFormSpec(string name, double duration,
            double flatPower, double intervalReduction)
        {
            Name = name; Duration = duration;
            FlatAttackPower = flatPower;
            AttackIntervalReduction = intervalReduction;
        }
    }

    // A source summon is not an inventory unit. Keeping an independent ID and
    // expiry prevents it from being spent as a fusion material or awarded on
    // expiry. Combat may be enabled only when its complete attack cadence and
    // damage packet are known.
    public sealed class SourceModelSummonState
    {
        public long Id { get; private set; }
        public long OwnerId { get; private set; }
        public string SkillName { get; private set; }
        public string Name { get; private set; }
        public double ExpiresAt { get; private set; }
        public bool StoryLane { get; private set; }
        public bool HasIndependentCombat { get; private set; }

        internal SourceModelSummonState(long id, long owner, string skill,
            string name, double expiresAt, bool storyLane, bool hasCombat)
        {
            Id = id; OwnerId = owner; SkillName = skill; Name = name;
            ExpiresAt = expiresAt; StoryLane = storyLane;
            HasIndependentCombat = hasCombat;
        }
    }

    public static class SourceModelTemporarySkillCatalog
    {
        private static bool PositiveFinite(double? value) =>
            value.HasValue && value.Value > 0 &&
            !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

        // Refuse incomplete mixed forms. In particular, Rumble Ball lacks its
        // attack power/speed values and Nika has a separate transformed-hit
        // packet; applying just the visual/form duration would be misleading.
        public static bool TryReadCompleteForm(ImportedCombatSkill skill,
            out SourceModelFormSpec form)
        {
            form = null;
            if (skill == null || skill.Components.Count != 1 ||
                !(skill.Trigger == "onAttack" || skill.Trigger == "activeHealth") ||
                skill.Components[0].Type != "transform") return false;
            var effect = skill.Components[0];
            foreach (var key in effect.Fields.Keys)
                if (key != "type" && key != "form" && key != "durationSeconds" &&
                    key != "attackPowerIncrease" &&
                    key != "attackIntervalReductionSeconds") return false;
            foreach (var key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "components" &&
                    key != "chancePercent" && key != "castTimeSeconds" &&
                    key != "healthCost" && key != "cooldownSeconds") return false;
            double? duration = effect.Number("durationSeconds");
            double? power = effect.Number("attackPowerIncrease");
            double? interval = effect.Number("attackIntervalReductionSeconds");
            string name = effect.Text("form");
            if (!PositiveFinite(duration) || !PositiveFinite(power) ||
                !PositiveFinite(interval) || string.IsNullOrWhiteSpace(name))
                return false;
            if (skill.Trigger == "onAttack" &&
                (!PositiveFinite(skill.ChancePercent) ||
                 !PositiveFinite(skill.Number("castTimeSeconds")))) return false;
            if (skill.Trigger == "activeHealth" &&
                !PositiveFinite(skill.Number("healthCost"))) return false;
            form = new SourceModelFormSpec(name, duration.Value,
                power.Value, interval.Value);
            return true;
        }

        // Instant clone-hit proxy. The original clone's object lifecycle and
        // subsequent procs are NOT inferred from this one documented packet.
        public static bool TryReadClonePulse(ImportedCombatSkill skill,
            out double damage, out double radius)
        {
            damage = 0; radius = 0;
            if (skill == null || skill.Trigger != "onAttack" ||
                skill.Components.Count != 1 ||
                skill.Components[0].Type != "clone" ||
                !PositiveFinite(skill.ChancePercent)) return false;
            foreach (var key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "components" &&
                    key != "chancePercent") return false;
            var clone = skill.Components[0];
            foreach (var key in clone.Fields.Keys)
                if (key != "type" && key != "onAttackMagicalDamage" &&
                    key != "attackRadius") return false;
            if (!PositiveFinite(clone.Number("onAttackMagicalDamage")) ||
                !PositiveFinite(clone.Number("attackRadius"))) return false;
            damage = clone.Number("onAttackMagicalDamage").Value;
            radius = clone.Number("attackRadius").Value;
            return true;
        }

        // Time-bounded units with unproven attack cadence are kept as distinct
        // summoned actors. The actor cannot attack or earn economy rewards
        // until a complete source-backed combat spec is supplied.
        public static bool TryReadTimedClone(ImportedCombatSkill skill,
            out string name, out double duration, out double stunSeconds)
        {
            name = null; duration = 0; stunSeconds = 0;
            if (skill == null || skill.Trigger != "onAttack" ||
                skill.Components.Count != 2 ||
                !PositiveFinite(skill.ChancePercent) ||
                skill.Components[0].Type != "summon" ||
                skill.Components[1].Type != "stun") return false;
            foreach (var key in skill.OriginalFields.Keys)
                if (key != "name" && key != "trigger" && key != "components" &&
                    key != "chancePercent" && key != "target") return false;
            var summon = skill.Components[0];
            foreach (var key in summon.Fields.Keys)
                if (key != "type" && key != "unit" && key != "durationSeconds" &&
                    key != "basicAttackRadius" &&
                    key != "basicAttackMagicalDamage") return false;
            var stun = skill.Components[1];
            foreach (var key in stun.Fields.Keys)
                if (key != "type" && key != "durationSeconds" &&
                    key != "scope") return false;
            if (skill.Target != "single" ||
                stun.Text("scope") != "singlePrimaryTarget" ||
                !PositiveFinite(summon.Number("durationSeconds")) ||
                !PositiveFinite(summon.Number("basicAttackRadius")) ||
                !PositiveFinite(summon.Number("basicAttackMagicalDamage")) ||
                !PositiveFinite(stun.Number("durationSeconds")) ||
                string.IsNullOrWhiteSpace(summon.Text("unit"))) return false;
            name = summon.Text("unit");
            duration = summon.Number("durationSeconds").Value;
            stunSeconds = stun.Number("durationSeconds").Value;
            return true;
        }

        public static double FlatFormPower(IReadOnlyList<CombatEffect> effects)
        {
            double power = 0;
            foreach (var effect in effects)
                if (effect.Type == CombatEffectType.AttackPowerBuff &&
                    effect.SourceId.StartsWith("practice-form:power:", StringComparison.Ordinal))
                    power = Math.Max(power, effect.Amount);
            return power;
        }

        public static double FormIntervalReduction(IReadOnlyList<CombatEffect> effects)
        {
            double reduction = 0;
            foreach (var effect in effects)
                if (effect.Type == CombatEffectType.AttackSpeedBuff &&
                    effect.SourceId.StartsWith("practice-form:interval:", StringComparison.Ordinal))
                    reduction = Math.Max(reduction, effect.Amount);
            return reduction;
        }
    }
}
