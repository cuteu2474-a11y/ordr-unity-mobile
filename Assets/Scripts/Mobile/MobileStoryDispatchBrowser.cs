using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Mobile
{
    // Presentation-only view of the real story lane. Eligibility is supplied
    // by the same original CWL/CWM service used by manual transactions.
    public enum MobileStoryRosterFilter { All, ReadyToDispatch, Deployed }

    public sealed class MobileStoryUnitEntry
    {
        public MobileUnitSnapshot Unit { get; private set; }
        public bool IsDeployed { get; private set; }
        public bool CanDispatch { get; private set; }
        public bool CanRecall { get; private set; }

        internal MobileStoryUnitEntry(MobileUnitSnapshot unit,
            bool deployed, bool dispatchable, bool recallable)
        {
            Unit = unit;
            IsDeployed = deployed;
            CanDispatch = dispatchable;
            CanRecall = recallable;
        }
    }

    // Only truly identical, currently actionable entries are grouped.
    // The UI supplies a conservative live-state signature; unknown or
    // enhanced states must be keyed by the unique instance instead.
    public sealed class MobileStoryUnitGroup
    {
        private readonly IReadOnlyList<MobileStoryUnitEntry> members;
        public MobileStoryUnitEntry First => members[0];
        public IReadOnlyList<MobileStoryUnitEntry> Members => members;
        public int Count => members.Count;
        public bool IsDeployed => First.IsDeployed;

        internal MobileStoryUnitGroup(List<MobileStoryUnitEntry> group)
        {
            if (group == null || group.Count == 0)
                throw new ArgumentException("빈 파견 묶음을 만들 수 없습니다.");
            members = group.AsReadOnly();
        }

        // Never choose from an old UI snapshot without checking the actual
        // dispatch service again at the instant the user taps.
        public long? FirstCurrentlyActionable(Func<long, bool> canAct)
        {
            if (canAct == null) throw new ArgumentNullException(nameof(canAct));
            foreach (var member in members)
                if (canAct(member.Unit.InstanceId))
                    return member.Unit.InstanceId;
            return null;
        }
    }

    public static class MobileStoryDispatchBrowser
    {
        public static IReadOnlyList<MobileStoryUnitEntry> Select(
            IReadOnlyList<MobileUnitSnapshot> owned,
            Func<long, bool> isDeployed, Func<long, bool> canDispatch,
            Func<long, bool> canRecall, MobileStoryRosterFilter filter,
            int requestedPage, int pageSize, out int deployedCount,
            out int readyCount, out int recallableCount,
            out int matchingCount, out int pageCount, out int resolvedPage)
        {
            if (owned == null || isDeployed == null || canDispatch == null ||
                canRecall == null)
                throw new ArgumentNullException("스토리 보유 유닛과 원본 파견 서비스가 필요합니다.");
            if (!Enum.IsDefined(typeof(MobileStoryRosterFilter), filter))
                throw new ArgumentOutOfRangeException(nameof(filter));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            var matches = Collect(owned, isDeployed, canDispatch, canRecall,
                filter, out deployedCount, out readyCount, out recallableCount);
            matchingCount = matches.Count;
            pageCount = Math.Max(1, (matchingCount + pageSize - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            var page = new List<MobileStoryUnitEntry>();
            for (int i = resolvedPage * pageSize;
                i < Math.Min(matchingCount, (resolvedPage + 1) * pageSize); i++)
                page.Add(matches[i]);
            return page.AsReadOnly();
        }

        // Pagination counts GROUPS rather than individual rows; no copies
        // are created in the shared unit roster. The state key is mandatory,
        // so a changed buff/cooldown cannot silently merge two active units.
        public static IReadOnlyList<MobileStoryUnitGroup> Group(
            IReadOnlyList<MobileUnitSnapshot> owned,
            Func<long, bool> isDeployed, Func<long, bool> canDispatch,
            Func<long, bool> canRecall, Func<MobileUnitSnapshot, string> stateKey,
            MobileStoryRosterFilter filter, int requestedPage, int pageSize,
            out int deployedCount, out int readyCount, out int recallableCount,
            out int matchingUnits, out int matchingGroups,
            out int pageCount, out int resolvedPage)
        {
            if (stateKey == null) throw new ArgumentNullException(nameof(stateKey));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            var matches = Collect(owned, isDeployed, canDispatch, canRecall,
                filter, out deployedCount, out readyCount, out recallableCount);
            matchingUnits = matches.Count;
            var groups = new List<List<MobileStoryUnitEntry>>();
            var byKey = new Dictionary<string, List<MobileStoryUnitEntry>>(
                StringComparer.Ordinal);
            foreach (var entry in matches)
            {
                string signature = stateKey(entry.Unit);
                if (string.IsNullOrWhiteSpace(signature))
                    throw new ArgumentException("파견 유닛 상태 키가 없습니다.", nameof(stateKey));
                string key = entry.Unit.Unit.Id + "|" +
                    (entry.IsDeployed ? "deployed" : "ready") + "|" +
                    (entry.CanRecall ? "recallable" : "not-recallable") +
                    "|" + signature;
                List<MobileStoryUnitEntry> members;
                if (!byKey.TryGetValue(key, out members))
                {
                    members = new List<MobileStoryUnitEntry>();
                    byKey.Add(key, members);
                    groups.Add(members);
                }
                members.Add(entry);
            }
            matchingGroups = groups.Count;
            pageCount = Math.Max(1, (groups.Count + pageSize - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            var page = new List<MobileStoryUnitGroup>();
            for (int i = resolvedPage * pageSize;
                i < Math.Min(groups.Count, (resolvedPage + 1) * pageSize); i++)
                page.Add(new MobileStoryUnitGroup(groups[i]));
            return page.AsReadOnly();
        }

        private static List<MobileStoryUnitEntry> Collect(
            IReadOnlyList<MobileUnitSnapshot> owned, Func<long, bool> isDeployed,
            Func<long, bool> canDispatch, Func<long, bool> canRecall,
            MobileStoryRosterFilter filter, out int deployedCount,
            out int readyCount, out int recallableCount)
        {
            if (owned == null || isDeployed == null || canDispatch == null ||
                canRecall == null)
                throw new ArgumentNullException("스토리 보유 유닛과 원본 파견 서비스가 필요합니다.");
            if (!Enum.IsDefined(typeof(MobileStoryRosterFilter), filter))
                throw new ArgumentOutOfRangeException(nameof(filter));

            var matches = new List<MobileStoryUnitEntry>();
            deployedCount = readyCount = recallableCount = 0;
            foreach (var unit in owned)
            {
                if (unit == null || unit.Unit == null || unit.InstanceId <= 0)
                    throw new ArgumentException("스토리 보유 유닛 정보가 올바르지 않습니다.",
                        nameof(owned));
                long id = unit.InstanceId;
                bool deployed = isDeployed(id);
                bool ready = !deployed && canDispatch(id);
                bool recallable = deployed && canRecall(id);
                if (deployed) deployedCount++;
                if (ready) readyCount++;
                if (recallable) recallableCount++;
                // "All" means all ACTIONABLE candidates plus already deployed
                // units. A blocked/unknown unit never appears in the picker.
                if ((!deployed && !ready) ||
                    (filter == MobileStoryRosterFilter.ReadyToDispatch && !ready) ||
                    (filter == MobileStoryRosterFilter.Deployed && !deployed))
                    continue;
                matches.Add(new MobileStoryUnitEntry(unit, deployed, ready, recallable));
            }
            matches.Sort((a, b) =>
            {
                int lane = b.IsDeployed.CompareTo(a.IsDeployed);
                if (lane != 0) return lane;
                int grade = MobileFusionBrowser.GradePriority(b.Unit.Unit.Grade)
                    .CompareTo(MobileFusionBrowser.GradePriority(a.Unit.Unit.Grade));
                if (grade != 0) return grade;
                int name = string.Compare(a.Unit.Unit.Name, b.Unit.Unit.Name,
                    StringComparison.Ordinal);
                return name != 0 ? name : a.Unit.InstanceId.CompareTo(b.Unit.InstanceId);
            });
            return matches;
        }
    }
}
