using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323HelperAlchemyEvidenceTests
    {
        [Test]
        public void OriginalA0J2SourceFieldsAreNotGuesses()
        {
            Assert.AreEqual("A0J2", Ordr323OriginalHelperAlchemy.AbilityRawcode);
            Assert.AreEqual(100, Ordr323OriginalHelperAlchemy.ManaCost);
            Assert.AreEqual(5, Ordr323OriginalHelperAlchemy.CooldownSeconds);
            Assert.AreEqual(99999, Ordr323OriginalHelperAlchemy.CastRange);
            Assert.AreEqual("R037", Ordr323OriginalHelperSource.HighestHelpResearchRawcode);
        }

        [TestCase("h01C", 0, 0, true)]
        [TestCase("h015", 0, 0, true)]
        [TestCase("h000", 1, 0, true)]
        [TestCase("h000", 0, 1, true)]
        [TestCase("h000", 3, 9, true)]
        [TestCase("h000", 0, 0, false)]
        [TestCase("h01B", 0, 0, false)]
        [TestCase("H01C", 0, 0, false)]
        [TestCase("", 1, 1, false)]
        public void CJZAcceptsOnlyListedOwnGradeAbilitiesOrTwoRawcodeExceptions(
            string targetRawcode, int a0ba, int a0b9, bool expected)
        {
            Assert.AreEqual(expected,
                Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                    0, 0, targetRawcode, a0ba, a0b9));
        }

        [Test]
        public void CJZRejectsOtherPlayersBeforeConsideringUnitGrade()
        {
            Assert.IsFalse(Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                0, 1, "h01C", 5, 4));
            Assert.IsFalse(Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                1, 0, "h015", 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                    -1, 0, "h01C", 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                    0, 0, "h015", -1, 0));
        }

        [Test]
        public void MaximumOutputAlsoGrantsOriginalR037Research()
        {
            var cow = new Ordr323OriginalHelperCowActor(0);
            Assert.AreEqual(0, cow.HighestHelpResearchLevel);
            var game = new Ordr.Mobile.Core.GameSession();
            game.Start();
            for (int i = 1; i <= 21; i++) game.BeginWave(i);
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, cow)
                .TryChooseMaximumOutput());
            Assert.AreEqual(1, cow.HighestHelpResearchLevel);
            Assert.AreEqual(2, cow.GetAbilityLevel("A0BZ"));
            Assert.AreEqual(0, cow.GetAbilityLevel("A0J2"));
        }
    }
}
