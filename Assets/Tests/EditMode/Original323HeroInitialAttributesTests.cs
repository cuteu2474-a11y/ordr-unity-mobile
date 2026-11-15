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

 
        private sealed class NativeStats : IOriginal323CurrentNativeHeroAttributeWitness
        {
            public Original323NativeHeroAttributeSnapshot Hero;
            public Original323NativeHeroAttributeSnapshot Satellite;
            public bool TryReadOwnedHero(long id, string rawcode,
                out Original323NativeHeroAttributeSnapshot snapshot)
            {
                snapshot = Hero;
                return snapshot != null;
            }
            public bool TryReadPurchasedSatellite(long id, string rawcode,
                out Original323NativeHeroAttributeSnapshot snapshot)
            {
                snapshot = Satellite;
                return snapshot != null;
            }
        }

        private static Original323NativeHeroAttributeSnapshot Observed(
            long id, string raw, long formRevision,
            int str, int agi, int intel, int withStr, int withAgi, int withIntel)
        {
            return new Original323NativeHeroAttributeSnapshot(
                Original323HeroAttributeWorld.OriginalW3xSha256,
                id, raw, formRevision, 1, str, agi, intel,
                withStr, withAgi, withIntel);
        }

        [Test]
        public void LiveBonusedAndUnbonusedStatsRemainIndependentAndCurrent()
        {
            string rawcode = "H098";
            long formRevision = 0;
            var witness = new NativeStats();
            var world = new Original323HeroAttributeWorld(
                id => id == 42
                    ? Original323HeroLevelRules.Evaluate(rawcode, 376) : null,
                id => formRevision);
            Original323UnitBalanceHeroInitialAttributes.RegisterVerifiedRows(world);
            Original323UnitBalanceHeroInitialAttributes.RegisterSparseNumericDefaults(world);
            world.ConnectOriginalCurrentNativeStats(witness);
            Assert.IsNull(world.ReadHero(42, "strength", true));
            witness.Hero = Observed(42, "H098", 0, 12, 13, 14, 20, 21, 22);
            Assert.AreEqual(12d, world.ReadHero(42, "strength", false));
            Assert.AreEqual(20d, world.ReadHero(42, "strength", true));
            Assert.AreEqual(22d, world.ReadHero(42, "intelligence", true));
            // This is a current observation; a dispel/unequip revision
            // changes the answer without re-registering a new callback.
            witness.Hero = new Original323NativeHeroAttributeSnapshot(
                Original323HeroAttributeWorld.OriginalW3xSha256,
                42, "H098", 0, 2, 12, 13, 14, 12, 13, 14);
            Assert.AreEqual(12d, world.ReadHero(42, "strength", true));
            rawcode = "H08E";
            formRevision++;
            // A stale H098 observation cannot cross the form boundary.
            Assert.IsNull(world.ReadHero(42, "strength", true));
        }

        [Test]
        public void JassStatSetCopiesUnbonusedTripletAndOnlyFutureXpGrowth()
        {
            string rawcode = "H098";
            long revision = 0;
            long xp = 376;
            var witness = new NativeStats {
                Hero = Observed(42, "H098", 0, 20, 30, 40, 25, 35, 45)
            };
            var world = new Original323HeroAttributeWorld(
                id => id == 42
                    ? Original323HeroLevelRules.Evaluate(rawcode, xp) : null,
                id => revision);
            Original323UnitBalanceHeroInitialAttributes.RegisterVerifiedRows(world);
            Original323UnitBalanceHeroInitialAttributes.RegisterSparseNumericDefaults(world);
            world.ConnectOriginalCurrentNativeStats(witness);
            Assert.IsNull(world.CaptureJassSetHeroStats(42, "H0BY"));
            var transfer = world.CaptureJassSetHeroStats(42, "H08E");
            Assert.IsNotNull(transfer);
            Assert.AreEqual("Bjq", transfer.OriginalJassCallback);
            rawcode = "H08E";
            revision++;
            Assert.IsTrue(world.CommitJassSetHeroStats(transfer));
            witness.Hero = null;
            Assert.AreEqual(20d, world.ReadHero(42, "strength", false));
            Assert.AreEqual(30d, world.ReadHero(42, "agility", false));
            Assert.AreEqual(40d, world.ReadHero(42, "intelligence", false));
            Assert.IsNull(world.ReadHero(42, "strength", true));
            xp = 592; // level six, one more new-form growth step
            Assert.AreEqual(21d, world.ReadHero(42, "strength", false));
            Assert.AreEqual(31d, world.ReadHero(42, "agility", false));
            Assert.AreEqual(41d, world.ReadHero(42, "intelligence", false));
            // A later form revision invalidates the previous SET receipt.
            rawcode = "H098";
            revision++;
            Assert.AreEqual(4d, world.ReadHero(42, "agility", false));
            rawcode = "H08E";
            revision++;
            Assert.AreEqual(4d, world.ReadHero(42, "agility", false));
            world.ForgetOwnedHero(42);
        }

        [Test]
        public void SatelliteStatsNeverBorrowAnotherPurchasedInstance()
        {
            var witness = new NativeStats {
                Satellite = Observed(81, "H0AB", 0, 2, 8, 0, 3, 11, 0)
            };
            var world = new Original323HeroAttributeWorld(
                id => null);
            Original323UnitBalanceHeroInitialAttributes.RegisterVerifiedRows(world);
            Original323UnitBalanceHeroInitialAttributes.RegisterSparseNumericDefaults(world);
            world.ConnectOriginalCurrentNativeStats(witness);
            Assert.AreEqual(11d, world.ReadSatellite(81, "H0AB", "agility", true));
            Assert.IsNull(world.ReadSatellite(82, "H0AB", "agility", true));
            Assert.IsNull(world.ReadSatellite(81, "H0AE", "agility", true));
            Assert.Throws<ArgumentException>(() =>
                Observed(81, "H0AB", 0, 1, 1, 1, -2, 1, 1));
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
        }
    }
}
