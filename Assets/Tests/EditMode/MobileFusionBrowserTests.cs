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
        public void OwnedUnitFilterIncludesLockedRecipesWithoutGrantingCraftPermission()
        {
            var sourceLockedButOwned = new MobileFusionPreview(
                "TR02", "로빈 초월", "원본 항법 제한 · 콜백 미검증",
                new long[] { 91, 92 },
                new[] { "원본 제작 거래 미연결" }, true);
            var sourceLockedAndMissing = new MobileFusionPreview(
                "TR03", "타시기 초월", "원본 항법 제한 · 콜백 미검증",
                new long[] { 93 },
                new[] { "조합 재료 1기 부족", "원본 제작 거래 미연결" }, false);
            int matches, pages, current;
            var filtered = MobileFusionBrowser.Select(
                new[] { sourceLockedButOwned, sourceLockedAndMissing },
                "", true, 0, 8, out matches, out pages, out current);
            Assert.AreEqual(1, matches);
            Assert.AreSame(sourceLockedButOwned, filtered[0]);
            Assert.IsTrue(sourceLockedButOwned.HasAllUnitMaterials);
            Assert.IsFalse(sourceLockedButOwned.IsReady,
                "보유 유닛 충족과 제작 승인 상태는 절대 같은 의미가 아닙니다.");
            Assert.IsFalse(sourceLockedAndMissing.HasAllUnitMaterials);
        }

        [Test]
        public void NearReadyShowsReadyAndCommonOnlyDeficitsSortedByGradeThenMissingCount()
        {
            var low = new MobileFusionPreview("L", "나미", "재료",
                new long[] { 17 }, new string[0], true, "희귀함");
            var high = new MobileFusionPreview("H", "샹크스", "재료",
                new long[] { 18 }, new string[0], true, "영원한");
            var sameGradeReady = new MobileFusionPreview("M0", "루피", "재료",
                new long[] { 19 }, new string[0], true, "전설");
            var sameGradeOne = new MobileFusionPreview("M1", "로빈", "흔함",
                new long[] { 20 }, new[] { "재료 부족: h003 1기" },
                false, "전설", 1);
            var sameGradeThree = new MobileFusionPreview("M3", "조로", "흔함",
                new long[] { 21 }, new[] { "재료 부족: h003 3기" },
                false, "전설", 3);
            var lowNear = new MobileFusionPreview("LN", "상디", "흔함",
                new long[] { 22 }, new[] { "재료 부족: h001 2기" },
                false, "특수함", 2);
            var sourceLocked = new MobileFusionPreview("X", "미승인", "재료",
                new long[] { 23 }, new string[0], true, "신비함");
            var cashBlocked = new MobileFusionPreview("C", "골드 부족", "골드",
                new long[] { 24 }, new[] { "골드 부족" }, true, "불멸의");
            var rareBlocked = new MobileFusionPreview("R", "희귀 재료 부족", "희귀",
                new long[] { 25 }, new[] { "재료 부족: h123 1기" }, false, "초월함");
            var inventory = new[] { low, sameGradeThree, rareBlocked, cashBlocked,
                sourceLocked, lowNear, sameGradeOne, sameGradeReady, high };
            var result = MobileFusionBrowser.SelectNearReadyHighGrade(inventory,
                id => id != "X");

            CollectionAssert.AreEqual(new[] { "H", "M0", "M1", "M3", "L", "LN" },
                new[] { result[0].RecipeId, result[1].RecipeId,
                    result[2].RecipeId, result[3].RecipeId,
                    result[4].RecipeId, result[5].RecipeId });
            Assert.AreEqual(6, result.Count);
            Assert.AreSame(low, inventory[0], "Filtering must not reorder inventory.");
            Assert.IsFalse(cashBlocked.IsReady);
            Assert.IsTrue(sourceLocked.IsReady, "Materials never grant commit authority.");
            Assert.AreEqual(1, sameGradeOne.MissingCommonUnits);
            int matching, pages, resolved;
            var page = MobileFusionBrowser.Select(result, "", false, 0, 2,
                out matching, out pages, out resolved);
            Assert.AreEqual(6, matching);
            Assert.AreEqual(3, pages);
            Assert.AreEqual("H", page[0].RecipeId);
            Assert.AreEqual("M0", page[1].RecipeId);
        }

        [Test]
        public void NearReadyRejectsInvalidSourcesAndKeepsStableSameGradeOrdering()
        {
            var z = new MobileFusionPreview("Z", "조로 · 초월함", "재료",
                new long[] { 18 }, new string[0], true, "초월함");
            var a = new MobileFusionPreview("A", "루피 · 초월함", "재료",
                new long[] { 19 }, new string[0], true, "초월함");
            var ready = MobileFusionBrowser.SelectNearReadyHighGrade(
                new[] { z, a }, id => true);
            Assert.AreEqual("A", ready[0].RecipeId);
            Assert.AreEqual("Z", ready[1].RecipeId);
            Assert.Throws<ArgumentNullException>(() =>
                MobileFusionBrowser.SelectNearReadyHighGrade(null, id => true));
            Assert.Throws<ArgumentNullException>(() =>
                MobileFusionBrowser.SelectNearReadyHighGrade(new[] { a }, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MobileFusionPreview("BAD", "나미", "재료", new long[] { 1 },
                    new string[0], true, "흔함", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MobileFusionPreview("BAD", "나미", "재료", new long[] { 1 },
                    new[] { "재료 부족" }, false, "흔함", 0));
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
