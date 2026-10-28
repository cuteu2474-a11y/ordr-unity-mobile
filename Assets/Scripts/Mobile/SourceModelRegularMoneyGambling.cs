using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Actual shared-wallet commerce in the development practice. Source CJB
    // payout/SLK cost and stock parameters; no counterfeit Warcraft witness.
    // Native stock initialisation/timer equivalence still needs device/replay QA.
    public sealed class SourceModelRegularMoneyGambling : IOriginalEconomyPort
    {
        public const string OfferId = "original:h06F:regular-money-gambling";
        public const int GoldCost = 10;
        public const int MaximumStock = 3;
        public const int RestockSeconds = 11;
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom random;
        private readonly Func<double> clock;
        private readonly Func<bool> canInteract;
        private readonly string receiptPrefix;
        private int stock = MaximumStock;
        private double? nextRestock;
        private double lastClock;
        private long sequence;
        private bool processing;
        private int? pendingFirst;
        private int? pendingSecond;
        public event Action<Exception> PostCommitObserverFailed;
        public long PurchaseCount { get; private set; }
        public long GoldReturned { get; private set; }

        public SourceModelRegularMoneyGambling(EconomyWallet sharedWallet,
            GameSession session, IOriginalWispRandom sharedScriptRandom,
            Func<double> logicalClock, Func<bool> interactionGate)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            clock = logicalClock ?? throw new ArgumentNullException(nameof(logicalClock));
            canInteract = interactionGate ?? throw new ArgumentNullException(nameof(interactionGate));
            receiptPrefix = "practice-money:" + Guid.NewGuid().ToString("N") + ":";
        }

        public int Stock { get { RefreshStock(); return stock; } }
        public double SecondsUntilRestock
        {
            get { double now = RefreshStock(); return nextRestock.HasValue ? Math.Max(0, nextRestock.Value - now) : 0; }
        }

        private double RefreshStock()
        {
            double now = clock();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < lastClock)
                throw new InvalidOperationException("도박소 논리 시간이 유효하지 않습니다.");
            lastClock = now;
            if (nextRestock.HasValue && now >= nextRestock.Value)
            {
                double cycles = Math.Floor((now - nextRestock.Value) / RestockSeconds) + 1;
                int added = (int)Math.Min(MaximumStock - stock, cycles);
                stock += added;
                nextRestock = stock == MaximumStock ? (double?)null : nextRestock.Value + added * RestockSeconds;
            }
            return now;
        }

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling) return Array.Empty<MobileManualOffer>();
            return new[] { new MobileManualOffer(OfferId, "일반 돈 도박", "10골드 · 75%: 10~50골드 / 25%: 3~5골드 반환" +
                " · 재고 " + Stock + "/3 · 11초마다 1회 충전 (연습 논리 시간)") };
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (category != ManualEconomyCategory.Gambling || offerId != OfferId)
                return new ManualActionResult(ManualActionState.Rejected, "실행 가능한 일반 돈 도박을 선택해 주세요.");
            if (processing || game.Phase != SessionPhase.Running || !canInteract())
                return new ManualActionResult(ManualActionState.Rejected, "진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            double now = RefreshStock();
            if (stock == 0 || wallet.Balance("gold") < GoldCost)
                return new ManualActionResult(ManualActionState.Rejected, stock == 0 ? "도박소 재고 충전을 기다려 주세요." : "10골드가 필요합니다.");
            processing = true;
            try
            {
                // Retain the first and second draw individually on errors. A
                // failed payout/overflow never buys the player a free reroll.
                if (!pendingFirst.HasValue)
                {
                    int value = random.NextInclusive(1, 100);
                    if (value < 1 || value > 100) throw new InvalidOperationException("도박 분기 난수 범위 오류");
                    pendingFirst = value;
                }
                bool high;
                int minimum, maximum;
                Original323MoneyGamblingRules.TryGetAmountBounds(Original323MoneyGamblingTier.Regular,
                    pendingFirst.Value, out high, out minimum, out maximum);
                if (!pendingSecond.HasValue)
                {
                    int value = random.NextInclusive(minimum, maximum);
                    if (value < minimum || value > maximum) throw new InvalidOperationException("도박 보상 난수 범위 오류");
                    pendingSecond = value;
                }
                int amount = pendingSecond.Value;
                long nextSequence = checked(sequence + 1);
                long nextCount = checked(PurchaseCount + 1);
                long nextTotal = checked(GoldReturned + amount);
                string receipt = receiptPrefix + nextSequence;
                Exception observerFailure = null;
                bool committed;
                try
                {
                    committed = wallet.TryExchangeOnce(receipt,
                        new ResourceBundle().Add("gold", GoldCost), new ResourceBundle().Add("gold", amount));
                }
                catch (Exception error)
                {
                    if (!wallet.HasCompletedTransaction(receipt)) throw;
                    // Granted observers run AFTER the atomic balance/receipt.
                    // Keep stock/count/result successful even if a UI fails.
                    committed = true;
                    observerFailure = error;
                }
                if (!committed)
                    return new ManualActionResult(ManualActionState.Rejected, "현재 잔액에서 거래를 완료할 수 없습니다.");
                sequence = nextSequence;
                PurchaseCount = nextCount;
                GoldReturned = nextTotal;
                if (stock == MaximumStock) nextRestock = now + RestockSeconds;
                stock--;
                pendingFirst = pendingSecond = null;
                if (observerFailure != null)
                    try { PostCommitObserverFailed?.Invoke(observerFailure); } catch { }
                return new ManualActionResult(ManualActionState.Succeeded,
                    "일반 돈 도박 · 10골드 사용 / " + amount + "골드 반환 (순증감 " +
                    (amount - GoldCost).ToString("+0;-0;0") + ")");
            }
            finally { processing = false; }
        }
    }
}
