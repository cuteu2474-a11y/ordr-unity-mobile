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
            public int LastRemainingChests=-1;
            public bool TryConsumeApprovedAttempt(int round,int remaining,
                out MobileTreasureAttempt ticket)
            {Calls++;LastRemainingChests=remaining;ticket=Attempt;return ticket!=null;}
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
        public void PolicyNeverSeesPreloadedFutureWaveChests()
        {
            var native=new Native();var rng=new Rolls();
            var hunt=new Original323TreasureHunt(native,rng);
            hunt.RegisterObservedSpawn("wave-20",20,100,100,
                "observed original BFj round20 chest");
            var policy=new Policy {Attempt=Ticket("future-check")};
            var gacha=new MobileTreasureGacha(hunt,policy);
            var result=gacha.TrySearch(10);
            Assert.AreEqual(MobileTreasureOutcome.Miss,result.Outcome);
            Assert.AreEqual(0,policy.LastRemainingChests);
            Assert.AreEqual(0,result.Collected);
            Assert.AreEqual(0,native.Payments);
            Assert.AreEqual(0,rng.Count);
            Assert.AreEqual(1,hunt.UncollectedCountAtOriginalRound(20));
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

        private sealed class RestoreWitness : IOriginal323TreasureResumeWitness
        {
            public bool Approved=true;
            public string Evidence="original W3X treasure session wallet and policy receipt ledger verified";
            public int Calls;
            public bool TryConfirmSameOriginalTreasureSession(
                string hash,string calibration,int spawned,int collected,
                IReadOnlyList<string> ids,out string proof)
            {
                Calls++;
                proof=Evidence;
                return Approved && spawned==2 && collected==0 && ids.Count==1;
            }
        }

        [Test]
        public void SaveAndVerifiedRestoreNeverReplaysConsumedAttempt()
        {
            var originalNative=new Native();var rng=new Rolls();
            var policy=new Policy {Attempt=Ticket("used-once",-4000,-2000)};
            var original=new MobileTreasureGacha(Hunt(originalNative,rng),policy);
            Assert.AreEqual(MobileTreasureOutcome.Miss,original.TrySearch(10).Outcome);
            var saved=original.CaptureCheckpoint();
            Assert.AreEqual(1,saved.ConsumedAttemptIds.Count);
            Assert.AreEqual(2,saved.SpawnedChests);
            Assert.AreEqual(0,saved.CollectedChests);

            var newNative=new Native();
            var restored=new MobileTreasureGacha(Hunt(newNative,new Rolls()),policy);
            var source=new RestoreWitness();
            Assert.IsTrue(restored.TryRestoreCheckpoint(saved,source));
            Assert.AreEqual(1,source.Calls);
            Assert.AreEqual(1,restored.AttemptsUsed);
            Assert.AreEqual(MobileTreasureOutcome.Unavailable,restored.TrySearch(10).Outcome);
            Assert.AreEqual(0,newNative.Casts);
            Assert.AreEqual(0,newNative.Payments);
            Assert.AreEqual(1,originalNative.Casts);
        }

        [Test]
        public void RestoreNeverTrustsUnverifiedOrMismatchedLedgers()
        {
            var policy=new Policy {Attempt=Ticket("once",-4000,-2000)};
            var first=new MobileTreasureGacha(Hunt(new Native(),new Rolls()),policy);
            first.TrySearch(10);
            var saved=first.CaptureCheckpoint();
            var restored=new MobileTreasureGacha(Hunt(new Native(),new Rolls()),policy);
            var denial=new RestoreWitness {Approved=false};
            Assert.IsFalse(restored.TryRestoreCheckpoint(saved,denial));
            Assert.AreEqual(0,restored.AttemptsUsed);
            var untrusted=new RestoreWitness {Evidence="TEST_ONLY fabricated checkpoint"};
            Assert.IsFalse(restored.TryRestoreCheckpoint(saved,untrusted));
            Assert.AreEqual(0,restored.AttemptsUsed);
            Assert.IsFalse(restored.TryRestoreCheckpoint(saved,null));

            var wrongCalibration=new Policy {
                Attempt=Ticket("once",-4000,-2000),
                CalibrationEvidence="different verified Warcraft calibration"
            };
            var changed=new MobileTreasureGacha(Hunt(new Native(),new Rolls()),
                wrongCalibration);
            var approved=new RestoreWitness();
            Assert.IsFalse(changed.TryRestoreCheckpoint(saved,approved));
            Assert.AreEqual(0,approved.Calls);
        }

        [Test]
        public void CannotResetAnAlreadyActiveAttemptJournal()
        {
            var policy=new Policy {Attempt=Ticket("once",-4000,-2000)};
            var current=new MobileTreasureGacha(Hunt(new Native(),new Rolls()),policy);
            var old=new MobileTreasureGacha(Hunt(new Native(),new Rolls()),policy);
            old.TrySearch(10);
            var saved=old.CaptureCheckpoint();
            current.TrySearch(10);
            Assert.IsFalse(current.TryRestoreCheckpoint(saved,new RestoreWitness()));
            Assert.AreEqual(1,current.AttemptsUsed);
            var changedChests=new Original323TreasureHunt(new Native(),new Rolls());
            changedChests.RegisterObservedSpawn("single",0,100,100,
                "original observed Warcraft BFj treasure spawn");
            var mismatched=new MobileTreasureGacha(changedChests,policy);
            Assert.IsFalse(mismatched.TryRestoreCheckpoint(saved,new RestoreWitness()));
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
