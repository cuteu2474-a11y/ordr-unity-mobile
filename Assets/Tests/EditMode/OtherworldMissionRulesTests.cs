using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class OtherworldMissionRulesTests
    {
        [Test]
        public void MysticFusionMissionOnlyAppearsInOtherworld()
        {
            Assert.IsFalse(OtherworldMissionRules.CanAppear(PersonalMissionKind.OtherworldMysticFusion, false));
            Assert.IsTrue(OtherworldMissionRules.CanAppear(PersonalMissionKind.OtherworldMysticFusion, true));
            Assert.IsTrue(OtherworldMissionRules.CanAppear(PersonalMissionKind.Other, false));
        }

        [Test]
        public void RerollIsRestrictedToBeforeRoundTen()
        {
            Assert.IsTrue(OtherworldMissionRules.IsRerollRoundEligible(1));
            Assert.IsTrue(OtherworldMissionRules.IsRerollRoundEligible(9));
            Assert.IsFalse(OtherworldMissionRules.IsRerollRoundEligible(10));
            Assert.IsFalse(OtherworldMissionRules.IsRerollRoundEligible(11));
            Assert.Throws<ArgumentOutOfRangeException>(() => OtherworldMissionRules.IsRerollRoundEligible(0));
        }
    }
}
