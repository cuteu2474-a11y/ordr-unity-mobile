using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileTreasureGachaTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            public int Count;
            private readonly Queue<int> values;
            public Rolls(params int[] rolls) { values=new Queue<int>(rolls); }
            public int NextInclusive(int min,int max) { Count++;return values.Dequeue(); }
        }
        private sealed class Native : IOriginal323TreasureNativeWorld
        {
            public int Casts,Payments;
            public bool Allow=true;
            public bool TryConfirmOriginalTreasureCast(double x,double y,
                double halfExtent,bool collect,int round,out string evidence)
            {
                Casts++;
                evidence="original Warcraft approved equivalent treasure cast";
                return Allow;
            }
            public bool TrySettleTreasureOnce(string chest,
                Original323TreasureReward reward,string cast,string spawn)
            { Payments++;return true; }
        }
        private sealed class Policy : IOriginal323MobileTreasureGachaPolicy
        {
            public bool IsCalibrationApproved {get;set;}=true;
            public string CalibrationEvidence {get;set;}=
                "Warcraft original source comparison observed probability";
            public MobileTreasureAttempt Attempt;
            public int Calls;
            public bool TryConsumeApprovedAttempt(int round,int remaining,
                out MobileTreasureAttempt ticket)
            {Calls++;ticket=Attempt;return ticket!=null;}
        }
        private static MobileTreasureAttempt Ticket(string id,double x=100,double y=100)
            =>new MobileTreasureAttempt(id,
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                10,x,y,true,false,false,false);
        private static Original323TreasureHunt Hunt(Native native,Rolls rng)
        {
            var hunt=new Original323TreasureHunt(native,rng);
            hunt.RegisterObservedSpawn("one",0,100,100,
                "original observed Warcraft BFj treasure spawn");
            hunt.RegisterObservedSpawn("two",0,130,120,
                "original observed Warcraft BFj treasure spawn");
            return hunt;
        }
        [Test]
        public void UncalibratedNeverConsumesTicketOrRewardRandom()
        {
            var native=new Native();var rng=new Rolls();
            var policy=new Policy {IsCalibrationApproved=false,Attempt=Ticket("x")};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),policy);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,gacha.TrySearch(10).Outcome);
            Assert.AreEqual(0,policy.Calls);
            Assert.AreEqual(0,native.Casts);
            Assert.AreEqual(0,rng.Count);
        }
        [Test]
        public void ZeroDiscoveryIsARealMissWithoutRewardRoll()
        {
            var native=new Native();var rng=new Rolls();
            var policy=new Policy {Attempt=Ticket("miss",-4000,-2000)};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),policy);
            var result=gacha.TrySearch(10);
            Assert.AreEqual(MobileTreasureOutcome.Miss,result.Outcome);
            Assert.AreEqual(0,result.Collected);
            Assert.AreEqual(0,rng.Count);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,gacha.TrySearch(10).Outcome);
            Assert.AreEqual(1,native.Casts);
        }
        [Test]
        public void MultiChestRewardsAreSourceBackedAndDisplayedIndividually()
        {
            var native=new Native();var rng=new Rolls(1,999,2,999);
            var policy=new Policy {Attempt=Ticket("ok")};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),policy);
            var result=gacha.TrySearch(10);
            Assert.AreEqual(MobileTreasureOutcome.Found,result.Outcome);
            Assert.AreEqual(2,result.Collected);
            Assert.AreEqual(2,result.Rewards.Count);
            Assert.AreEqual(WispResourceIds.Choice,result.Rewards[0].WispResourceId);
            Assert.AreEqual(WispResourceIds.Uncommon,result.Rewards[1].WispResourceId);
            Assert.AreEqual(4,rng.Count);
            Assert.AreEqual(2,native.Payments);
            StringAssert.Contains("선택위습",result.RewardText());
        }
        [Test]
        public void NativeDenialCannotGiveSuccessOrReroll()
        {
            var native=new Native {Allow=false};var rng=new Rolls();
            var policy=new Policy {Attempt=Ticket("no")};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),policy);
            Assert.AreEqual(MobileTreasureOutcome.Uncertain,gacha.TrySearch(10).Outcome);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,gacha.TrySearch(10).Outcome);
            Assert.AreEqual(0,rng.Count);
            Assert.AreEqual(1,native.Casts);
        }
        [Test]
        public void OriginalDreamAndNamiTicketMismatchNeverPaysOrRolls()
        {
            var native=new Native();var rng=new Rolls();
            var p=new Policy {Attempt=Ticket("dream")};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),p);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,
                gacha.TrySearch(10,true,true).Outcome);
            Assert.AreEqual(0,native.Casts);
            Assert.AreEqual(0,native.Payments);
            Assert.AreEqual(0,rng.Count);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,
                gacha.TrySearch(10,true,false).Outcome); // used ticket
        }

        [Test]
        public void WrongRoundAndInvalidInputNeverAdvanceRewards()
        {
            var native=new Native();var rng=new Rolls();
            var p=new Policy {Attempt=Ticket("wrong")};
            var gacha=new MobileTreasureGacha(Hunt(native,rng),p);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,gacha.TrySearch(11).Outcome);
            Assert.AreEqual(0,native.Casts);
            Assert.AreEqual(0,rng.Count);
            Assert.Throws<ArgumentException>(()=>new MobileTreasureAttempt(
                "invalid",Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                10,1,2,false,true,false,false));
        }
    }
}
