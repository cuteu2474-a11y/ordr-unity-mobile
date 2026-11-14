using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrResumeUnitGamblingStateTests
    {
        [Test]
        public void OtherworldPendingIdentityAndBranchAreValidatedBeforeResume()
        {
            var good = new SourceModelOtherworldUnitGamblingState {
                Pending = new SourceModelOtherworldPendingState {
                    Navigation = Original323GamblingNavigation.Casino,
                    HasBranch = true, Branch = 80,
                    HasCasinoBranch = true, CasinoBranch = 1,
                    HasIndex = true, Index = 41,
                    HasOutcome = true, Won = false,
                    Rawcode = Original323OtherworldGamblingRules.CasinoRareRawcodes[41]
                }
            };
            SourceModelOtherworldUnitGambling.ValidateState(good);
            good.Pending.Rawcode = "h060";
            Assert.Throws<ArgumentException>(() =>
                SourceModelOtherworldUnitGambling.ValidateState(good));
            good.Pending.Rawcode = Original323OtherworldGamblingRules.CasinoRareRawcodes[41];
            good.Pending.Index = 99;
            Assert.Throws<FormatException>(() =>
                SourceModelOtherworldUnitGambling.ValidateState(good));
        }

        [Test]
        public void UnitGamblingPendingResultsRejectInventedUnits()
        {
            var good = new SourceModelUnitGamblingState {
                Pending = new SourceModelUnitGamblingPendingState {
                    Tier = OriginalGambleTier.Low,
                    Navigation = Original323GamblingNavigation.None,
                    Ready = true, Won = true
                }
            };
            good.Pending.Rolls.Add(99);
            good.Pending.Rawcodes.Add(Original323UnitGamblingRules.Common[0]);
            SourceModelUnitGambling.ValidateState(good);
            good.Pending.Rawcodes[0] = "not-a-source-unit";
            Assert.Throws<FormatException>(() => SourceModelUnitGambling.ValidateState(good));
        }

        [Test]
        public void RerollPendingRejectsInventedUnitAndBrokenIdentity()
        {
            var snapshot = new SourceModelUnitRerollState {
                PendingActor = 77,
                PendingNavigation = Original323GamblingNavigation.Casino,
                PendingRawcode = Original323OtherworldGamblingRules.RandomRawcodes[1]
            };
            SourceModelUnitReroll.ValidateState(snapshot);
            snapshot.PendingRawcode = "fake";
            Assert.Throws<FormatException>(() => SourceModelUnitReroll.ValidateState(snapshot));
            snapshot.PendingActor = 0;
            snapshot.PendingRawcode = null;
            Assert.Throws<FormatException>(() => SourceModelUnitReroll.ValidateState(snapshot));
        }
    }
}
