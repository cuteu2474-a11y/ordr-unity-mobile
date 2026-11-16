using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Core;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileWispActionJournalTests
    {
        [Test]
        public void UnusedWispIndicatorsUseWalletVisibilityAndStayInsideMainBattle()
        {
            Assert.IsFalse(MobileUnspentWispOverlay.ShouldShow(0));
            Assert.IsFalse(MobileUnspentWispOverlay.ShouldShow(-1));
            Assert.IsTrue(MobileUnspentWispOverlay.ShouldShow(1));
            Assert.IsTrue(MobileUnspentWispOverlay.ShouldShow(5));
            Assert.IsTrue(MobileUnspentWispOverlay.ShouldShowWispResource(
                WispResourceIds.Random, 5));
            Assert.IsFalse(MobileUnspentWispOverlay.ShouldShowWispResource(
                WispResourceIds.Random, 0));
            Assert.IsFalse(MobileUnspentWispOverlay.ShouldShowWispResource(
                WispResourceIds.Choice, 5),
                "Choice Wisps must remain in the Fusion panel and never hover.");
            Assert.IsFalse(MobileUnspentWispOverlay.ShouldShowWispResource(
                WispResourceIds.Uncommon, 5));
            Rect[] fields = {
                new Rect(160, 60, 350, 160),
                new Rect(160, 60, 180, 120),
                new Rect(160, 60, 140, 100)
            };
            foreach (var field in fields)
            {
                for (int count = 1; count <= 2; count++)
                    for (int i = 0; i < count; i++)
                    {
                        Rect start = MobileUnspentWispOverlay.MarkerRect(
                            field, 1f, i, count, 0);
                        Rect bob = MobileUnspentWispOverlay.MarkerRect(
                            field, 1f, i, count, 1.2f);
                        Assert.IsTrue(field.Contains(start.min), "marker top-left");
                        Assert.IsTrue(field.Contains(start.max), "marker bottom-right");
                        Assert.IsTrue(field.Contains(bob.min));
                        Assert.IsTrue(field.Contains(bob.max));
                        Assert.Greater(start.width, 0);
                        Assert.Greater(start.height, 0);
                    }
            }
            // GUI coordinates and hit-test coordinates must use the same
            // scale and hover phase, including when the drawer is open.
            Rect physical = new Rect(240, 80, 420, 170);
            Rect marker = MobileUnspentWispOverlay.MarkerRect(
                physical, 1.5f, 0, 2, 2.8f);
            Assert.IsTrue(marker.Contains(marker.center));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MobileUnspentWispOverlay.MarkerRect(physical, 1, 2, 2, 0));
        }

        [Test]
        public void DirectUseOnlyCallsOriginalTransactionOnce()
        {
            int calls = 0;
            var journal = new MobileWispActionJournal();
            var result = journal.ExecuteDirect("소환", () =>
            {
                calls++;
                return new ManualActionResult(ManualActionState.Succeeded, "소환 성공");
            });
            Assert.AreEqual(1, calls);
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            StringAssert.Contains("완료", journal.Recent[0]);
        }

        [Test]
        public void FailedAndUnavailableTransactionsDoNotRetry()
        {
            int calls = 0;
            var journal = new MobileWispActionJournal();
            var rejected = journal.ExecuteDirect("위습 추첨", () =>
            {
                calls++;
                return new ManualActionResult(ManualActionState.Rejected, "위습 부족");
            });
            var missing = journal.ExecuteDirect("Merge", () =>
            {
                calls++;
                return ManualActionResult.Unavailable("선택위습");
            });
            Assert.AreEqual(2, calls);
            Assert.AreEqual(ManualActionState.Rejected, rejected.State);
            Assert.AreEqual(ManualActionState.NotImplemented, missing.State);
            StringAssert.Contains("중단", journal.Recent[1]);
        }

        [Test]
        public void JournalKeepsOnlyLatestActualReceipts()
        {
            var journal = new MobileWispActionJournal(3);
            for (int i = 0; i < 6; i++)
            {
                int value = i;
                journal.ExecuteDirect("액션 " + i, () =>
                    new ManualActionResult(ManualActionState.Succeeded, "결과 " + value));
            }
            Assert.AreEqual(3, journal.Recent.Count);
            StringAssert.Contains("액션 5", journal.Recent[0]);
            StringAssert.Contains("액션 3", journal.Recent[2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MobileWispActionJournal(0));
            Assert.Throws<ArgumentNullException>(() =>
                journal.ExecuteDirect("소환", null));
        }

        [Test]
        public void ANullWorldResultNeverBecomesAnInventedSuccess()
        {
            var journal = new MobileWispActionJournal();
            var result = journal.ExecuteDirect("소환", () => null);
            Assert.AreEqual(ManualActionState.Rejected, result.State);
            StringAssert.Contains("중단", journal.Recent[0]);
        }
    }
}
