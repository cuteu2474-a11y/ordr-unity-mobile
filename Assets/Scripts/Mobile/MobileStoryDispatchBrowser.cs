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

            var matches = new List<MobileStoryUnitEntry>();
            deployedCount = readyCount = recallableCount = 0;
            foreach (var unit in owned)
            {
                if (unit == null || unit.Unit == null || unit.InstanceId <= 0)
                    throw new ArgumentException("스토리 보유 유닛 정보가 올바르지 않습니다.", nameof(owned));
                long id = unit.InstanceId;
                bool deployed = isDeployed(id);
                bool ready = !deployed && canDispatch(id);
                bool recallable = deployed && canRecall(id);
                if (deployed) deployedCount++;
                if (ready) readyCount++;
                if (recallable) recallableCount++;
                if (filter == MobileStoryRosterFilter.ReadyToDispatch && !ready ||
                    filter == MobileStoryRosterFilter.Deployed && !deployed)
                    continue;
                matches.Add(new MobileStoryUnitEntry(unit, deployed, ready, recallable));
            }
            // Live story actors first; within each lane sort higher grade first.
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
            matchingCount = matches.Count;
            pageCount = Math.Max(1, (matchingCount + pageSize - 1) / pageSize);
            resolvedPage = Math.Max(0, Math.Min(requestedPage, pageCount - 1));
            var page = new List<MobileStoryUnitEntry>();
            for (int i = resolvedPage * pageSize;
                i < Math.Min(matchingCount, (resolvedPage + 1) * pageSize); i++)
                page.Add(matches[i]);
            return page.AsReadOnly();
        }
    }
}
