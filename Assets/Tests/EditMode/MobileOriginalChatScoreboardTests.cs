using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileOriginalChatScoreboardTests
    {
        [Test]
        public void OriginalChatCategoriesAndNineCommonsHaveStableLabels()
        {
            Assert.AreEqual(9, MobileOriginalChatScoreboard.CommonCatalogIds.Length);
            Assert.AreEqual(9, MobileOriginalChatScoreboard.CommonNames.Length);
            Assert.AreEqual(5, MobileOriginalChatScoreboard.GambleNames.Length);
            Assert.AreEqual(7, MobileOriginalChatScoreboard.PlusNames.Length);
            Assert.AreEqual(1, MobileOriginalChatScoreboard.MinusNames.Length);
            Assert.AreEqual("C-08", MobileOriginalChatScoreboard.CommonCatalogIds[7]);
            Assert.AreEqual("총병", MobileOriginalChatScoreboard.CommonNames[7]);
            Assert.AreEqual("C-09", MobileOriginalChatScoreboard.CommonCatalogIds[8]);
            Assert.AreEqual("칼병", MobileOriginalChatScoreboard.CommonNames[8]);
        }

        [Test]
        public void CountsOnlyConfirmedCommonDrawAndNeverInventsOtherSources()
        {
            var journal = new MobileOriginalChatScoreboard();
            Assert.IsFalse(journal.RecordCommittedCommonWispDraw("PS-01"));
            Assert.IsFalse(journal.RecordCommittedCommonWispDraw("U-01"));
            Assert.IsFalse(journal.RecordCommittedCommonWispDraw(null));
            Assert.AreEqual(0, journal.ConfirmedCommonDraws);
            Assert.IsTrue(journal.RecordCommittedCommonWispDraw("C-01"));
            Assert.IsTrue(journal.RecordCommittedCommonWispDraw("C-01"));
            Assert.IsTrue(journal.RecordCommittedCommonWispDraw("C-09"));
            Assert.AreEqual(3, journal.ConfirmedCommonDraws);
            Assert.AreEqual(2, journal.CommonDrawCount("C-01"));
            Assert.AreEqual(1, journal.CommonDrawCount("C-09"));
            Assert.AreEqual(0, journal.CommonDrawCount("C-02"));
        }

        [Test]
        public void NewSessionBeginsWithNoInheritedDraws()
        {
            var first = new MobileOriginalChatScoreboard();
            first.RecordCommittedCommonWispDraw("C-03");
            var second = new MobileOriginalChatScoreboard();
            Assert.AreEqual(0, second.ConfirmedCommonDraws);
            Assert.AreEqual(0, second.CommonDrawCount("C-03"));
        }
    }
}
