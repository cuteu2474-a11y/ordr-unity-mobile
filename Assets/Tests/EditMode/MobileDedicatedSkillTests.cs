using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileDedicatedSkillTests
    {
        private sealed class OriginalWorld : IOriginalDedicatedSkillPort
        {
            public int Calls;
            public IReadOnlyList<MobileDedicatedSkillOffer> AvailableSkills()
            {
                return new[] {
                    new MobileDedicatedSkillOffer("test:locked", "해금 전", "원본 조건", false, false),
                    new MobileDedicatedSkillOffer("test:insufficient", "재화 부족", "원본 비용", true, false),
                    new MobileDedicatedSkillOffer("test:ready", "사용 가능", "원본 비용", true, true)
                };
            }
            public ManualActionResult Execute(string id)
            {
                Calls++;
                return new ManualActionResult(ManualActionState.Succeeded, "TEST_ONLY");
            }
        }

        [Test]
        public void UnconnectedAndLockedActionsDoNotExecute()
        {
            var actions = new MobileDedicatedSkillActions();
            Assert.AreEqual(ManualActionState.NotImplemented, actions.Request("test:ready").State);
            var world = new OriginalWorld();
            actions.Configure(world);
            Assert.AreEqual(ManualActionState.Rejected, actions.Request("test:locked").State);
            Assert.AreEqual(ManualActionState.Rejected, actions.Request("test:insufficient").State);
            Assert.AreEqual(ManualActionState.Rejected, actions.Request("test:unknown").State);
            Assert.AreEqual(0, world.Calls);
            Assert.AreEqual(ManualActionState.Succeeded, actions.Request("test:ready").State);
            Assert.AreEqual(1, world.Calls);
        }

        [Test]
        public void DedicatedMarginNeverOverlapsStoryOrBattle()
        {
            var wide = MobileMapSkillLayout.Calculate(new Rect(0, 0, 1920, 1080), 1080, 1.5f);
            Assert.IsTrue(wide.SideMargin);
            Assert.GreaterOrEqual(wide.SkillMargin.xMin, wide.Story.xMax);
            var portrait = MobileMapSkillLayout.Calculate(new Rect(0, 0, 390, 844), 844, 1f);
            Assert.IsFalse(portrait.SideMargin);
            Assert.GreaterOrEqual(portrait.SkillMargin.yMin, portrait.Story.yMax);
        }
    }
}
