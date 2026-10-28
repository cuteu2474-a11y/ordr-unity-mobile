using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // In-memory action receipts only; no new RNG, cost, reward or automatic use.
    public sealed class MobileWispActionJournal
    {
        private readonly int capacity;
        private readonly List<string> history = new List<string>();
        public IReadOnlyList<string> Recent => history.AsReadOnly();

        public MobileWispActionJournal(int maxEntries = 6)
        {
            if (maxEntries < 1 || maxEntries > 50)
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            capacity = maxEntries;
        }

        // One user action causes exactly one call to the original transaction.
        public ManualActionResult ExecuteDirect(string label, Func<ManualActionResult> commit)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("위습 사용 행동이 필요합니다.", nameof(label));
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            var result = commit() ?? new ManualActionResult(
                ManualActionState.Rejected, "원본 거래 결과가 없습니다.");
            var state = result.State == ManualActionState.Succeeded ? "완료" :
                result.State == ManualActionState.NotImplemented ? "미연결" : "중단";
            history.Insert(0, label + " · " + state + " · " + result.Message);
            if (history.Count > capacity) history.RemoveAt(history.Count - 1);
            return result;
        }
    }
}
