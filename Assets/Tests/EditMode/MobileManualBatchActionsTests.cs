using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileManualBatchActionsTests
    {
        private static ManualActionResult Success => new ManualActionResult(
            ManualActionState.Succeeded, "verified single commit");
        private static ManualActionResult Rejected => new ManualActionResult(
            ManualActionState.Rejected, "single transaction was rejected");

        [Test]
        public void DuplicateStaleAndTooLargeSelectionNeverConsumeAnything()
        {
            int calls = 0;
            Func<long, ManualActionResult> sell = id => { calls++; return Success; };
            Assert.AreEqual(ManualActionState.Rejected,
                MobileManualBatchActions.SellSelected(new long[] { 1, 1 },
                    _ => true, sell).State);
            Assert.AreEqual(ManualActionState.Rejected,
                MobileManualBatchActions.SellSelected(new long[] { 1, 2 },
                    id => id != 2, sell).State);
            Assert.AreEqual(ManualActionState.Rejected,
                MobileManualBatchActions.SellSelected(new long[0],
                    _ => true, sell).State);
            Assert.AreEqual(ManualActionState.Rejected,
                MobileManualBatchActions.SellSelected(new long[11],
                    _ => true, sell).State);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void SuccessfulSalesUseOnlyTheOriginalPerUnitCallbackInOrder()
        {
            var used = new List<long>();
            var result = MobileManualBatchActions.SellSelected(
                new long[] { 21, 24, 29 },
                id => id != 0,
                id => { used.Add(id); return Success; });
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            CollectionAssert.AreEqual(new long[] { 21, 24, 29 }, used);
            StringAssert.Contains("3/3", result.Message);
        }

        [Test]
        public void FailureStopsWithoutRetryOrUndoingPreviousCommits()
        {
            var attempted = new List<long>();
            var result = MobileManualBatchActions.SellSelected(
                new long[] { 1, 2, 3, 4 }, _ => true,
                id => { attempted.Add(id); return id == 3 ? Rejected : Success; });
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            StringAssert.Contains("2/4", result.Message);
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, attempted);
        }

        [Test]
        public void LiveEligibilityIsRecheckedBeforeEachIrreversibleCommit()
        {
            bool stillAvailable = true;
            var attempted = new List<long>();
            var result = MobileManualBatchActions.SellSelected(
                new long[] { 5, 6, 7 }, id => id != 6 || stillAvailable,
                id => { attempted.Add(id); stillAvailable = false; return Success; });
            CollectionAssert.AreEqual(new long[] { 5 }, attempted);
            StringAssert.Contains("1/3", result.Message);
        }

        [Test]
        public void ResourceBatchUsesOneConfirmedDebitPerIterationAndStopsOnShortage()
        {
            int calls = 0;
            var result = MobileManualBatchActions.RepeatExplicitResourceUse(5,
                () => ++calls <= 2 ? Success : Rejected);
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            Assert.AreEqual(3, calls);
            StringAssert.Contains("2/5", result.Message);
            Assert.AreEqual(ManualActionState.Rejected,
                MobileManualBatchActions.RepeatExplicitResourceUse(11,
                    () => { calls++; return Success; }).State);
            Assert.AreEqual(3, calls);
        }
    }
}
