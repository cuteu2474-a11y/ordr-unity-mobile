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
        private readonly Original323MoneyGamblingTier tier;
        private readonly Func<bool> unlocked;
        private long returned;
        private long purchases;
        private readonly List<string> recentResults = new List<string>();
        public IReadOnlyList<string> RecentResults => recentResults.AsReadOnly();
        public int Price => IsHighGrade ? 500 : GoldCost;
        public int Capacity => IsHighGrade ? 7 : MaximumStock;
        public int RefillSeconds => IsHighGrade ? 9 : RestockSeconds;
        public bool IsHighGrade => tier == Original323MoneyGamblingTier.HighGrade;
        public bool IsUnlocked => unlocked();
        public bool Graduated => wallet.Balance(SourceModelMoneyGamblingDesk.GraduatedResource) > 0;
        public string Title => IsHighGrade ? "고급 돈 도박" : "일반 돈 도박";
        public string OfferKey => IsHighGrade ? SourceModelMoneyGamblingDesk.HighOfferId : OfferId;
        public long NetGold => checked(GoldReturned - PurchaseCount * Price);
        public event Action<Exception> PostCommitObserverFailed;
        public long PurchaseCount => IsHighGrade ? wallet.Balance(SourceModelMoneyGamblingDesk.HighCountResource) : purchases;
        public long GoldReturned => IsHighGrade ? wallet.Balance(SourceModelMoneyGamblingDesk.HighReturnResource) : returned;

        public SourceModelRegularMoneyGambling(EconomyWallet sharedWallet,
            GameSession session, IOriginalWispRandom sharedScriptRandom,
            Func<double> logicalClock, Func<bool> interactionGate,
            Original323MoneyGamblingTier moneyTier = Original323MoneyGamblingTier.Regular,
            Func<bool> unlockGate = null)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            clock = logicalClock ?? throw new ArgumentNullException(nameof(logicalClock));
            canInteract = interactionGate ?? throw new ArgumentNullException(nameof(interactionGate));
            if (!Enum.IsDefined(typeof(Original323MoneyGamblingTier), moneyTier))
                throw new ArgumentOutOfRangeException(nameof(moneyTier));
            tier = moneyTier;
            unlocked = unlockGate ?? (() => !IsHighGrade);
            stock = Capacity;
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
                double cycles = Math.Floor((now - nextRestock.Value) / RefillSeconds) + 1;
                int added = (int)Math.Min(Capacity - stock, cycles);
                stock += added;
                nextRestock = stock == Capacity ? (double?)null : nextRestock.Value + added * RefillSeconds;
            }
            return now;
        }

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling) return Array.Empty<MobileManualOffer>();
            return new[] { new MobileManualOffer(OfferKey, Title,
                (IsHighGrade ? "500골드 · 70%: 500~4500 / 30%: 300~400골드 반환" :
                    "10골드 · 75%: 10~50 / 25%: 3~5골드 반환") +
                " · 재고 " + Stock + "/" + Capacity + " · " + RefillSeconds + "초마다 1회 충전" +
                (Graduated ? " · 돈 도박 졸업" : !IsUnlocked ? " · 아론 보스 처치 후 해금" : "")) };
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (category != ManualEconomyCategory.Gambling || offerId != OfferKey)
                return new ManualActionResult(ManualActionState.Rejected, "실행 가능한 일반 돈 도박을 선택해 주세요.");
            if (processing || game.Phase != SessionPhase.Running || !canInteract())
                return new ManualActionResult(ManualActionState.Rejected, "진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            if (Graduated) return new ManualActionResult(ManualActionState.Rejected, "돈 도박을 졸업했습니다.");
            if (!IsUnlocked) return new ManualActionResult(ManualActionState.Rejected, "아론 보스를 처치하면 고급 돈 도박이 열립니다.");
            double now = RefreshStock();
            if (stock == 0 || wallet.Balance("gold") < Price)
                return new ManualActionResult(ManualActionState.Rejected, stock == 0 ? "도박소 재고 충전을 기다려 주세요." : Price + "골드가 필요합니다.");
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
                Original323MoneyGamblingRules.TryGetAmountBounds(tier,
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
                var rewards = new ResourceBundle().Add("gold", amount);
                if (IsHighGrade)
                {
                    rewards.Add(SourceModelMoneyGamblingDesk.HighReturnResource, amount)
                        .Add(SourceModelMoneyGamblingDesk.HighCountResource, 1);
                    if (nextTotal >= SourceModelMoneyGamblingDesk.GraduationGold)
                        rewards.Add(SourceModelMoneyGamblingDesk.GraduatedResource, 1)
                            .Add("source:native-research:R01M", 1)
                            .Add(SourceModelMoneyGamblingDesk.WoodRightsResource, 5);
                }
                Exception observerFailure = null;
                bool committed;
                try
                {
                    committed = wallet.TryExchangeOnce(receipt,
                        new ResourceBundle().Add("gold", Price), rewards);
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
                purchases = nextCount;
                returned = nextTotal;
                if (stock == Capacity) nextRestock = now + RefillSeconds;
                stock--;
                pendingFirst = pendingSecond = null;
                if (observerFailure != null)
                    try { PostCommitObserverFailed?.Invoke(observerFailure); } catch { }
                string result = Title + " · " + Price + "골드 사용 / " + amount +
                    "골드 반환 (순증감 " + (amount - Price).ToString("+0;-0;0") + ")" +
                    (Graduated ? " · 졸업! 목재 구매 기회 +5" : "");
                recentResults.Insert(0, result);
                if (recentResults.Count > 6) recentResults.RemoveAt(6);
                return new ManualActionResult(ManualActionState.Succeeded, result);
            }
            finally { processing = false; }
        }
    }
}
