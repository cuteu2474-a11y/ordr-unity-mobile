using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    public interface IOriginalMissionRandom
    {
        int NextIndex(int exclusiveUpperBound);
    }

    // 대체 RNG: 재현 가능한 테스트를 위한 구현이며 Warcraft III RNG와 같다는 주장은 하지 않는다.
    public sealed class SeededMissionRandom : IOriginalMissionRandom
    {
        private readonly Random random;
        public SeededMissionRandom(int seed) { random = new Random(seed); }
        public int NextIndex(int exclusiveUpperBound) { return random.Next(exclusiveUpperBound); }
    }

    public interface IOriginalMissionRewardSink
    {
        // 모든 보상을 한 트랜잭션으로 지급한다. 성공 시에만 반환하고 실패 시 예외를 던진다.
        // 같은 transactionKey의 재호출은 이중 지급되지 않아야 한다.
        void Grant(string transactionKey, IReadOnlyList<OriginalMissionReward> rewards);
    }

    // Unity 경제/아이템 인벤토리가 붙기 전까지 사용 가능한 실제 상태 저장형 보상 장부.
    public sealed class OriginalMissionRewardLedger : IOriginalMissionRewardSink
    {
        private readonly Dictionary<string, int> items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> abilityLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> processed = new HashSet<string>(StringComparer.Ordinal);
        public int Lumber { get; private set; }
        public int TraitPoints { get; private set; }

        public int ItemCount(string rawId) { int v; return items.TryGetValue(rawId, out v) ? v : 0; }
        public int AbilityLevel(string rawId) { int v; return abilityLevels.TryGetValue(rawId, out v) ? v : 0; }
        public int TransactionCount { get { return processed.Count; } }

        public void Grant(string transactionKey, IReadOnlyList<OriginalMissionReward> rewards)
        {
            if (string.IsNullOrWhiteSpace(transactionKey)) throw new ArgumentException("보상 거래 ID가 필요합니다.");
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            if (processed.Contains(transactionKey)) return;

            // 지급 도중 오류가 발생해 일부 보상만 지급되는 것을 방지한다.
            var nextItems = new Dictionary<string, int>(items, StringComparer.Ordinal);
            var nextAbilities = new Dictionary<string, int>(abilityLevels, StringComparer.Ordinal);
            int nextLumber = Lumber;
            int nextTraits = TraitPoints;

            foreach (var reward in rewards)
            {
                if (reward == null) throw new ArgumentException("null 보상은 허용하지 않습니다.");
                int oldValue;
                switch (reward.Kind)
                {
                    case OriginalMissionRewardKind.Item:
                        nextItems.TryGetValue(reward.RawId, out oldValue);
                        nextItems[reward.RawId] = checked(oldValue + reward.Count);
                        break;
                    case OriginalMissionRewardKind.Lumber:
                        nextLumber = checked(nextLumber + reward.Count);
                        break;
                    case OriginalMissionRewardKind.AddAbilityLevel:
                        nextAbilities.TryGetValue(reward.RawId, out oldValue);
                        nextAbilities[reward.RawId] = checked(oldValue + reward.Count);
                        break;
                    case OriginalMissionRewardKind.SetAbilityLevel:
                        nextAbilities.TryGetValue(reward.RawId, out oldValue);
                        nextAbilities[reward.RawId] = reward.Count;
                        break;
                    case OriginalMissionRewardKind.TraitPoint:
                        nextTraits = checked(nextTraits + reward.Count);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(reward.Kind));
                }
            }

            items.Clear();
            foreach (var pair in nextItems) items.Add(pair.Key, pair.Value);
            abilityLevels.Clear();
            foreach (var pair in nextAbilities) abilityLevels.Add(pair.Key, pair.Value);
            Lumber = nextLumber; TraitPoints = nextTraits;
            processed.Add(transactionKey);
        }
    }

    public sealed class OriginalPersonalMissionSlot
    {
        public int Index { get; private set; }
        public OriginalMissionRule Mission { get; private set; }
        public bool Completed { get; private set; }

        internal OriginalPersonalMissionSlot(int index, OriginalMissionRule mission)
        {
            Index = index;
            Mission = mission;
        }
        internal void Replace(OriginalMissionRule replacement)
        {
            Mission = replacement; Completed = false;
        }
        internal void MarkCompleted() { Completed = true; }
    }

    public enum OriginalGambleTier { Low, Middle, High }

    // 원본 BcN/BcQ, CT4~CT9, CL*/CM* 흐름을 추상화한 1인용 개인 미션 세션.
    // 반드시 실제 성공한 게임 이벤트만 이 세션에 전달한다.
    public sealed class OriginalPersonalMissionSession
    {
        private readonly bool otherworld;
        private readonly IOriginalMissionRandom random;
        private readonly IOriginalMissionRewardSink rewardSink;
        private readonly string sessionId;
        private readonly OriginalPersonalMissionSlot[] slots = new OriginalPersonalMissionSlot[3];
        private readonly Dictionary<OriginalMissionSignal, long> counts =
            new Dictionary<OriginalMissionSignal, long>();
        private bool started;
        private bool rerolled;

        public int CurrentRound { get; private set; }
        public bool HasRerolled { get { return rerolled; } }
        public bool AllThreeCompleted
        {
            get
            {
                return started && slots[0].Completed && slots[1].Completed && slots[2].Completed;
            }
        }
        // 원본 58496~58499: 클리어 시 개인 미션 3종 달성 -> 베리 1개 추가.
        // 여기에서는 보상을 중복 지급하지 않고 게임 클리어 로직에 확인값만 제공한다.
        public int BonusBerryOnVictory { get { return AllThreeCompleted ? 1 : 0; } }
        public IReadOnlyList<OriginalPersonalMissionSlot> Slots
        {
            get
            {
                EnsureStarted();
                return Array.AsReadOnly(slots);
            }
        }

        public OriginalPersonalMissionSession(bool otherworld, IOriginalMissionRandom random,
            IOriginalMissionRewardSink rewardSink, string sessionId)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (rewardSink == null) throw new ArgumentNullException(nameof(rewardSink));
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("게임 세션 ID가 필요합니다.");
            this.otherworld = otherworld; this.random = random;
            this.rewardSink = rewardSink; this.sessionId = sessionId;
        }

        public void Start()
        {
            if (started) throw new InvalidOperationException("미션 세션을 이미 시작했습니다.");
            var candidates = OriginalPersonalMissionCatalog.Available(otherworld);
            for (int i = 0; i < slots.Length; i++)
            {
                int selected = Roll(candidates.Count);
                slots[i] = new OriginalPersonalMissionSlot(i, candidates[selected]);
                candidates.RemoveAt(selected);
            }
            started = true;
        }

        // GameSession.BeginWave 성공 직후 호출. 원본 라운드 변수는 게임 시작 시 0이다.
        public void BeginRound(int round)
        {
            EnsureStarted();
            if (round != CurrentRound + 1)
                throw new ArgumentOutOfRangeException(nameof(round), "라운드는 순차적으로 증가해야 합니다.");
            CurrentRound = round;
        }

        public void EndRound()
        {
            EnsureStarted();
            Evaluate(OriginalMissionSignal.RoundEnded);
        }

        public bool TryReroll(int slotIndex)
        {
            EnsureStarted();
            ValidateSlot(slotIndex);
            if (CurrentRound >= 10 || rerolled || slots[slotIndex].Completed) return false;
            // 원본 CT7: 뽑힌 새 미션은 기존 슬롯의 미션과 달라야 하고
            // 다른 두 슬롯과도 겹치지 않는다.
            var candidates = OriginalPersonalMissionCatalog.Available(otherworld);
            foreach (var slot in slots)
                candidates.Remove(slot.Mission);
            if (candidates.Count == 0) throw new InvalidOperationException("교체할 미션 후보가 없습니다.");
            slots[slotIndex].Replace(candidates[Roll(candidates.Count)]);
            rerolled = true;
            return true;
        }

        private int Roll(int count)
        {
            int index = random.NextIndex(count);
            if (index < 0 || index >= count)
                throw new InvalidOperationException("난수 생성기가 후보 범위를 벗어났습니다.");
            return index;
        }

        public long EventCount(OriginalMissionSignal signal)
        {
            long count;
            return counts.TryGetValue(signal, out count) ? count : 0;
        }

        public int ProgressAt(int slotIndex)
        {
            EnsureStarted(); ValidateSlot(slotIndex);
            var rule = slots[slotIndex].Mission;
            long progress = rule.AllGambleTiersRequired ?
                Math.Min(EventCount(OriginalMissionSignal.LowGambleAttempted),
                Math.Min(EventCount(OriginalMissionSignal.MidGambleAttempted),
                         EventCount(OriginalMissionSignal.HighGambleAttempted))) :
                EventCount(rule.Signal);
            return (int)Math.Min(rule.RequiredCount, progress);
        }

        public void Record(OriginalMissionSignal signal, int quantity = 1)
        {
            EnsureStarted();
            if (signal == OriginalMissionSignal.RoundEnded)
                throw new ArgumentException("라운드 종료는 EndRound를 사용하세요.");
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            long old = EventCount(signal);
            counts[signal] = old > long.MaxValue - quantity ? long.MaxValue : old + quantity;
            Evaluate(signal);
        }

        public void RecordGamble(OriginalGambleTier tier, bool success)
        {
            switch (tier)
            {
                case OriginalGambleTier.Low:
                    Record(OriginalMissionSignal.LowGambleAttempted);
                    if (!success) Record(OriginalMissionSignal.LowGambleFailed);
                    break;
                case OriginalGambleTier.Middle:
                    Record(OriginalMissionSignal.MidGambleAttempted);
                    if (!success) Record(OriginalMissionSignal.MidGambleFailed);
                    break;
                case OriginalGambleTier.High:
                    Record(OriginalMissionSignal.HighGambleAttempted);
                    if (!success) Record(OriginalMissionSignal.HighGambleFailed);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        // 원본 Q4의 생성유닛 어빌리티와 유닛 rawcode 조건(51312~51522, 51702)을 그대로 사용.
        // isHighestTier는 원본 Q7 카운터의 조합 조건을 확인한 조합 시스템만 참으로 전달한다.
        public void RecordSuccessfulFusion(string unitRawId, bool isHero,
            IEnumerable<string> abilityRawIds, bool isHighestTier)
        {
            if (abilityRawIds == null) throw new ArgumentNullException(nameof(abilityRawIds));
            var abilities = new HashSet<string>(abilityRawIds, StringComparer.Ordinal);
            if (isHero && abilities.Contains("A07O")) Record(OriginalMissionSignal.TranscendentFusion);
            if (abilities.Contains("A905")) Record(OriginalMissionSignal.ImmortalFusion);
            if (abilities.Contains("A906")) Record(OriginalMissionSignal.EternalFusion);
            if (abilities.Contains("A907")) Record(OriginalMissionSignal.LimitedFusion);
            if (abilities.Contains("A0QH")) Record(OriginalMissionSignal.DistortedFusion);
            if (unitRawId == "h067" || unitRawId == "h08O" || unitRawId == "H0BK")
                Record(OriginalMissionSignal.ItemRequiredFusion);
            if (abilities.Contains("A909")) Record(OriginalMissionSignal.MysticFusion);
            if (isHighestTier) Record(OriginalMissionSignal.HighestTierFusion);
        }

        private void Evaluate(OriginalMissionSignal raisedSignal)
        {
            foreach (var slot in slots)
            {
                if (slot.Completed) continue;
                var rule = slot.Mission;
                if (rule.CheckAtRoundEnd != (raisedSignal == OriginalMissionSignal.RoundEnded))
                    continue;
                if (!rule.CheckAtRoundEnd &&
                    rule.Signal != raisedSignal &&
                    !(rule.AllGambleTiersRequired &&
                      (raisedSignal == OriginalMissionSignal.MidGambleAttempted ||
                       raisedSignal == OriginalMissionSignal.HighGambleAttempted)))
                    continue;
                if (rule.MustFinishBeforeRound > 0 && CurrentRound >= rule.MustFinishBeforeRound)
                    continue;
                if (ProgressAt(slot.Index) < rule.RequiredCount) continue;
                rewardSink.Grant(sessionId + ":slot" + slot.Index + ":" + rule.Id, rule.Rewards);
                slot.MarkCompleted();
            }
        }

        private static void ValidateSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 3) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }
        private void EnsureStarted()
        {
            if (!started) throw new InvalidOperationException("게임 시작 후 사용하세요.");
        }
    }
}
