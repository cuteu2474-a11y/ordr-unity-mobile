using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323StoryUnlockRulesTests
    {
        [Test]
        public void ConsecutiveStoryBuildingsFollowDeathCallbackAndFinalDelay()
        {
            for (int i = 1; i <= 12; i++)
            {
                int next;
                double seconds;
                Assert.IsTrue(Original323StoryUnlockRules.TryNextStageOnDestroy(
                    i, 6, out next, out seconds));
                Assert.AreEqual(i + 1, next);
                Assert.AreEqual(0, seconds);
            }
            int stage;
            double delay;
            Assert.IsTrue(Original323StoryUnlockRules.TryNextStageOnDestroy(
                13, 6, out stage, out delay));
            Assert.AreEqual(14, stage);
            Assert.AreEqual(120, delay);
            Assert.IsFalse(Original323StoryUnlockRules.TryNextStageOnDestroy(
                14, 6, out stage, out delay));
            Assert.IsFalse(Original323StoryUnlockRules.TryNextStageOnDestroy(
                13, 4, out stage, out delay));
        }

        [TestCase(3,9)]
        [TestCase(4,11)]
        [TestCase(5,12)]
        [TestCase(6,13)]
        public void OriginalRound35DeadlineUsesDestroyedBuildingsNotMerelySpawn(int difficulty,
            int minimumDestroyed)
        {
            Assert.IsTrue(Original323StoryUnlockRules.ShouldLoseAtRoundStart(
                35, difficulty, minimumDestroyed - 1));
            Assert.IsFalse(Original323StoryUnlockRules.ShouldLoseAtRoundStart(
                35, difficulty, minimumDestroyed));
            Assert.IsFalse(Original323StoryUnlockRules.ShouldLoseAtRoundStart(
                34, difficulty, minimumDestroyed - 1));
            Assert.IsFalse(Original323StoryUnlockRules.ShouldLoseAtRoundStart(
                36, difficulty, minimumDestroyed - 1));
        }

        [Test]
        public void NightmareEggheadTimerOnlyStartsOnActualSpawn()
        {
            Assert.IsFalse(Original323StoryUnlockRules.EggheadTimedOut(6, 239.9, false));
            Assert.IsTrue(Original323StoryUnlockRules.EggheadTimedOut(6, 240, false));
            Assert.IsFalse(Original323StoryUnlockRules.EggheadTimedOut(6, 1000, true));
            Assert.IsFalse(Original323StoryUnlockRules.EggheadTimedOut(4, 1000, false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323StoryUnlockRules.EggheadTimedOut(6, -1, false));
        }
    }
}
