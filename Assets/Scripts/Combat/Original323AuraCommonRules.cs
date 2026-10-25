using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    // A source-proven aura contribution, independent of unit name/grade.
    // The caller must establish actual ability/rank, target eligibility and
    // current buff presence before constructing these calculation inputs.
    public sealed class Original323AuraContribution
    {
        public string AbilityRawcode { get; }
        public string BuffRawcode { get; }
        public int Level { get; }
        public double Amount { get; }
        public CombatEffectType Type { get; }

        public Original323AuraContribution(string ability, string buff, int level, double amount,
            CombatEffectType type = CombatEffectType.ArmorReduction)
        {
            if (ability == null || ability.Length != 4 || buff == null || buff.Length != 4 ||
                level < 1 || double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0 ||
                !Enum.IsDefined(typeof(CombatEffectType), type))
                throw new ArgumentException("원본 오라 ID/레벨/값이 유효하지 않습니다.");
            AbilityRawcode = ability; BuffRawcode = buff; Level = level; Amount = amount; Type = type;
        }
    }

    public sealed class Original323AuraUnitState
    {
        public bool Alive { get; }
        public bool Hidden { get; }
        public bool Invisible { get; }
        public bool MagicImmune { get; }
        public string Evidence { get; }
        public Original323AuraUnitState(bool alive, bool hidden, bool invisible,
            bool magicImmune, string originalEvidence)
        {
            if (string.IsNullOrWhiteSpace(originalEvidence))
                throw new ArgumentException("원본 유닛 상태의 근거가 필요합니다.");
            Alive = alive; Hidden = hidden; Invisible = invisible;
            MagicImmune = magicImmune; Evidence = originalEvidence;
        }
    }

    public static class Original323AuraCommonRules
    {
        public const string OfficialAuraRules =
            "https://classic.battle.net/war3/basics/spellbasics.shtml";
        // Explicit original map overrides. The native movement equation,
        // clamp order, immobilization and SetUnitMoveSpeed exceptions are
        // NOT established merely by knowing these bounds.
        public const double MapMinimumUnitSpeed = 89;
        public const double MapMaximumUnitSpeed = 490;

        // Common exclusion stage ONLY. Passing it is not approval of a
        // custom target mask, radius, current rank, pulse tick or linger.
        public static bool PassesCommonEligibility(Original323AuraUnitState source,
            Original323AuraUnitState target, bool friendlyToSource)
        {
            return source != null && target != null && source.Alive && target.Alive &&
                !source.Hidden && !source.Invisible && !target.Hidden && !target.Invisible &&
                (!target.MagicImmune || friendlyToSource);
        }

        // Blizzard documents non-stacking of the same aura. Resolve only
        // identical native ability + rank + buff + authored value. Different
        // IDs sharing a buff, unequal ranks, and different aura combinations
        // need additional native proof; never infer sum/max/min by label.
        public static bool TryResolveIdenticalAura(
            IReadOnlyList<Original323AuraContribution> contributions, out double amount)
        {
            amount = 0;
            if (contributions == null) return false;
            Original323AuraContribution first = null;
            foreach (var contribution in contributions)
            {
                if (contribution == null) return false;
                if (first == null) first = contribution;
                else if (first.AbilityRawcode != contribution.AbilityRawcode ||
                    first.BuffRawcode != contribution.BuffRawcode ||
                    first.Level != contribution.Level || first.Amount != contribution.Amount ||
                    first.Type != contribution.Type)
                    return false;
            }
            amount = first == null ? 0 : first.Amount;
            return true;
        }

        // Parse only structured runtime identity. This is not provenance:
        // callers MUST also verify that the effect object was actually
        // emitted by the current source-backed aura runtime.
        public static bool TryReadEffectIdentity(CombatEffect effect,
            out Original323AuraContribution contribution)
        {
            contribution = null;
            if (effect == null || effect.Origin != CombatEffectOrigin.Aura ||
                effect.DurationSeconds != 0) return false;
            var fields = effect.SourceId.Split(':');
            long caster;
            int level;
            if (fields.Length != 5 || fields[0] != "original-aura" ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out caster) || caster < 1 ||
                fields[2].Length != 4 || fields[3].Length != 4 ||
                !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out level) || level < 1)
                return false;
            contribution = new Original323AuraContribution(fields[2], fields[3], level, effect.Amount, effect.Type);
            return true;
        }
    }
}
