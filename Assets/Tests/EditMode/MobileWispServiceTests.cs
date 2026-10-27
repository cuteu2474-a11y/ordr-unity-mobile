using System;
using System.Collections.Generic;
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
            public int Calls;
            public WispReward Roll(string id)
            {
                Calls++;
                return new WispReward(id, Calls == 1 ? "h000" : "h001",
                    id == WispResourceIds.Rare ? "A0YE" : null);
            }
        }
        private sealed class World : IWispRewardWorld
        {
            public int Count;
            public WispReward LastReward;
            public bool Fail;
            public bool FailCommit;
            public IWispRewardTransaction Begin() => new Transaction(this);
            private sealed class Transaction : IWispRewardTransaction
            {
                private readonly World world;
                private bool committed;
                private bool applied;
                public Transaction(World parent) { world = parent; }
                public void Apply(WispReward reward)
                {
                    world.LastReward = reward;
                    if (world.Fail) throw new InvalidOperationException();
                    world.Count++;
                    applied = true;
                }
                public void Commit()
                {
                    if (world.FailCommit) throw new InvalidOperationException();
                    committed = true;
                }
                public void Dispose() { if (!committed && applied) world.Count--; }
            }
        }

        private sealed class MergeWorld : IVerifiedChoiceWispMergeWorld
        {
            public int Created;
            public bool Allow = true;
            public bool FailCommit;
            public IReadOnlyList<VerifiedChoiceWispTarget> AvailableTargets() =>
                new[] { new VerifiedChoiceWispTarget("h00M", "TEST_ONLY", "TEST_ONLY") };
            public IChoiceWispMergeTransaction Begin(string rawcode) => new Transaction(this);
            private sealed class Transaction : IChoiceWispMergeTransaction
            {
                private readonly MergeWorld world;
                private bool applied, committed;
                public Transaction(MergeWorld parent) { world = parent; }
                public bool TryApply()
                {
                    if (!world.Allow) return false;
                    world.Created++; applied = true;
                    return true;
                }
                public void Commit()
                {
                    if (world.FailCommit) throw new InvalidOperationException();
                    committed = true;
                }
                public void Dispose() { if (applied && !committed) world.Created--; }
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
        public void OtherSharedWalletRewardPathsAlsoRedeemAutomatically()
        {
            var wallet = new EconomyWallet();
            var world = new World();
            var svc = new MobileWispService(wallet, new Roller(), world);
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Uncommon, 2));
            Assert.AreEqual(2, world.Count);
            Assert.AreEqual(0, svc.Balance(WispResourceIds.Uncommon));
            Assert.IsTrue(wallet.TryGrantOnce("quest-a",
                new ResourceBundle().Add(WispResourceIds.Legendary, 1)));
            Assert.AreEqual(3, world.Count);
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Legendary));
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
        public void FailureDuringWorldCommitRollsBackBothUnitAndBalance()
        {
            var wallet = new EconomyWallet();
            var world = new World { FailCommit = true };
            var svc = new MobileWispService(wallet, new Roller(), world);
            Assert.Throws<InvalidOperationException>(() => svc.Grant(WispResourceIds.Rare));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Rare));
            Assert.AreEqual(0, world.Count);
            world.FailCommit = false;
            Assert.AreEqual(1, svc.FlushAutomatic());
            Assert.AreEqual(1, world.Count);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void FailedAutomaticRewardRetainsTheSameDrawUntilCommit(bool failCommit)
        {
            var wallet = new EconomyWallet();
            var roller = new Roller();
            var world = new World { Fail = !failCommit, FailCommit = failCommit };
            using (var service = new MobileWispService(wallet, roller, world))
            {
                Assert.Throws<InvalidOperationException>(() => service.Grant(WispResourceIds.Special));
                WispReward first = world.LastReward;
                Assert.AreEqual(1, roller.Calls);
                Assert.AreEqual(0, world.Count);
                Assert.AreEqual(1, service.Balance(WispResourceIds.Special));
                Assert.Throws<InvalidOperationException>(() => service.FlushAutomatic());
                Assert.AreSame(first, world.LastReward);
                Assert.AreEqual(1, roller.Calls);
                world.Fail = world.FailCommit = false;
                Assert.AreEqual(1, service.FlushAutomatic());
                Assert.AreSame(first, world.LastReward);
                Assert.AreEqual(1, roller.Calls);
                service.Grant(WispResourceIds.Special);
                Assert.AreEqual(2, roller.Calls);
                Assert.AreNotSame(first, world.LastReward);
                Assert.AreEqual(2, world.Count);
                Assert.AreEqual(0, service.Balance(WispResourceIds.Special));
            }
        }

        [Test]
        public void PendingBalancesCanResumeAfterSaveRestoreWithoutAQueue()
        {
            var wallet = new EconomyWallet();
            new MobileWispService(wallet, new Roller { Verified = false }, new World())
                .Grant(WispResourceIds.Special, 2);
            var saved = wallet.CaptureState();
            var restored = new EconomyWallet();
            restored.RestoreState(saved);
            var world = new World();
            Assert.AreEqual(2, new MobileWispService(restored, new Roller(), world)
                .FlushAutomatic());
            Assert.AreEqual(0, restored.Balance(WispResourceIds.Special));
            Assert.AreEqual(2, world.Count);
        }
        [Test]
        public void RewardSourceIdPreventsDuplicatePayment()
        {
            var wallet = new EconomyWallet();
            var world = new World();
            var svc = new MobileWispService(wallet, new Roller(), world);
            Assert.IsTrue(svc.GrantOnce("mission-17", WispResourceIds.Uncommon));
            Assert.IsFalse(svc.GrantOnce("mission-17", WispResourceIds.Uncommon));
            Assert.AreEqual(1, world.Count);
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Uncommon));
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
            var merge = new MergeWorld { Allow = false };
            Assert.IsFalse(svc.MergeChoice("h00M", merge));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
            merge.Allow = true;
            Assert.IsFalse(svc.MergeChoice("hXYZ", merge));
            Assert.IsTrue(svc.MergeChoice("h00M", merge));
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Choice));
            Assert.AreEqual(1, merge.Created);
            Assert.IsFalse(svc.MergeChoice("h00M", merge));
            Assert.AreEqual(1, merge.Created);
        }
        [Test]
        public void FailedMergeCommitCannotGrantAFreeUnit()
        {
            var wallet = new EconomyWallet();
            var svc = new MobileWispService(wallet, new Roller(), new World());
            var merge = new MergeWorld { FailCommit = true };
            svc.Grant(WispResourceIds.Choice);
            Assert.Throws<InvalidOperationException>(() => svc.MergeChoice("h00M", merge));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
            Assert.AreEqual(0, merge.Created);
        }
    }
}
