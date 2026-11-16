using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileHudPresentationTests
    {
        [Test]
        public void EveryNavigationTabHasATitleBeforeDrawingItsHeader()
        {
            var panel = typeof(OrdrMobileTouchPanel);
            var tabType = panel.GetNestedType("Tab", BindingFlags.NonPublic);
            var title = panel.GetMethod("TabTitle", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(tabType);
            Assert.IsNotNull(title);
            foreach (var tab in Enum.GetValues(tabType))
            {
                string text = null;
                Assert.DoesNotThrow(() => text = (string)title.Invoke(null, new[] { tab }),
                    "Navigation header failed: " + tab);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), "Empty navigation title: " + tab);
            }
        }

        [Test]
        public void SevenOriginalPlayerFacilitiesOpenIndependentLeftNavigationPages()
        {
            var panel = typeof(OrdrMobileTouchPanel);
            var tabType = panel.GetNestedType("Tab", BindingFlags.NonPublic);
            var title = panel.GetMethod("TabTitle", BindingFlags.Static | BindingFlags.NonPublic);
            var names = new Dictionary<string, string> {
                { "Gambling", "도박소" }, { "UpgradeOne", "강화소 1" },
                { "UpgradeTwo", "강화소 2" }, { "Helper", "도움소" },
                { "Missions", "미션" }, { "Research", "연구소" },
                { "LogPose", "기록지침" },
            };
            Assert.IsFalse(Enum.IsDefined(tabType, "Facilities"),
                "Player buildings must not be hidden in another facility tab.");
            Assert.IsFalse(Enum.IsDefined(tabType, "Shop"),
                "An unconnected generic shop must not regain a menu tab.");
            Assert.IsNotNull(panel.GetMethod("DrawMissions",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "Missions must provide one entry for both shared and personal goals.");
            Assert.IsNotNull(panel.GetMethod("DrawSharedMissionBoard",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "Shared missions must be selectable from the Missions page.");
            Assert.IsNotNull(panel.GetMethod("DrawPersonalMissions",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "The personal-mission page must remain available.");
            foreach (var entry in names)
            {
                var tab = Enum.Parse(tabType, entry.Key);
                Assert.AreEqual(entry.Value, title.Invoke(null, new[] { tab }), entry.Key);
            }
        }

        [Test]
        public void FacilityNavigationUsesTextOnlyButtonsWithoutDecorativeOverlays()
        {
            var panel = typeof(OrdrMobileTouchPanel);
            Assert.IsNotNull(panel.GetMethod("DrawLeftTab",
                BindingFlags.Instance | BindingFlags.NonPublic),
                "All seven facilities must remain accessible from the left rail.");
            Assert.IsNull(panel.GetMethod("GetFacilityTabTint",
                BindingFlags.Static | BindingFlags.NonPublic),
                "The retired accent-mark overlay must not be reintroduced.");
            Assert.IsNull(panel.GetNestedType("FacilityVisual", BindingFlags.NonPublic),
                "Decorative facility slogans and banners must stay removed.");
        }

        [Test]
        public void GamblingTapExecutesOnceWhileHoldingOnlyRevealsOfferRules()
        {
            var gesture = new MobileLongPressGesture();
            var origin = new Vector2(150f, 240f);
            gesture.Begin("regular", 10f, origin);
            Assert.AreEqual(MobileLongPressResult.Tap,
                gesture.Complete("regular", 10.3f));
            gesture.Begin("regular", 20f, origin);
            Assert.AreEqual(MobileLongPressResult.Hold,
                gesture.Complete("regular", 20.6f));
            gesture.Begin("regular", 30f, origin);
            Assert.AreEqual(MobileLongPressResult.None,
                gesture.Complete("high", 30.8f),
                "A different offer must not consume another offer's press.");
            gesture.Cancel();
            gesture.Begin("regular", 40f, origin);
            Assert.IsFalse(gesture.Move("regular", new Vector2(157f, 244f)));
            Assert.IsTrue(gesture.Move("regular", new Vector2(180f, 244f)));
            Assert.AreEqual(MobileLongPressResult.None,
                gesture.Complete("regular", 41f),
                "Dragging must never spend gold or open descriptions.");
            gesture.Begin("regular", 50f, origin);
            gesture.Cancel();
            Assert.AreEqual(MobileLongPressResult.Tap,
                gesture.Complete("regular", 50.1f),
                "Keyboard activation keeps the existing single-action behavior.");
        }

        [Test]
        public void ThreeOriginalResearchBuildingsKeepSeparateVerifiedInventories()
        {
            var buildings = new[] {
                Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding,
                Original323PlayerBuildingResearchCatalog.SecondUpgradeBuilding,
                Original323PlayerBuildingResearchCatalog.ResearchBuilding,
            };
            var expectedSizes = new[] { 8, 10, 4 };
            var seen = new HashSet<string>();
            for (int i = 0; i < buildings.Length; i++)
            {
                var inventory = Original323PlayerBuildingResearchCatalog.ForBuilding(buildings[i]);
                Assert.AreEqual(expectedSizes[i], inventory.Count, buildings[i]);
                foreach (var upgrade in inventory)
                {
                    Assert.IsTrue(seen.Add(upgrade.Rawcode), "Duplicated original research code");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(upgrade.Name));
                    Assert.Greater(upgrade.MaximumLevel, 0);
                }
            }
            Assert.AreEqual(7, Original323PlayerBuildingResearchCatalog.ForBuilding("h06Q")[0].MaximumLevel);
            Assert.AreEqual("R01V", Original323PlayerBuildingResearchCatalog.ForBuilding("h06R")[9].Rawcode);
            Assert.AreEqual("기후 변화", Original323PlayerBuildingResearchCatalog.ForBuilding("h06S")[3].Name);
            var laboratory = Original323PlayerBuildingResearchCatalog.ForBuilding("h06S");
            StringAssert.Contains("+0.45/초", laboratory[0].EffectSummary);
            StringAssert.Contains("+12%", laboratory[1].EffectSummary);
            StringAssert.Contains("+0.8/초", laboratory[2].EffectSummary);
            StringAssert.Contains("-10%", laboratory[3].EffectSummary);
            Assert.IsNull(Original323PlayerBuildingResearchCatalog.ForBuilding("h06Q")[0].EffectSummary);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323PlayerBuildingResearchCatalog.ForBuilding("h062"));
        }

        [Test]
        public void TabNavigationRestoresReadingPositionAndDiscardsUnconfirmedUnitActions()
        {
            var root = new GameObject("ORDR navigation state");
            root.SetActive(false);
            try
            {
                var panel = root.AddComponent<OrdrMobileTouchPanel>();
                var type = typeof(OrdrMobileTouchPanel);
                var tabType = type.GetNestedType("Tab", BindingFlags.NonPublic);
                var select = type.GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic);
                var scroll = type.GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic);
                var pendingSale = type.GetField("pendingCommonSaleId", BindingFlags.Instance | BindingFlags.NonPublic);
                var pendingDecomposition = type.GetField("pendingDecompositionId", BindingFlags.Instance | BindingFlags.NonPublic);
                var units = Enum.Parse(tabType, "Units");
                var fusion = Enum.Parse(tabType, "Fusion");
                select.Invoke(panel, new[] { units, (object)true });
                scroll.SetValue(panel, new Vector2(0, 230));
                pendingSale.SetValue(panel, (long?)42);
                pendingDecomposition.SetValue(panel, (long?)43);
                select.Invoke(panel, new[] { fusion, (object)true });
                Assert.IsNull(pendingSale.GetValue(panel));
                Assert.IsNull(pendingDecomposition.GetValue(panel));
                scroll.SetValue(panel, new Vector2(0, 80));
                select.Invoke(panel, new[] { units, (object)true });
                Assert.AreEqual(230f, ((Vector2)scroll.GetValue(panel)).y);
                type.GetMethod("CloseDrawer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                select.Invoke(panel, new[] { units, (object)true });
                Assert.AreEqual(230f, ((Vector2)scroll.GetValue(panel)).y);
                // Contextual links deliberately start their new search at the top.
                select.Invoke(panel, new[] { fusion, (object)false });
                Assert.AreEqual(0f, ((Vector2)scroll.GetValue(panel)).y);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(1920f,1080f,440f)]
        [TestCase(2288f,1080f,440f)]
        [TestCase(756f,390f,460f)]
        [TestCase(1152f,700f,320f)]
        public void FiveResourceSlotsRemainInsideTheirLandscapeArea(
            float width,float height,float dpi)
        {
            var safe=new Rect(0,0,width,height);
            float scale=MobileMapSkillLayout.UiScale(safe,dpi);
            var layout=MobileMapSkillLayout.Calculate(safe,height,scale);
            Assert.AreEqual(0f, layout.HeaderHeight, "The gameplay title header must not reserve space.");
            Assert.AreEqual(52f, layout.StatusMargin.height / scale, .01f);
            Assert.Less(layout.StatusMargin.yMax, layout.Battle.yMin);
            Assert.AreEqual(safe.height - 6f * scale, layout.Story.yMax, .02f);
            var area=MobileMapSkillLayout.Scaled(layout.StatusMargin,scale);
            area = new Rect(area.xMax - Mathf.Min(900f, area.width - 12f) - 6f, area.y,
                Mathf.Min(900f, area.width - 12f), area.height);
            var chips=MobileMapSkillLayout.StatusChipRects(area, 5);
            Assert.AreEqual(5,chips.Length);
            for(int i=0;i<5;i++)
            {
                Assert.Greater(chips[i].width,0f);
                Assert.GreaterOrEqual(chips[i].xMin,area.xMin-.01f);
                Assert.LessOrEqual(chips[i].xMax,area.xMax+.01f);
                Assert.AreEqual(area.height,chips[i].height,.01f);
                if(i>0)Assert.Greater(chips[i].xMin,chips[i-1].xMax);
            }
            Assert.LessOrEqual(layout.Battle.xMax,layout.RightDrawer.xMin);
            Assert.GreaterOrEqual(layout.SkillMargin.yMin,layout.Battle.yMax);
            Assert.GreaterOrEqual(layout.LeftTabs.width / scale, 150f);
            Assert.IsTrue(layout.BlocksWorldTap(layout.SkillMargin.center, false));
            Assert.IsTrue(layout.BlocksWorldTap(layout.StatusMargin.center, false));
            // The closed detail drawer must not catch taps; opening it
            // blocks its own region without changing either battlefield.
            Assert.IsFalse(layout.BlocksWorldTap(layout.RightDrawer.center, false));
            Assert.IsTrue(layout.BlocksWorldTap(layout.RightDrawer.center, true));
            Assert.IsFalse(layout.BlocksWorldTap(layout.Battle.center, false));
            Assert.IsFalse(layout.BlocksWorldTap(layout.Story.center, true));
        }

        [Test]
        public void TitleScreenBlocksWorldInputAndStartsOnlyOnePreparationSession()
        {
            var root = new GameObject("ORDR title-to-game integration");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                var shell = root.AddComponent<OrdrMainSceneShell>();
                typeof(OrdrMainSceneShell).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(shell, null);
                Assert.IsTrue(shell.IsStartScreenVisible);
                Assert.Throws<InvalidOperationException>(() => shell.EnterGame());
                Assert.IsTrue(shell.IsStartScreenVisible);
                Assert.IsNull(owner.Combat);
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                var panel = root.GetComponent<OrdrMobileTouchPanel>();
                Assert.IsTrue(panel.BlocksWorldTap(Vector2.zero));
                shell.EnterGame();
                Assert.IsFalse(shell.IsStartScreenVisible);
                Assert.IsTrue(owner.IsUnitPreparation);
                Assert.AreEqual(0, owner.Combat.Game.CurrentWave);
                // CUM starts Loguetown in the preparation session. It is a
                // static story BUILDING, not a counted wave monster.
                Assert.AreEqual(1, owner.Combat.EnemyCount);
                Assert.AreEqual(0, owner.Combat.Game.AliveEnemies);
                Assert.IsNotNull(owner.SoloStory);
                Assert.AreEqual(1, owner.SoloStory.ActiveStage);
                var originalStory = owner.SoloStory;
                var originalSession = owner.SharedEconomy;
                long gold = originalSession.Wallet.Balance("gold");
                long wood = originalSession.Wallet.Balance("wood");
                int actors = originalSession.Units.Count;
                shell.EnterGame();
                Assert.AreSame(originalSession, owner.SharedEconomy);
                Assert.AreSame(originalStory, owner.SoloStory);
                Assert.AreEqual(1, owner.Combat.EnemyCount);
                Assert.AreEqual(0, owner.Combat.Game.AliveEnemies);
                Assert.AreEqual(gold, owner.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(wood, owner.SharedEconomy.Wallet.Balance("wood"));
                Assert.AreEqual(actors, owner.SharedEconomy.Units.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PanelDragScrollsAndConsumesReleaseWithoutTurningATapIntoAnAction()
        {
            var gesture = new MobilePanelScrollGesture();
            var viewport = new Rect(300, 60, 300, 250);
            float y = 100f;
            gesture.Begin(new Vector2(350, 150), viewport, y);
            Assert.IsTrue(gesture.IsTracking);
            Assert.IsFalse(gesture.Move(new Vector2(350, 154), 500f, ref y));
            Assert.AreEqual(100f, y);
            Assert.IsFalse(gesture.End(), "A short tap must remain available to buttons.");
            gesture.Begin(new Vector2(350, 150), viewport, y);
            Assert.IsTrue(gesture.Move(new Vector2(350, 100), 500f, ref y));
            Assert.AreEqual(150f, y);
            Assert.IsTrue(gesture.Move(new Vector2(350, -900), 500f, ref y));
            Assert.AreEqual(500f, y, "Dragging past the viewport clamps to the content bottom.");
            Assert.IsTrue(gesture.End(), "Drag release must be consumed before drawing buttons.");
            Assert.IsFalse(gesture.IsTracking);
            Assert.IsFalse(gesture.End());
        }

        [Test]
        public void PanelGestureLeavesScrollbarAndOutsideTouchesAloneAndCancelsOnNavigation()
        {
            var gesture = new MobilePanelScrollGesture();
            var viewport = new Rect(300, 60, 300, 250);
            float y = 100f;
            foreach (var point in new[] { new Vector2(290, 150), new Vector2(590, 150) })
            {
                gesture.Begin(point, viewport, y);
                Assert.IsFalse(gesture.IsTracking);
                Assert.IsFalse(gesture.Move(new Vector2(350, 90), 500, ref y));
            }
            gesture.Begin(new Vector2(350, 100), viewport, y);
            Assert.IsTrue(gesture.Move(new Vector2(350, 300), 500, ref y));
            Assert.AreEqual(0f, y);
            gesture.Cancel();
            Assert.IsFalse(gesture.Move(new Vector2(350, 50), 500, ref y));
            Assert.IsFalse(gesture.End());
        }

        [Test]
        public void SkillSwipeConsumesReleaseAndClampsAtBothEnds()
        {
            var gesture = new MobilePanelScrollGesture();
            var viewport = new Rect(200, 300, 300, 70);
            float x = 20f;
            gesture.Begin(new Vector2(480, 360), viewport, x, true, false);
            Assert.IsTrue(gesture.IsTracking, "Hidden scrollbars must not reserve an untouchable edge.");
            Assert.IsFalse(gesture.Move(new Vector2(476, 360), 500, ref x));
            Assert.IsTrue(gesture.Move(new Vector2(400, 360), 500, ref x));
            Assert.AreEqual(100f, x);
            Assert.IsTrue(gesture.Move(new Vector2(-400, 360), 500, ref x));
            Assert.AreEqual(500f, x);
            Assert.IsTrue(gesture.End(), "Swipe release must not cast the skill.");
            gesture.Begin(new Vector2(300, 340), viewport, x, true, false);
            Assert.IsTrue(gesture.Move(new Vector2(900, 340), 500, ref x));
            Assert.AreEqual(0f, x);
            gesture.Cancel();
            Assert.IsFalse(gesture.End());
        }

        [Test]
        public void InvalidSlotDimensionsFailBeforeRendering()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                MobileMapSkillLayout.StatusChipRects(new Rect(0,0,0,30)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                MobileMapSkillLayout.StatusChipRects(new Rect(0,0,200,0)));
        }
    }
}

