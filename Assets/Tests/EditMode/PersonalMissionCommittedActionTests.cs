using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public class PersonalMissionCommittedActionTests
    {
        private sealed class Sale : IOriginalCommonSalePort
        {
            public ManualActionState Result;
            public ManualActionResult ConfirmSale(long instance)
            { return new ManualActionResult(Result, "TEST_ONLY sale result"); }
        }

        [Test]
        public void OnlyConfirmedSaleRaisesMissionEvent()
        {
            var manual = new MobileManualInteraction();
            var sale = new Sale { Result = ManualActionState.Rejected };
            manual.ConfigureOriginalCommonSale(sale);
            int events = 0;
            manual.CommonSaleSucceeded += _ => events++;
            Assert.AreEqual(ManualActionState.Rejected, manual.RequestCommonSale(1).State);
            Assert.AreEqual(0, events);
            sale.Result = ManualActionState.Succeeded;
            Assert.AreEqual(ManualActionState.Succeeded, manual.RequestCommonSale(1).State);
            Assert.AreEqual(1, events);
            manual.RequestCommonSale(0);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void ObserverOrDiagnosticFailureNeverTurnsCommittedSaleIntoARejection()
        {
            var manual = new MobileManualInteraction();
            manual.ConfigureOriginalCommonSale(new Sale { Result = ManualActionState.Succeeded });
            int laterObserver = 0;
            manual.CommonSaleSucceeded += _ => { throw new InvalidOperationException("TEST_ONLY observer"); };
            manual.CommonSaleSucceeded += _ => laterObserver++;
            manual.PostCommitObserverFailed += _ => { throw new InvalidOperationException("TEST_ONLY diagnostics"); };
            Assert.AreEqual(ManualActionState.Succeeded, manual.RequestCommonSale(1).State);
            Assert.AreEqual(1, laterObserver);
        }

        [Test]
        public void SaleEventReplaysCannotIncreaseMissionCountTwice()
        {
            var game = new GameSession(); game.Start();
            var missions = PersonalMissionGameplay.Start(game, new EconomyWallet(),
                new MobileMissionRandom(1), "TEST_ONLY-sales");
            var manual = new MobileManualInteraction();
            manual.ConfigureOriginalCommonSale(new Sale { Result = ManualActionState.Succeeded });
            manual.CommonSaleSucceeded += id => missions.RecordCommonSale(id);
            manual.RequestCommonSale(7);
            manual.RequestCommonSale(7);
            manual.RequestCommonSale(8);
            Assert.AreEqual(2, missions.Missions.EventCount(OriginalMissionSignal.CommonUnitSold));
        }
    }
}
