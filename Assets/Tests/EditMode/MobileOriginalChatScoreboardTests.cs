using NUnit.Framework;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileOriginalChatScoreboardTests
    {
        [Test]
        public void PersonalRecordsKeepNineCommonAndFiveGambleLabels()
        {
            Assert.AreEqual(9, MobileOriginalChatScoreboard.CommonCatalogIds.Length);
            Assert.AreEqual(9, MobileOriginalChatScoreboard.CommonNames.Length);
            Assert.AreEqual(5, MobileOriginalChatScoreboard.GambleNames.Length);
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
        public void UtilitySpecSumsEachOwnedSkillButOnlyOnePhasePerSkill()
        {
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
                "{\"unitId\":\"TEST-A\",\"sourceUrl\":\"fixture\",\"skillSpecCompleteness\":\"partial\"," +
                "\"basicAttack\":{},\"passives\":[],\"skills\":[" +
                "{\"name\":\"dual phase\",\"trigger\":\"onAttack\",\"components\":[" +
                "{\"type\":\"armorReduction\",\"amount\":12}," +
                "{\"type\":\"movementSlow\",\"percent\":45}," +
                "{\"type\":\"lingeringSlow\",\"percent\":15}]}]}]}");
            var snapshot = MobileOriginalUtilityBoard.Capture(
                new[] { "TEST-A", "TEST-A", "", null }, profiles, null);
            Assert.AreEqual(2, snapshot.UnitCount);
            Assert.AreEqual(0, snapshot.AuraDefinitions);
            Assert.AreEqual(2, snapshot.TriggeredDefinitions);
            Assert.AreEqual(24d, snapshot.TriggeredArmorReduction);
            Assert.AreEqual(90d, snapshot.TriggeredMovementSlowPercent);
        }

        [Test]
        public void UtilitySpecNeverInventsMissingSourceNumbers()
        {
            var snapshot = MobileOriginalUtilityBoard.Capture(
                new[] { "UNKNOWN" }, null, null);
            Assert.AreEqual(1, snapshot.UnitCount);
            Assert.AreEqual(0, snapshot.AuraDefinitions);
            Assert.AreEqual(0, snapshot.TriggeredDefinitions);
            Assert.AreEqual(0d, snapshot.AuraArmorReduction);
            Assert.AreEqual(0d, snapshot.TriggeredMovementSlowPercent);
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
