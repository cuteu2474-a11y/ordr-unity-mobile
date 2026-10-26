using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Original JASS: CJB (regular money gambling), CJC (high-grade).
    // Purchase costs are Warcraft shop object data, NOT these callbacks.
    // The native sold-unit purchase must be committed by a verified world.
    public enum Original323MoneyGamblingTier { Regular, HighGrade }

    public sealed class Original323MoneyGamblingOutcome
    {
        public string TransactionId { get; private set; }
        public Original323MoneyGamblingTier Tier { get; private set; }
        public int OwnerSlot { get; private set; }
        public bool HighPayout { get; private set; }
        public int GoldReturned { get; private set; }
        public Original323MoneyGamblingOutcome(string id,
            Original323MoneyGamblingTier tier, int ownerSlot,
            bool highPayout, int amount)
        {
            TransactionId = id;
            Tier = tier; OwnerSlot = ownerSlot;
            HighPayout = highPayout; GoldReturned = amount;
        }
    }

    public static class Original323MoneyGamblingRules
    {
        // CJB: BFi(1,100)>25 -> BFi(10,50); else BFi(3,5)
        // CJC: BFi(1,100)>30 -> BFi(500,4500); else BFi(300,400)
        // Both branches ALWAYS consume two BFi draws, in that order.
        public static bool TryGetAmountBounds(Original323MoneyGamblingTier tier,
            int firstRoll, out bool highPayout, out int min, out int max)
        {
            highPayout = false; min = 0; max = 0;
            if (!Enum.IsDefined(typeof(Original323MoneyGamblingTier), tier) ||
                firstRoll < 1 || firstRoll > 100) return false;
            highPayout = firstRoll >
                (tier == Original323MoneyGamblingTier.Regular ? 25 : 30);
            if (tier == Original323MoneyGamblingTier.Regular)
            {
                min = highPayout ? 10 : 3;
                max = highPayout ? 50 : 5;
            }
            else
            {
                min = highPayout ? 500 : 300;
                max = highPayout ? 4500 : 400;
            }
            return true;
        }

        public static bool TryEvaluate(Original323MoneyGamblingTier tier,
            int firstRoll, int secondRoll,
            out bool highPayout, out int amount)
        {
            amount = 0;
            int min, max;
            if (!TryGetAmountBounds(tier, firstRoll,
                out highPayout, out min, out max) ||
                secondRoll < min || secondRoll > max)
                return false;
            amount = secondRoll;
            return true;
        }
    }

    public interface IOriginal323MoneyGamblingPurchaseWitness
    {
        // A real Warcraft shop purchase and sold unit must already be
        // authorized by original object data; no prices are inferred here.
        bool TryConfirmOriginalPurchase(string uniqueTransactionId,
            Original323MoneyGamblingTier tier, out int playerSlot,
            out int firstOriginalRoll, out int secondOriginalRoll,
            out int observedGoldReturn, out string sourceEvidence);
    }

    public interface IOriginal323MoneyGamblingSettlement
    {
        // Atomically validate the original purchase cost, sold-unit removal,
        // shared economy balance and one-time receipt. This layer MUST NOT
        // award gold without that native-world purchase transaction.
        bool TrySettleVerifiedPurchaseOnce(string uniqueTransactionId,
            Original323MoneyGamblingTier tier, int playerSlot,
            int confirmedGoldReturn, string sourceEvidence);
    }

    // Resolves 2 successive shared BFi calls only after their results match
    // original purchase observations. If settlement fails, an immutable
    // receipt is retained so a retry cannot re-roll the same purchased item.
    public sealed class Original323MoneyGamblingService
    {
        private sealed class Receipt
        {
            public Original323MoneyGamblingOutcome Outcome;
            public string Evidence;
        }

        private readonly Original323SharedBfiRandom shared;
        private readonly IOriginal323MoneyGamblingPurchaseWitness witness;
        private readonly IOriginal323MoneyGamblingSettlement settlement;
        private readonly Dictionary<string, Receipt> receipts =
            new Dictionary<string, Receipt>(StringComparer.Ordinal);
        private readonly HashSet<string> settled =
            new HashSet<string>(StringComparer.Ordinal);

        public Original323MoneyGamblingService(Original323SharedBfiRandom source,
            IOriginal323MoneyGamblingPurchaseWitness originalPurchases,
            IOriginal323MoneyGamblingSettlement nativeSettlement)
        {
            shared = source ?? throw new ArgumentNullException(nameof(source));
            witness = originalPurchases ??
                throw new ArgumentNullException(nameof(originalPurchases));
            settlement = nativeSettlement ??
                throw new ArgumentNullException(nameof(nativeSettlement));
        }

        public bool TrySettle(string uniqueTransactionId,
            Original323MoneyGamblingTier tier,
            out Original323MoneyGamblingOutcome outcome)
        {
            outcome = null;
            if (string.IsNullOrWhiteSpace(uniqueTransactionId) ||
                !Enum.IsDefined(typeof(Original323MoneyGamblingTier), tier) ||
                settled.Contains(uniqueTransactionId)) return false;

            Receipt receipt;
            if (!receipts.TryGetValue(uniqueTransactionId, out receipt))
            {
                int player, first, second, observedAmount;
                string evidence;
                if (!witness.TryConfirmOriginalPurchase(uniqueTransactionId,
                        tier, out player, out first, out second,
                        out observedAmount, out evidence) ||
                    player < 0 || player > 3 ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                    return false;

                bool high;
                int amount, low, highBound;
                if (!Original323MoneyGamblingRules.TryEvaluate(
                        tier, first, second, out high, out amount) ||
                    amount != observedAmount ||
                    !Original323MoneyGamblingRules.TryGetAmountBounds(tier,
                        first, out high, out low, out highBound))
                    return false;

                int predictedFirst, intermediate, predictedSecond, finalState;
                if (!shared.TryPreviewNext(1, 100,
                        out predictedFirst, out intermediate) ||
                    predictedFirst != first ||
                    !Original323SharedBfiRandom.TryPreviewFromState(
                        intermediate, low, highBound,
                        out predictedSecond, out finalState) ||
                    predictedSecond != second)
                    return false;

                // Only after BOTH source draws are checked, consume in order
                // from the same map-owned script RNG shared by all systems.
                int actualFirst = shared.NextForPurpose(
                    Original323ScriptRandomPurpose.Gambling, 1, 100);
                int actualSecond = shared.NextForPurpose(
                    Original323ScriptRandomPurpose.Gambling, low, highBound);
                if (actualFirst != first || actualSecond != second)
                    throw new InvalidOperationException(
                        "확인된 도박 두 회 난수와 실제 공통 난수열이 달라졌습니다.");

                receipt = new Receipt {
                    Outcome = new Original323MoneyGamblingOutcome(
                        uniqueTransactionId, tier, player, high, amount),
                    Evidence = evidence
                };
                receipts.Add(uniqueTransactionId, receipt);
            }
            if (receipt.Outcome.Tier != tier) return false;
            if (!settlement.TrySettleVerifiedPurchaseOnce(
                    uniqueTransactionId, tier, receipt.Outcome.OwnerSlot,
                    receipt.Outcome.GoldReturned, receipt.Evidence))
                return false;
            settled.Add(uniqueTransactionId);
            outcome = receipt.Outcome;
            return true;
        }
    }
}
