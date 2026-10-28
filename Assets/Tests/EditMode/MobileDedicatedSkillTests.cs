using System;
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

        private static void AssertStackedLandscape(Rect safe, float screenHeight, float dpi)
        {
            var layout = MobileMapSkillLayout.Calculate(
                safe, screenHeight, MobileMapSkillLayout.UiScale(safe, dpi));
            Assert.Greater(layout.Battle.height, layout.Story.height * 3.9f);
            Assert.AreEqual(layout.Battle.xMin, layout.SkillMargin.xMin, 0.01f);
            Assert.AreEqual(layout.Battle.xMin, layout.Story.xMin, 0.01f);
            Assert.AreEqual(layout.Battle.width, layout.SkillMargin.width, 0.01f);
            Assert.AreEqual(layout.Battle.width, layout.Story.width, 0.01f);
            Assert.Less(layout.Battle.yMax, layout.SkillMargin.yMin);
            Assert.Less(layout.SkillMargin.yMax, layout.Story.yMin);
            Assert.LessOrEqual(layout.Story.yMax, screenHeight + 0.01f);
            Assert.Less(layout.LeftTabs.xMax, layout.Battle.xMin);
            Assert.AreEqual(layout.LeftTabs.yMin, layout.Battle.yMin, 0.01f);
            Assert.GreaterOrEqual(layout.LeftTabs.yMax, layout.Story.yMax - 0.01f);
            Assert.AreEqual(layout.Battle.yMin, layout.RightDrawer.yMin, 0.01f);
            Assert.Greater(layout.RightDrawer.yMax, layout.Story.yMax);
            Assert.Less(layout.Battle.xMax, layout.RightDrawer.xMin);
            Assert.Greater(layout.RightDrawer.width, 0f);
            Assert.Less(layout.RightDrawer.xMax, safe.xMax);
            // Both battlefields are intentionally smaller than the former
            // full-width, full-height layout.  Right details never overlap.
            Assert.Less(layout.Battle.width, safe.width * 0.60f);
            Assert.Less(layout.Story.width, safe.width * 0.60f);
            Assert.Less(layout.Battle.height + layout.SkillMargin.height +
                layout.Story.height, safe.height * 0.88f);
            Assert.Greater(layout.RightDrawer.width, layout.Battle.width * 0.80f);
            Assert.Greater(layout.StatusMargin.height, 18f);
            Assert.Less(layout.StatusMargin.height, safe.height * 0.17f);
            Assert.GreaterOrEqual(layout.LeftTabs.width, 150f);
            Assert.GreaterOrEqual(layout.LeftTabs.width /
                MobileMapSkillLayout.UiScale(safe, dpi), 90f);
            Assert.GreaterOrEqual(layout.StatusMargin.yMin, layout.Story.yMax);
            Assert.LessOrEqual(layout.StatusMargin.yMax, screenHeight + 0.01f);
            Assert.Less(layout.StatusMargin.xMax, layout.RightDrawer.xMin);
            Assert.LessOrEqual(layout.RightDrawer.yMax, screenHeight + 0.01f);
            var viewport = layout.MainCameraViewport(safe.xMax + (screenHeight - safe.yMax), screenHeight);
            Assert.Greater(viewport.height, 0f);
            Assert.Less(viewport.yMax, 1f);
            Assert.Greater(layout.SkillMargin.height, 30f);
        }

        [Test]
        public void LandscapeTabletAndPhonePlaceMainAboveRibbonAboveSmallStory()
        {
            AssertStackedLandscape(new Rect(0f, 0f, 1920f, 1080f), 1080f, 240f);
            AssertStackedLandscape(new Rect(24f, 12f, 1152f, 700f), 724f, 300f);
            AssertStackedLandscape(new Rect(44f, 0f, 756f, 390f), 390f, 460f);
            // Insets and high DPI similar to the reported Android screenshot.
            AssertStackedLandscape(new Rect(112f, 0f, 2288f, 1080f),
                1080f, 440f);
        }


        [Test]
        public void LeftTabsAlwaysRejectWorldInputAndRightDrawerBlocksOnlyWhileOpen()
        {
            var layout = MobileMapSkillLayout.Calculate(
                new Rect(24f, 12f, 1152f, 700f), 724f, 1.2f);
            Vector2 left = layout.LeftTabs.center;
            Vector2 drawer = layout.RightDrawer.center;
            Vector2 unobstructedBattle = new Vector2(
                layout.Battle.xMin + 10f, layout.Battle.yMin + 10f);
            Assert.IsTrue(layout.BlocksWorldTap(left, false));
            Assert.IsTrue(layout.BlocksWorldTap(left, true));
            Assert.IsFalse(layout.BlocksWorldTap(drawer, false));
            Assert.IsTrue(layout.BlocksWorldTap(drawer, true));
            Assert.IsFalse(layout.BlocksWorldTap(unobstructedBattle, true));
            Assert.Less(layout.Battle.xMax, layout.RightDrawer.xMin);
        }


        [Test]
        public void ReducedMapsKeepSkillRibbonAndStorySeparateOnSmallLandscapeScreens()
        {
            var layout = MobileMapSkillLayout.Calculate(
                new Rect(0f, 0f, 844f, 390f), 390f, 1f);
            Assert.Greater(layout.SkillMargin.height, 40f);
            Assert.Greater(layout.Story.height, 28f);
            Assert.Greater(layout.Battle.height, 120f);
            Assert.Less(layout.Story.yMax, layout.RightDrawer.yMax);
            Assert.Greater(layout.RightDrawer.width, 250f);
        }

        [Test]
        public void PortraitHasNoAlternateMapLayout()
        {
            Assert.Throws<ArgumentException>(() =>
                MobileMapSkillLayout.Calculate(new Rect(0, 0, 390, 844), 844, 1f));
        }
    }
}
