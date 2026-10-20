using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // ORDR S2 2.323[C] war3map.j (원본 맵에서 직접 추출):
    // 미션 ID/이름: 1357~1461, 등록: 57705~57725
    // 목표/보상/판정: 50924~51712
    // 원본 맵 스크립트를 복제하지 않고 확인한 규칙만 데이터로 옮긴다.
    public enum OriginalMissionSignal
    {
        UniqueItemAcquired,
        SelectionWispUsed,
        WoodGambleAttempted,
        LowGambleAttempted, MidGambleAttempted, HighGambleAttempted,
        LowGambleFailed, MidGambleFailed, HighGambleFailed,
        StoryMvpWon,
        TranscendentFusion, ImmortalFusion, EternalFusion,
        LimitedFusion, DistortedFusion, ItemRequiredFusion,
        HighestTierFusion, CommonUnitSold, MysticFusion,
        RoundEnded
    }

    public enum OriginalMissionRewardKind
    {
        Item, Lumber, AddAbilityLevel, SetAbilityLevel, TraitPoint
    }

    public sealed class OriginalMissionReward
    {
        public OriginalMissionRewardKind Kind { get; private set; }
        public string RawId { get; private set; }
        public int Count { get; private set; }

        public OriginalMissionReward(OriginalMissionRewardKind kind, string rawId, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if ((kind == OriginalMissionRewardKind.Item ||
                kind == OriginalMissionRewardKind.AddAbilityLevel ||
                kind == OriginalMissionRewardKind.SetAbilityLevel) && string.IsNullOrWhiteSpace(rawId))
                throw new ArgumentException("아이템/능력 원본 ID가 필요합니다.");
            Kind = kind; RawId = rawId ?? ""; Count = count;
        }
    }

    public sealed class OriginalMissionRule
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Objective { get; private set; }
        public OriginalMissionSignal Signal { get; private set; }
        public int RequiredCount { get; private set; }
        public bool OtherworldOnly { get; private set; }
        public bool CheckAtRoundEnd { get; private set; }
        public int MustFinishBeforeRound { get; private set; }
        public bool AllGambleTiersRequired { get; private set; }
        public IReadOnlyList<OriginalMissionReward> Rewards { get; private set; }

        public OriginalMissionRule(string id, string name, string objective,
            OriginalMissionSignal signal, int count, bool otherworldOnly,
            bool atRoundEnd, int beforeRound, bool allGambleTiers,
            params OriginalMissionReward[] rewards)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("미션 식별자와 이름이 필요합니다.");
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (beforeRound < 0) throw new ArgumentOutOfRangeException(nameof(beforeRound));
            if (rewards == null || rewards.Length == 0) throw new ArgumentException("보상 데이터가 필요합니다.");
            Id = id; Name = name; Objective = objective;
            Signal = signal; RequiredCount = count; OtherworldOnly = otherworldOnly;
            CheckAtRoundEnd = atRoundEnd; MustFinishBeforeRound = beforeRound;
            AllGambleTiersRequired = allGambleTiers;
            Rewards = Array.AsReadOnly((OriginalMissionReward[])rewards.Clone());
        }
    }

    public static class OriginalPersonalMissionCatalog
    {
        private static OriginalMissionReward Item(string id, int count)
        {
            return new OriginalMissionReward(OriginalMissionRewardKind.Item, id, count);
        }
        private static OriginalMissionReward Lumber(int count)
        {
            return new OriginalMissionReward(OriginalMissionRewardKind.Lumber, "", count);
        }
        private static OriginalMissionReward AddLevel(string id, int count)
        {
            return new OriginalMissionReward(OriginalMissionRewardKind.AddAbilityLevel, id, count);
        }
        private static OriginalMissionReward SetLevel(string id, int count)
        {
            return new OriginalMissionReward(OriginalMissionRewardKind.SetAbilityLevel, id, count);
        }
        private static OriginalMissionReward Trait(int count)
        {
            return new OriginalMissionReward(OriginalMissionRewardKind.TraitPoint, "", count);
        }
        private static OriginalMissionRule Rule(string id, string name, string objective,
            OriginalMissionSignal signal, int required, bool otherworld, bool roundEnd,
            int beforeRound, bool allGambles, params OriginalMissionReward[] rewards)
        {
            return new OriginalMissionRule(id, name, objective, signal, required,
                otherworld, roundEnd, beforeRound, allGambles, rewards);
        }

        // 2.323[C] 미션 전체: Q000~Q016 공통 17종, Q017 이세계 전용 1종.
        // e018 등은 원본의 아이템 rawcode. 공개 이름으로 추측하여 치환하지 않는다.
        private static readonly IReadOnlyList<OriginalMissionRule> rules =
            Array.AsReadOnly(new OriginalMissionRule[]
            {
                Rule("Q000", "고고학자", "유니크 아이템 2개 이상 획득",
                    OriginalMissionSignal.UniqueItemAcquired, 2, false, false, 0, false, Item("e018", 2), Item("e0IX", 1)),
                Rule("Q001", "오늘만산다", "15라운드 전에 선택위습 11회 사용",
                    OriginalMissionSignal.SelectionWispUsed, 11, false, false, 15, false, Item("e018", 3)),
                Rule("Q002", "목재수집", "목재 도박 9회 이상",
                    OriginalMissionSignal.WoodGambleAttempted, 9, false, true, 0, false, Lumber(3)),
                Rule("Q003", "도박중독", "하급·중급·고급 도박 각각 4회",
                    OriginalMissionSignal.LowGambleAttempted, 4, false, false, 0, true, Item("e018", 2)),
                Rule("Q004", "도박실패(하급)", "하급 도박 4회 실패",
                    OriginalMissionSignal.LowGambleFailed, 4, false, false, 0, false, Item("e017", 1)),
                Rule("Q005", "도박실패(중급)", "중급 도박 4회 실패",
                    OriginalMissionSignal.MidGambleFailed, 4, false, false, 0, false, AddLevel("A09C", 1)),
                Rule("Q006", "도박실패(고급)", "고급 도박 4회 실패",
                    OriginalMissionSignal.HighGambleFailed, 4, false, false, 0, false, Item("e019", 1)),
                Rule("Q007", "MVP 선정", "스토리 기여도 1등 4회",
                    OriginalMissionSignal.StoryMvpWon, 4, false, false, 0, false, Item("e018", 1)),
                Rule("Q008", "유닛조합(초월)", "초월 등급 1회 조합",
                    OriginalMissionSignal.TranscendentFusion, 1, false, false, 0, false, Item("e018", 2)),
                Rule("Q009", "유닛조합(불멸)", "불멸 등급 1회 조합",
                    OriginalMissionSignal.ImmortalFusion, 1, false, false, 0, false, Item("e018", 2)),
                Rule("Q010", "유닛조합(영원)", "영원 등급 1회 조합",
                    OriginalMissionSignal.EternalFusion, 1, false, false, 0, false, Item("e018", 2)),
                Rule("Q011", "유닛조합(제한)", "제한 등급 1회 조합",
                    OriginalMissionSignal.LimitedFusion, 1, false, false, 0, false, Item("e018", 1)),
                Rule("Q012", "유닛조합(왜곡)", "왜곡 등급 1회 조합",
                    OriginalMissionSignal.DistortedFusion, 1, false, false, 0, false, Item("e018", 1)),
                Rule("Q013", "유닛조합(아이템)", "지정된 아이템 필요 유닛 1회 조합",
                    OriginalMissionSignal.ItemRequiredFusion, 1, false, false, 0, false, Item("e018", 3)),
                Rule("Q014", "콰트로", "최상위 유닛 4개 조합",
                    OriginalMissionSignal.HighestTierFusion, 4, false, false, 0, false, SetLevel("A0X0", 2)),
                Rule("Q015", "트리플", "최상위 유닛 3개 조합",
                    OriginalMissionSignal.HighestTierFusion, 3, false, false, 0, false, Trait(1)),
                Rule("Q016", "리사이클", "흔함 유닛 9개 판매",
                    OriginalMissionSignal.CommonUnitSold, 9, false, true, 0, false, Item("e018", 2)),
                Rule("Q017", "유닛조합(신비)", "신비 등급 1회 조합",
                    OriginalMissionSignal.MysticFusion, 1, true, false, 0, false, Lumber(5))
            });

        public static IReadOnlyList<OriginalMissionRule> All { get { return rules; } }

        public static OriginalMissionRule Find(string id)
        {
            foreach (var rule in rules)
                if (string.Equals(rule.Id, id, StringComparison.Ordinal)) return rule;
            return null;
        }

        public static List<OriginalMissionRule> Available(bool otherworld)
        {
            var result = new List<OriginalMissionRule>();
            foreach (var rule in rules)
                if (otherworld || !rule.OtherworldOnly) result.Add(rule);
            return result;
        }
    }
}
