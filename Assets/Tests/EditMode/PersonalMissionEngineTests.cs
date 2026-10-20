using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class PersonalMissionEngineTests
    {
        // 테스트용 가상 미션이며 원작의 미션 목표나 보상 수치가 아니다.
        private static PersonalMissionDefinition Mission(string id, bool otherworld = false)
        {
            return new PersonalMissionDefinition(id, MissionEventType.UnitFusion,
                "test-mystic", 2, otherworld, "https://ordr.kr/patch-notes/2-320");
        }

        [Test]
        public void MissionProgressCompletesAndCannotClaimTwice()
        {
            var engine = new PersonalMissionEngine(true);
            engine.Assign(Mission("test", true));
            Assert.IsTrue(engine.Record(MissionEventType.UnitFusion, "test-mystic", 1));
            Assert.AreEqual(1, engine.Progress);
            Assert.IsFalse(engine.Record(MissionEventType.EnemyDefeated, "test-mystic", 1));
            Assert.IsTrue(engine.Record(MissionEventType.UnitFusion, "test-mystic", 9));
            Assert.AreEqual(2, engine.Progress);
            Assert.AreEqual(MissionStatus.Completed, engine.Status);
            engine.ConfirmRewardGranted();
            Assert.Throws<InvalidOperationException>(() => engine.ConfirmRewardGranted());
            engine.ClearClaimed();
            Assert.IsNull(engine.ActiveMission);
        }

        [Test]
        public void OtherworldMissionRejectedInNormalMode()
        {
            var engine = new PersonalMissionEngine(false);
            Assert.Throws<InvalidOperationException>(() => engine.Assign(Mission("test", true)));
            Assert.IsNull(engine.ActiveMission);
        }

        [Test]
        public void RerollBlockedFromRoundTenWithoutLosingProgress()
        {
            var engine = new PersonalMissionEngine(true);
            engine.Assign(Mission("test-old"));
            engine.Record(MissionEventType.UnitFusion, "test-mystic", 1);
            Assert.Throws<InvalidOperationException>(() =>
                engine.ReplaceAfterVerifiedReroll(Mission("test-new"), 10));
            Assert.AreEqual("test-old", engine.ActiveMission.Id);
            Assert.AreEqual(1, engine.Progress);
            engine.ReplaceAfterVerifiedReroll(Mission("test-new"), 9);
            Assert.AreEqual(0, engine.Progress);
        }

        [Test]
        public void InvalidCountDoesNotAlterProgress()
        {
            var engine = new PersonalMissionEngine(true);
            engine.Assign(Mission("test"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                engine.Record(MissionEventType.UnitFusion, "test-mystic", -1));
            Assert.AreEqual(0, engine.Progress);
        }
    }
}
