using System.Linq;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelSixthAttackCatalogTests
    {
        [Test]
        public void PinnedOriginalCallbacksHaveUniqueOwnersAndRealHits()
        {
            Assert.AreEqual(5, SourceModelSixthAttackCatalog.All.Count);
            Assert.AreEqual(5, SourceModelSixthAttackCatalog.All
                .Select(row => row.UnitRawcode).Distinct().Count());
            foreach (var row in SourceModelSixthAttackCatalog.All)
            {
                SourceScriptAttackSpec lookedUp;
                Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet(
                    row.UnitRawcode, out lookedUp));
                Assert.AreSame(row, lookedUp);
                Assert.Greater(row.Always.Count + row.OnProc.Count, 0);
            }
        }

        [Test]
        public void SpecialBranchesRetainTheirJassOperandsAndOrder()
        {
            SourceScriptAttackSpec morgan;
            SourceScriptAttackSpec decken;
            SourceScriptAttackSpec akainu;
            SourceScriptAttackSpec kinemon;
            SourceScriptAttackSpec akainuHidden;
            Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet("h05H", out morgan));
            Assert.AreEqual(11, morgan.RollSuccess);
            Assert.AreEqual("CCE", morgan.JassFunction);
            Assert.AreEqual(35000, morgan.OnProc[0].Damage);
            Assert.AreEqual(.9, morgan.OnProc[0].BossCurrentHealthPercent);
            Assert.AreEqual(4.5, morgan.OnProc[0].EnragedMaxHealthPercent);

            Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet("h01T", out decken));
            Assert.AreEqual(1, decken.RollSides);
            Assert.AreEqual(.42, decken.DelaySeconds);
            Assert.AreEqual(400, decken.OnProc[0].Radius);
            Assert.AreEqual(1500, decken.OnProc[0].Damage);

            Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet("h01P", out akainu));
            Assert.AreEqual(12, akainu.RollSuccess);
            Assert.AreEqual(10000, akainu.OnProc[0].Damage);

            Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet("h02J", out kinemon));
            Assert.AreEqual(10, kinemon.RollSuccess);
            Assert.AreEqual(1.5, kinemon.OnProc[0].Maximum);

            Assert.IsTrue(SourceModelSixthAttackCatalog.TryGet("h03Z", out akainuHidden));
            Assert.AreEqual("Bq9", akainuHidden.JassFunction);
            Assert.AreEqual(16, akainuHidden.RollSuccess);
            Assert.AreEqual(250000, akainuHidden.OnProc[0].Damage);
            Assert.AreEqual(500, akainuHidden.OnProc[1].Radius);
            Assert.AreEqual(100000, akainuHidden.OnProc[1].Damage);
            Assert.AreEqual(375000, akainuHidden.OnProc[2].Damage);
            Assert.IsTrue(akainuHidden.OnProc[2].StoryOnly);
        }
    }
}
