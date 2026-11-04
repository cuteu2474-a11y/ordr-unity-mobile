using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Source CWR has CHCT x1 on five Change recipes and IFCT x3 on six
    // Distorted recipes. Their native mutation callbacks are not extracted.
    // Mobile outcome model: successful crafted results consume a LIFETIME
    // attempt slot (not a free token), recorded in the SAME saved wallet.
    // This policy is NOT a claim that the native counter uses the same reset
    // or decrement semantics. Other-world mode has one shared player wallet.
    public static class Original323FusionCountLedger
    {
        public const string ChangeUseId = "fusion:change-created";
        public const string ImmortalUseId = "fusion:distorted-created";

        private static readonly HashSet<string> Changes =
            new HashSet<string>(StringComparer.Ordinal) {
                "A0K2", "A0K3", "A0K4", "A0K6", "A0K7"
            };
        private static readonly HashSet<string> Distorted =
            new HashSet<string>(StringComparer.Ordinal) {
                "IF01", "IF02", "IF03", "IF04", "IF05", "IF06"
            };

        public static bool IsCounterFusion(string ability) =>
            ability != null && (Changes.Contains(ability) || Distorted.Contains(ability));

        public static bool IsApproved(string ability, OriginalJassFusionInstruction condition)
        {
            string id, label;
            return TryResolve(ability, condition, out id, out label);
        }

        public static bool TryResolve(string ability, OriginalJassFusionInstruction condition,
            out string resourceId, out string displayName)
        {
            resourceId = null;
            displayName = null;
            if (condition == null) return false;
            if (Changes.Contains(ability) && condition.Kind == "CHCT" &&
                condition.Rawcode == "CHCT" && condition.Count == 1)
            {
                resourceId = ChangeUseId;
                displayName = "변화 제작 횟수";
                return true;
            }
            if (Distorted.Contains(ability) && condition.Kind == "IFCT" &&
                condition.Rawcode == "IFCT" && condition.Count == 3)
            {
                resourceId = ImmortalUseId;
                displayName = "왜곡 제작 횟수";
                return true;
            }
            return false;
        }

        public static bool CanCreate(EconomyWallet wallet, string ability,
            OriginalJassFusionInstruction condition, out string reason)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            string id, label;
            if (!TryResolve(ability, condition, out id, out label))
            {
                reason = "원본 CHCT/IFCT 승인 목록에 없는 조합입니다.";
                return false;
            }
            long used = wallet.Balance(id);
            bool allowed = used >= 0 && used < condition.Count;
            reason = allowed ? null : label + " 한도 도달 (사용 " +
                used + "/" + condition.Count + ")";
            return allowed;
        }

        // An increment is part of the EXISTING wallet exchange rewards.
        // No starting quota is granted: the balance counts prior successes.
        // The caller MUST stage this alongside real unit consumption and
        // gold/wood in a reversible swap, restoring the wallet on failure.
        public static ResourceBundle BuildUsageReward(string ability,
            OriginalJassFusionRegistration recipe)
        {
            if (recipe == null || recipe.AbilityRawcode != ability)
                throw new FormatException("원본 횟수 조건 등록식 불일치: " + ability);
            var result = new ResourceBundle();
            int approved = 0;
            foreach (var condition in recipe.OrderedConditions)
            {
                string id, label;
                if (TryResolve(ability, condition, out id, out label))
                {
                    result.Add(id, 1);
                    approved++;
                }
                else if (condition.Kind == "CHCT" || condition.Kind == "IFCT")
                    throw new FormatException("원본 횟수 조건 미승인: " + ability);
            }
            if (Changes.Contains(ability) || Distorted.Contains(ability))
            {
                if (approved != 1)
                    throw new FormatException("원본 횟수 조건 중복 또는 누락: " + ability);
                return result;
            }
            if (approved != 0)
                throw new FormatException("다른 조합이 횟수 상태를 수정하려고 합니다: " + ability);
            return null;
        }
    }
}
