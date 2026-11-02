using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{

    // Source catalog identity, not display name, defines one unit group.
    public sealed class MobileOwnedUnitGroup
    {
        private readonly IReadOnlyList<MobileUnitSnapshot> members;
        public string UnitId { get; private set; }
        public string Name { get; private set; }
        public string Grade { get; private set; }
        public int Count { get { return members.Count; } }
        public IReadOnlyList<MobileUnitSnapshot> Members { get { return members; } }

        internal MobileOwnedUnitGroup(List<MobileUnitSnapshot> entries)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("빈 유닛 묶음은 만들 수 없습니다.");
            UnitId = entries[0].Unit.Id;
            Name = entries[0].Unit.Name;
            Grade = entries[0].Unit.Grade;
            members = entries.AsReadOnly();
        }

        public bool Contains(long? id)
        {
            if (!id.HasValue) return false;
            foreach (var item in members)
                if (item.InstanceId == id.Value) return true;
            return false;
        }

        public long NextSelectable(long? current)
        {
            for (int i = 0; i < members.Count; i++)
                if (current.HasValue && members[i].InstanceId == current.Value)
                    return members[(i + 1) % members.Count].InstanceId;
            return members[0].InstanceId;
        }
    }

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


        // Filter real instances before grouping and paginate the groups, not
        // individual units. Sales, decomposition and positions are not mutated.
        public static IReadOnlyList<MobileOwnedUnitGroup> Group(
            IReadOnlyList<MobileUnitSnapshot> owned,
            OwnedRosterFilter filter, Func<MobileUnitSnapshot, bool> canSell,
            int requestedPage, int pageSize,
            out int unitCount, out int groupCount,
            out int pageCount, out int resolvedPage)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            if (canSell == null) throw new ArgumentNullException(nameof(canSell));
            if (!Enum.IsDefined(typeof(OwnedRosterFilter), filter))
                throw new ArgumentOutOfRangeException(nameof(filter));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            var sourceGroups = new List<List<MobileUnitSnapshot>>();
            var byId = new Dictionary<string, List<MobileUnitSnapshot>>(StringComparer.Ordinal);
            unitCount = 0;
            foreach (var entry in owned)
            {
                if (entry == null || entry.Unit == null || string.IsNullOrWhiteSpace(entry.Unit.Id))
                    throw new ArgumentException("보유 유닛 정보가 올바르지 않습니다.");
                if (filter == OwnedRosterFilter.Saleable && !canSell(entry)) continue;
                if (filter == OwnedRosterFilter.Decomposable &&
                    !Original323UnitDecompositionService.Supports(entry.Unit.Id)) continue;
                unitCount++;
                List<MobileUnitSnapshot> members;
                if (!byId.TryGetValue(entry.Unit.Id, out members))
                {
                    members = new List<MobileUnitSnapshot>();
                    byId.Add(entry.Unit.Id, members);
                    sourceGroups.Add(members);
                }
                members.Add(entry);
            }
            groupCount = sourceGroups.Count;
            pageCount = Math.Max(1, (groupCount + pageSize - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            var shown = new List<MobileOwnedUnitGroup>();
            for (int i = resolvedPage * pageSize;
                i < Math.Min(groupCount, (resolvedPage + 1) * pageSize); i++)
                shown.Add(new MobileOwnedUnitGroup(sourceGroups[i]));
            return shown.AsReadOnly();
        }

        private static bool Has(string source, string needle) =>
            !string.IsNullOrEmpty(source) &&
            source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
