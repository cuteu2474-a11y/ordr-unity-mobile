using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileFusionGoalTrackerTests
    {
        private static MobileFusionPreview Offer(string id, string title,
            params string[] missing)
        {
            return new MobileFusionPreview(id,title,"원본 재료 목록",
                new long[] { 12 },missing);
        }

        [Test]
        public void GoalRemainsPreviewOnlyEvenWhenMaterialsAreReady()
        {
            var goal=new MobileFusionGoalTracker();
            Assert.IsTrue(goal.Toggle("A012"));
            var state=goal.Resolve(new[] {Offer("A012","루피 기어 세컨드")},
                id=>false);
            Assert.IsFalse(state.CanCraftNow);
            Assert.AreEqual("원본 제작 기능 미연결",state.Status);
            Assert.IsTrue(state.MaterialsReady);
            Assert.IsFalse(state.Approved);
            Assert.IsFalse(goal.Toggle("A012"));
            Assert.IsNull(goal.Resolve(new[] {Offer("A012","루피")},
                id=>true));
        }

        [Test]
        public void MissingMaterialsShowTheFirstThreeAndDoNotInventCrafting()
        {
            var goal=new MobileFusionGoalTracker();
            goal.Toggle("R-01");
            var state=goal.Resolve(new[] {
                Offer("R-01","원본 유닛","루피 1","조로 2","상디 3","목재 4")
            },id=>true);
            Assert.AreEqual("재료 부족",state.Status);
            Assert.IsFalse(state.CanCraftNow);
            StringAssert.Contains("루피 1",state.MissingSummary());
            StringAssert.Contains("외 1종",state.MissingSummary());
            Assert.AreEqual(4,state.MissingMaterials.Count);
        }

        [Test]
        public void LivePreviewRechecksAfterRosterChangesWithoutMutatingIt()
        {
            var goal=new MobileFusionGoalTracker();
            goal.Toggle("A012");
            var before=goal.Resolve(new[] {Offer("A012","루피 진화","루피 3")},
                id=>true);
            var after=goal.Resolve(new[] {Offer("A012","루피 진화")},
                id=>true);
            Assert.IsFalse(before.CanCraftNow);
            Assert.IsTrue(after.CanCraftNow);
            Assert.AreEqual("지금 제작 가능",after.Status);
            Assert.AreEqual("A012",goal.RecipeId);
        }

        [Test]
        public void MissingRecipeIsNotTreatedAsReady()
        {
            var goal=new MobileFusionGoalTracker();
            goal.Toggle("A0ZZ");
            var state=goal.Resolve(new MobileFusionPreview[0],id=>true);
            Assert.IsFalse(state.IsFound);
            Assert.IsFalse(state.CanCraftNow);
            Assert.AreEqual("조합식 확인 필요",state.Status);
            Assert.Throws<ArgumentException>(()=>goal.Toggle(" "));
            Assert.Throws<ArgumentOutOfRangeException>(()=>state.MissingSummary(0));
        }
    }
}
