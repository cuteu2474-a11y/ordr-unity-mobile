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
            Rolls draws, FakeWorld world, Func<bool> gate = null)
        {
            return new SourceModelMaterialSupport(wallet, game, draws, world,
                gate ?? (() => true));
        }

        [Test]
        public void PaymentRefundPhysicalRosterAndTwoUseLimitAreAtomic()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 250));
            var game = new GameSession(); game.Start();
            // Original CJU: ship on BFi(1,1000)<=10, otherwise common
            // on BFi(1,100)<=50, then jY[1..9] unit selection.
            var draws = new Rolls(10, 11, 50, 9);
            var world = new FakeWorld();
            var service = Build(wallet, game, draws, world);
            Assert.AreEqual(2, service.RemainingPurchases);
            Assert.AreEqual(1, service.AvailableOffers(ManualEconomyCategory.Gambling).Count);
            var first = service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            Assert.AreEqual(ManualActionState.Succeeded, first.State);
            Assert.AreEqual(180, wallet.Balance("gold"));
            Assert.AreEqual(1, wallet.Balance(SourceModelMaterialSupport.PurchaseCountResource));
            Assert.AreEqual("h060", world.Rawcodes[0]);
            var second = service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            Assert.AreEqual(ManualActionState.Succeeded, second.State);
            Assert.AreEqual(110, wallet.Balance("gold"));
            Assert.AreEqual(Original323UnitGamblingRules.Common[8], world.Rawcodes[1]);
            Assert.AreEqual(2, world.Commits);
            Assert.AreEqual(0, service.RemainingPurchases);
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(110, wallet.Balance("gold"));
            Assert.AreEqual(4, draws.Calls);
        }

        [Test]
        public void RejectedAndFailedPurchasesDoNotSpendOrReroll()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 90));
            var game = new GameSession(); game.Start();
            bool paused = true;
            var draws = new Rolls(1000, 51, 12);
            var world = new FakeWorld { ThrowInStage = true };
            var service = Build(wallet, game, draws, world, () => !paused);
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            paused = false;
            Assert.AreEqual(ManualActionState.Rejected,
                service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(0, draws.Calls);
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
            Assert.AreEqual(Original323UnitGamblingRules.Uncommon[12], world.Rawcodes[0]);
            Assert.AreEqual(3, draws.Calls);
        }


        [Test]
        public void EveryOriginalCjuLotteryPathConsumesItsActualBfiCalls()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 1000));
            var game = new GameSession(); game.Start();
            var world = new FakeWorld();
            var draws = new Rolls(1, 11, 50, 1);
            var support = Build(wallet, game, draws, world);
            Assert.AreEqual(ManualActionState.Succeeded,
                support.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual("h060", world.Rawcodes[0]);
            Assert.AreEqual(1, draws.Calls);
            Assert.AreEqual(ManualActionState.Succeeded,
                support.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(Original323UnitGamblingRules.Common[0], world.Rawcodes[1]);
            Assert.AreEqual(4, draws.Calls);
        }

        [Test]
        public void PurchaseCountSurvivesWalletRestoreAndPreventsRepeat()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 500));
            var game = new GameSession(); game.Start();
            var world = new FakeWorld();
            var service = Build(wallet, game, new Rolls(1000, 1, 1), world);
            service.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId);
            var restored = new EconomyWallet();
            restored.RestoreState(wallet.CaptureState());
            var next = Build(restored, game, new Rolls(1000, 100, 0), world);
            Assert.AreEqual(1, next.RemainingPurchases);
            Assert.AreEqual(ManualActionState.Succeeded,
                next.Confirm(ManualEconomyCategory.Gambling, SourceModelMaterialSupport.OfferId).State);
            Assert.AreEqual(0, next.RemainingPurchases);
        }
    }
}
