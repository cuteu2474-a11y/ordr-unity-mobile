using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // User-confirmed convenience actions. Each commit still passes through
    // the original one-at-a-time service and its currency/RNG/mission hooks.
    // This is deliberately NOT an all-or-nothing batch transaction.
    public static class MobileManualBatchActions
    {
        public const int MaximumUnitSales = 10;
        public const int MaximumResourceDraws = 10;

        public static ManualActionResult SellSelected(
            IReadOnlyList<long> selected, Func<long, bool> canSellNow,
            Func<long, ManualActionResult> sellOnce)
        {
            if (selected == null || canSellNow == null || sellOnce == null)
                throw new ArgumentNullException("판매 대상과 원본 거래 서비스가 필요합니다.");
            if (selected.Count < 1 || selected.Count > MaximumUnitSales)
                return Reject("일괄 판매는 1~10기만 직접 선택할 수 있습니다.");

            var unique = new HashSet<long>();
            foreach (long id in selected)
                if (id <= 0 || !unique.Add(id))
                    return Reject("일괄 판매 유닛이 중복되거나 존재하지 않습니다.");

            // Validate the entire selection BEFORE the first irreversible sale.
            foreach (long id in selected)
                if (!canSellNow(id))
                    return Reject("선택한 유닛이 제거·파견됐거나 판매할 수 없습니다. 다시 선택해 주세요.");

            int completed = 0;
            foreach (long id in selected)
            {
                // Every previous sale may have changed the live roster.
                if (!canSellNow(id))
                    return Report(completed, selected.Count,
                        "판매 대상이 변경되어 중단했습니다.");
                ManualActionResult outcome;
                try { outcome = sellOnce(id); }
                catch (Exception error)
                {
                    return Report(completed, selected.Count,
                        "원본 거래 오류로 중단: " + error.Message);
                }
                if (outcome == null || outcome.State != ManualActionState.Succeeded)
                    return Report(completed, selected.Count,
                        outcome == null ? "원본 거래 결과가 없습니다." : outcome.Message);
                completed++;
            }
            return Report(completed, selected.Count, null);
        }

        public static ManualActionResult RepeatExplicitResourceUse(
            int count, Func<ManualActionResult> spendOne)
        {
            if (spendOne == null) throw new ArgumentNullException(nameof(spendOne));
            if (count < 1 || count > MaximumResourceDraws)
                return Reject("랜덤위습 자원 추첨은 1~10회만 직접 지정할 수 있습니다.");
            int completed = 0;
            for (int i = 0; i < count; i++)
            {
                // Each call debits one real Wisp and consumes the next genuine
                // transaction RNG; never skip or replay a failed draw.
                ManualActionResult result;
                try { result = spendOne(); }
                catch (Exception error)
                {
                    return Report(completed, count, "원본 자원 추첨 오류: " + error.Message,
                        "랜덤위습 추첨");
                }
                if (result == null || result.State != ManualActionState.Succeeded)
                    return Report(completed, count,
                        result == null ? "원본 자원 추첨 결과가 없습니다." : result.Message,
                        "랜덤위습 추첨");
                completed++;
            }
            return Report(completed, count, null, "랜덤위습 추첨");
        }

        private static ManualActionResult Report(int completed, int requested,
            string error, string action = "선택 유닛 판매")
        {
            var state = completed > 0 ? ManualActionState.Succeeded : ManualActionState.Rejected;
            return new ManualActionResult(state,
                action + " " + completed + "/" + requested + "회 처리" +
                (completed < requested ? " · 나머지 미실행 · " +
                    (error ?? "거래가 중단됐습니다.") : " · 전부 완료"));
        }

        private static ManualActionResult Reject(string message) =>
            new ManualActionResult(ManualActionState.Rejected, message);
    }
}
