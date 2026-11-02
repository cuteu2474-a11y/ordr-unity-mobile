using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323EvolutionProgressTests
    {
        private sealed class FixedRandom : IOriginal323NativeUnitPoolRandom
        {
            public int Calls;
            public bool Fail;
            public readonly int Value;
            public FixedRandom(int value) { Value = value; }
            public int NextInclusive(int minimum, int maximum)
            {
                Calls++;
                if (Fail) { Fail = false; throw new InvalidOperationException("native fail"); }
                if (Value < minimum || Value > maximum) throw new ArgumentOutOfRangeException();
                return Value;
            }
        }

        [Test]
        public void BothOriginalPoolsHavePinnedIdentitiesAndNoAbsalom()
        {
            Assert.AreEqual(13, Original323EvolutionSourceRules.UncommonPool.Count);
            Assert.AreEqual(32, Original323EvolutionSourceRules.SpecialPool.Count);
            Assert.AreEqual("h00M", Original323EvolutionSourceRules.UncommonPool[0].Rawcode);
            Assert.AreEqual("h00F", Original323EvolutionSourceRules.UncommonPool[12].Rawcode);
            Assert.AreEqual("h01K", Original323EvolutionSourceRules.SpecialPool[0].Rawcode);
            Assert.AreEqual("h01I", Original323EvolutionSourceRules.SpecialPool[31].Rawcode);
            foreach (var entry in Original323EvolutionSourceRules.SpecialPool)
                Assert.AreNotEqual("S-17", entry.CatalogId);
            Assert.AreEqual(152, Original323EvolutionSourceRules.RequiredLineKills);
            Assert.AreEqual(2.02, Original323EvolutionSourceRules.UnsealSeconds, 1e-9);
        }

        [Test]
        public void ExperienceRequiresActivationAndUsesUniqueSourceKills()
        {
            var p = new Original323EvolutionProgress(4);
            Assert.IsFalse(p.RecordVerifiedLineDefeat(1, "a", 0));
            p.Activate(8, "S-01");
            Assert.Throws<InvalidOperationException>(() => p.Activate(9, "S-02"));
            Assert.IsTrue(p.RecordVerifiedLineDefeat(1, "a", 1));
            Assert.IsFalse(p.RecordVerifiedLineDefeat(1, "a", 1));
            Assert.IsTrue(p.RecordVerifiedLineDefeat(2, "a", 2));
            Assert.AreEqual(2, p.LineDefeats);
            Assert.Throws<ArgumentException>(() => p.RecordVerifiedLineDefeat(0, "bad", 3));
        }

        [Test]
        public void KillOneHundredFiftyTwoUnlocksAfterExactlyTwoPointZeroTwoSeconds()
        {
            var p = new Original323EvolutionProgress(5);
            p.Activate(9, "S-02");
            for (int i = 1; i <= 151; i++)
                Assert.IsTrue(p.RecordVerifiedLineDefeat(1, "a" + i, i * 0.1));
            Assert.IsFalse(p.IsUnsealing);
            Assert.IsTrue(p.RecordVerifiedLineDefeat(2, "a152", 16));
            Assert.IsTrue(p.IsUnsealing);
            Assert.AreEqual(152, p.LineDefeats);
            Assert.IsFalse(p.RecordVerifiedLineDefeat(2, "extra", 16.1));
            Assert.IsFalse(p.CanRelease(18.01));
            Assert.IsTrue(p.CanRelease(18.02));
            p.RecordSourceConfirmedRelease(100);
            Assert.IsTrue(p.IsReleased);
            Assert.AreEqual(100L, p.ReleasedUnitId.Value);
            Assert.IsFalse(p.CanRelease(100));
            Assert.Throws<InvalidOperationException>(() => p.RecordSourceConfirmedRelease(101));
        }

        [Test]
        public void PartialNativeFailureDoesNotConsumeAnotherScriptDraw()
        {
            var script = new FixedRandom(8);
            var native = new FixedRandom(3) { Fail = true };
            var session = new Original323EvolutionDrawSession(script, native);
            Assert.Throws<InvalidOperationException>(() => session.Prepare());
            Assert.IsFalse(session.IsPrepared);
            session.Prepare();
            Assert.IsTrue(session.IsPrepared);
            session.Prepare();
            Assert.AreEqual(1, script.Calls);
            Assert.AreEqual(2, native.Calls);
            Assert.AreEqual("U-08", session.Uncommon.CatalogId);
            Assert.AreEqual("S-03", session.Special.CatalogId);
            Assert.Throws<ArgumentException>(() =>
                new Original323EvolutionDrawSession(script, script));
        }
    }
}
