using System;
using System.Collections.Generic;
using System.Text;

namespace Ordr.Mobile.Core
{
    // A small, local-only support log. It never records account information,
    // sends telemetry, or changes the deterministic combat clock.
    public sealed class OfflineSessionDiagnostics
    {
        private const int Capacity = 64;
        private readonly Queue<string> records = new Queue<string>();

        public int Count { get { return records.Count; } }

        public void Record(int wave, double elapsed, string kind, string message)
        {
            if (wave < 0 || double.IsNaN(elapsed) || double.IsInfinity(elapsed) ||
                elapsed < 0 || string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("진행 기록의 라운드·시간·분류가 올바르지 않습니다.");
            if (records.Count == Capacity) records.Dequeue();
            string safeMessage = (message ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (safeMessage.Length > 400) safeMessage = safeMessage.Substring(0, 400);
            records.Enqueue("라운드 " + wave + " / 게임시간 " +
                elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "초 / " + kind + " / " + safeMessage);
        }

        public string Export()
        {
            var builder = new StringBuilder();
            builder.AppendLine("ORDR S2 2.323[C] / nightmare-otherworld / offline");
            foreach (string record in records) builder.AppendLine(record);
            return builder.ToString();
        }
    }
}
