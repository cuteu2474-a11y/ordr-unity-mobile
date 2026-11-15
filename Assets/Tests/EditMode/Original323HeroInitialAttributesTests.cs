using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    // Static regression source only; execution is a separate user request.
    public sealed class Original323HeroInitialAttributesTests
    {
        private static Original323HeroAttributeWorld CreateWorld()
        {
            var world = new Original323HeroAttributeWorld(id =>
                id == 1 ? Original323HeroLevelRules.Evaluate("H090", 376) :
                id == 2 ? Original323HeroLevelRules.Evaluate("H08V", 376) :
                id == 3 ? Original323HeroLevelRules.Evaluate("H05N", 376) : null);
            Original323UnitBalanceHeroInitialAttributes.RegisterVerifiedRows(world);
            Original323UnitBalanceHeroInitialAttributes.RegisterSparseNumericDefaults(world);
            return world;
        }

        [Test]
        public void MissingNativeNumericCellsDecodeToZeroThenGainRealXpGrowth()
        {
            var world = CreateWorld();
            Assert.AreEqual(3d, world.ReadHero(1, "strength", false));
            Assert.AreEqual(0d, world.ReadHero(1, "agility", false));
            Assert.AreEqual(3d, world.ReadHero(2, "intelligence", false));
            Assert.AreEqual(11d, world.ReadHero(3, "strength", false));
            Assert.AreEqual(17d, world.ReadHero(3, "agility", false));
        }

        [Test]
        public void MissingBonusEvidenceNeverMeansNativeBonusesAreZero()
        {
            var world = CreateWorld();
            Assert.IsNull(world.ReadHero(1, "strength", true));
            Assert.IsNull(world.ReadSatellite(55, "H0AB", "agility", true));
            Assert.AreEqual(0d, world.ReadSatellite(55, "H0AB", "agility", false));
            Assert.IsNull(world.ReadHero(991, "strength", false));
            Assert.IsNull(world.ReadHero(1, "invalid", false));

            world.ConnectOriginalNativeBonuses((id, attribute) => 0, null);
            Assert.AreEqual(3d, world.ReadHero(1, "strength", true));
            Assert.IsNull(world.ReadSatellite(55, "H0AB", "agility", true));
        }

        [Test]
        public void UnknownOrWrongSourceRowsCannotBeSynthesized()
        {
            var world = CreateWorld();
            Assert.Throws<ArgumentException>(() =>
                world.RegisterOriginalUnitBalanceRow("WRONG", "HZZZ", 0, 0, 0));
            Assert.Throws<ArgumentException>(() =>
                world.RegisterOriginalUnitBalanceRow(
                    Original323HeroAttributeWorld.OriginalW3xSha256, "h003", 0, 0, 0));
            Assert.Throws<ArgumentException>(() =>
                world.RegisterOriginalUnitBalanceSparseNumericDefault(
                    Original323HeroAttributeWorld.OriginalW3xSha256, "WRONG", "H090"));
        }
    }
}
