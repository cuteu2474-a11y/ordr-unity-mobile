using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // CIK -> CIJ unlocks R009 after an actual o01V boss death.
    // CJC graduation replaces h062 with h08C, R01M and five h0AK stock rights.
    // Shop initial stock/timers remain an explicit development model.
    public sealed class SourceModelMoneyGamblingDesk : IOriginalEconomyPort
    {
        public const string HighOfferId = "original:H0AZ:high-money-gambling";
        public const string WoodOfferId = "original:h0AK:graduate-wood-purchase";
        public const string UnlockResource = "source:native-research:R009";
        public const string GraduatedResource = "source:money-gambling:graduated";
        public const string HighReturnResource = "source:money-gambling:high-gross-return";
        public const string HighCountResource = "source:money-gambling:high-purchases";
        public const string WoodRightsResource = "source:money-gambling:graduate-wood-rights";
        public const int GraduationGold = 35000;
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly Func<bool> gate;
        private readonly string woodPrefix = "graduate-wood:" + Guid.NewGuid().ToString("N") + ":";
        private long woodSequence;
        private bool processing;
        public SourceModelRegularMoneyGambling Regular { get; }
        public SourceModelRegularMoneyGambling High { get; }
        public bool Graduated => wallet.Balance(GraduatedResource) > 0;
        public long WoodRights => wallet.Balance(WoodRightsResource);
        public event Action<Exception> PostCommitObserverFailed;

        public SourceModelMoneyGamblingDesk(EconomyWallet shared, GameSession session,
            IOriginalWispRandom random, Func<double> clock, Func<bool> interaction)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            Regular = new SourceModelRegularMoneyGambling(wallet, game, random, clock, gate);
            High = new SourceModelRegularMoneyGambling(wallet, game, random, clock, gate,
                Original323MoneyGamblingTier.HighGrade, () => wallet.Balance(UnlockResource) > 0);
            Regular.PostCommitObserverFailed += Report;
            High.PostCommitObserverFailed += Report;
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

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling) return Array.Empty<MobileManualOffer>();
            if (Graduated)
                return new[] { new MobileManualOffer(WoodOfferId, "졸업 상점 · 목재 구매",
                    "10000골드 → 목재 1개 · 남은 구매 기회 " + WoodRights + "회") };
            var offers = new List<MobileManualOffer>();
            offers.AddRange(Regular.AvailableOffers(category));
            offers.AddRange(High.AvailableOffers(category));
            return offers.AsReadOnly();
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (processing || category != ManualEconomyCategory.Gambling ||
                game.Phase != SessionPhase.Running || !gate())
                return new ManualActionResult(ManualActionState.Rejected, "진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            processing = true;
            try
            {
                if (offerId == SourceModelRegularMoneyGambling.OfferId)
                    return Regular.Confirm(category, offerId);
                if (offerId == HighOfferId) return High.Confirm(category, offerId);
                if (offerId != WoodOfferId || !Graduated || WoodRights < 1)
                    return new ManualActionResult(ManualActionState.Rejected, "이용 가능한 돈 도박·졸업 상점 항목을 선택해 주세요.");
                long next = checked(woodSequence + 1);
                string receipt = woodPrefix + next;
                bool committed;
                try
                {
                    committed = wallet.TryExchangeOnce(receipt,
                        new ResourceBundle().Add("gold", 10000).Add(WoodRightsResource, 1),
                        new ResourceBundle().Add("wood", 1));
                }
                catch (Exception error)
                {
                    if (!wallet.HasCompletedTransaction(receipt)) throw;
                    committed = true;
                    Report(error);
                }
                if (!committed)
                    return new ManualActionResult(ManualActionState.Rejected, "10000골드와 목재 구매 기회 1회가 필요합니다.");
                woodSequence = next;
                return new ManualActionResult(ManualActionState.Succeeded, "10000골드 사용 · 목재 1개 구매");
            }
            finally { processing = false; }
        }
    }
}
