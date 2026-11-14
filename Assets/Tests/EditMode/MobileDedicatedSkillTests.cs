using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
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

        private sealed class TargetedWorld : IOriginalDedicatedSkillPort,
            IOriginalTargetedDedicatedSkillPort
        {
            public int TargetCalls;
            public int UntargetedCalls;
            public long TargetId;
            public IReadOnlyList<MobileDedicatedSkillOffer> AvailableSkills()
            {
                return new[] {
                    new MobileDedicatedSkillOffer("copy:31", "복사", "대상 지정",
                        true, true, 31, requiresAllyTarget: true),
                    new MobileDedicatedSkillOffer("copy:32", "다른 소유자", "대상 지정",
                        true, true, 32, requiresAllyTarget: true),
                    new MobileDedicatedSkillOffer("upgrade:31", "강화", "자원 소비",
                        true, true, 31, requiresConfirmation: true)
                };
            }
            public ManualActionResult Execute(string id)
            {
                UntargetedCalls++;
                return new ManualActionResult(ManualActionState.Succeeded, "직접 시전");
            }
            public ManualActionResult ExecuteForAlly(string offeredSkillId,
                long ownerInstanceId, long targetInstanceId)
            {
                TargetCalls++;
                TargetId = targetInstanceId;
                return new ManualActionResult(ManualActionState.Succeeded, "대상 시전");
            }
        }

        [Test]
        public void LiveManualSkillStatusRemainsReadOnlyAndValidated()
        {
            var skill = new MobileDedicatedSkillOffer("cast:7", "변신", "10초 재사용",
                true, false, 7, liveStatus: "현재 불사조형",
                cooldownRemainingSeconds: 5.25);
            Assert.AreEqual("현재 불사조형", skill.LiveStatus);
            Assert.AreEqual(5.25, skill.CooldownRemainingSeconds);
            Assert.IsFalse(skill.CanExecute);
            Assert.Throws<ArgumentException>(() =>
                new MobileDedicatedSkillOffer("cast:7", "변신", "재사용",
                    true, false, 7, cooldownRemainingSeconds: double.NaN));
            Assert.Throws<ArgumentException>(() =>
                new MobileDedicatedSkillOffer("cast:7", "변신", "재사용",
                    true, false, 7, cooldownRemainingSeconds: -1));
        }

        [Test]
        public void SelectedOwnerAndTargetAreBoundIndependently()
        {
            var actions = new MobileDedicatedSkillActions();
            var world = new TargetedWorld();
            actions.Configure(world);
            Assert.AreEqual(2, actions.AvailableFor(31).Count);
            Assert.AreEqual(1, actions.AvailableFor(32).Count);
            Assert.AreEqual(0, actions.AvailableFor(0).Count);
            Assert.AreEqual(ManualActionState.Rejected,
                actions.Request("copy:31").State);
            Assert.AreEqual(0, world.UntargetedCalls);
            Assert.AreEqual(ManualActionState.Rejected,
                actions.RequestTargeted("copy:31", 32, 81).State);
            Assert.AreEqual(ManualActionState.Rejected,
                actions.RequestTargeted("upgrade:31", 31, 81).State);
            Assert.AreEqual(0, world.TargetCalls);
            Assert.AreEqual(ManualActionState.Succeeded,
                actions.RequestTargeted("copy:31", 31, 81).State);
            Assert.AreEqual(81, world.TargetId);
            Assert.AreEqual(1, world.TargetCalls);
        }

        [Test]
        public void ExistingOtamaOneTimeReceiptCanBeUsedAsDedicatedActionId()
        {
            string id = SpecialGradeMobilePolicy.OtamaRewardId(77);
            Assert.AreEqual("otama-dango:77", id);
            var wallet = new Ordr.Mobile.Core.EconomyWallet();
            Assert.IsFalse(wallet.HasCompletedTransaction(id));
            Assert.IsTrue(wallet.TryGrantOnce(id,
                new Ordr.Mobile.Core.ResourceBundle().Add("wood", 16)));
            Assert.IsTrue(wallet.HasCompletedTransaction(id));
            Assert.AreEqual(16, wallet.Balance("wood"));
            Assert.IsFalse(wallet.TryGrantOnce(id,
                new Ordr.Mobile.Core.ResourceBundle().Add("wood", 16)));
            Assert.AreEqual(16, wallet.Balance("wood"));
        }

        [Test]
        public void SatelliteUnknownChoicesNeverCreateFreeResources()
        {
            long trait, wood;
            Assert.IsFalse(SourceModelNativeCharacterRules.TryPlanSatelliteCost(
                "H000", 0, out trait, out wood));
            Assert.IsFalse(SourceModelNativeCharacterRules.TryPlanSatelliteCost(
                "H0AB", 3, out trait, out wood));
            Assert.Throws<ArgumentException>(() =>
                SourceModelNativeCharacterRules.SatelliteActionId("H000"));
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
            // The documented story field uses 24% of field space; the main map remains dominant.
            Assert.Greater(layout.Battle.height, layout.Story.height * 3f);
            Assert.AreEqual(layout.Battle.xMin, layout.SkillMargin.xMin, 0.01f);
            Assert.AreEqual(layout.Battle.xMin, layout.Story.xMin, 0.01f);
            Assert.AreEqual(layout.Battle.width, layout.SkillMargin.width, 0.01f);
            Assert.AreEqual(layout.Battle.width, layout.Story.width, 0.01f);
            Assert.Less(layout.Battle.yMax, layout.SkillMargin.yMin);
            Assert.Less(layout.SkillMargin.yMax, layout.Story.yMin);
            Assert.LessOrEqual(layout.Story.yMax, screenHeight + 0.01f);
            Assert.Less(layout.LeftTabs.xMax, layout.Battle.xMin);
            Assert.Less(layout.LeftTabs.yMin, layout.Battle.yMin);
            Assert.GreaterOrEqual(layout.LeftTabs.yMax, layout.Story.yMax - 0.01f);
            Assert.AreEqual(layout.Battle.yMin, layout.RightDrawer.yMin, 0.01f);
            Assert.AreEqual(layout.RightDrawer.yMax, layout.Story.yMax, .02f);
            Assert.Less(layout.Battle.xMax, layout.RightDrawer.xMin);
            Assert.Greater(layout.RightDrawer.width, 0f);
            Assert.Less(layout.RightDrawer.xMax, safe.xMax);
            // Keep the requested narrow maps/right details, but reclaim the
            // old title header and bottom resource-card space vertically.
            Assert.Less(layout.Battle.width, safe.width * 0.60f);
            Assert.Less(layout.Story.width, safe.width * 0.60f);
            // The enlarged 52px resource bar and touch-sized skill ribbon
            // intentionally reserve more height than the old 34px status UI.
            // Account for two safe-edge margins and all three map gaps;
            // compact 390px landscapes still retain at least 79% of the field.
            float mapFields = layout.Battle.height + layout.SkillMargin.height +
                layout.Story.height;
            float reserved = safe.height - mapFields;
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            Assert.LessOrEqual(reserved, layout.StatusMargin.height +
                12f * scale + 27f + 0.02f);
            Assert.Greater(mapFields, safe.height * 0.79f);
            Assert.Greater(layout.RightDrawer.width, layout.Battle.width * 0.80f);
            Assert.Greater(layout.StatusMargin.height, 18f);
            Assert.Less(layout.StatusMargin.height, safe.height * 0.17f);
            Assert.GreaterOrEqual(layout.LeftTabs.width, 150f);
            Assert.GreaterOrEqual(layout.LeftTabs.width /
                MobileMapSkillLayout.UiScale(safe, dpi), 90f);
            Assert.Less(layout.StatusMargin.yMax, layout.Battle.yMin);
            Assert.LessOrEqual(layout.StatusMargin.yMax, screenHeight + 0.01f);
            Assert.AreEqual(layout.StatusMargin.xMax, layout.RightDrawer.xMax, .02f);
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
            // The title/footer cards were removed: story and details now
            // both finish at the six-pixel bottom safe-area inset.
            Assert.AreEqual(384f, layout.Story.yMax, .01f);
            Assert.AreEqual(layout.RightDrawer.yMax, layout.Story.yMax, .01f);
            Assert.Less(layout.Battle.yMax, layout.SkillMargin.yMin);
            Assert.Less(layout.SkillMargin.yMax, layout.Story.yMin);
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
