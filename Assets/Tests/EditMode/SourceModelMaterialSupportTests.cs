using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelMaterialSupportTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public int Calls;
            public Rolls(params int[] numbers) { values = new Queue<int>(numbers); }
            public int NextInclusive(int minimum, int maximum)
            {
                Calls++;
                int value = values.Dequeue();
                Assert.That(value, Is.InRange(minimum, maximum));
                return value;
            }
        }

        private sealed class FakeWorld : IOriginal323UnitGamblingWorld
        {
            public bool ThrowInStage;
            public int Commits;
            public int RollsBack;
            public readonly List<string> Rawcodes = new List<string>();
            public IOriginal323UnitGamblingTransaction Begin(
                IReadOnlyList<string> codes, string ability)
            {
                Assert.IsNull(ability);
                Assert.AreEqual(1, codes.Count);
                return new FakePurchase(this, codes[0]);
            }
            private sealed class FakePurchase : IOriginal323UnitGamblingTransaction
            {
                private readonly FakeWorld parent;
                private readonly string code;
                private bool committed;
                public FakePurchase(FakeWorld owner, string rawcode)
                { parent = owner; code = rawcode; }
                public string Stage()
                {
                    parent.Rawcodes.Add(code);
                    if (parent.ThrowInStage) throw new InvalidOperationException("spawn failed");
                    return "테스트 유닛";
                }
                public void Commit() { parent.Commits++; committed = true; }
                public void Dispose() { if (!committed) parent.RollsBack++; }
            }
        }

        private static SourceModelMaterialSupport Build(EconomyWallet wallet, GameSession game,
            Rolls grades, Rolls units, FakeWorld world, Func<bool> gate = null)
        {
            return new SourceModelMaterialSupport(wallet, game, grades, units, world,
                gate ?? (() => true));
        }

        [Test]
        public void PaymentRefundPhysicalRosterAndTwoUseLimitAreAtomic()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 250));
            var game = new GameSession(); game.Start();
            var grades = new Rolls(1, 2);
            var units = new Rolls(0, 12);
            var world = new FakeWorld();
            var service = Build(wallet, game, grades, units, world);
            Assert.AreEqual(2, service.RemainingPurchases);
            Assert.AreEqual(1, service.AvailableOffers(ManualEconomyCategory.Gambling).Count);
            var first = service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            Assert.AreEqual(ManualActionState.Succeeded, first.State);
            Assert.AreEqual(180, wallet.Balance("gold"));
            Assert.AreEqual(1, wallet.Balance(SourceModelMaterialSupport.PurchaseCountResource));
            Assert.AreEqual(Original323UnitGamblingRules.Common[0], world.Rawcodes[0]);
            var second = service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            Assert.AreEqual(ManualActionState.Succeeded, second.State);
            Assert.AreEqual(110, wallet.Balance("gold"));
            Assert.AreEqual(Original323UnitGamblingRules.Uncommon[12], world.Rawcodes[1]);
            Assert.AreEqual(2, world.Commits);
            Assert.AreEqual(0, service.RemainingPurchases);
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(110, wallet.Balance("gold"));
            Assert.AreEqual(2, grades.Calls);
            Assert.AreEqual(2, units.Calls);
        }

        [Test]
        public void RejectedAndFailedPurchasesDoNotSpendOrReroll()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 90));
            var game = new GameSession(); game.Start();
            bool paused = true;
            var grades = new Rolls(2);
            var units = new Rolls(3);
            var world = new FakeWorld { ThrowInStage = true };
            var service = Build(wallet, game, grades, units, world, () => !paused);
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            paused = false;
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(0, grades.Calls);
            wallet.Grant(new ResourceBundle().Add("gold", 20));
            Assert.Throws<InvalidOperationException>(() =>
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId));
            Assert.AreEqual(110, wallet.Balance("gold"));
            Assert.AreEqual(0, service.PurchasedCount);
            Assert.AreEqual(1, world.RollsBack);
            world.ThrowInStage = false;
            Assert.AreEqual(ManualActionState.Succeeded,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(40, wallet.Balance("gold"));
            Assert.AreEqual(world.Rawcodes[0], world.Rawcodes[1]);
            Assert.AreEqual(1, grades.Calls);
            Assert.AreEqual(1, units.Calls);
        }

        [Test]
        public void PurchaseCountSurvivesWalletRestoreAndPreventsRepeat()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 500));
            var game = new GameSession(); game.Start();
            var world = new FakeWorld();
            var service = Build(wallet, game, new Rolls(1, 1), new Rolls(1, 2), world);
            service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            var restored = new EconomyWallet();
            restored.RestoreState(wallet.CaptureState());
            var next = Build(restored, game, new Rolls(2), new Rolls(0), world);
            Assert.AreEqual(1, next.RemainingPurchases);
            Assert.AreEqual(ManualActionState.Succeeded,
                next.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(0, next.RemainingPurchases);
        }
    }
}
