using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrResumeEconomyStateTests
    {
        private static EconomyWallet Wallet(long gold)
        {
            var result = new EconomyWallet();
            result.Grant(new ResourceBundle().Add("gold", gold));
            return result;
        }

        [Test]
        public void PersistedBfiStreamReplaysAllDrawsAndNextResult()
        {
            var original = new MobileRollbackWispRandom(123456);
            original.NextInclusive(1, 1000);
            original.NextInclusive(1, 100);
            original.NextInclusive(0, 12);
            var state = original.CaptureState();
            var recovered = MobileRollbackWispRandom.Restore(state);
            Assert.AreEqual(original.NextInclusive(1, 10000),
                recovered.NextInclusive(1, 10000));
            Assert.AreEqual(original.NextInclusive(10, 100),
                recovered.NextInclusive(10, 100));
            state.Draws[0].Result = state.Draws[0].Minimum - 1;
            Assert.Throws<FormatException>(() => MobileRollbackWispRandom.Restore(state));
        }

        [Test]
        public void MoneyDeskStockAndTimersDoNotResetAfterRestore()
        {
            var wallet = Wallet(300);
            var game = new GameSession(); game.Start();
            var random = new MobileRollbackWispRandom(491);
            double time = 0;
            var desk = new SourceModelMoneyGamblingDesk(wallet, game, random,
                () => time, () => true);
            var bought = desk.Confirm(ManualEconomyCategory.Gambling,
                SourceModelRegularMoneyGambling.OfferId);
            Assert.AreEqual(ManualActionState.Succeeded, bought.State);
            time = 2;
            var checkpoint = desk.CaptureState();
            var restoredWallet = new EconomyWallet();
            restoredWallet.RestoreState(wallet.CaptureState());
            var restarted = new SourceModelMoneyGamblingDesk(restoredWallet, game,
                MobileRollbackWispRandom.Restore(random.CaptureState()),
                () => time, () => true);
            restarted.RestoreState(checkpoint);
            Assert.AreEqual(desk.Regular.Stock, restarted.Regular.Stock);
            Assert.AreEqual(desk.Regular.SecondsUntilRestock,
                restarted.Regular.SecondsUntilRestock, 1e-8);
            Assert.AreEqual(checkpoint.Regular.ReceiptPrefix,
                restarted.Regular.CaptureState().ReceiptPrefix);
            time = 11;
            Assert.AreEqual(desk.Regular.Stock, restarted.Regular.Stock);
            checkpoint.Regular.Stock = 500;
            Assert.Throws<FormatException>(() =>
                SourceModelMoneyGamblingDesk.ValidateState(checkpoint));
        }

        [Test]
        public void InconsistentMaterialSupportWalletCannotBeRebuilt()
        {
            var wallet = Wallet(200);
            var game = new GameSession(); game.Start();
            var old = new SourceModelMoneyGamblingDesk(wallet, game,
                new MobileRollbackWispRandom(222), () => 1, () => true);
            var state = old.CaptureState();
            state.MaterialSupport = new SourceModelMaterialSupportState { PurchasedCount = 1 };
            var restored = new SourceModelMoneyGamblingDesk(wallet, game,
                new MobileRollbackWispRandom(222), () => 1, () => true);
            // No registered support port and no corresponding purchase receipt.
            Assert.Throws<InvalidOperationException>(() => restored.RestoreState(state));
        }

        [Test]
        public void PendingMaterialSupportRejectsForgedRawcodeAndInvalidGrades()
        {
            var valid = new SourceModelMaterialSupportState {
                PurchasedCount = 1, HasShipRoll = true, ShipRoll = 20,
                HasGradeRoll = true, GradeRoll = 70,
                HasUnitRoll = true, UnitRoll = 12,
                PendingRawcode = Original323UnitGamblingRules.Uncommon[12]
            };
            SourceModelMaterialSupport.ValidateState(valid);
            valid.PendingRawcode = "h060";
            Assert.Throws<FormatException>(() =>
                SourceModelMaterialSupport.ValidateState(valid));
            valid.PendingRawcode = Original323UnitGamblingRules.Uncommon[12];
            valid.ShipRoll = 10;
            Assert.Throws<FormatException>(() =>
                SourceModelMaterialSupport.ValidateState(valid));
        }
    }
}
