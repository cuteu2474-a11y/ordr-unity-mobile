using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Exact native hero attributes are not a rarity, unit-point value or
    // nominal attack-power conversion. Baselines must come from the pinned
    // 2.323[C] UnitBalance.slk; level additions come from the LIVE XP wallet.
    // Bonuses (GetHeroStatBJ with includeBonuses=true) are a separate live
    // native observation. Missing any input yields null, NEVER a guessed 0.
    public sealed class Original323HeroAttributeWorld
    {
        public const string OriginalW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        private sealed class BaseAttributes
        {
            public int Strength;
            public int Agility;
            public int Intelligence;
        }

        private readonly Func<long, Original323HeroLevelState> liveLevels;
        private readonly Dictionary<string, BaseAttributes> sourceBases =
            new Dictionary<string, BaseAttributes>(StringComparer.Ordinal);
        private Func<long, string, double?> liveHeroBonus;
        private Func<long, string, double?> liveSatelliteBonus;

        public Original323HeroAttributeWorld(
            Func<long, Original323HeroLevelState> witnessedLevels)
        {
            liveLevels = witnessedLevels ??
                throw new ArgumentNullException(nameof(witnessedLevels));
        }

        // No default rows: direct original UnitBalance.slk inspection exposes
        // explicit STR/AGI/INT triples for 4 of 70 uppercase H rawcodes.
        // All 66 other hero rows OMIT the fields; an omitted cell is not a
        // witnessed zero and must remain unknown until sourced separately.
        // Use only matching rawcode and pinned-map original SLK evidence.
        public void RegisterOriginalUnitBalanceRow(string sourceW3xSha256,
            string originalRawcode, int strength, int agility, int intelligence)
        {
            if (sourceW3xSha256 != OriginalW3xSha256 ||
                string.IsNullOrEmpty(originalRawcode) ||
                originalRawcode.Length != 4 || originalRawcode[0] != 'H' ||
                strength < 0 || agility < 0 || intelligence < 0)
                throw new ArgumentException("원본 영웅 능력치의 출처·형태·값이 유효하지 않습니다.");
            BaseAttributes existing;
            if (sourceBases.TryGetValue(originalRawcode, out existing))
            {
                if (existing.Strength != strength || existing.Agility != agility ||
                    existing.Intelligence != intelligence)
                    throw new InvalidOperationException(
                        "동일한 원본 영웅 형태에 상충하는 SLK 능력치가 등록됐습니다.");
                return;
            }
            sourceBases.Add(originalRawcode, new BaseAttributes {
                Strength = strength, Agility = agility, Intelligence = intelligence
            });
        }

        // Native bonus readers MUST report an explicit integer zero when
        // the original object has no bonuses. A missing reader is UNKNOWN,
        // not evidence that the native bonus is zero.
        public void ConnectOriginalNativeBonuses(
            Func<long, string, double?> heroBonuses,
            Func<long, string, double?> satelliteBonuses)
        {
            if (liveHeroBonus != null || liveSatelliteBonus != null ||
                (heroBonuses == null && satelliteBonuses == null))
                throw new InvalidOperationException(
                    "원본 능력치 보너스의 중복/빈 연결이 허용되지 않습니다.");
            liveHeroBonus = heroBonuses;
            liveSatelliteBonus = satelliteBonuses;
        }

        private static bool ValidInteger(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) &&
                !double.IsInfinity(value.Value) &&
                Math.Floor(value.Value) == value.Value &&
                value.Value >= -int.MaxValue && value.Value <= int.MaxValue;
        }

        private static double? Baseline(BaseAttributes source, string attribute)
        {
            if (attribute == "strength") return source.Strength;
            if (attribute == "agility") return source.Agility;
            if (attribute == "intelligence") return source.Intelligence;
            return null;
        }

        public double? ReadHero(long ownedInstanceId, string attribute,
            bool includeBonuses)
        {
            if (ownedInstanceId <= 0) return null;
            Original323HeroLevelState growth = liveLevels(ownedInstanceId);
            if (growth == null) return null;
            BaseAttributes source;
            if (!sourceBases.TryGetValue(growth.SourceRawcode, out source))
                return null; // No verified native starting attributes.
            double? initial = Baseline(source, attribute);
            if (!initial.HasValue) return null;
            double added = attribute == "strength" ? growth.AddedStrength :
                attribute == "agility" ? growth.AddedAgility :
                growth.AddedIntelligence;
            return ApplyBonus(initial.Value + added,
                includeBonuses, liveHeroBonus, ownedInstanceId, attribute);
        }

        // Satellites are independently purchased hero objects, not the
        // creator's STR/AGI copied by rawcode. Each individual has its own
        // bonus witness; their base attribute is that form's original SLK
        // starting value, and unobserved native XP/traits are not invented.
        public double? ReadSatellite(long satelliteInstanceId, string rawcode,
            string attribute, bool includeBonuses)
        {
            if (satelliteInstanceId <= 0) return null;
            BaseAttributes source;
            if (!sourceBases.TryGetValue(rawcode ?? "", out source))
                return null;
            double? initial = Baseline(source, attribute);
            if (!initial.HasValue) return null;
            return ApplyBonus(initial.Value, includeBonuses,
                liveSatelliteBonus, satelliteInstanceId, attribute);
        }

        private static double? ApplyBonus(double initial, bool includeBonuses,
            Func<long, string, double?> nativeBonus,
            long instanceId, string attribute)
        {
            if (double.IsInfinity(initial) || double.IsNaN(initial) ||
                initial < 0 || initial > int.MaxValue) return null;
            if (!includeBonuses) return initial;
            if (nativeBonus == null) return null;
            double? bonus = nativeBonus(instanceId, attribute);
            if (!ValidInteger(bonus)) return null;
            double result = initial + bonus.Value;
            return result >= 0 && result <= int.MaxValue ? (double?)result : null;
        }
    }
}
