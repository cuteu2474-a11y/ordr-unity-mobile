using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class EconomyTransactionsTests
    {
        // 아래 재화량과 유닛 ID는 거래 원자성 검증을 위한 가상 값이며 원작 수치가 아니다.
        [Test]
        public void FusionConsumesAllIngredientsAndGrantsOneResult()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("test-unit-a", 2).Add("test-unit-b", 1).Add("test-wood", 3));
            var recipe = new CatalogTransaction("test-fusion",
                new ResourceBundle().Add("test-unit-a", 2).Add("test-unit-b", 1).Add("test-wood", 3),
                new ResourceBundle().Add("test-result", 1), "https://ordr.kr/");
            Assert.IsTrue(recipe.Execute(wallet));
            Assert.AreEqual(0, wallet.Balance("test-unit-a"));
            Assert.AreEqual(0, wallet.Balance("test-wood"));
            Assert.AreEqual(1, wallet.Balance("test-result"));
        }

        [Test]
        public void FailedPurchaseDoesNotConsumePartialMaterials()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("test-gold", 10));
            var purchase = new CatalogTransaction("test-shop",
                new ResourceBundle().Add("test-gold", 5).Add("test-wood", 1),
                new ResourceBundle().Add("test-wisp", 1), "https://ordr.kr/");
            Assert.IsFalse(purchase.Execute(wallet));
            Assert.AreEqual(10, wallet.Balance("test-gold"));
            Assert.AreEqual(0, wallet.Balance("test-wisp"));
        }

        [Test]
        public void OverflowCannotPartiallyConsumeResources()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("test-gold", 10).Add("test-result", long.MaxValue));
            Assert.Throws<OverflowException>(() => wallet.TryExchange(
                new ResourceBundle().Add("test-gold", 5),
                new ResourceBundle().Add("test-result", 1)));
            Assert.AreEqual(10, wallet.Balance("test-gold"));
        }

        [Test]
        public void OneTimeRewardIsAtomicAndRestoredWithWallet()
        {
            var wallet = new EconomyWallet();
            var reward = new ResourceBundle().Add("wood", 16);
            Assert.IsTrue(wallet.TryGrantOnce("otama-dango:1", reward));
            Assert.IsFalse(wallet.TryGrantOnce("otama-dango:1", reward));
            var restored = new EconomyWallet();
            restored.RestoreState(wallet.CaptureState());
            Assert.AreEqual(16, restored.Balance("wood"));
            Assert.IsFalse(restored.TryGrantOnce("otama-dango:1", reward));
            Assert.IsTrue(restored.TryGrantOnce("otama-dango:2", reward));
            Assert.AreEqual(32, restored.Balance("wood"));
        }

        [Test]
        public void FailedRewardAndInvalidSaveCannotConsumeUse()
        {
            var wallet = new EconomyWallet();
            var reward = new ResourceBundle().Add("wood", 16);
            wallet.Grant(new ResourceBundle().Add("wood", long.MaxValue));
            Assert.Throws<OverflowException>(() => wallet.TryGrantOnce("once", reward));
            Assert.Throws<FormatException>(() => wallet.RestoreState(new EconomyWalletState {
                Balances = new System.Collections.Generic.List<EconomyBalanceState> {
                    new EconomyBalanceState { Id = "wood", Amount = -1 }
                }
            }));
            Assert.AreEqual(long.MaxValue, wallet.Balance("wood"));
            wallet.RestoreState(new EconomyWalletState());
            Assert.IsTrue(wallet.TryGrantOnce("once", reward));
            Assert.AreEqual(16, wallet.Balance("wood"));
        }

        [Test]
        public void UnverifiedGamblingIsBlocked()
        {
            var gambling = new GamblingTable("test-gambling");
            Assert.IsFalse(gambling.IsVerified);
            Assert.Throws<InvalidOperationException>(() => gambling.Play());
        }
    }
}
