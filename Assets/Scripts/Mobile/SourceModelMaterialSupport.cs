using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Original ORDR S2 2.323[C] war3map.j CJU (registered sold unit h0AX):
    // BFi(1,1000)<=10: pirate ship h060 (1%). Otherwise
    // BFi(1,100)<=50: common jY[1..9] (49.5% overall), or
    // uncommon jZ[0..12] (49.5% overall). All three calls share
    // the same script BFi RNG lane and the sold item refunds 30 gold.
    // Original script SHA256: 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d.
    [Serializable]
    public sealed class SourceModelMaterialSupportState
    {
        public int Version = 1;
        public long PurchasedCount;
        public bool HasShipRoll;
        public int ShipRoll;
        public bool HasGradeRoll;
        public int GradeRoll;
        public bool HasUnitRoll;
        public int UnitRoll;
        public string PendingRawcode;
    }

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
        private readonly IOriginal323UnitGamblingWorld world;
        private readonly Func<bool> canInteract;
        private int? pendingShipRoll;
        private int? pendingGradeRoll;
        private int? pendingUnitRoll;
        private string pendingRawcode;
        private bool processing;
        public event Action<Exception> PostCommitObserverFailed;

        public SourceModelMaterialSupport(EconomyWallet shared, GameSession session,
            IOriginalWispRandom originalBfiRandom,
            IOriginal323UnitGamblingWorld roster, Func<bool> interaction)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            scriptRandom = originalBfiRandom ?? throw new ArgumentNullException(nameof(originalBfiRandom));
            world = roster ?? throw new ArgumentNullException(nameof(roster));
            canInteract = interaction ?? throw new ArgumentNullException(nameof(interaction));
        }

        public SourceModelMaterialSupportState CaptureState()
        {
            if (processing) throw new InvalidOperationException("물품 지원 거래 중에는 저장할 수 없습니다.");
            var state = new SourceModelMaterialSupportState {
                PurchasedCount = PurchasedCount,
                HasShipRoll = pendingShipRoll.HasValue,
                ShipRoll = pendingShipRoll.GetValueOrDefault(),
                HasGradeRoll = pendingGradeRoll.HasValue,
                GradeRoll = pendingGradeRoll.GetValueOrDefault(),
                HasUnitRoll = pendingUnitRoll.HasValue,
                UnitRoll = pendingUnitRoll.GetValueOrDefault(),
                PendingRawcode = pendingRawcode
            };
            ValidateState(state);
            return state;
        }

        public static void ValidateState(SourceModelMaterialSupportState state)
        {
            if (state == null || state.Version != 1 ||
                state.PurchasedCount < 0 || state.PurchasedCount > PurchaseLimit ||
                (state.HasShipRoll && (state.ShipRoll < 1 || state.ShipRoll > 1000)) ||
                (!state.HasShipRoll && (state.ShipRoll != 0 || state.HasGradeRoll ||
                    state.HasUnitRoll || state.PendingRawcode != null)) ||
                (state.HasGradeRoll && (state.GradeRoll < 1 || state.GradeRoll > 100)) ||
                (!state.HasGradeRoll && (state.GradeRoll != 0 || state.HasUnitRoll)) ||
                (!state.HasUnitRoll && state.UnitRoll != 0) ||
                (state.PurchasedCount == PurchaseLimit && state.HasShipRoll))
                throw new FormatException("물품 지원 보류 추첨 상태가 올바르지 않습니다.");
            if (!state.HasShipRoll) return;
            if (state.ShipRoll <= 10)
            {
                if (state.HasGradeRoll || state.HasUnitRoll ||
                    (state.PendingRawcode != null && state.PendingRawcode != "h060"))
                    throw new FormatException("물품 지원 해적선 추첨 기록 불일치");
                return;
            }
            if (state.PendingRawcode == "h060" ||
                (state.HasUnitRoll && !state.HasGradeRoll))
                throw new FormatException("물품 지원 유닛 추첨 기록 불일치");
            if (state.HasGradeRoll)
            {
                bool common = state.GradeRoll <= 50;
                var pool = common ? Original323UnitGamblingRules.Common :
                    Original323UnitGamblingRules.Uncommon;
                int minimum = common ? 1 : 0;
                int maximum = common ? 9 : 12;
                if (state.HasUnitRoll && (state.UnitRoll < minimum ||
                    state.UnitRoll > maximum))
                    throw new FormatException("물품 지원 유닛 인덱스 불일치");
                if (state.PendingRawcode != null && (!state.HasUnitRoll ||
                    state.PendingRawcode != pool[state.UnitRoll - minimum]))
                    throw new FormatException("물품 지원 유닛 신원 불일치");
            }
            else if (state.PendingRawcode != null)
                throw new FormatException("등급 없는 유닛 추첨 결과가 남았습니다.");
        }

        public void RestoreState(SourceModelMaterialSupportState state)
        {
            ValidateState(state);
            if (processing || pendingShipRoll.HasValue || pendingGradeRoll.HasValue ||
                pendingUnitRoll.HasValue || pendingRawcode != null ||
                PurchasedCount != state.PurchasedCount)
                throw new InvalidOperationException("물품 지원은 새 거래 서비스와 동일한 지갑에만 복원합니다.");
            pendingShipRoll = state.HasShipRoll ? (int?)state.ShipRoll : null;
            pendingGradeRoll = state.HasGradeRoll ? (int?)state.GradeRoll : null;
            pendingUnitRoll = state.HasUnitRoll ? (int?)state.UnitRoll : null;
            pendingRawcode = state.PendingRawcode;
        }

        public long PurchasedCount => wallet.Balance(PurchaseCountResource);
        public long RemainingPurchases => Math.Max(0L, PurchaseLimit - PurchasedCount);

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling)
                return Array.Empty<MobileManualOffer>();
            return new [] { new MobileManualOffer(OfferId, "물품 지원",
                "100골드 결제 → 30골드 반환 + 해적선 1% / 흔함 49.5% / 안흔함 49.5%" +
                " · 남은 지원 " + RemainingPurchases + "/2회" +
                " · 원본 2.323[C] CJU/h0AX의 BFi 분기") };
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
                // Cache every original CJU BFi call separately. An exception
                // after the first roll cannot cause an unrelated reroll or
                // consume a second global BFi value on retry.
                if (!pendingShipRoll.HasValue)
                {
                    int roll = scriptRandom.NextInclusive(1, 1000);
                    if (roll < 1 || roll > 1000)
                        throw new InvalidOperationException("물품 지원 해적선 추첨 범위 오류");
                    pendingShipRoll = roll;
                }
                if (pendingShipRoll.Value <= 10)
                {
                    pendingRawcode = "h060";
                }
                else if (pendingRawcode == null)
                {
                    if (!pendingGradeRoll.HasValue)
                    {
                        int roll = scriptRandom.NextInclusive(1, 100);
                        if (roll < 1 || roll > 100)
                            throw new InvalidOperationException("물품 지원 등급 추첨 범위 오류");
                        pendingGradeRoll = roll;
                    }
                    bool common = pendingGradeRoll.Value <= 50;
                    var pool = common ? Original323UnitGamblingRules.Common :
                        Original323UnitGamblingRules.Uncommon;
                    int minimum = common ? 1 : 0;
                    int maximum = common ? 9 : 12;
                    if (!pendingUnitRoll.HasValue)
                    {
                        int roll = scriptRandom.NextInclusive(minimum, maximum);
                        if (roll < minimum || roll > maximum)
                            throw new InvalidOperationException("물품 지원 유닛 추첨 범위 오류");
                        pendingUnitRoll = roll;
                    }
                    pendingRawcode = pool[pendingUnitRoll.Value - minimum];
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
                    pendingShipRoll = null;
                    pendingGradeRoll = null;
                    pendingUnitRoll = null;
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
