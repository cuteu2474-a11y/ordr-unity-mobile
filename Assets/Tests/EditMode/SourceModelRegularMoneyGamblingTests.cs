using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelRegularMoneyGamblingTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public readonly List<string> Ranges = new List<string>();
            public Rolls(params int[] input) { values = new Queue<int>(input); }
            public int NextInclusive(int min, int max)
            { Ranges.Add(min + ".." + max); return values.Dequeue(); }
        }
        private sealed class Shop
        {
            public readonly EconomyWallet Wallet = new EconomyWallet();
            public readonly GameSession Game = new GameSession();
            public readonly Rolls Random;
            public readonly SourceModelRegularMoneyGambling Service;
            public bool Paused;
            public double Time;
            public Shop(params int[] values)
            {
                Random = new Rolls(values);
                Service = new SourceModelRegularMoneyGambling(Wallet, Game, Random,
                    () => Time, () => !Paused);
                Game.Start();
                Wallet.Grant(new ResourceBundle().Add("gold", 100));
            }
            public ManualActionResult Buy() => Service.Confirm(ManualEconomyCategory.Gambling,
                SourceModelRegularMoneyGambling.OfferId);
        }

        [TestCase(25, 3, 93, "3..5")]
        [TestCase(1, 5, 95, "3..5")]
        [TestCase(26, 10, 100, "10..50")]
        [TestCase(100, 50, 140, "10..50")]
        public void ActualWalletDebitAndPayoutUseExactlyTwoOriginalRanges(int branch, int reward,
            int finalGold, string secondRange)
        {
            var shop = new Shop(branch, reward);
            Assert.AreEqual(ManualActionState.Succeeded, shop.Buy().State);
            Assert.AreEqual(finalGold, shop.Wallet.Balance("gold"));
            Assert.AreEqual(2, shop.Service.Stock);
            Assert.AreEqual(1, shop.Service.PurchaseCount);
            Assert.AreEqual(reward, shop.Service.GoldReturned);
            CollectionAssert.AreEqual(new[] { "1..100", secondRange }, shop.Random.Ranges);
        }

        [Test]
        public void ListingIsFreeAndInsufficientFundsNeverRoll()
        {
            var shop = new Shop();
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(1, shop.Service.AvailableOffers(ManualEconomyCategory.Gambling).Count);
            Assert.AreEqual(0, shop.Service.AvailableOffers(ManualEconomyCategory.Shop).Count);
            shop.Wallet.TryExchange(new ResourceBundle().Add("gold", 100), new ResourceBundle());
            Assert.AreEqual(ManualActionState.Rejected, shop.Buy().State);
            Assert.AreEqual(0, shop.Random.Ranges.Count);
            Assert.AreEqual(3, shop.Service.Stock);
        }

        [Test]
        public void EmptyShelfRefillsOneAtATimeAndNeverBanksTimeWhileFull()
        {
            var shop = new Shop(25, 3, 25, 3, 25, 3, 25, 3);
            for (int i = 0; i < 3; i++) Assert.AreEqual(ManualActionState.Succeeded, shop.Buy().State);
            Assert.AreEqual(ManualActionState.Rejected, shop.Buy().State);
            Assert.AreEqual(6, shop.Random.Ranges.Count);
            shop.Time = 10.999;
            Assert.AreEqual(0, shop.Service.Stock);
            shop.Time = 11;
            Assert.AreEqual(1, shop.Service.Stock);
            Assert.AreEqual(11, shop.Service.SecondsUntilRestock);
            shop.Time = 22;
            Assert.AreEqual(2, shop.Service.Stock);
            shop.Time = 1000;
            Assert.AreEqual(3, shop.Service.Stock);
            Assert.AreEqual(ManualActionState.Succeeded, shop.Buy().State);
            Assert.AreEqual(11, shop.Service.SecondsUntilRestock);
        }

        [Test]
        public void PausedEndedAndWrongCategoryActionsNeverSpendOrDraw()
        {
            var shop = new Shop();
            shop.Paused = true;
            Assert.AreEqual(ManualActionState.Rejected, shop.Buy().State);
            shop.Paused = false;
            Assert.AreEqual(ManualActionState.Rejected, shop.Service.Confirm(
                ManualEconomyCategory.Shop, SourceModelRegularMoneyGambling.OfferId).State);
            Assert.AreEqual(ManualActionState.Rejected, shop.Service.Confirm(
                ManualEconomyCategory.Gambling, "H0AZ").State);
            shop.Game.Finish(false);
            Assert.AreEqual(ManualActionState.Rejected, shop.Buy().State);
            Assert.AreEqual(100, shop.Wallet.Balance("gold"));
            Assert.AreEqual(0, shop.Random.Ranges.Count);
        }

        [Test]
        public void FractionalPurchaseTimeDoesNotResetExistingRestockTimer()
        {
            var shop = new Shop(25, 3, 25, 3);
            shop.Time = 0.5;
            shop.Buy();
            shop.Time = 5;
            shop.Buy();
            Assert.AreEqual(6.5, shop.Service.SecondsUntilRestock);
            shop.Time = 11.5;
            Assert.AreEqual(2, shop.Service.Stock);
        }

        [Test]
        public void OverflowRetainsOutcomeAndRetryDoesNotReroll()
        {
            var shop = new Shop(100, 50);
            shop.Wallet.Grant(new ResourceBundle().Add("gold", long.MaxValue - 100));
            Assert.Throws<OverflowException>(() => shop.Buy());
            Assert.AreEqual(long.MaxValue, shop.Wallet.Balance("gold"));
            Assert.AreEqual(3, shop.Service.Stock);
            Assert.AreEqual(0, shop.Service.PurchaseCount);
            shop.Wallet.TryExchange(new ResourceBundle().Add("gold", 100), new ResourceBundle());
            Assert.AreEqual(ManualActionState.Succeeded, shop.Buy().State);
            Assert.AreEqual(long.MaxValue - 60, shop.Wallet.Balance("gold"));
            Assert.AreEqual(2, shop.Random.Ranges.Count);
        }

        [Test]
        public void PostCommitObserverFailureCannotDoublePayOrLoseStockAndReentryIsRejected()
        {
            var shop = new Shop(100, 50);
            Exception reported = null;
            shop.Service.PostCommitObserverFailed += error => reported = error;
            shop.Wallet.Granted += _ => {
                Assert.AreEqual(ManualActionState.Rejected, shop.Buy().State);
                throw new InvalidOperationException("TEST_ONLY display failure");
            };
            Assert.AreEqual(ManualActionState.Succeeded, shop.Buy().State);
            Assert.IsNotNull(reported);
            Assert.AreEqual(140, shop.Wallet.Balance("gold"));
            Assert.AreEqual(2, shop.Service.Stock);
            Assert.AreEqual(1, shop.Service.PurchaseCount);
            Assert.AreEqual(1, shop.Wallet.CaptureState().CompletedRewards.Count);
            Assert.AreEqual(2, shop.Random.Ranges.Count);
        }

        [Test]
        public void ExchangeReceiptRestoresWithWalletAndRejectsRepeatDebit()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 10));
            Assert.IsTrue(wallet.TryExchangeOnce("TEST_ONLY-purchase", new ResourceBundle().Add("gold", 10),
                new ResourceBundle().Add("gold", 3)));
            var restored = new EconomyWallet();
            restored.RestoreState(wallet.CaptureState());
            Assert.IsFalse(restored.TryExchangeOnce("TEST_ONLY-purchase", new ResourceBundle(),
                new ResourceBundle().Add("gold", 50)));
            Assert.AreEqual(3, restored.Balance("gold"));
            Assert.IsTrue(restored.HasCompletedTransaction("TEST_ONLY-purchase"));
        }

        [Test]
        public void BackwardOrInvalidClockIsRejectedBeforeGambling()
        {
            var shop = new Shop();
            shop.Time = 3;
            Assert.AreEqual(3, shop.Service.Stock);
            foreach (double invalid in new[] { 2, double.NaN, double.PositiveInfinity })
            {
                shop.Time = invalid;
                Assert.Throws<InvalidOperationException>(() => shop.Buy());
            }
            Assert.AreEqual(0, shop.Random.Ranges.Count);
        }

        [Test]
        public void DefaultPreparationRoutesManualConfirmationToSameWalletWithoutFakeMissionProgress()
        {
            var root = new GameObject("ORDR money shop integration");
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                controller.ConfigureRandomWispResourceRng(new Rolls(100, 50));
                controller.BeginUnitPreparation();
                var offered = controller.ManualActions.ListEconomyOffers(ManualEconomyCategory.Gambling);
                Assert.IsTrue(offered.Any(o => o.Id == SourceModelRegularMoneyGambling.OfferId));
                Assert.AreEqual(60, controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(ManualActionState.Succeeded, controller.ManualActions.RequestEconomyAction(
                    ManualEconomyCategory.Gambling, SourceModelRegularMoneyGambling.OfferId).State);
                Assert.AreEqual(100, controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(0, controller.Combat.CharacterCount);
                Assert.AreEqual(0, controller.PersonalMissions.Missions.EventCount(OriginalMissionSignal.LowGambleAttempted));
                controller.SetManualGameplayPause(true);
                Assert.AreEqual(ManualActionState.Rejected, controller.ManualActions.RequestEconomyAction(
                    ManualEconomyCategory.Gambling, SourceModelRegularMoneyGambling.OfferId).State);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
