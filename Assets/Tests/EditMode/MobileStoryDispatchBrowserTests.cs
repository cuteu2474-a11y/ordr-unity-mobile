using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileStoryDispatchBrowserTests
    {
        private static MobileUnitSnapshot[] Owned()
        {
            var units = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            return new[] {
                new MobileUnitSnapshot(11, units.Require("C-01"), null,
                    new UnitState(11, "C-01", 0, 0)),
                new MobileUnitSnapshot(12, units.Require("S-02"), null,
                    new UnitState(12, "S-02", 0, 0)),
                new MobileUnitSnapshot(13, units.Require("C-08"), null,
                    new UnitState(13, "C-08", 0, 0))
            };
        }

        [Test]
        public void OnlyRealSourceEligibleUnitsEnterDispatchViews()
        {
            int deployed, ready, recall, count, pages, page;
            var owned = Owned();
            var all = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.All, 0, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(3, count);
            Assert.AreEqual(1, deployed);
            Assert.AreEqual(1, ready);
            Assert.AreEqual(1, recall);
            Assert.AreEqual(11, all[0].Unit.InstanceId);
            Assert.AreEqual(12, all[1].Unit.InstanceId);
            Assert.IsTrue(all[0].CanRecall);
            Assert.IsFalse(all[0].CanDispatch);
            Assert.IsTrue(all[1].CanDispatch);

            var readyOnly = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.ReadyToDispatch, 0, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(1, count);
            Assert.AreEqual(12, readyOnly[0].Unit.InstanceId);
            Assert.AreEqual(3, owned.Length, "읽기 전용 UI가 보유 유닛을 바꾸면 안 됩니다.");

            var deployedOnly = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.Deployed, 9, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(0, page);
            Assert.AreEqual(11, deployedOnly[0].Unit.InstanceId);
        }

        [Test]
        public void StoryRecallBatchPreflightsAndReportsPartialWithoutSkippingOriginalPort()
        {
            int calls = 0;
            var denied = MobileManualBatchActions.RecallStoryUnits(
                new long[] { 11, 12 }, id => id == 11, id =>
                {
                    calls++;
                    return new ManualActionResult(ManualActionState.Succeeded, "복귀");
                });
            Assert.AreEqual(ManualActionState.Rejected, denied.State);
            Assert.AreEqual(0, calls, "먼저 모든 대상의 원본 복귀 조건을 검사해야 합니다.");

            var incomplete = MobileManualBatchActions.RecallStoryUnits(
                new long[] { 11, 12 }, id => true, id =>
                {
                    calls++;
                    return new ManualActionResult(id == 11 ?
                        ManualActionState.Succeeded : ManualActionState.Rejected,
                        id == 11 ? "복귀" : "복귀 불가");
                });
            Assert.AreEqual(ManualActionState.Succeeded, incomplete.State);
            StringAssert.Contains("1/2", incomplete.Message);
            Assert.AreEqual(2, calls);
            Assert.Throws<ArgumentNullException>(() =>
                MobileManualBatchActions.RecallStoryUnits(null, id => true, id =>
                    new ManualActionResult(ManualActionState.Succeeded, "복귀")));
        }

        [Test]
        public void StoryRewardPreviewUsesTheActualBasePayoutWithoutFakeMvp()
        {
            var first = Original323SoloStoryScenario.BaseRewardSummary(1);
            StringAssert.Contains("골드 180", first);
            StringAssert.Contains("선택위습 3", first);
            StringAssert.DoesNotContain("500", first, "MVP 보너스는 기본 보상이 아닙니다.");
            StringAssert.Contains("초월위습 1회",
                Original323SoloStoryScenario.BaseRewardSummary(10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SoloStoryScenario.BaseRewardSummary(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SoloStoryScenario.BaseRewardSummary(15));
        }
    }
}
