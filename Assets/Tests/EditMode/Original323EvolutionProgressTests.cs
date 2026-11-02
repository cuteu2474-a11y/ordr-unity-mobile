using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323EvolutionProgressTests
    {
        [Test]
        public void DistinctVerifiedDeathsCountOnce()
        {
            var p = new Original323EvolutionProgress(4);
            Assert.IsTrue(p.RecordVerifiedLineDefeat(1, "line-1"));
            Assert.IsFalse(p.RecordVerifiedLineDefeat(1, "line-1"));
            Assert.IsTrue(p.RecordVerifiedLineDefeat(2, "line-1"));
            Assert.AreEqual(2, p.LineDefeats);
            Assert.Throws<ArgumentException>(() => p.RecordVerifiedLineDefeat(0, "line-2"));
            Assert.Throws<ArgumentException>(() => p.RecordVerifiedLineDefeat(1, ""));
        }

        [Test]
        public void ReleaseIsRecordedOnceAndStopsCounting()
        {
            var p = new Original323EvolutionProgress(3);
            p.RecordVerifiedLineDefeat(1, "line-1");
            p.RecordSourceConfirmedRelease(99);
            Assert.IsTrue(p.IsReleased);
            Assert.AreEqual(99L, p.ReleasedUnitId.Value);
            Assert.IsFalse(p.RecordVerifiedLineDefeat(1, "line-2"));
            Assert.Throws<InvalidOperationException>(() => p.RecordSourceConfirmedRelease(100));
        }
    }
