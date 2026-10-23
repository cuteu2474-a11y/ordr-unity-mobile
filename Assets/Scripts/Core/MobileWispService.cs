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
        public readonly string UnitRawcode;
        public readonly string BonusAbilityRawcode;
        public WispReward(string unitRawcode, string bonusAbilityRawcode = null)
        {
            if (string.IsNullOrWhiteSpace(unitRawcode)) throw new ArgumentException(nameof(unitRawcode));
            UnitRawcode = unitRawcode;
            BonusAbilityRawcode = bonusAbilityRawcode;
        }
    }

    // 확률 및 풀은 원본에서 검증한 공급자만 제공한다. 임의 테이블은 허용하지 않는다.
    public interface IVerifiedWispRoller
    {
        bool IsVerified(string wispId);
        WispReward Roll(string wispId);
    }

    // 원본 유닛 생성과 추가 능력 부여를 하나의 롤백 가능한 작업으로 구현한다.
    public interface IWispRewardWorld
    {
        void Apply(WispReward reward);
    }

    public sealed class MobileWispService
    {
        private readonly EconomyWallet wallet;
        private readonly IVerifiedWispRoller roller;
        private readonly IWispRewardWorld world;
        private readonly Queue<string> pending = new Queue<string>();
        private bool processing;
        public MobileWispService(EconomyWallet sharedWallet, IVerifiedWispRoller verifiedRoller,
            IWispRewardWorld transactionalWorld)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            roller = verifiedRoller ?? throw new ArgumentNullException(nameof(verifiedRoller));
            world = transactionalWorld ?? throw new ArgumentNullException(nameof(transactionalWorld));
        }
        public long Balance(string id) => wallet.Balance(id);

        // 획득 시 자동 4종은 추첨 후 소모. 실패하면 해당 위습은 지갑에 남아 재시도 가능.
        // 원본 유닛 월드 트랜잭션이 성공해야만 위습 차감이 확정된다.
        public void Grant(string id, long count = 1)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (!IsKnown(id)) throw new ArgumentException("알 수 없는 위습");
            wallet.Grant(new ResourceBundle().Add(id, count));
            if (WispResourceIds.IsAuto(id))
            {
                for (long i = 0; i < count; i++) pending.Enqueue(id);
                FlushAutomatic();
            }
        }

        public int FlushAutomatic()
        {
            if (processing) return 0;
            processing = true;
            int applied = 0;
            try
            {
                while (pending.Count > 0)
                {
                    string id = pending.Peek();
                    if (!roller.IsVerified(id)) break;
                    if (wallet.Balance(id) == 0) { pending.Dequeue(); continue; }
                    // Apply must be transactional: if it throws, wallet remains unchanged.
                    var reward = roller.Roll(id);
                    if (reward == null) throw new InvalidOperationException("검증되지 않은 위습 보상");
                    world.Apply(reward);
                    if (!wallet.TryExchange(new ResourceBundle().Add(id, 1), new ResourceBundle()))
                        throw new InvalidOperationException("위습 소모 실패");
                    pending.Dequeue();
                    applied++;
                }
            }
            finally { processing = false; }
            return applied;
        }

        // 선택위습은 Merge UI에서만 명시적으로 소비. 검증된 선택 결과만 허용.
        public bool MergeChoice(string selectedUnitRawcode, Func<string, bool> verifiedAtomicMerge)
        {
            if (string.IsNullOrWhiteSpace(selectedUnitRawcode) || verifiedAtomicMerge == null)
                throw new ArgumentException("검증된 Merge 선택과 실행기가 필요합니다.");
            if (wallet.Balance(WispResourceIds.Choice) < 1) return false;
            // Merge executor must not independently charge the same wisp.
            if (!verifiedAtomicMerge(selectedUnitRawcode)) return false;
            return wallet.TryExchange(new ResourceBundle().Add(WispResourceIds.Choice, 1),
                new ResourceBundle());
        }

        public static bool IsKnown(string id) => id == WispResourceIds.Random ||
            id == WispResourceIds.Choice || WispResourceIds.IsAuto(id);
    }
}
