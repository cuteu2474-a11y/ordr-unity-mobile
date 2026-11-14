using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // 100G support: older 11.089 patch notes confirm a 30G refund,
    // one common/uncommon unit and two purchases per game. The 2.323[C]
    // native product and exact grade odds have not been recovered.
    // Keep the provisional 1:1 grade draw isolated for replacement.
    public sealed class SourceModelMaterialSupport : IOriginalEconomyPort
    {
        public const string OfferId = "mobile:material-support:100g";
        public const string PurchaseCountResource = "mobile:material-support:purchased";
        public const int GoldCost = 100;
        public const int GoldRefund = 30;
        public const int PurchaseLimit = 2;

        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom scriptRandom;
        private readonly IOriginalWispRandom nativePoolRandom;
        private readonly IOriginal323UnitGamblingWorld world;
        private readonly Func<bool> canInteract;
        private int? pendingGrade;
        private string pendingRawcode;
        private bool processing;
        public event Action<Exception> PostCommitObserverFailed;

        public SourceModelMaterialSupport(EconomyWallet shared, GameSession session,
            IOriginalWispRandom gradeRandom, IOriginalWispRandom unitRandom,
            IOriginal323UnitGamblingWorld roster, Func<bool> interaction)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            scriptRandom = gradeRandom ?? throw new ArgumentNullException(nameof(gradeRandom));
            nativePoolRandom = unitRandom ?? throw new ArgumentNullException(nameof(unitRandom));
            world = roster ?? throw new ArgumentNullException(nameof(roster));
            canInteract = interaction ?? throw new ArgumentNullException(nameof(interaction));
            if (ReferenceEquals(gradeRandom, unitRandom))
                throw new ArgumentException("등급과 네이티브 유닛 추첨 난수 경로는 분리합니다.");
        }

        public long PurchasedCount => wallet.Balance(PurchaseCountResource);
        public long RemainingPurchases => Math.Max(0L, PurchaseLimit - PurchasedCount);

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling)
                return Array.Empty<MobileManualOffer>();
            return new [] { new MobileManualOffer(OfferId, "물품 지원",
                "100골드 결제 → 30골드 반환 + 흔함/안흔함 유닛 1기" +
                " · 남은 지원 " + RemainingPurchases + "/2회" +
                " · 구버전 11.089 근거, 2.323[C] 확률 미확인(현재 등급별 1:1 추첨)") };
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (category != ManualEconomyCategory.Gambling || offerId != OfferId)
                return Reject("물품 지원 항목을 선택해 주세요.");
            if (processing || game.Phase != SessionPhase.Running || !canInteract())
                return Reject("진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            if (PurchasedCount < 0 || PurchasedCount > PurchaseLimit)
                throw new InvalidOperationException("물품 지원 구매 장부가 올바르지 않습니다.");
            if (RemainingPurchases == 0)
                return Reject("물품 지원 2회를 모두 사용했습니다.");
            if (wallet.Balance("gold") < GoldCost)
                return Reject("물품 지원에는 100골드가 필요합니다.");

            processing = true;
            try
            {
                // Preserve both partial grade and complete unit draws on
                // exceptions: neither a failed spawn nor a declined debit
                // may reroll a purchase for free.
                if (!pendingGrade.HasValue)
                {
                    int grade = scriptRandom.NextInclusive(1, 2);
                    if (grade < 1 || grade > 2)
                        throw new InvalidOperationException("물품 지원 등급 추첨 범위 오류");
                    pendingGrade = grade;
                }
                if (pendingRawcode == null)
                {
                    var pool = pendingGrade.Value == 1 ?
                        Original323UnitGamblingRules.Common :
                        Original323UnitGamblingRules.Uncommon;
                    int index = nativePoolRandom.NextInclusive(0, pool.Count - 1);
                    if (index < 0 || index >= pool.Count)
                        throw new InvalidOperationException("물품 지원 유닛 추첨 범위 오류");
                    pendingRawcode = pool[index];
                }
                string receipt = "mobile:material-support:purchase:" + (PurchasedCount + 1);
                using (var spawn = world.Begin(new [] { pendingRawcode }, null))
                {
                    string unitName = spawn.Stage();
                    bool paid;
                    Exception observerError = null;
                    try
                    {
                        paid = wallet.TryExchangeOnce(receipt,
                            new ResourceBundle().Add("gold", GoldCost),
                            new ResourceBundle().Add("gold", GoldRefund)
                                .Add(PurchaseCountResource, 1));
                    }
                    catch (Exception error)
                    {
                        if (!wallet.HasCompletedTransaction(receipt)) throw;
                        paid = true;
                        observerError = error;
                    }
                    if (!paid) return Reject("물품 지원 결제 실패. 추첨 결과는 유지됩니다.");
                    spawn.Commit();
                    pendingGrade = null;
                    pendingRawcode = null;
                    if (observerError != null)
                        try { PostCommitObserverFailed?.Invoke(observerError); } catch { }
                    return new ManualActionResult(ManualActionState.Succeeded,
                        "물품 지원 완료 · 100골드 사용 / 30골드 반환 · " +
                        unitName + " 획득 · 남은 " + RemainingPurchases + "회");
                }
            }
            finally { processing = false; }
        }

        private static ManualActionResult Reject(string reason) =>
            new ManualActionResult(ManualActionState.Rejected, reason);
    }
}
