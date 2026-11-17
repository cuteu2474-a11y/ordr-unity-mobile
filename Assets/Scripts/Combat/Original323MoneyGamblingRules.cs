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


    // Snapshot only the native purchase event and its two already-consumed
    // original BFi ordinals. Never reproduce the shop cost or gold refund
    // while preparing a restored world.
    [Serializable]
    public sealed class Original323MoneyGamblingReceiptState
    {
        public string TransactionId;
        public Original323MoneyGamblingTier Tier;
        public int OwnerSlot;
        public int FirstRoll;
        public int SecondRoll;
        public long FirstBfiOrdinal;
        public long SecondBfiOrdinal;
        public bool HighPayout;
        public int GoldReturned;
        public bool Settled;
        public string SourceEvidence;
    }

    [Serializable]
    public sealed class Original323MoneyGamblingState
    {
        public int Version = 1;
        public List<Original323MoneyGamblingReceiptState> Receipts =
            new List<Original323MoneyGamblingReceiptState>();
    }

    public interface IOriginal323MoneyGamblingRestoreWitness
    {
        // Read-only authority for the NEW private world. Verify that this
        // exact shop purchase and its completed/pending settlement right
        // belong to the saved session at the saved clock. Pending retries
        // require an idempotent native settlement keyed by TransactionId.
        bool TryConfirmRestoredReceipt(string sessionId, double logicalTime,
            Original323MoneyGamblingReceiptState receipt,
            out string sourceEvidence);
    }

    public sealed class Original323MoneyGamblingRestorePorts
    {
        // Created for the reconstructed world, NOT the published scene.
        public IOriginal323MoneyGamblingPurchaseWitness Purchases;
        public IOriginal323MoneyGamblingSettlement Settlement;
        public IOriginal323MoneyGamblingRestoreWitness RestoreWitness;
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
            public int FirstRoll;
            public int SecondRoll;
            public long FirstBfiOrdinal;
            public long SecondBfiOrdinal;
        }

        private readonly Original323SharedBfiRandom shared;
        private readonly IOriginal323MoneyGamblingPurchaseWitness witness;
        private readonly IOriginal323MoneyGamblingSettlement settlement;
        private readonly Dictionary<string, Receipt> receipts =
            new Dictionary<string, Receipt>(StringComparer.Ordinal);
        private readonly HashSet<string> settled =
            new HashSet<string>(StringComparer.Ordinal);
        private bool processing;
        private bool unsafePartialDraw;
        public Original323SharedBfiRandom SharedRandom => shared;

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


        public Original323MoneyGamblingState CaptureState()
        {
            if (processing || unsafePartialDraw)
                throw new NotSupportedException(
                    "원본 CJB/CJC 도박 난수·정산이 진행 중이거나 부분 실패했습니다.");
            var state = new Original323MoneyGamblingState();
            foreach (var pair in receipts)
            {
                var receipt = pair.Value;
                var result = receipt.Outcome;
                state.Receipts.Add(new Original323MoneyGamblingReceiptState {
                    TransactionId = result.TransactionId, Tier = result.Tier,
                    OwnerSlot = result.OwnerSlot,
                    FirstRoll = receipt.FirstRoll,
                    SecondRoll = receipt.SecondRoll,
                    FirstBfiOrdinal = receipt.FirstBfiOrdinal,
                    SecondBfiOrdinal = receipt.SecondBfiOrdinal,
                    HighPayout = result.HighPayout,
                    GoldReturned = result.GoldReturned,
                    Settled = settled.Contains(pair.Key),
                    SourceEvidence = receipt.Evidence
                });
            }
            state.Receipts.Sort((a, b) =>
                a.FirstBfiOrdinal.CompareTo(b.FirstBfiOrdinal));
            ValidateState(state, shared.CaptureCheckpoint());
            return state;
        }

        public static void ValidateState(Original323MoneyGamblingState state,
            Original323SharedBfiCheckpoint sharedBfi)
        {
            Original323SharedBfiRandom.ValidateCheckpoint(sharedBfi);
            if (state == null || state.Version != 1 ||
                state.Receipts == null || state.Receipts.Count > 100000 ||
                state.Receipts.Count > sharedBfi.DrawCount / 2)
                throw new FormatException("원본 CJB/CJC 구매 저장 장부가 잘못됐습니다.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            long lastOrdinal = 0;
            foreach (var entry in state.Receipts)
            {
                bool high;
                int amount;
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.TransactionId) ||
                    entry.TransactionId.Length > 256 ||
                    !unique.Add(entry.TransactionId) ||
                    !Enum.IsDefined(typeof(Original323MoneyGamblingTier), entry.Tier) ||
                    entry.OwnerSlot < 0 || entry.OwnerSlot > 3 ||
                    entry.FirstBfiOrdinal <= lastOrdinal ||
                    entry.SecondBfiOrdinal <= entry.FirstBfiOrdinal ||
                    entry.SecondBfiOrdinal - entry.FirstBfiOrdinal != 1 ||
                    entry.SecondBfiOrdinal > sharedBfi.DrawCount ||
                    !Original323MoneyGamblingRules.TryEvaluate(
                        entry.Tier, entry.FirstRoll, entry.SecondRoll,
                        out high, out amount) ||
                    amount != entry.GoldReturned ||
                    high != entry.HighPayout ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        entry.SourceEvidence))
                    throw new FormatException(
                        "원본 도박 구매 ID·결과·전역 BFi 소비 순번이 불일치합니다.");
                lastOrdinal = entry.SecondBfiOrdinal;
            }
        }

        // No purchase, RNG, purchase cost, payout or item cleanup is executed
        // during restoration. The native source must independently attest
        // both settled transactions and pending retry rights.
        public void RestoreState(Original323MoneyGamblingState saved,
            string sessionId, double logicalTime,
            IOriginal323MoneyGamblingRestoreWitness authority)
        {
            ValidateState(saved, shared.CaptureCheckpoint());
            if (processing || unsafePartialDraw || receipts.Count != 0 ||
                settled.Count != 0 || authority == null ||
                string.IsNullOrWhiteSpace(sessionId) ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                logicalTime < 0)
                throw new NotSupportedException(
                    "새 도박 장부와 같은 세션의 원본 구매 목격자가 필요합니다.");
            // Verify ALL source receipts before mutating the new ledger.
            foreach (var item in saved.Receipts)
            {
                string observed;
                if (!authority.TryConfirmRestoredReceipt(
                        sessionId, logicalTime, item, out observed) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(observed))
                    throw new NotSupportedException(
                        "원본 구매·미정산/완료 영수증을 새 월드에서 확인하지 못했습니다.");
            }
            foreach (var item in saved.Receipts)
            {
                receipts.Add(item.TransactionId, new Receipt {
                    Outcome = new Original323MoneyGamblingOutcome(
                        item.TransactionId, item.Tier, item.OwnerSlot,
                        item.HighPayout, item.GoldReturned),
                    Evidence = item.SourceEvidence,
                    FirstRoll = item.FirstRoll, SecondRoll = item.SecondRoll,
                    FirstBfiOrdinal = item.FirstBfiOrdinal,
                    SecondBfiOrdinal = item.SecondBfiOrdinal
                });
                if (item.Settled) settled.Add(item.TransactionId);
            }
        }

        public bool TrySettle(string uniqueTransactionId,
            Original323MoneyGamblingTier tier,
            out Original323MoneyGamblingOutcome outcome)
        {
            outcome = null;
            if (processing || unsafePartialDraw)
                throw new NotSupportedException(
                    "진행 중이거나 일부만 소모된 원본 BFi 도박 사건은 재시도할 수 없습니다.");
            processing = true;
            try { return TrySettleCore(uniqueTransactionId, tier, out outcome); }
            finally { processing = false; }
        }

        private bool TrySettleCore(string uniqueTransactionId,
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
                long before = shared.DrawCount;
                try
                {
                    int actualFirst = shared.NextForPurpose(
                        Original323ScriptRandomPurpose.Gambling, 1, 100);
                    int actualSecond = shared.NextForPurpose(
                        Original323ScriptRandomPurpose.Gambling, low, highBound);
                    if (actualFirst != first || actualSecond != second)
                        throw new InvalidOperationException(
                            "확인된 도박 두 회 난수와 실제 공통 난수열이 달라졌습니다.");
                }
                catch
                {
                    // Native draw 1 may succeed while draw 2 fails. Without
                    // a complete receipt the source BFi cursor is orphaned:
                    // never publish a misleading restorable checkpoint.
                    if (shared.DrawCount != before) unsafePartialDraw = true;
                    throw;
                }

                receipt = new Receipt {
                    Outcome = new Original323MoneyGamblingOutcome(
                        uniqueTransactionId, tier, player, high, amount),
                    Evidence = evidence,
                    FirstRoll = first, SecondRoll = second,
                    FirstBfiOrdinal = before + 1,
                    SecondBfiOrdinal = shared.DrawCount
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
