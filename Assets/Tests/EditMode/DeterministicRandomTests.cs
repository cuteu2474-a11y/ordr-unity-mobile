using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var first = new DeterministicRandom(123);
            var second = new DeterministicRandom(123);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(first.NextUInt(), second.NextUInt());
        }
    }
}
