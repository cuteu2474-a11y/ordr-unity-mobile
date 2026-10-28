using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323GaimonDreamStateTests
    {
        private sealed class YlWitness : IOriginal323GaimonNamiBonusWitness
        {
            public bool Nami,Available=true;
            public int Calls;
            public string Evidence="source W3X verified YL player flag";
            public bool TryReadOriginalYlFlag(out bool namiBonus,out string sourceEvidence)
            {
                Calls++;namiBonus=Nami;sourceEvidence=Evidence;
                return Available;
            }
        }

        [Test]
        public void CCPCommitsOnceAndDoesNotGuessNami()
        {
            var dream=new Original323GaimonDreamState();
            var witness=new YlWitness {Nami=false};
            Assert.IsFalse(dream.TryApplyAfterOwnedGaimon(false,witness));
            Assert.IsFalse(dream.TryApplyAfterOwnedGaimon(true,null));
            Assert.IsFalse(dream.Applied);
            Assert.AreEqual(850d,dream.SearchHalfExtent);
            Assert.IsTrue(dream.TryApplyAfterOwnedGaimon(true,witness));
            Assert.IsTrue(dream.Applied);
            Assert.IsFalse(dream.NamiBonusWhenApplied);
            Assert.AreEqual(978d,dream.SearchHalfExtent);
            Assert.AreEqual(2,dream.TreasureWispQuantity);
            witness.Nami=true;
            Assert.IsFalse(dream.TryApplyAfterOwnedGaimon(true,witness));
            Assert.AreEqual(978d,dream.SearchHalfExtent);
            Assert.AreEqual(1,witness.Calls);
        }

        [Test]
        public void SourceYlAtFirstCommittedGaimonControls1125Range()
        {
            var dream=new Original323GaimonDreamState();
            var witness=new YlWitness {Nami=true};
            Assert.IsTrue(dream.TryApplyAfterOwnedGaimon(true,witness));
            Assert.AreEqual(1125d,dream.SearchHalfExtent);
            Assert.IsTrue(dream.NamiBonusWhenApplied);
        }

        [Test]
        public void MissingOrUntrustedYlDoesNotIrreversiblyApplyCCP()
        {
            var d=new Original323GaimonDreamState();
            var w=new YlWitness {Available=false};
            Assert.IsFalse(d.TryApplyAfterOwnedGaimon(true,w));
            w.Available=true;w.Evidence="TEST_ONLY data";
            Assert.IsFalse(d.TryApplyAfterOwnedGaimon(true,w));
            Assert.IsFalse(d.Applied);
            w.Evidence="original CCP / YL source event";
            Assert.IsTrue(d.TryApplyAfterOwnedGaimon(true,w));
        }

        [Test]
        public void OriginalPlayerFlagSurvivesRemovalAndRoundTrip()
        {
            var d=new Original323GaimonDreamState();
            Assert.IsTrue(d.TryApplyAfterOwnedGaimon(true,new YlWitness {Nami=true}));
            var saved=d.Capture();
            var restored=new Original323GaimonDreamState();
            restored.Restore(saved);
            Assert.AreEqual(1125d,restored.SearchHalfExtent);
            Assert.IsFalse(restored.TryApplyAfterOwnedGaimon(true,new YlWitness()));
            restored.Restore(new Original323GaimonDreamSnapshot(false,false));
            Assert.AreEqual(850d,restored.SearchHalfExtent);
            Assert.IsFalse(restored.Applied);
        }
    }
}
