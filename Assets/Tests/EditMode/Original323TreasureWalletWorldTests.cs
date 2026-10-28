using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323TreasureWalletWorldTests
    {
        private sealed class Casts : IOriginal323TreasureLiveCastWitness
        {
            public bool Approved = true;
            public int Calls;
            public bool TryConfirmOriginalCast(double x,double y,
                double halfExtent,bool collect,int originalRound,
                out string originalCastEvidence)
            {
                Calls++;
                originalCastEvidence = Approved
                    ? "ORDR S2 2.323[C] independently observed BeN cast"
                    : null;
                return Approved;
            }
        }

        private static string Cast(Original323TreasureWalletWorld world)
        {
            string evidence;
            Assert.IsTrue(world.TryConfirmOriginalTreasureCast(
                100,200,850,true,20,out evidence));
            return evidence;
        }
        private static readonly string Spawn =
            "ORDR S2 2.323[C] independently observed BFj spawn";

        [Test]
        public void LiveCastAndRealSharedWalletAreBothRequiredBeforeGrant()
        {
            var wallet = new EconomyWallet();
            var casts = new Casts { Approved = false };
            var world = new Original323TreasureWalletWorld(wallet,casts);
            string proof;
            Assert.IsFalse(world.TryConfirmOriginalTreasureCast(
                100,200,850,true,20,out proof));
            var reward = new Original323TreasureReward(WispResourceIds.Random,2,false);
            Assert.IsFalse(world.TrySettleTreasureOnce("chest-01",reward,
                "fake",Spawn));
            Assert.AreEqual(0,wallet.Balance(WispResourceIds.Random));

            casts.Approved = true;
            proof=Cast(world);
            Assert.IsTrue(world.TrySettleTreasureOnce("chest-01",reward,
                proof,Spawn));
            Assert.AreEqual(2,wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1,world.SettledCount);
        }

        [Test]
        public void DuplicateAndAlteredRewardsCannotBeCreditedTwice()
        {
            var wallet=new EconomyWallet();
            var world=new Original323TreasureWalletWorld(wallet,new Casts());
            var proof=Cast(world);
            var original=new Original323TreasureReward(WispResourceIds.Uncommon,1,false);
            Assert.IsTrue(world.TrySettleTreasureOnce("chest",original,proof,Spawn));
            Assert.IsTrue(world.TrySettleTreasureOnce("chest",original,proof,Spawn));
            var changed=new Original323TreasureReward(WispResourceIds.Random,2,false);
            Assert.IsFalse(world.TrySettleTreasureOnce("chest",changed,proof,Spawn));
            Assert.AreEqual(1,wallet.Balance(WispResourceIds.Uncommon));
            Assert.AreEqual(0,wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1,wallet.CaptureState().CompletedRewards.Count);
        }

        [Test]
        public void BonusBerryRequiresVerifiedResourceBeforeAnyPartOfChestPays()
        {
            var wallet=new EconomyWallet();
            var reward=new Original323TreasureReward(WispResourceIds.Choice,2,true);
            var blocked=new Original323TreasureWalletWorld(wallet,new Casts());
            var proof=Cast(blocked);
            Assert.IsFalse(blocked.TrySettleTreasureOnce("berry",reward,proof,Spawn));
            Assert.AreEqual(0,wallet.Balance(WispResourceIds.Choice));

            var enabled=new Original323TreasureWalletWorld(wallet,new Casts(),
                "verified:source-berry", "source object verified BeI Sx resource");
            var accepted=Cast(enabled);
            Assert.IsTrue(enabled.TrySettleTreasureOnce("berry",reward,accepted,Spawn));
            Assert.AreEqual(2,wallet.Balance(WispResourceIds.Choice));
            Assert.AreEqual(1,wallet.Balance("verified:source-berry"));
            Assert.AreEqual(1,wallet.CaptureState().CompletedRewards.Count);
            Assert.Throws<ArgumentException>(()=>
                new Original323TreasureWalletWorld(wallet,new Casts(),"gold","source"));
        }

        [Test]
        public void ObserverFailureDoesNotLoseReceiptOrRepeatPayout()
        {
            var wallet=new EconomyWallet();
            var world=new Original323TreasureWalletWorld(wallet,new Casts());
            var proof=Cast(world);
            int notifications=0;
            wallet.Granted += _=>{notifications++;throw new InvalidOperationException("observer");};
            var reward=new Original323TreasureReward(WispResourceIds.Random,1,false);
            Assert.Throws<InvalidOperationException>(()=>
                world.TrySettleTreasureOnce("observed",reward,proof,Spawn));
            Assert.AreEqual(1,wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1,world.SettledCount);
            Assert.IsTrue(world.TrySettleTreasureOnce("observed",reward,proof,Spawn));
            Assert.AreEqual(1,notifications);
            Assert.AreEqual(1,wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void ForeignReceiptNeverAuthorizesAnUnrelatedReward()
        {
            var wallet=new EconomyWallet();
            wallet.TryGrantOnce("original323:treasure:foreign",
                new ResourceBundle().Add("gold",1));
            var world=new Original323TreasureWalletWorld(wallet,new Casts());
            var proof=Cast(world);
            Assert.IsFalse(world.TrySettleTreasureOnce("foreign",
                new Original323TreasureReward(WispResourceIds.Random,1,false),
                proof,Spawn));
            Assert.AreEqual(0,wallet.Balance(WispResourceIds.Random));
        }
    }
}
