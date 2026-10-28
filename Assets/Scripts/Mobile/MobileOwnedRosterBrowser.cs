using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Read-only roster browser: no unit selection, currency or combat mutation.
    public enum OwnedRosterFilter { All, Saleable, Decomposable }

    public static class MobileOwnedRosterBrowser
    {
        public static IReadOnlyList<MobileUnitSnapshot> Select(
            IReadOnlyList<MobileUnitSnapshot> owned, string query,
            OwnedRosterFilter filter, Func<MobileUnitSnapshot, bool> canSell,
            int requestedPage, int pageSize,
            out int matchingCount, out int pageCount, out int resolvedPage)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            if (canSell == null) throw new ArgumentNullException(nameof(canSell));
            if (!Enum.IsDefined(typeof(OwnedRosterFilter), filter))
                throw new ArgumentOutOfRangeException(nameof(filter));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            string needle = (query ?? "").Trim();
            var matched = new List<MobileUnitSnapshot>();
            foreach (var unit in owned)
            {
                if (unit == null || unit.Unit == null)
                    throw new ArgumentException("보유 목록에 빈 유닛이 있습니다.");
                if (filter == OwnedRosterFilter.Saleable && !canSell(unit)) continue;
                if (filter == OwnedRosterFilter.Decomposable &&
                    !Original323UnitDecompositionService.Supports(unit.Unit.Id)) continue;
                if (needle.Length > 0 &&
                    !Has(unit.Unit.Name, needle) && !Has(unit.Unit.Grade, needle) &&
                    !Has(unit.Unit.Id, needle) && !Has(unit.InstanceId.ToString(), needle))
                    continue;
                matched.Add(unit);
            }
            matchingCount = matched.Count;
            pageCount = Math.Max(1, (matched.Count + pageSize - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            var page = new List<MobileUnitSnapshot>();
            for (int i = resolvedPage * pageSize;
                i < Math.Min(matched.Count, (resolvedPage + 1) * pageSize); i++)
                page.Add(matched[i]);
            return page.AsReadOnly();
        }

        private static bool Has(string source, string needle) =>
            !string.IsNullOrEmpty(source) &&
            source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
