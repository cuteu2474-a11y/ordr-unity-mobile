using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // ORDR S2 2.323[C]: Units/UnitBalance.slk hero growth, war3mapMisc.txt
    // hero XP settings, and JASS Bz6/Bz7 (Shirahoshi). Values below are
    // source-form rules, not fabricated initial STR/AGI/INT object values.
    public sealed class Original323HeroLevelState
    {
        public string SourceRawcode { get; }
        public long Experience { get; }
        public int Level { get; }
        public long ExperienceToNextLevel { get; }
        public int AddedStrength { get; }
        public int AddedAgility { get; }
        public int AddedIntelligence { get; }
        // Warcraft native primary attribute damage: StrAttackBonus = 800.
        // Only the INCREMENT after level one is added to source object dice.
        public int AdditionalBasicAttackDamage { get; }
        // Native AgiAttackSpeedBonus = 0.01 per point: +1% per added AGI.
        public int AddedAttackSpeedPercent { get; }
        public bool ShirahoshiFourPointTraitActive { get; }
        public int ShirahoshiA0L1Level { get; }
        public int ShirahoshiA0RULevel { get; }
        public int ShirahoshiA0KZLevel { get; }

        internal Original323HeroLevelState(string rawcode, long xp, int level,
            long next, int strength, int agility, int intelligence, int damage,
            bool trait, int a0l1, int a0ru, int a0kz)
        {
            SourceRawcode = rawcode; Experience = xp; Level = level;
            ExperienceToNextLevel = next;
            AddedStrength = strength; AddedAgility = agility;
            AddedIntelligence = intelligence;
            AdditionalBasicAttackDamage = damage;
            AddedAttackSpeedPercent = agility;
            ShirahoshiFourPointTraitActive = trait;
            ShirahoshiA0L1Level = a0l1;
            ShirahoshiA0RULevel = a0ru;
            ShirahoshiA0KZLevel = a0kz;
        }
    }

    public static class Original323HeroLevelRules
    {
        public const int MaximumLevel = 24;
        private enum Primary { Strength, Agility, Intelligence }
        private sealed class Growth
        {
            public readonly double Strength, Agility, Intelligence;
            public readonly Primary Main;
            public Growth(double strength, double agility, double intelligence, Primary main)
            { Strength = strength; Agility = agility; Intelligence = intelligence; Main = main; }
        }
        private static readonly Dictionary<string, Growth> Profiles = BuildProfiles();
        private static readonly long[] CumulativeThresholds = BuildThresholds();

        private static Dictionary<string, Growth> BuildProfiles()
        {
            var result = new Dictionary<string, Growth>(StringComparer.Ordinal);
            Add(result,
                "H0BT H090 H08X H0AY H09D H0C9 H095 H0BY H0B4 H09E H0BX H0AE H09A H092 H09F H099",
                new Growth(.85, .21, .21, Primary.Strength));
            Add(result,
                "H094 H09B H08V H093 H09I H0B6 H0BN H0AM H09C H08U H0B1 H091 H08B H096 H0BV H0BL H0AA H0C1 H097 H08Y H0BO",
                new Growth(.21, .21, .85, Primary.Intelligence));
            Add(result,
                "H0B3 H0CO H09G H0BK H0AB H0B5 H0B2 H0CL H098 H08E H08Z H08W H09H H08J",
                new Growth(.21, .85, .21, Primary.Agility));
            Add(result, "H0BE H0BZ H0BD H0C0",
                new Growth(.21, 1.06, .21, Primary.Agility));
            Add(result, "H05N H0A5",
                new Growth(.42, 1.70, .42, Primary.Agility));
            Add(result, "H08T", new Growth(0, 0, 0, Primary.Agility));
            if (result.Count != 58)
                throw new InvalidOperationException("원본 영웅 형태 58개의 성장 인덱스가 불완전합니다.");
            return result;
        }

        private static void Add(Dictionary<string, Growth> target, string raws, Growth growth)
        {
            foreach (string raw in raws.Split(' '))
                if (target.ContainsKey(raw))
                    throw new InvalidOperationException("원본 영웅 성장 rawcode 중복: " + raw);
                else target.Add(raw, growth);
        }

        // OpenRealm's Warcraft hero implementation: index 0..3 is the
        // configured NeedHeroXP list; later index i uses truncation of
        // NeedHeroXPFormulaA * previous + B*i (+ default C=0).
        private static long[] BuildThresholds()
        {
            var requirements = new long[MaximumLevel - 1];
            requirements[0] = 33; requirements[1] = 75;
            requirements[2] = 116; requirements[3] = 152;
            for (int i = 4; i < requirements.Length; i++)
                requirements[i] = (long)Math.Floor(1.03 * requirements[i - 1] + 15 * i);
            var cumulative = new long[MaximumLevel];
            for (int level = 2; level <= MaximumLevel; level++)
                cumulative[level - 1] = checked(
                    cumulative[level - 2] + requirements[level - 2]);
            return cumulative;
        }

        public static long TotalExperienceForLevel(int level)
        {
            if (level < 1 || level > MaximumLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return CumulativeThresholds[level - 1];
        }

        public static int LevelForExperience(long experience)
        {
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            int level = 1;
            while (level < MaximumLevel && experience >= CumulativeThresholds[level])
                level++;
            return level;
        }

        // Trait flag must originate from an actual, four-point Bz6 action.
        // The source Bz7 milestones only apply after that activation.
        public static Original323HeroLevelState Evaluate(
            string originalHeroRawcode, long experience,
            bool shirahoshiFourPointTraitActive = false)
        {
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            Growth growth;
            if (originalHeroRawcode == null ||
                !Profiles.TryGetValue(originalHeroRawcode, out growth))
                return null;

            int level = LevelForExperience(experience);
            int completed = level - 1;
            // Original Warcraft native integer attributes truncate the
            // cumulative float growth; they do NOT round each level.
            int str = (int)(completed * growth.Strength);
            int agi = (int)(completed * growth.Agility);
            int intel = (int)(completed * growth.Intelligence);
            bool shira = (originalHeroRawcode == "H08U" ||
                          originalHeroRawcode == "H0C1") &&
                         shirahoshiFourPointTraitActive;
            int a0l1 = shira && level >= 16 ? 2 : 1;
            int a0ru = shira && level >= 20 ? 2 : 1;
            int a0kz = shira && level >= 24 ? 2 : 1;
            if (a0ru == 2)
            {
                str += 10; agi += 10; intel += 10;
            }
            int primary = growth.Main == Primary.Strength ? str :
                growth.Main == Primary.Intelligence ? intel : agi;
            long next = level == MaximumLevel ? 0 : CumulativeThresholds[level] - experience;
            return new Original323HeroLevelState(originalHeroRawcode,
                experience, level, next, str, agi, intel,
                checked(primary * 800), shira, a0l1, a0ru, a0kz);
        }
    }
}
