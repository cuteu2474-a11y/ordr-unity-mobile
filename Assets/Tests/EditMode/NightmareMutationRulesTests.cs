using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class NightmareMutationRulesTests
    {
        [TestCase(ElderCurse.None, 211, 102)]
        [TestCase(ElderCurse.Nusjuro, 211, 117)]
        [TestCase(ElderCurse.Warcury, 221, 102)]
        [TestCase(ElderCurse.Saturn, 211, 102)]
        public void RecommendedTargetsMatchVersion2323Guide(ElderCurse curse, int armor, int slow)
        {
            var targets = NightmareMutationRules.RecommendedBuild(curse);
            Assert.AreEqual(armor, targets.ArmorReduction);
            Assert.AreEqual(slow, targets.MovementSlowPercent);
        }

        [Test]
        public void GameModeIsFixedToNightmareMutation()
        {
            Assert.AreEqual("nightmare", NightmareMutationRules.Difficulty);
            Assert.AreEqual("mutation", NightmareMutationRules.Mode);
        }
    }
}
