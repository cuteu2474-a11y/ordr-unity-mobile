using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 위습은 모바일에서 전투 유닛이 아니라 독립 재화다.
    public static class WispResourceIds
    {
        public const string Random = "wisp:e0IX";
        public const string Choice = "wisp:e018";
        public const string Uncommon = "wisp:e017";
        public const string Special = "wisp:e016";
        public const string Rare = "wisp:e019";
        public const string Legendary = "wisp:e015";
        public static bool IsAuto(string id) =>
            id == Uncommon || id == Special || id == Rare || id == Legendary;
    }

    public sealed class WispReward
    {
        public readonly string SourceWispId;
        public readonly string UnitRawcode;
        public readonly string BonusAbilityRawcode;
        public WispReward(string sourceWispId, string unitRawcode,
            string bonusAbilityRawcode = null)
        {
            if (!WispResourceIds.IsAuto(sourceWispId) || string.IsNullOrWhiteSpace(unitRawcode) ||
                unitRawcode.Length != 4)
                throw new ArgumentException("원본 위습 종류와 4자리 보상 유닛 코드 필요");
            SourceWispId = sourceWispId;
            UnitRawcode = unitRawcode;
            BonusAbilityRawcode = bonusAbilityRawcode;
        }
    }

    [Serializable]
    public sealed class PendingWispRewardState
    {
        public string SourceWispId;
        public string UnitRawcode;
        public string BonusAbilityRawcode;
    }

    // 확률 및 풀은 원본에서 검증한 공급자만 제공한다. 임의 테이블은 허용하지 않는다.
    public interface IVerifiedWispRoller
    {
        bool IsVerified(string wispId);
        WispReward Roll(string wispId);
    }

    // The world transaction must roll back both the spawned unit and A0YE
    // when Apply or Commit fails. Dispose rolls back every uncommitted change.
    public interface IWispRewardWorld
    {
        IWispRewardTransaction Begin();
    }

    public interface IWispRewardTransaction : IDisposable
    {
        void Apply(WispReward reward);
        void Commit();
    }

    // Merge is also one transaction, including the selected unit and all
    // original additional requirements. No callback may create a free unit.
    public interface IVerifiedChoiceWispMergeWorld
    {
        // Original-rule approved targets only. No client-invented suggestions.
        IReadOnlyList<VerifiedChoiceWispTarget> AvailableTargets();
        IChoiceWispMergeTransaction Begin(string selectedUnitRawcode);
    }

    public sealed class VerifiedChoiceWispTarget
    {
        public string Rawcode { get; private set; }
        public string Name { get; private set; }
        public string OriginalRequirements { get; private set; }
        public VerifiedChoiceWispTarget(string rawcode, string name, string requirements)
        {
            if (string.IsNullOrWhiteSpace(rawcode) || rawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(requirements))
                throw new ArgumentException("원본 선택 위습 Merge 대상/조건이 필요합니다.");
            Rawcode = rawcode; Name = name; OriginalRequirements = requirements;
        }
    }

    public interface IChoiceWispMergeTransaction : IDisposable
    {
        bool TryApply();
        void Commit();
    }

    public sealed class MobileWispService : IDisposable
    {
        private readonly EconomyWallet wallet;
        private readonly IVerifiedWispRoller roller;
        private readonly IWispRewardWorld world;
        private bool processing;
        private bool disposed;
        private readonly bool deferAutomatic;
        private static readonly string[] AutomaticKinds = { WispResourceIds.Uncommon,
            WispResourceIds.Special, WispResourceIds.Rare, WispResourceIds.Legendary };
        private readonly Dictionary<string, string> failures = new Dictionary<string, string>();
        public string AutomaticFailure(string id) => failures.TryGetValue(id, out var reason) ? reason : null;
        public MobileWispService(EconomyWallet sharedWallet, IVerifiedWispRoller verifiedRoller,
            IWispRewardWorld transactionalWorld, bool deferAutomaticUntilFlush = false)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            roller = verifiedRoller ?? throw new ArgumentNullException(nameof(verifiedRoller));
            world = transactionalWorld ?? throw new ArgumentNullException(nameof(transactionalWorld));
            deferAutomatic = deferAutomaticUntilFlush;
            wallet.Granted += OnWalletGranted;
        }
        public long Balance(string id) => wallet.Balance(id);
        // The UI must not advertise a source-verified pool as playable until
        // its world/ability adapter has explicitly opted into the roller.
        public bool CanAutomaticallyResolve(string id) =>
            WispResourceIds.IsAuto(id) && roller.IsVerified(id);
        public void Dispose() { disposed = true; wallet.Granted -= OnWalletGranted; }
        private void RequireActive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MobileWispService));
        }

        private void OnWalletGranted(ResourceBundle reward)
        {
            // Unity drains only at a safe frame boundary, after the enclosing
            // story/shop/fusion transaction has committed or rolled back.
            if (deferAutomatic || disposed) return;
            foreach (var entry in reward.Entries)
                if (WispResourceIds.IsAuto(entry.Key))
                { FlushAutomatic(); return; }
        }

        // 자동 4종은 지갑 잔액 자체가 미처리 작업 목록이다. 세이브 복원 뒤에도
        // FlushAutomatic()으로 이어서 처리하며 별도 큐와 지갑이 어긋나지 않는다.
        public void Grant(string id, long count = 1)
        {
            RequireActive();
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (!IsKnown(id)) throw new ArgumentException("알 수 없는 위습");
            wallet.Grant(new ResourceBundle().Add(id, count));
        }

        // 이벤트 ID가 있는 상점·임무·유닛 보상은 이 경로로만 지급한다.
        public bool GrantOnce(string sourceEventId, string id, long count = 1)
        {
            RequireActive();
            if (string.IsNullOrWhiteSpace(sourceEventId) || !IsKnown(id) || count <= 0)
                throw new ArgumentException("원본 보상 이벤트·위습 ID·수량이 필요합니다.");
            bool granted = wallet.TryGrantOnce("wisp:" + sourceEventId,
                new ResourceBundle().Add(id, count));
            return granted;
        }

        public int FlushAutomatic()
        {
            RequireActive();
            if (processing) return 0;
            processing = true;
            try
            {
                int applied = 0;
                foreach (string id in AutomaticKinds) applied += FlushTier(id);
                return applied;
            }
            finally { processing = false; }
        }

        // Isolate failures by tier. Failed tiers stay blocked until an explicit
        // retry, avoiding repeated spawn/rollback work every Unity frame.
        public int FlushAutomaticSafely(bool retryFailed = false)
        {
            RequireActive();
            if (processing) return 0;
            processing = true;
            int applied = 0;
            try
            {
                foreach (string id in AutomaticKinds)
                {
                    if (wallet.Balance(id) == 0) { failures.Remove(id); continue; }
                    if (!retryFailed && failures.ContainsKey(id)) continue;
                    try { FlushTier(id, () => applied++); failures.Remove(id); }
                    catch (Exception error) { failures[id] = error.Message; }
                }
                return applied;
            }
            finally { processing = false; }
        }

        private int FlushTier(string id, Action onApplied = null)
        {
            if (!roller.IsVerified(id)) return 0;
            int applied = 0;
            while (wallet.Balance(id) > 0)
            {
                using (var transaction = world.Begin() ??
                    throw new InvalidOperationException("위습 월드 거래를 열지 못했습니다."))
                {
                    var reward = wallet.PendingWispReward(id);
                    if (reward == null)
                    {
                        reward = roller.Roll(id);
                        if (reward == null || reward.SourceWispId != id)
                            throw new InvalidOperationException("검증되지 않은 위습 보상");
                        wallet.RememberWispReward(reward);
                    }
                    transaction.Apply(reward);
                    var before = wallet.CaptureState();
                    try
                    {
                        if (!wallet.TryExchange(new ResourceBundle().Add(id, 1),
                            new ResourceBundle()))
                            throw new InvalidOperationException("위습 소모 실패");
                        transaction.Commit();
                        wallet.CompleteWispReward(id);
                        failures.Remove(id);
                    }
                    catch
                    {
                        wallet.RestoreState(before);
                        throw;
                    }
                }
                applied = checked(applied + 1);
                onApplied?.Invoke();
            }
            return applied;
        }

        // 선택위습은 Merge UI에서만 명시적으로 소비. 검증된 선택 결과만 허용.
        public bool MergeChoice(string selectedUnitRawcode, IVerifiedChoiceWispMergeWorld mergeWorld)
        {
            RequireActive();
            if (string.IsNullOrWhiteSpace(selectedUnitRawcode) ||
                selectedUnitRawcode.Length != 4 || mergeWorld == null)
                throw new ArgumentException("검증된 Merge 선택과 실행기가 필요합니다.");
            if (wallet.Balance(WispResourceIds.Choice) < 1) return false;
            var available = mergeWorld.AvailableTargets();
            bool matched = false;
            if (available != null)
                foreach (var target in available)
                    if (target != null && target.Rawcode == selectedUnitRawcode)
                    { matched = true; break; }
            if (!matched) return false;
            using (var transaction = mergeWorld.Begin(selectedUnitRawcode) ??
                throw new InvalidOperationException("Merge 월드 거래를 열지 못했습니다."))
            {
                if (!transaction.TryApply()) return false;
                var before = wallet.CaptureState();
                try
                {
                    if (!wallet.TryExchange(new ResourceBundle().Add(WispResourceIds.Choice, 1),
                        new ResourceBundle()))
                        throw new InvalidOperationException("Merge 위습 소모 실패");
                    transaction.Commit();
                }
                catch
                {
                    wallet.RestoreState(before);
                    throw;
                }
                return true;
            }
        }

        public static bool IsKnown(string id) => id == WispResourceIds.Random ||
            id == WispResourceIds.Choice || WispResourceIds.IsAuto(id);
    }
}

