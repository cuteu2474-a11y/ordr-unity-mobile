using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Opt-in recipe shortcuts for the current play session. Does not invent
    // recipes or persist a gameplay save. Source transaction has final authority.
    public sealed class MobileFusionBookmarks
    {
        private readonly HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        public int Count => ids.Count;
        public bool Contains(string recipeId) =>
            recipeId != null && ids.Contains(recipeId);

        public bool Toggle(string recipeId)
        {
            if (string.IsNullOrWhiteSpace(recipeId))
                throw new ArgumentException("원본 조합 ID가 필요합니다.", nameof(recipeId));
            if (ids.Remove(recipeId)) return false;
            if (ids.Count >= 12) return false;
            ids.Add(recipeId);
            return true;
        }

        public IReadOnlyList<MobileFusionPreview> Filter(
            IReadOnlyList<MobileFusionPreview> current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            var result = new List<MobileFusionPreview>();
            foreach (var entry in current)
                if (entry != null && Contains(entry.RecipeId)) result.Add(entry);
            return result.AsReadOnly();
        }
    }

    // Multiple separately committed source fusions initiated by ONE explicit
    // confirmation. There is intentionally no all-or-nothing transaction.
    public static class MobileFusionProductionAssistant
    {
        public const int MaximumRepeatCount = 5;

        public static ManualActionResult CraftRepeated(
            string recipeId, int requested,
            Func<string, bool> canCommitNow,
            Func<IReadOnlyList<MobileFusionPreview>> freshPreview,
            Func<MobileFusionPreview, ManualActionResult> confirmOne)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || requested < 1 ||
                requested > MaximumRepeatCount)
                return Reject("원본 조합식과 1~5회 제작 횟수를 선택해 주세요.");
            if (canCommitNow == null || freshPreview == null || confirmOne == null)
                throw new ArgumentNullException("원본 제작 권한/재료/거래 서비스가 필요합니다.");

            int made = 0;
            for (int step = 0; step < requested; step++)
            {
                MobileFusionPreview fresh = null;
                try
                {
                    // The previous fusion has already consumed actor IDs,
                    // material units and possibly wood. Never reuse its preview.
                    if (!canCommitNow(recipeId))
                        return Report(made, requested, "원본 제작 허가가 없습니다.");
                    var all = freshPreview();
                    if (all == null)
                        return Report(made, requested, "현재 보유 재료를 확인하지 못했습니다.");
                    foreach (var offer in all)
                        if (offer != null &&
                            string.Equals(offer.RecipeId, recipeId, StringComparison.Ordinal))
                        {
                            fresh = offer;
                            break;
                        }
                    if (fresh == null || !fresh.IsReady)
                        return Report(made, requested,
                            "재료·비용 부족 또는 제작 조건 미충족");
                    // The domain fusion port checks ownership, cost and
                    // special requirements again while actually committing.
                    var result = confirmOne(fresh);
                    if (result == null || result.State != ManualActionState.Succeeded)
                        return Report(made, requested,
                            result == null ? "원본 거래 결과 미확인" : result.Message);
                    made++;
                }
                catch (Exception error)
                {
                    // A post-commit observer may throw. Never retry because a
                    // transaction could have committed before the exception.
                    return Report(made, requested,
                        "거래 상태 확인 필요 · 재시도하지 않음: " + error.Message);
                }
            }
            return Report(made, requested, null);
        }

        private static ManualActionResult Report(int made, int requested, string reason)
        {
            return new ManualActionResult(made == 0 ?
                ManualActionState.Rejected : ManualActionState.Succeeded,
                "동일 조합 " + made + "/" + requested + "회 완료" +
                (made < requested ? " · 나머지 미실행 · " + reason : ""));
        }
        private static ManualActionResult Reject(string explanation) =>
            new ManualActionResult(ManualActionState.Rejected, explanation);
    }
}
