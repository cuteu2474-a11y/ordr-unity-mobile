using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class MobileWispServiceTests
    {
        private sealed class Roller : IVerifiedWispRoller
        {
            public bool Verified = true;
            public bool IsVerified(string id) => Verified;
            public WispReward Roll(string id) => new WispReward("TEST_ONLY_UNIT", id == WispResourceIds.Rare ? "A0YE" : null);
        }
        private sealed class World : IWispRewardWorld
        {
            public int Count;
            public bool Fail;
            public void Apply(WispReward reward)
            {
                if (Fail) throw new InvalidOperationException();
                Count++;
            }
        }
        [Test]
        public void RandomAndChoiceStayInWallet()
        {
            var wallet = new EconomyWallet();
            var world = new World();
            var svc = new MobileWispService(wallet, new Roller(), world);
            svc.Grant(WispResourceIds.Random, 5);
            svc.Grant(WispResourceIds.Choice, 2);
            Assert.AreEqual(5, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Choice));
            Assert.AreEqual(0, world.Count);
        }
        [Test]
        public void AutoKindsConsumeAfterReward()
        {
            var wallet = new EconomyWallet();
            var world = new World();
            var svc = new MobileWispService(wallet, new Roller(), world);
            foreach (var id in new [] { WispResourceIds.Uncommon, WispResourceIds.Special, WispResourceIds.Rare, WispResourceIds.Legendary })
            {
                svc.Grant(id);
                Assert.AreEqual(0, wallet.Balance(id));
            }
            Assert.AreEqual(4, world.Count);
        }
        [Test]
        public void FailedRewardPreservesWisp()
        {
            var wallet = new EconomyWallet();
            var world = new World { Fail = true };
            var svc = new MobileWispService(wallet, new Roller(), world);
            Assert.Throws<InvalidOperationException>(() => svc.Grant(WispResourceIds.Rare));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Rare));
            world.Fail = false;
            Assert.AreEqual(1, svc.FlushAutomatic());
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Rare));
        }
        [Test]
        public void UnverifiedRollNeverConsumes()
        {
            var wallet = new EconomyWallet();
            var svc = new MobileWispService(wallet, new Roller { Verified = false }, new World());
            svc.Grant(WispResourceIds.Special);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Special));
        }
        [Test]
        public void ChoiceMergeRequiresExplicitSelection()
        {
            var wallet = new EconomyWallet();
            var svc = new MobileWispService(wallet, new Roller(), new World());
            svc.Grant(WispResourceIds.Choice);
            Assert.IsFalse(svc.MergeChoice("TEST_ONLY", _ => false));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
            Assert.IsTrue(svc.MergeChoice("TEST_ONLY", _ => true));
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Choice));
        }
    }
}
