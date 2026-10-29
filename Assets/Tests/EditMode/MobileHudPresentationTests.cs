using System;
using NUnit.Framework;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileHudPresentationTests
    {
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
            // Header leaves at least 48 logical pixels for the pause button
            // after its vertical inset, including on high-density displays.
            Assert.GreaterOrEqual(layout.HeaderHeight / scale - 8f, 48f - .01f);
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
        public void InvalidSlotDimensionsFailBeforeRendering()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                MobileMapSkillLayout.StatusChipRects(new Rect(0,0,0,30)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                MobileMapSkillLayout.StatusChipRects(new Rect(0,0,200,0)));
        }
    }
}
