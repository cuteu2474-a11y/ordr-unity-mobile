using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Exact object-data dice and Warcraft attack-type classification for
    // catalogued units. These are native attack CANDIDATES, not proof that
    // JASS/upgrades leave the attack untouched or that HP rounding is known.
    public sealed class OriginalMapCommonNativeAttackEvidence
    {
        public string UnitId { get; private set; }
        public string Rawcode { get; private set; }
        public int BaseDamagePlus { get; private set; }
        public int DiceCount { get; private set; }
        public int DiceSides { get; private set; }
        public double CooldownSeconds { get; private set; }
        public Ordr323AttackType NativeAttackType { get; private set; }

        internal OriginalMapCommonNativeAttackEvidence(OriginalMapCharacterBasics source)
        {
            var attack = source.Attack1;
            UnitId = source.UnitId;
            Rawcode = source.OriginalRawcode;
            BaseDamagePlus = attack.BaseDamagePlus.Value;
            DiceCount = attack.DiceCount.Value;
            DiceSides = attack.DiceSides.Value;
            CooldownSeconds = attack.AttackCooldownSeconds.Value;
            switch (attack.OriginalAttackType)
            {
                // Warcraft Object Editor 'normal' is the normal/melee damage
                // table row, not JASS ATTACK_TYPE_NORMAL ('spells').
                case "normal": NativeAttackType = Ordr323AttackType.Melee; break;
                case "pierce": NativeAttackType = Ordr323AttackType.Pierce; break;
                case "siege": NativeAttackType = Ordr323AttackType.Siege; break;
                case "hero": NativeAttackType = Ordr323AttackType.Hero; break;
                default: throw new FormatException(
                    UnitId + ": 분류하지 않은 워크래프트 평타 공격 타입입니다.");
            }
        }

        // Only the dice expression is sourced. The supplied PRNG does not
        // purport to reproduce the Warcraft RNG state/order.
        public long RollObjectBaseDamage(ICombatProcRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            long raw = BaseDamagePlus;
            for (int die = 0; die < DiceCount; die++)
            {
                int roll = random.Next(DiceSides);
                if (roll < 0 || roll >= DiceSides)
                    throw new InvalidOperationException("주사위 난수 공급자가 유효 범위를 벗어났습니다.");
                raw = checked(raw + roll + 1);
            }
            return raw;
        }

        // A known map constant, useful before a complete verified hit exists.
        // This does not decide buffs, target armor, resistance or rounding.
        public double AttackTableFactor(Ordr323ArmorType armorType)
        {
            return Ordr323MapDamageMath.AttackVersusArmor(NativeAttackType, armorType);
        }
    }

    // Every linked map object can supply attack dice/cooldown. Native combat
    // is *still* disabled unless the independent attack-path verifier approves
    // that specific catalog ID and rawcode (including any splash/JASS effects).
    public sealed class OriginalMapMappedNativeAttackEvidenceCatalog
    {
        private readonly Dictionary<string, OriginalMapCommonNativeAttackEvidence> entries =
            new Dictionary<string, OriginalMapCommonNativeAttackEvidence>(StringComparer.Ordinal);

        public int Count { get { return entries.Count; } }
        public IEnumerable<OriginalMapCommonNativeAttackEvidence> All
        {
            get { return entries.Values; }
        }

        // One map-object eligibility check is shared by all-grade and strict
        // nine-common audits. This does not approve final combat damage.
        internal static bool HasCompleteSourceAttack(OriginalMapCharacterBasics unit)
        {
            if (unit == null || string.IsNullOrWhiteSpace(unit.OriginalRawcode))
                return false;
            var attack = unit.Attack1;
            return attack != null && attack.BaseDamagePlus.HasValue &&
                attack.DiceCount.HasValue && attack.DiceSides.HasValue &&
                attack.AttackCooldownSeconds.HasValue &&
                !string.IsNullOrWhiteSpace(attack.OriginalAttackType);
        }

        public OriginalMapMappedNativeAttackEvidenceCatalog(OriginalMapBaseStatCatalog map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var rawcodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in map.All)
            {
                if (!HasCompleteSourceAttack(entry))
                    continue;
                if (!rawcodes.Add(entry.OriginalRawcode))
                    throw new FormatException("동일한 원본 공격 rawcode가 중복 연결됐습니다: " +
                        entry.OriginalRawcode);
                var candidate = new OriginalMapCommonNativeAttackEvidence(entry);
                entries.Add(entry.UnitId, candidate);
            }
        }

        public OriginalMapCommonNativeAttackEvidence Find(string catalogId)
        {
            OriginalMapCommonNativeAttackEvidence evidence;
            return catalogId != null && entries.TryGetValue(catalogId, out evidence)
                ? evidence : null;
        }
    }

    public sealed class OriginalMapCommonNativeAttackEvidenceCatalog
    {
        private readonly Dictionary<string, OriginalMapCommonNativeAttackEvidence> entries =
            new Dictionary<string, OriginalMapCommonNativeAttackEvidence>(StringComparer.Ordinal);

        public int Count { get { return entries.Count; } }

        public OriginalMapCommonNativeAttackEvidenceCatalog(OriginalMapBaseStatCatalog map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            for (int index = 1; index <= 9; index++)
            {
                string id = "C-" + index.ToString("D2");
                var entry = map.Find(id);
                if (entry == null ||
                    entry.MappingStatus != "verified-common-map-identity" ||
                    !OriginalMapMappedNativeAttackEvidenceCatalog.HasCompleteSourceAttack(entry))
                    throw new FormatException(id + ": 원본 흔함 공격 데이터가 불완전합니다.");
                entries.Add(id, new OriginalMapCommonNativeAttackEvidence(entry));
            }
        }

        public OriginalMapCommonNativeAttackEvidence Find(string catalogUnitId)
        {
            OriginalMapCommonNativeAttackEvidence evidence;
            return catalogUnitId != null && entries.TryGetValue(catalogUnitId, out evidence)
                ? evidence : null;
        }
    }
}
