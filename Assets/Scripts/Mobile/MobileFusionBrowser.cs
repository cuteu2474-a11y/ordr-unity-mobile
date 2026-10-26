using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Pure mobile catalog presentation; NEVER consumes materials, currency,
    // triggers game logic or changes source recipe readiness.
    public static class MobileFusionBrowser
    {
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
                if (materialsReadyOnly && !recipe.IsReady) continue;
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
