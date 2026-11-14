using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileGameSpeedClockTests
    {
        [Test]
        public void OneTwoFourShareOriginalCombatSecondWithoutAlteringRules()
        {
            var clock = new MobileGameSpeedClock();
            Assert.AreEqual(1, clock.Multiplier);
            Assert.AreEqual(0.04, clock.Scale(0.04, true), 1e-12);
            clock.SetMultiplier(2);
            Assert.AreEqual(0.08, clock.Scale(0.04, true), 1e-12);
            clock.SetMultiplier(4);
            Assert.AreEqual(0.16, clock.Scale(0.04, true), 1e-12);
            clock.SetMultiplier(1);
            Assert.AreEqual(0.04, clock.Scale(0.04, true), 1e-12);
        }

        [Test]
        public void PauseAndBackgroundNeverAccrueMissedSimulationSeconds()
        {
            var clock = new MobileGameSpeedClock();
            clock.SetMultiplier(4);
            Assert.AreEqual(0, clock.Scale(180, false));
            Assert.AreEqual(0, clock.Scale(0.02, false));
            Assert.AreEqual(0.08, clock.Scale(0.02, true), 1e-12);
            // Long frames are bounded instead of releasing repeated
            // attacks, round rewards or cooldowns on app focus return.
            Assert.AreEqual(1.0, clock.Scale(180, true), 1e-12);
        }

        [Test]
        public void RejectsUnsupportedRatesAndInvalidFrameTimes()
        {
            var clock = new MobileGameSpeedClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetMultiplier(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetMultiplier(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetMultiplier(8));
            Assert.AreEqual(1, clock.Multiplier);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                clock.Scale(double.NaN, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                clock.Scale(double.PositiveInfinity, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                clock.Scale(-0.01, true));
        }
    }
}
