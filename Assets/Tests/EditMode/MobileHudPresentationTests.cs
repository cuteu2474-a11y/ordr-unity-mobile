using System;
using System.Reflection;
using NUnit.Framework;
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
        public void ThreeResourceSlotsRemainInsideTheirLandscapeArea(
            float width,float height,float dpi)
        {
            var safe=new Rect(0,0,width,height);
            float scale=MobileMapSkillLayout.UiScale(safe,dpi);
            var layout=MobileMapSkillLayout.Calculate(safe,height,scale);
            Assert.AreEqual(0f, layout.HeaderHeight, "The gameplay title header must not reserve space.");
            Assert.LessOrEqual(layout.StatusMargin.height / scale, 28f);
            Assert.Less(layout.StatusMargin.yMax, layout.Battle.yMin);
            Assert.AreEqual(safe.height - 6f * scale, layout.Story.yMax, .02f);
            var area=MobileMapSkillLayout.Scaled(layout.StatusMargin,scale);
            var chips=MobileMapSkillLayout.StatusChipRects(area);
            Assert.AreEqual(3,chips.Length);
            for(int i=0;i<3;i++)
            {
                Assert.Greater(chips[i].width,0f);
                Assert.GreaterOrEqual(chips[i].xMin,area.xMin-.01f);
                Assert.LessOrEqual(chips[i].xMax,area.xMax+.01f);
                Assert.AreEqual(area.height,chips[i].height,.01f);
                if(i>0)Assert.Greater(chips[i].xMin,chips[i-1].xMax);
            }
            Assert.LessOrEqual(layout.Battle.xMax,layout.RightDrawer.xMin);
            Assert.GreaterOrEqual(layout.SkillMargin.yMin,layout.Battle.yMax);
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
                Assert.AreEqual(0, owner.Combat.EnemyCount);
                var originalSession = owner.SharedEconomy;
                long gold = originalSession.Wallet.Balance("gold");
                long wood = originalSession.Wallet.Balance("wood");
                int actors = originalSession.Units.Count;
                shell.EnterGame();
                Assert.AreSame(originalSession, owner.SharedEconomy);
                Assert.AreEqual(gold, owner.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(wood, owner.SharedEconomy.Wallet.Balance("wood"));
                Assert.AreEqual(actors, owner.SharedEconomy.Units.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
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
