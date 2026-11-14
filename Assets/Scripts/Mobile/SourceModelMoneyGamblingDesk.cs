using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // CIK/CIJ unlock R009 from the real o01V boss death; CJC graduates
    // to h08C. Native h0AL/CI7 is 15000 gold -> one shared trait point.
    // Native H0B0/CJF is 5000 gold -> one random rare/special source actor,
    // with R01N (story stage five) and a one-use research entitlement.
    // The original SLK sets both graduated shelves to 1 / 3600 seconds.
    public sealed class SourceModelMoneyGamblingDesk : IOriginalEconomyPort
    {
        public const string HighOfferId = "original:H0AZ:high-money-gambling";
        public const string WoodOfferId = "original:h0AK:graduate-wood-purchase";
        public const string TraitOfferId = "original:h0AL:graduate-trait-point";
        public const string RareOfferId = "original:H0B0:graduate-rare-unit";
        public const string UnlockResource = "source:native-research:R009";
        public const string GraduatedResource = "source:money-gambling:graduated";
        public const string HighReturnResource = "source:money-gambling:high-gross-return";
        public const string HighCountResource = "source:money-gambling:high-purchases";
        public const string WoodRightsResource = "source:money-gambling:graduate-wood-rights";
        // BhL AI13 adds two native h08C/h0AK shop stocks even if acquired
        // before CJC graduation. Count spent extras separately; the actual
        // item receipt itself is permanent and must never be minted again.
        public const string Ai13WoodStockSpentResource =
            "source:money-gambling:AI13-wood-stock-spent";
        public const string RarePurchasedResource = "source:money-gambling:H0B0:claimed";
        public const int GraduationGold = 35000;
        public const int TraitGoldCost = 15000;
        public const int RareGoldCost = 5000;
        public const int GraduatedStockCapacity = 1;
        public const int GraduatedStockRefillSeconds = 3600;

        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly Func<bool> gate;
        private readonly Func<double> clock;
        private readonly string receiptPrefix =
            "graduated-shop:" + Guid.NewGuid().ToString("N") + ":";
        private long sequence;
        private bool processing;
        private int traitStock = GraduatedStockCapacity;
        private int rareStock = GraduatedStockCapacity;
        private double? traitRestock;
        private double? rareRestock;
        private double lastClock;
        private IOriginal323GraduatedRareShopWorld rareWorld;
        private Func<bool> rareResearchUnlocked;

        public SourceModelRegularMoneyGambling Regular { get; }
        public SourceModelRegularMoneyGambling High { get; }
        public SourceModelOtherworldUnitGambling Otherworld { get; private set; }
        public SourceModelUnitGambling UnitTiers { get; private set; }
        public SourceModelMaterialSupport MaterialSupport { get; private set; }
        public void ConfigureMaterialSupport(SourceModelMaterialSupport service)
        {
            if (MaterialSupport != null) throw new InvalidOperationException("물품 지원 중복 연결");
            MaterialSupport = service ?? throw new ArgumentNullException(nameof(service));
        }
        public void ConfigureUnitTiers(SourceModelUnitGambling service)
        {
            if (UnitTiers != null) throw new InvalidOperationException("유닛 도박 중복 연결");
            UnitTiers = service ?? throw new ArgumentNullException(nameof(service));
        }
        public bool Graduated => wallet.Balance(GraduatedResource) > 0;
        public long WoodRights
        {
            get
            {
                long extra = Original323SourceItemPassiveRules
                    .ExtraGraduatedWoodStock(wallet);
                long spentExtra = wallet.Balance(Ai13WoodStockSpentResource);
                if (spentExtra < 0 || spentExtra > extra)
                    throw new InvalidOperationException(
                        "AI13 목재 구매 추가 재고 장부가 일치하지 않습니다.");
                return checked(wallet.Balance(WoodRightsResource) + extra - spentExtra);
            }
        }
        public bool HasBoughtRare => wallet.Balance(RarePurchasedResource) > 0;
        public int TraitStock { get { RefreshGraduatedStock(); return traitStock; } }
        public int RareStock { get { RefreshGraduatedStock(); return rareStock; } }
        public bool RareResearchUnlocked => rareResearchUnlocked != null && rareResearchUnlocked();
        public event Action<Exception> PostCommitObserverFailed;

        public SourceModelMoneyGamblingDesk(EconomyWallet shared, GameSession session,
            IOriginalWispRandom random, Func<double> logicalClock, Func<bool> interaction)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            clock = logicalClock ?? throw new ArgumentNullException(nameof(logicalClock));
            Regular = new SourceModelRegularMoneyGambling(wallet, game, random, clock, gate);
            High = new SourceModelRegularMoneyGambling(wallet, game, random, clock, gate,
                Original323MoneyGamblingTier.HighGrade, () => wallet.Balance(UnlockResource) > 0);
            Regular.PostCommitObserverFailed += Report;
            High.PostCommitObserverFailed += Report;
        }

        // Connected after the original 50 source identities have been validated.
        public void ConfigureOtherworld(SourceModelOtherworldUnitGambling service)
        {
            if (Otherworld != null) throw new InvalidOperationException("이세계 도박 중복 연결");
            Otherworld = service ?? throw new ArgumentNullException(nameof(service));
        }

        // Connected after the original 50 source identities have been validated.
        // Without a combat roster world the H0B0 purchase is never charged.
        public void ConfigureRareReward(IOriginal323GraduatedRareShopWorld source,
            Func<bool> hasStoryStageFiveResearch)
        {
            if (rareWorld != null || rareResearchUnlocked != null)
                throw new InvalidOperationException("졸업 상점 희귀 유닛 원본 월드 중복 연결");
            rareWorld = source ?? throw new ArgumentNullException(nameof(source));
            rareResearchUnlocked = hasStoryStageFiveResearch ??
                throw new ArgumentNullException(nameof(hasStoryStageFiveResearch));
        }

        private void Report(Exception error)
        {
            try { PostCommitObserverFailed?.Invoke(error); } catch { }
        }

        internal void RecordDefeatedSourceBoss(string instanceId, string originalRawcode)
        {
            if (originalRawcode != "o01V" || string.IsNullOrWhiteSpace(instanceId) ||
                game.Phase != SessionPhase.Running || wallet.Balance(UnlockResource) > 0) return;
            string receipt = "source:money-gambling:CIK:CIJ:R009";
            try { wallet.TryGrantOnce(receipt, new ResourceBundle().Add(UnlockResource, 1)); }
            catch (Exception error)
            {
                if (!wallet.HasCompletedTransaction(receipt)) throw;
                Report(error);
            }
        }

        private static void Refill(ref int stock, ref double? next, double now)
        {
            if (!next.HasValue || now < next.Value) return;
            double cycles = Math.Floor((now - next.Value) / GraduatedStockRefillSeconds) + 1;
            stock = Math.Min(GraduatedStockCapacity, stock + (int)Math.Min(
                GraduatedStockCapacity - stock, cycles));
            next = stock == GraduatedStockCapacity
                ? (double?)null : next.Value + cycles * GraduatedStockRefillSeconds;
        }

        private double RefreshGraduatedStock()
        {
            double now = clock();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < lastClock)
                throw new InvalidOperationException("졸업 상점 논리 시간 불일치");
            lastClock = now;
            Refill(ref traitStock, ref traitRestock, now);
            Refill(ref rareStock, ref rareRestock, now);
            return now;
        }

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling) return Array.Empty<MobileManualOffer>();
            if (Graduated)
            {
                RefreshGraduatedStock();
                var graduatedOffers = new List<MobileManualOffer> {
                    new MobileManualOffer(WoodOfferId, "졸업 상점 · 목재 구매",
                        "10000골드 → 목재 1개 · 남은 구매 기회 " + WoodRights + "회"),
                    new MobileManualOffer(TraitOfferId, "졸업 상점 · 특성 포인트",
                        "15000골드 → 특성포인트 1개 · 재고 " + traitStock +
                        "/1 · 3600초마다 1회 충전"),
                    new MobileManualOffer(RareOfferId, "졸업 상점 · 고급 유닛",
                        "5000골드 → 무작위 희귀 99% / 특수 1% · 단 1회" +
                        (HasBoughtRare ? " · 구매 완료" :
                            !RareResearchUnlocked ? " · 스토리 5단계 완료 후 해금" :
                            rareWorld == null ? " · 소환 월드 미연결" :
                            " · 재고 " + rareStock + "/1"))
                };
                if (MaterialSupport != null) graduatedOffers.AddRange(MaterialSupport.AvailableOffers(category));
                if (Otherworld != null) graduatedOffers.AddRange(Otherworld.AvailableOffers(category));
                if (UnitTiers != null) graduatedOffers.AddRange(UnitTiers.AvailableOffers(category));
                return graduatedOffers.AsReadOnly();
            }
            var offers = new List<MobileManualOffer>();
            offers.AddRange(Regular.AvailableOffers(category));
            offers.AddRange(High.AvailableOffers(category));
            if (MaterialSupport != null) offers.AddRange(MaterialSupport.AvailableOffers(category));
            if (Otherworld != null) offers.AddRange(Otherworld.AvailableOffers(category));
            if (UnitTiers != null) offers.AddRange(UnitTiers.AvailableOffers(category));
            return offers.AsReadOnly();
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (processing || category != ManualEconomyCategory.Gambling ||
                game.Phase != SessionPhase.Running || !gate())
                return new ManualActionResult(ManualActionState.Rejected,
                    "진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            processing = true;
            try
            {
                if (offerId == SourceModelRegularMoneyGambling.OfferId)
                    return Regular.Confirm(category, offerId);
                if (offerId == HighOfferId) return High.Confirm(category, offerId);
                if (MaterialSupport != null && offerId == SourceModelMaterialSupport.OfferId)
                    return MaterialSupport.Confirm(category, offerId);
                if (offerId == SourceModelOtherworldUnitGambling.OfferId && Otherworld != null)
                    return Otherworld.Confirm(category, offerId);
                if (UnitTiers != null && (offerId == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Low) ||
                    offerId == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Middle) ||
                    offerId == Original323UnitGamblingRules.OfferId(OriginalGambleTier.High)))
                    return UnitTiers.Confirm(category, offerId);
                if (!Graduated)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "돈 도박 졸업 후 이용할 수 있는 항목입니다.");
                if (offerId == WoodOfferId) return BuyWood();
                if (offerId == TraitOfferId) return BuyTraitPoint();
                if (offerId == RareOfferId) return BuyRareUnit();
                return new ManualActionResult(ManualActionState.Rejected,
                    "이용 가능한 돈 도박·졸업 상점 항목을 선택해 주세요.");
            }
            finally { processing = false; }
        }

        private bool TryWalletExchange(string receipt, ResourceBundle costs, ResourceBundle rewards)
        {
            try { return wallet.TryExchangeOnce(receipt, costs, rewards); }
            catch (Exception error)
            {
                if (!wallet.HasCompletedTransaction(receipt)) throw;
                Report(error);
                return true;
            }
        }

        private ManualActionResult BuyWood()
        {
            if (WoodRights < 1 || wallet.Balance("gold") < 10000)
                return new ManualActionResult(ManualActionState.Rejected,
                    "10000골드와 목재 구매 기회 1회가 필요합니다.");
            long next = checked(sequence + 1);
            // Existing five CJC rights are consumed first. AI13's two
            // native i5 stock units remain spendable after the regular
            // rights are exhausted, with their own durable spend receipt.
            bool spendBase = wallet.Balance(WoodRightsResource) > 0;
            var cost = new ResourceBundle().Add("gold", 10000);
            var gain = new ResourceBundle().Add("wood", 1);
            if (spendBase) cost.Add(WoodRightsResource, 1);
            else gain.Add(Ai13WoodStockSpentResource, 1);
            if (!TryWalletExchange(receiptPrefix + next, cost, gain))
                return new ManualActionResult(ManualActionState.Rejected, "목재 구매 거래 실패");
            sequence = next;
            return new ManualActionResult(ManualActionState.Succeeded,
                "10000골드 사용 · 목재 1개 구매");
        }

        private ManualActionResult BuyTraitPoint()
        {
            double now = RefreshGraduatedStock();
            if (traitStock < 1 || wallet.Balance("gold") < TraitGoldCost)
                return new ManualActionResult(ManualActionState.Rejected,
                    traitStock < 1 ? "특성포인트 구매 재고 충전을 기다려 주세요." :
                        "특성포인트 구매에 15000골드가 필요합니다.");
            long next = checked(sequence + 1);
            if (!TryWalletExchange(receiptPrefix + next,
                new ResourceBundle().Add("gold", TraitGoldCost),
                new ResourceBundle().Add(OriginalMissionWalletRewards.TraitPointsId, 1)))
                return new ManualActionResult(ManualActionState.Rejected, "특성포인트 거래 실패");
            sequence = next;
            traitStock--;
            if (!traitRestock.HasValue) traitRestock = now + GraduatedStockRefillSeconds;
            return new ManualActionResult(ManualActionState.Succeeded,
                "15000골드 사용 · 특성포인트 1개 획득");
        }

        private ManualActionResult BuyRareUnit()
        {
            double now = RefreshGraduatedStock();
            if (HasBoughtRare)
                return new ManualActionResult(ManualActionState.Rejected,
                    "고급 유닛 생성은 이미 1회 이용했습니다.");
            if (!RareResearchUnlocked)
                return new ManualActionResult(ManualActionState.Rejected,
                    "스토리 5단계 연구(R01N) 해금 후 구매할 수 있습니다.");
            if (rareWorld == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "고급 유닛 원본 추첨·생성 월드가 연결되지 않았습니다.");
            if (rareStock < 1 || wallet.Balance("gold") < RareGoldCost)
                return new ManualActionResult(ManualActionState.Rejected,
                    rareStock < 1 ? "고급 유닛 재고 충전을 기다려 주세요." :
                        "고급 유닛 구매에 5000골드가 필요합니다.");
            long next = checked(sequence + 1);
            string receipt = receiptPrefix + next;
            using (IOriginal323GraduatedRareShopTransaction spawn = rareWorld.Begin())
            {
                long actorId;
                string rawcode, unitName;
                if (!spawn.TrySpawn(out actorId, out rawcode, out unitName) || actorId <= 0 ||
                    string.IsNullOrEmpty(rawcode) || string.IsNullOrEmpty(unitName))
                    return new ManualActionResult(ManualActionState.Rejected, "고급 유닛 소환 실패");
                if (!TryWalletExchange(receipt,
                    new ResourceBundle().Add("gold", RareGoldCost),
                    new ResourceBundle().Add(RarePurchasedResource, 1)))
                    return new ManualActionResult(ManualActionState.Rejected, "고급 유닛 거래 실패");
                spawn.Commit();
                sequence = next;
                rareStock--;
                if (!rareRestock.HasValue) rareRestock = now + GraduatedStockRefillSeconds;
                return new ManualActionResult(ManualActionState.Succeeded,
                    "5000골드 사용 · " + unitName + " 소환 완료");
            }
        }
    }
}
