using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileFusionProductionAssistantTests
    {
        private static MobileFusionPreview Offer(string id, bool ready)
        {
            return new MobileFusionPreview(id, "원본 제작식", "실제 재료",
                new long[] { 41 }, ready ? new string[0] : new[] { "루피 1기 부족" });
        }

        [Test]
        public void RepeatsOnlyWithFreshMaterialsAndOneSourceCommitPerResult()
        {
            int previews = 0, commits = 0;
            var result = MobileFusionProductionAssistant.CraftRepeated(
                "A012", 3, id => id == "A012",
                () => { previews++; return new[] { Offer("A012", commits < 2) }; },
                offer => { commits++; return new ManualActionResult(
                    ManualActionState.Succeeded, "원본 조합 성공"); });
            Assert.AreEqual(3, previews);
            Assert.AreEqual(2, commits);
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            StringAssert.Contains("2/3", result.Message);
        }

        [Test]
        public void UnapprovedRecipeNeverRunsOrConsumesPreviewMaterial()
        {
            int previews = 0, commits = 0;
            var result = MobileFusionProductionAssistant.CraftRepeated(
                "A19Z", 5, _ => false,
                () => { previews++; return new[] { Offer("A19Z", true) }; },
                _ => { commits++; return new ManualActionResult(
                    ManualActionState.Succeeded, "잘못된 성공"); });
            Assert.AreEqual(ManualActionState.Rejected, result.State);
            Assert.AreEqual(0, previews);
            Assert.AreEqual(0, commits);
        }

        [Test]
        public void FailedOrThrowingCommitStopsWithoutUnknownRetry()
        {
            int commits = 0;
            var rejected = MobileFusionProductionAssistant.CraftRepeated(
                "A012", 5, _ => true, () => new[] { Offer("A012", true) },
                _ => { commits++; return new ManualActionResult(
                    ManualActionState.Rejected, "재료 재검증 실패"); });
            Assert.AreEqual(ManualActionState.Rejected, rejected.State);
            Assert.AreEqual(1, commits);
            var unknown = MobileFusionProductionAssistant.CraftRepeated(
                "A012", 3, _ => true, () => new[] { Offer("A012", true) },
                _ => { commits++; throw new InvalidOperationException("observer"); });
            Assert.AreEqual(ManualActionState.Rejected, unknown.State);
            Assert.AreEqual(2, commits);
            StringAssert.Contains("재시도하지 않음", unknown.Message);
        }

        [Test]
        public void BookmarksHaveStableOrderAndNeverAuthorizeCrafting()
        {
            var favorite = new MobileFusionBookmarks();
            Assert.IsTrue(favorite.Toggle("A012"));
            Assert.IsTrue(favorite.Toggle("A00E"));
            var mixed = new[] { Offer("A00E", false), Offer("ET01", true), Offer("A012", true) };
            var filtered = favorite.Filter(mixed);
            Assert.AreEqual(2, filtered.Count);
            Assert.AreSame(mixed[0], filtered[0]);
            Assert.AreSame(mixed[2], filtered[1]);
            Assert.IsFalse(favorite.Toggle("A012"));
            Assert.AreEqual(1, favorite.Count);
        }

        [Test]
        public void ZeroOrExcessiveRequestsDoNotAttemptTransaction()
        {
            int made = 0;
            foreach (int count in new[] { 0, 6 })
            {
                var result = MobileFusionProductionAssistant.CraftRepeated(
                    "A012", count, _ => true,
                    () => new[] { Offer("A012", true) },
                    _ => { made++; return new ManualActionResult(
                        ManualActionState.Succeeded, "ok"); });
                Assert.AreEqual(ManualActionState.Rejected, result.State);
            }
            Assert.AreEqual(0, made);
        }
    }
}
