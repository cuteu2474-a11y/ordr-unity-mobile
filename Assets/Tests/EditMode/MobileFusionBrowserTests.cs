using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileFusionBrowserTests
    {
        private static MobileFusionPreview Offer(
            string id, string name, string materials, params string[] missing)
        {
            return new MobileFusionPreview(id, name, materials,
                new long[] { 17 }, missing);
        }

        [Test]
        public void PagesCoverEveryOriginalRecipeWithoutRenderingHundredsAtOnce()
        {
            var offers = new List<MobileFusionPreview>();
            for (int i = 0; i < 265; i++)
                offers.Add(Offer("recipe-" + i, "원본 조합 " + i, "원본 재료"));
            int matches, pages, current;
            var first = MobileFusionBrowser.Select(offers, "", false, 0, 8,
                out matches, out pages, out current);
            Assert.AreEqual(265, matches);
            Assert.AreEqual(34, pages);
            Assert.AreEqual(8, first.Count);
            Assert.AreSame(offers[0], first[0]);
            var last = MobileFusionBrowser.Select(offers, "", false, 999, 8,
                out matches, out pages, out current);
            Assert.AreEqual(33, current);
            Assert.AreEqual(1, last.Count);
            Assert.AreSame(offers[264], last[0]);
            Assert.AreEqual(265, offers.Count);
        }

        [Test]
        public void FiltersKoreanNameMaterialsMissingAndCaseInsensitiveIds()
        {
            var offers = new[] {
                Offer("MY-01", "고죠 사토루", "코알라 + 랜덤"),
                Offer("E-01", "에넬", "블루노 + 나미", "우솝 1기 부족"),
                Offer("C-01", "루피", "조로 + 상디")
            };
            int matches, pages, current;
            var byName = MobileFusionBrowser.Select(offers, "  고죠  ",
                false, 0, 8, out matches, out pages, out current);
            Assert.AreEqual(1, matches);
            Assert.AreSame(offers[0], byName[0]);

            var byMaterial = MobileFusionBrowser.Select(offers, "블루노",
                false, 0, 8, out matches, out pages, out current);
            Assert.AreSame(offers[1], byMaterial[0]);

            var byMissing = MobileFusionBrowser.Select(offers, "우솝",
                false, 0, 8, out matches, out pages, out current);
            Assert.AreSame(offers[1], byMissing[0]);

            var byId = MobileFusionBrowser.Select(offers, "my-01",
                false, 0, 8, out matches, out pages, out current);
            Assert.AreSame(offers[0], byId[0]);
        }

        [Test]
        public void ReadyFilterDoesNotInventPermissionOrAlterMissingMaterials()
        {
            var ready = Offer("R1", "재료 충분", "골드·추가 조건 별도 승인");
            var missing = Offer("R2", "미확보", "원본 재료", "상디 1기 부족");
            var offers = new[] { missing, ready };
            int matches, pages, current;
            var filtered = MobileFusionBrowser.Select(offers, "",
                true, 0, 8, out matches, out pages, out current);
            Assert.AreEqual(1, matches);
            Assert.AreSame(ready, filtered[0]);
            Assert.IsFalse(missing.IsReady);
            Assert.AreEqual(1, missing.MissingMaterials.Count);
            Assert.AreEqual(2, offers.Length);
        }

        [Test]
        public void EmptyResultsAndInvalidPageSizesAreHandledSafely()
        {
            var offers = new[] { Offer("R1", "나미", "우솝") };
            int matches, pages, current;
            var empty = MobileFusionBrowser.Select(offers, "없는 캐릭터",
                false, 10, 8, out matches, out pages, out current);
            Assert.AreEqual(0, empty.Count);
            Assert.AreEqual(0, matches);
            Assert.AreEqual(1, pages);
            Assert.AreEqual(0, current);
            Assert.Throws<ArgumentOutOfRangeException>(() => {
                int m, p, c;
                MobileFusionBrowser.Select(offers, "", false, 0, 0,
                    out m, out p, out c);
            });
            Assert.Throws<ArgumentNullException>(() => {
                int m, p, c;
                MobileFusionBrowser.Select(null, "", false, 0, 8,
                    out m, out p, out c);
            });
        }
    }
}
