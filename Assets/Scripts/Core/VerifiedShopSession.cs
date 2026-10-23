using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 원본에서 검증된 상점 항목만 등록한다. 가격이나 구매 제한을 임의로 생성하지 않는다.
    public sealed class VerifiedShopOffer
    {
        public string Id { get; }
        public ResourceBundle Cost { get; }
        public ResourceBundle Reward { get; }
        public int? PurchaseLimit { get; }
        public string Evidence { get; }

        public VerifiedShopOffer(string id, ResourceBundle cost, ResourceBundle reward,
            int? purchaseLimit, string evidence)
        {
            if (string.IsNullOrWhiteSpace(id) || cost == null || reward == null ||
                string.IsNullOrWhiteSpace(evidence) || (purchaseLimit.HasValue && purchaseLimit.Value <= 0))
                throw new ArgumentException("원본 근거가 있는 상점 항목·비용·보상·구매 제한이 필요합니다.");
            bool hasCost = false, hasReward = false;
            foreach (var entry in cost.Entries) hasCost = true;
            foreach (var entry in reward.Entries) hasReward = true;
            if (!hasCost || !hasReward) throw new ArgumentException("빈 상점 거래는 허용되지 않습니다.");
            Id = id; Cost = cost; Reward = reward;
            PurchaseLimit = purchaseLimit; Evidence = evidence;
        }
    }

    // 단일 게임 세션 상점. 구매 횟수는 거래가 성공한 경우에만 증가한다.
    public sealed class VerifiedShopSession
    {
        private readonly EconomyWallet wallet;
        private readonly Dictionary<string, VerifiedShopOffer> offers =
            new Dictionary<string, VerifiedShopOffer>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> purchases =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public VerifiedShopSession(EconomyWallet wallet)
        {
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        public void Register(VerifiedShopOffer offer)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            if (offers.ContainsKey(offer.Id)) throw new InvalidOperationException("상점 항목 ID 중복");
            offers.Add(offer.Id, offer);
        }

        public int Purchased(string id) => purchases.TryGetValue(id, out var count) ? count : 0;

        public bool CanBuy(string id)
        {
            if (!offers.TryGetValue(id, out var offer)) return false;
            if (offer.PurchaseLimit.HasValue && Purchased(id) >= offer.PurchaseLimit.Value)
                return false;
            foreach (var cost in offer.Cost.Entries)
                if (wallet.Balance(cost.Key) < cost.Value) return false;
            return true;
        }

        public bool Buy(string id)
        {
            if (!CanBuy(id)) return false;
            var offer = offers[id];
            int next = checked(Purchased(id) + 1);
            if (!wallet.TryExchange(offer.Cost, offer.Reward)) return false;
            purchases[id] = next;
            return true;
        }
    }
}
