using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileRollbackWispRandomTests
    {
        [Test]
        public void AbortedSaleRollReplaysItsResultWithoutChangingLaterDraws()
        {
            var actual = new MobileRollbackWispRandom(17842);
            var reference = new MobileRollbackWispRandom(17842);
            Assert.AreEqual(reference.NextInclusive(1, 10000),
                actual.NextInclusive(1, 10000));

            int beforeFailedSale = actual.CaptureCheckpoint();
            int failedSaleRoll = actual.NextInclusive(1, 100);
            actual.RestoreCheckpoint(beforeFailedSale);
            Assert.AreEqual(failedSaleRoll, actual.NextInclusive(1, 100));
            Assert.AreEqual(reference.NextInclusive(1, 100),
                failedSaleRoll);
            Assert.AreEqual(reference.NextInclusive(0, 8),
                actual.NextInclusive(0, 8));
        }

        [Test]
        public void RewindAcrossMultipleRangesKeepsChronologicalStream()
        {
            var actual = new MobileRollbackWispRandom(7);
            int checkpoint = actual.CaptureCheckpoint();
            int a = actual.NextInclusive(1, 10000);
            int b = actual.NextInclusive(0, 8);
            int c = actual.NextInclusive(1, 100);
            actual.RestoreCheckpoint(checkpoint);
            Assert.AreEqual(a, actual.NextInclusive(1, 10000));
            Assert.AreEqual(b, actual.NextInclusive(0, 8));
            Assert.AreEqual(c, actual.NextInclusive(1, 100));
        }

        [Test]
        public void InvalidCheckpointOrRangeDoesNotAdvanceTheStream()
        {
            var actual = new MobileRollbackWispRandom(123);
            var reference = new MobileRollbackWispRandom(123);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                actual.RestoreCheckpoint(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                actual.RestoreCheckpoint(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                actual.NextInclusive(3, 2));
            Assert.AreEqual(reference.NextInclusive(1, 100),
                actual.NextInclusive(1, 100));
        }
    }
}
