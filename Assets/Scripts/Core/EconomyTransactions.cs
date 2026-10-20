using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 원작 비용과 보상은 외부 도감 명세로 주입한다. 이 파일에는 임의 가격/확률이 없다.
    public sealed class ResourceBundle
    {
        private readonly Dictionary<string, long> amounts = new Dictionary<string, long>(StringComparer.Ordinal);
        public ResourceBundle Add(string id, long amount)
        {
            if (string.IsNullOrWhiteSpace(id) || amount <= 0) throw new ArgumentException("자원 식별자와 양수 수량이 필요합니다.");
            long existing;
            amounts.TryGetValue(id, out existing);
            amounts[id] = checked(existing + amount);
            return this;
        }
        public IEnumerable<KeyValuePair<string, long>> Entries => amounts;
        public long Get(string id) { long amount; return amounts.TryGetValue(id, out amount) ? amount : 0; }
    }

    public sealed class EconomyWallet
    {
        private readonly Dictionary<string, long> balances = new Dictionary<string, long>(StringComparer.Ordinal);
        public long Balance(string id) { long value; return balances.TryGetValue(id, out value) ? value : 0; }

        public void Grant(ResourceBundle rewards)
        {
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            var next = Snapshot();
            Apply(next, rewards, 1);
            Replace(next);
        }

        public bool TryExchange(ResourceBundle costs, ResourceBundle rewards)
        {
            if (costs == null || rewards == null) throw new ArgumentNullException();
            var next = Snapshot();
            if (!CanAfford(next, costs)) return false;
            Apply(next, costs, -1);
            Apply(next, rewards, 1);
            Replace(next);
            return true;
        }

        private Dictionary<string, long> Snapshot() => new Dictionary<string, long>(balances, StringComparer.Ordinal);
        private static bool CanAfford(Dictionary<string, long> wallet, ResourceBundle cost)
        {
            foreach (var item in cost.Entries)
            {
                long current;
                if (!wallet.TryGetValue(item.Key, out current) || current < item.Value) return false;
            }
            return true;
        }
        private static void Apply(Dictionary<string, long> wallet, ResourceBundle bundle, int sign)
        {
            foreach (var item in bundle.Entries)
            {
                long old;
                wallet.TryGetValue(item.Key, out old);
                long updated = checked(old + checked(item.Value * sign));
                if (updated < 0) throw new InvalidOperationException("잔액이 음수입니다.");
                wallet[item.Key] = updated;
            }
        }
        private void Replace(Dictionary<string, long> next)
        {
            balances.Clear();
            foreach (var item in next) balances.Add(item.Key, item.Value);
        }
    }

    public sealed class CatalogTransaction
    {
        public string Id { get; }
        public ResourceBundle Costs { get; }
        public ResourceBundle Rewards { get; }
        public string SourceUrl { get; }
        public CatalogTransaction(string id, ResourceBundle costs, ResourceBundle rewards, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(id) || costs == null || rewards == null ||
                !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("거래 ID, 비용, 보상 및 출처 URL이 필요합니다.");
            Id = id; Costs = costs; Rewards = rewards; SourceUrl = sourceUrl;
        }
        public bool Execute(EconomyWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            return wallet.TryExchange(Costs, Rewards);
        }
    }

    // 확률/보상표를 모르면 도박 실행을 허용하지 않는다.
    public sealed class GamblingTable
    {
        public string Id { get; }
        public GamblingTable(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("도박 종류가 필요합니다.");
            Id = id;
        }
        public bool IsVerified => false;
        public void Play() => throw new InvalidOperationException("원작 확률과 보상표 검증 전에는 도박을 실행할 수 없습니다.");
    }
}
