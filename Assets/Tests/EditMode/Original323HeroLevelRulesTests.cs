using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public class Original323HeroLevelRulesTests
    {
        [Test]
        public void SourceXpThresholdsUseConfiguredFourStepsThenTruncatedFormula()
        {
            Assert.AreEqual(0, Original323HeroLevelRules.TotalExperienceForLevel(1));
            Assert.AreEqual(33, Original323HeroLevelRules.TotalExperienceForLevel(2));
            Assert.AreEqual(108, Original323HeroLevelRules.TotalExperienceForLevel(3));
            Assert.AreEqual(224, Original323HeroLevelRules.TotalExperienceForLevel(4));
            Assert.AreEqual(376, Original323HeroLevelRules.TotalExperienceForLevel(5));
            Assert.AreEqual(592, Original323HeroLevelRules.TotalExperienceForLevel(6));
            Assert.AreEqual(1, Original323HeroLevelRules.LevelForExperience(32));
            Assert.AreEqual(4, Original323HeroLevelRules.LevelForExperience(375));
            Assert.AreEqual(5, Original323HeroLevelRules.LevelForExperience(376));
            Assert.AreEqual(24, Original323HeroLevelRules.LevelForExperience(long.MaxValue));
        }

        [Test]
        public void NativeTruncationAppliesAtCumulativeLevelsWithoutRoundingEachGain()
        {
            var str = Original323HeroLevelRules.Evaluate("H0BY", 376);
            Assert.AreEqual(5, str.Level);
            Assert.AreEqual(3, str.AddedStrength);
            Assert.AreEqual(0, str.AddedAgility);
            Assert.AreEqual(2400, str.AdditionalBasicAttackDamage);
            var agi = Original323HeroLevelRules.Evaluate("H05N", 376);
            Assert.AreEqual(6, agi.AddedAgility);
            Assert.AreEqual(1, agi.AddedStrength);
            Assert.AreEqual(4800, agi.AdditionalBasicAttackDamage);
            Assert.AreEqual(6, agi.AddedAttackSpeedPercent);
            Assert.AreEqual(0, Original323HeroLevelRules.Evaluate("H08T", 376)
                .AdditionalBasicAttackDamage);
        }

        [Test]
        public void ShirahoshiMilestonesRequireSourceFourPointTrait()
        {
            long xp16 = Original323HeroLevelRules.TotalExperienceForLevel(16);
            var inactive = Original323HeroLevelRules.Evaluate("H08U", xp16);
            var active = Original323HeroLevelRules.Evaluate("H08U", xp16, true);
            Assert.AreEqual(1, inactive.ShirahoshiA0L1Level);
            Assert.AreEqual(2, active.ShirahoshiA0L1Level);
            long xp20 = Original323HeroLevelRules.TotalExperienceForLevel(20);
            var twenty = Original323HeroLevelRules.Evaluate("H0C1", xp20, true);
            var beforeTrait = Original323HeroLevelRules.Evaluate("H0C1", xp20);
            Assert.AreEqual(2, twenty.ShirahoshiA0RULevel);
            Assert.AreEqual(beforeTrait.AddedStrength + 10, twenty.AddedStrength);
            Assert.AreEqual(beforeTrait.AddedAgility + 10, twenty.AddedAgility);
            Assert.AreEqual(beforeTrait.AddedIntelligence + 10, twenty.AddedIntelligence);
            Assert.AreEqual(1, twenty.ShirahoshiA0KZLevel);
            Assert.AreEqual(2, Original323HeroLevelRules.Evaluate("H0C1",
                Original323HeroLevelRules.TotalExperienceForLevel(24), true)
                .ShirahoshiA0KZLevel);
        }

        [Test]
        public void UnmappedOrInvalidSourceXpNeverCreatesFakeHeroLevels()
        {
            Assert.IsNull(Original323HeroLevelRules.Evaluate("h08U", 300));
            Assert.IsNull(Original323HeroLevelRules.Evaluate("HXXX", 300));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323HeroLevelRules.LevelForExperience(-1));
        }
    }
}
