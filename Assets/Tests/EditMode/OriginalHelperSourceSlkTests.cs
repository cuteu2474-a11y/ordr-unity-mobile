using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalHelperSourceSlkTests
    {
        private readonly Ordr323OriginalHelperCastDataSource source =
            new Ordr323OriginalHelperCastDataSource();

        [TestCase("A0BZ", 1, 500, 100)]
        [TestCase("A0BZ", 2, 333, 66)]
        [TestCase("A07T", 1, 320, 150)]
        [TestCase("A07T", 2, 320, 100)]
        public void ExactOriginalAbilityDataCastFields(
            string rawcode, int level, double mana, double cooldown)
        {
            OriginalHelperCowVerifiedCastData entry;
            Assert.IsTrue(source.TryRead(rawcode, level, out entry));
            Assert.AreEqual(99999.0, entry.CastRange);
            Assert.AreEqual(mana, entry.ManaCost);
            Assert.AreEqual(cooldown, entry.CooldownSeconds);
            Assert.AreEqual(level, entry.Level);
            Assert.AreEqual(rawcode, entry.AbilityRawcode);
            StringAssert.Contains(Ordr323OriginalHelperSource.CastSourceSha256,
                entry.Evidence);
        }

        [Test]
        public void OriginalObjectMissingA0BZLevelThreeManaDoesNotGetInvented()
        {
            OriginalHelperCowVerifiedCastData entry;
            Assert.IsFalse(source.TryRead("A0BZ", 3, out entry));
            Assert.IsFalse(source.TryRead("A07T", 3, out entry));
            Assert.IsFalse(source.TryRead("A000", 1, out entry));
        }

        [Test]
        public void OriginalCowInitialManaRegenAndAbilityPresenceComeFromSlks()
        {
            Assert.AreEqual(15, Ordr323OriginalHelperSource.StartingMana);
            Assert.AreEqual(10000, Ordr323OriginalHelperSource.MaximumMana);
            Assert.AreEqual(0.3, Ordr323OriginalHelperSource.BaseManaRegenPerSecond);

            var cow = new Ordr323OriginalHelperCowActor(0);
            Assert.AreEqual("h08A", cow.OriginalRawcode);
            Assert.AreEqual(0, cow.OwnerPlayerId);
            Assert.AreEqual(-5888, cow.X);
            Assert.AreEqual(4464, cow.Y);
            Assert.AreEqual(15, cow.CurrentMana);
            Assert.AreEqual(10000, cow.MaximumMana);
            Assert.AreEqual(1, cow.GetAbilityLevel("A0BZ"));
            Assert.AreEqual(1, cow.GetAbilityLevel("A07T"));
            cow.AdvanceBaseManaRegeneration(10);
            Assert.AreEqual(18, cow.CurrentMana, 0.000001);
            cow.SetMana(9999.9);
            cow.AdvanceBaseManaRegeneration(10);
            Assert.AreEqual(10000, cow.CurrentMana);
            Assert.IsTrue(cow.IsOriginalCastReady("A0BZ"));
        }

        [Test]
        public void OriginalFourPlayerHelperPositionsAndInvalidPlayer()
        {
            var expected = new[] {
                new [] {-5888.0, 4464.0},
                new [] {128.0, 4464.0},
                new [] {-5888.0, -656.0},
                new [] {128.0, -656.0},
            };
            for (int i = 0; i < expected.Length; i++)
            {
                var actor = new Ordr323OriginalHelperCowActor(i);
                Assert.AreEqual(expected[i][0], actor.X);
                Assert.AreEqual(expected[i][1], actor.Y);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new Ordr323OriginalHelperCowActor(4));
        }

        [Test]
        public void UpgradeDoesNotOccurWithoutOriginalNavigationEvidence()
        {
            var cow = new Ordr323OriginalHelperCowActor(0);
            Assert.Throws<ArgumentException>(() =>
                cow.ConfigureVerifiedAbilityUpgrade("A0BZ", 2, ""));
            Assert.AreEqual(1, cow.GetAbilityLevel("A0BZ"));
            cow.ConfigureVerifiedAbilityUpgrade("A0BZ", 2, "TEST_ONLY navigational upgrade");
            Assert.AreEqual(2, cow.GetAbilityLevel("A0BZ"));
            Assert.Throws<ArgumentException>(() =>
                cow.ConfigureVerifiedAbilityUpgrade("A07T", 3, "TEST_ONLY"));
        }
    }
}
