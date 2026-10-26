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

    // 잔액과 1회 보상 처리 이력을 같은 저장 슬롯에 보존한다.
    [Serializable]
    public sealed class EconomyBalanceState
    {
        public string Id;
        public long Amount;
    }

    [Serializable]
    public sealed class EconomyWalletState
    {
        public List<EconomyBalanceState> Balances = new List<EconomyBalanceState>();
        public List<string> CompletedRewards = new List<string>();
    }

    public sealed class EconomyWallet
    {
        // Shared session consumers (e.g. automatic Wisp redemption) observe
        // all reward paths, including missions, commerce and direct exchanges.
        public event Action<ResourceBundle> Granted;
        private readonly Dictionary<string, long> balances = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<string> completedRewards =
            new HashSet<string>(StringComparer.Ordinal);
        public long Balance(string id) { long value; return balances.TryGetValue(id, out value) ? value : 0; }

        // 중복 시 false. 오버플로 등 예외 발생 시 잔액/사용 기록 모두 보존.
        public bool TryGrantOnce(string rewardId, ResourceBundle reward)
        {
            return TryGrantOnce(rewardId, reward, new Dictionary<string, long>());
        }

        // Mission rewards can SET an ability level as well as add resources.
        // Stage the entire bundle before committing its durable receipt.
        public bool TryGrantOnce(string rewardId, ResourceBundle reward,
            IReadOnlyDictionary<string, long> absoluteBalances)
        {
            if (string.IsNullOrWhiteSpace(rewardId)) throw new ArgumentException(nameof(rewardId));
            if (reward == null) throw new ArgumentNullException(nameof(reward));
            if (absoluteBalances == null) throw new ArgumentNullException(nameof(absoluteBalances));
            if (completedRewards.Contains(rewardId)) return false;
            var next = Snapshot();
            Apply(next, reward, 1);
            foreach (var entry in absoluteBalances)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0 ||
                    reward.Get(entry.Key) != 0)
                    throw new ArgumentException("동일 자원의 추가·설정 보상을 함께 지정할 수 없습니다.");
                next[entry.Key] = entry.Value;
            }
            Replace(next);
            completedRewards.Add(rewardId);
            NotifyGranted(reward);
            return true;
        }

        // Unity JsonUtility 직렬화 가능한 DTO. 실제 저장 시스템 연동은 별도.
        public EconomyWalletState CaptureState()
        {
            var state = new EconomyWalletState();
            foreach (var pair in balances)
                state.Balances.Add(new EconomyBalanceState { Id = pair.Key, Amount = pair.Value });
            state.Balances.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            state.CompletedRewards.AddRange(completedRewards);
            state.CompletedRewards.Sort(StringComparer.Ordinal);
            return state;
        }

        public void RestoreState(EconomyWalletState state)
        {
            if (state == null || state.Balances == null || state.CompletedRewards == null)
                throw new ArgumentException("재화 저장 상태가 불완전합니다.", nameof(state));
            var next = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in state.Balances)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id) ||
                    entry.Amount < 0 || next.ContainsKey(entry.Id))
                    throw new FormatException("재화 잔액 저장 상태가 잘못됐습니다.");
                next.Add(entry.Id, entry.Amount);
            }
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in state.CompletedRewards)
                if (string.IsNullOrWhiteSpace(key) || !done.Add(key))
                    throw new FormatException("1회 보상 저장 상태가 잘못됐습니다.");
            Replace(next);
            completedRewards.Clear();
            foreach (var key in done) completedRewards.Add(key);
        }

        public void Grant(ResourceBundle rewards)
        {
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            var next = Snapshot();
            Apply(next, rewards, 1);
            Replace(next);
            NotifyGranted(rewards);
        }

        public bool TryExchange(ResourceBundle costs, ResourceBundle rewards)
        {
            if (costs == null || rewards == null) throw new ArgumentNullException();
            var next = Snapshot();
            if (!CanAfford(next, costs)) return false;
            Apply(next, costs, -1);
            Apply(next, rewards, 1);
            Replace(next);
            NotifyGranted(rewards);
            return true;
        }

        private void NotifyGranted(ResourceBundle rewards)
        {
            if (Granted == null) return;
            foreach (var entry in rewards.Entries)
            {
                Granted(rewards);
                return;
            }
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
