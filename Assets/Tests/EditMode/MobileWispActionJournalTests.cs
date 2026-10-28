using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileWispActionJournalTests
    {
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
