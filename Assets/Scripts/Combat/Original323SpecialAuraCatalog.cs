using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Source: original-2323C-special-six-aura-evidence.json, re-extracted
    // from the pinned W3X. Object values do not establish target immunity,
    // stacking, linger ticks, or the native attack-buff damage formula.
    public sealed class Original323SpecialAura
    {
        public string UnitId { get; }
        public string UnitRawcode { get; }
        public string AbilityRawcode { get; }
        public string BuffRawcode { get; }
        public string ParentAbility { get; }
        public string TargetMask { get; }
        public CombatEffectType Type { get; }
        public double Amount { get; }
        public double SignedRawObjectValue { get; }
        public double Radius { get; }
        public bool TargetsAllies => Type == CombatEffectType.AttackPowerBuff;
        public string Evidence => "W3X:" + Original323UncommonAbilityCatalog.SourceW3xSha256 +
            "/" + UnitRawcode + "/" + AbilityRawcode + "/" + BuffRawcode;

        internal Original323SpecialAura(string id, string unit, string ability,
            string buff, string parent, CombatEffectType type, double amount,
            double rawValue, double radius)
        {
            UnitId = id; UnitRawcode = unit; AbilityRawcode = ability;
            BuffRawcode = buff; ParentAbility = parent; Type = type;
            Amount = amount; SignedRawObjectValue = rawValue; Radius = radius;
            TargetMask = TargetsAllies
                ? "air,invulnerable,organic,self,ground,vulnerable,friend"
                : "air,enemies,ground";
        }

        public string SourceKey(long caster) =>
            "original-aura:" + caster.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" + AbilityRawcode + ":" + BuffRawcode + ":1";
    }

    public sealed class Original323SpecialAuraCatalog
    {
        private readonly Dictionary<string, Original323SpecialAura> entries =
            new Dictionary<string, Original323SpecialAura>(StringComparer.Ordinal);
        public int Count => entries.Count;
        public IEnumerable<Original323SpecialAura> All => entries.Values;

        public Original323SpecialAuraCatalog(OriginalMapBaseStatCatalog map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.SourceW3xSha256 != Original323UncommonAbilityCatalog.SourceW3xSha256)
                throw new FormatException("특별함 오라 원본 버전 불일치");
            var pinned = new[] {
                new Original323SpecialAura("S-10", "h016", "A08N", "B011", "AHad",
                    CombatEffectType.ArmorReduction, 3, -3, 800),
                new Original323SpecialAura("S-26", "h01E", "A0EO", "B02G", "AHad",
                    CombatEffectType.ArmorReduction, 3, -3, 800),
                new Original323SpecialAura("S-14", "h01F", "A08F", "B012", "AOae",
                    CombatEffectType.MovementSlowPercent, 5, -.05, 800),
                new Original323SpecialAura("S-29", "h01A", "A08E", "B001", "AOae",
                    CombatEffectType.MovementSlowPercent, 5, -.05, 850),
                new Original323SpecialAura("S-30", "h00Y", "A08H", "B004", "AOae",
                    CombatEffectType.MovementSlowPercent, 5, -.05, 800),
                new Original323SpecialAura("S-25", "h01D", "A0EM", "B00V", "ACac",
                    CombatEffectType.AttackPowerBuff, 7, .07000000029802322, 850)
            };
            foreach (var aura in pinned)
            {
                var owner = map.Find(aura.UnitId);
                if (owner == null || owner.OriginalRawcode != aura.UnitRawcode)
                    throw new FormatException("특별함 오라 소유 유닛 불일치: " + aura.UnitId);
                bool found = false;
                foreach (string code in owner.OriginalAbilityRawcodes)
                    if (code == aura.AbilityRawcode) found = true;
                if (!found) throw new FormatException("원본 오라 능력 누락: " + aura.AbilityRawcode);
                entries.Add(aura.UnitId, aura);
            }
        }

        public Original323SpecialAura Find(string unitId)
        {
            Original323SpecialAura value;
            return unitId != null && entries.TryGetValue(unitId, out value) ? value : null;
        }
    }

    // Read-only original target AND buff-presence-at-this-tick approval.
    // Proves source ability present at level 1, native mask/ownership/immunity,
    // strict radius at this tick and aura update/linger semantics. If native
    // linger requires an out-of-range buff, this strict-radius path must NOT
    // be approved until that timeline has its own implementation.
    // Rejected/unknown is never interpreted as an eligible default target.
    public interface IOriginal323SpecialAuraTargetRules
    {
        bool CanAffectEnemy(Original323SpecialAura aura, UnitState caster,
            SpatialCombatEnemy target, double time, out string evidence);
        bool CanAffectAlly(Original323SpecialAura aura, UnitState caster,
            UnitState target, double time, out string evidence);
    }
}
