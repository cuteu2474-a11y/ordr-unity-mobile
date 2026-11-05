using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Pure mobile catalog presentation; NEVER consumes materials, currency,
    // triggers game logic or changes source recipe readiness.
    public static class MobileFusionBrowser
    {
        // Live near-ready catalog: all non-common conditions must already be met.
        // A candidate with only common deficits is visible, but cannot be
        // committed until original source checks pass again on user confirmation.
        public static IReadOnlyList<MobileFusionPreview> SelectNearReadyHighGrade(
            IReadOnlyList<MobileFusionPreview> all, Func<string, bool> canExecute)
        {
            if (all == null) throw new ArgumentNullException(nameof(all));
            if (canExecute == null) throw new ArgumentNullException(nameof(canExecute));
            var nearReady = new List<MobileFusionPreview>();
            foreach (var offer in all)
            {
                if (offer == null)
                    throw new ArgumentException("조합식 목록에 빈 항목이 있습니다.", nameof(all));
                if (!canExecute(offer.RecipeId)) continue;
                if (offer.IsReady || (offer.MissingCommonUnits.HasValue &&
                    offer.MissingCommonUnits.Value > 0))
                    nearReady.Add(offer);
            }
            nearReady.Sort((a, b) =>
            {
                int grade = GradePriority(b.ResultGrade).CompareTo(GradePriority(a.ResultGrade));
                if (grade != 0) return grade;
                // Immediately craftable before missing commons within same grade.
                int missingA = a.IsReady ? 0 : a.MissingCommonUnits.Value;
                int missingB = b.IsReady ? 0 : b.MissingCommonUnits.Value;
                int deficit = missingA.CompareTo(missingB);
                if (deficit != 0) return deficit;
                int name = string.CompareOrdinal(a.Title, b.Title);
                return name != 0 ? name : string.CompareOrdinal(a.RecipeId, b.RecipeId);
            });
            return nearReady.AsReadOnly();
        }

        // UI-only ordering. Special grades are presented above regular grades;
        // original fusion prerequisites, prices and outcomes are unchanged.
        public static int GradePriority(string grade)
        {
            switch (grade)
            {
                case "신비함": return 160;
                case "세라핌": return 150;
                case "불멸의": return 140;
                case "영원한": return 130;
                case "초월함": return 120;
                case "왜곡됨": return 110;
                case "제한됨": return 100;
                case "변화": return 90;
                case "히든": case "해적선": return 80;
                case "전설": return 70;
                case "희귀함": return 60;
                case "특수함": return 55;
                case "특별함": return 50;
                case "안흔함": return 40;
                case "흔함": return 30;
                case "랜덤": return 20;
                default: return 0;
            }
        }
        // Exact UNIT rawcode only; name matching can accidentally include a
        // different character or a recipe whose title merely mentions it.
        // Read-only navigation never grants crafting authority.
        public static IReadOnlyList<MobileFusionPreview> FilterBySourceMaterial(
            IReadOnlyList<MobileFusionPreview> offers,
            OriginalJassFusionCatalog original, string materialRawcode)
        {
            if (offers == null) throw new ArgumentNullException(nameof(offers));
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (string.IsNullOrWhiteSpace(materialRawcode) ||
                materialRawcode.Length != 4)
                throw new ArgumentException("원본 재료 객체 ID는 네 글자여야 합니다.", nameof(materialRawcode));
            var result = new List<MobileFusionPreview>();
            foreach (var offer in offers)
            {
                if (offer == null)
                    throw new ArgumentException("조합식 목록에 빈 항목이 있습니다.", nameof(offers));
                var registration = original.FindByAbility(offer.RecipeId);
                if (registration == null) continue;
                foreach (var input in registration.OrderedConditions)
                    if (input.Kind == "UNIT" && input.Rawcode == materialRawcode)
                    {
                        result.Add(offer);
                        break;
                    }
            }
            return result.AsReadOnly();
        }

        public static IReadOnlyList<MobileFusionPreview> Select(
            IReadOnlyList<MobileFusionPreview> all, string search,
            bool materialsReadyOnly, int requestedPage, int pageSize,
            out int matchingCount, out int pageCount, out int resolvedPage)
        {
            if (all == null) throw new ArgumentNullException(nameof(all));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            string needle = (search ?? "").Trim();
            var matched = new List<MobileFusionPreview>();
            foreach (var recipe in all)
            {
                if (recipe == null)
                    throw new ArgumentException("조합식 목록에 빈 항목이 있습니다.", nameof(all));
                if (materialsReadyOnly && !recipe.HasAllUnitMaterials) continue;
                if (needle.Length > 0 &&
                    !Contains(recipe.Title, needle) &&
                    !Contains(recipe.RecipeId, needle) &&
                    !Contains(recipe.OriginalMaterialsAndCost, needle) &&
                    !ContainsAny(recipe.MissingMaterials, needle))
                    continue;
                matched.Add(recipe);
            }
            matchingCount = matched.Count;
            pageCount = Math.Max(1,
                1 + (Math.Max(1, matchingCount) - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            int start = resolvedPage * pageSize;
            var page = new List<MobileFusionPreview>();
            for (int i = start; i < Math.Min(matched.Count, start + pageSize); i++)
                page.Add(matched[i]);
            return page.AsReadOnly();
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) &&
                haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAny(IReadOnlyList<string> words, string needle)
        {
            foreach (string word in words)
                if (Contains(word, needle)) return true;
            return false;
        }
    }
}
