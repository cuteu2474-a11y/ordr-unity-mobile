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
        public bool RewardPending { get; private set; }

        internal OriginalPersonalMissionSlot(int index, OriginalMissionRule mission)
        {
            Index = index;
            Mission = mission;
        }
        internal void Replace(OriginalMissionRule replacement)
        {
            Mission = replacement; Completed = false; RewardPending = false;
        }
        internal void MarkRewardPending() { RewardPending = true; }
        internal void MarkCompleted() { Completed = true; RewardPending = false; }
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
        public string SessionId { get { return sessionId; } }
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
            if (CurrentRound >= 10 || rerolled || slots[slotIndex].Completed ||
                slots[slotIndex].RewardPending) return false;
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
            if (!Enum.IsDefined(typeof(OriginalMissionSignal), signal))
                throw new ArgumentOutOfRangeException(nameof(signal));
            if (signal == OriginalMissionSignal.RoundEnded)
                throw new ArgumentException("라운드 종료는 EndRound를 사용하세요.");
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            long old = EventCount(signal);
            counts[signal] = old > long.MaxValue - quantity ? long.MaxValue : old + quantity;
            Evaluate(signal);
        }

        public void RecordGamble(OriginalGambleTier tier, bool success)
        {
            var signals = new List<OriginalMissionSignal>();
            switch (tier)
            {
                case OriginalGambleTier.Low:
                    signals.Add(OriginalMissionSignal.LowGambleAttempted);
                    if (!success) signals.Add(OriginalMissionSignal.LowGambleFailed);
                    break;
                case OriginalGambleTier.Middle:
                    signals.Add(OriginalMissionSignal.MidGambleAttempted);
                    if (!success) signals.Add(OriginalMissionSignal.MidGambleFailed);
                    break;
                case OriginalGambleTier.High:
                    signals.Add(OriginalMissionSignal.HighGambleAttempted);
                    if (!success) signals.Add(OriginalMissionSignal.HighGambleFailed);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
            RecordBatch(signals);
        }

        private void RecordBatch(List<OriginalMissionSignal> signals)
        {
            EnsureStarted();
            foreach (var signal in signals)
            {
                long old = EventCount(signal);
                counts[signal] = old == long.MaxValue ? old : old + 1;
            }
            foreach (var signal in signals) Evaluate(signal, false);
            RetryPendingRewards();
        }

        // 원본 Q4의 생성유닛 어빌리티와 유닛 rawcode 조건(51312~51522, 51702)을 그대로 사용.
        // isHighestTier는 원본 Q7 카운터의 조합 조건을 확인한 조합 시스템만 참으로 전달한다.
        public void RecordSuccessfulFusion(string unitRawId, bool isHero,
            IEnumerable<string> abilityRawIds, bool isHighestTier)
        {
            if (abilityRawIds == null) throw new ArgumentNullException(nameof(abilityRawIds));
            var signals = new List<OriginalMissionSignal>();
            var abilities = new HashSet<string>(abilityRawIds, StringComparer.Ordinal);
            if (isHero && abilities.Contains("A07O")) signals.Add(OriginalMissionSignal.TranscendentFusion);
            if (abilities.Contains("A905")) signals.Add(OriginalMissionSignal.ImmortalFusion);
            if (abilities.Contains("A906")) signals.Add(OriginalMissionSignal.EternalFusion);
            if (abilities.Contains("A907")) signals.Add(OriginalMissionSignal.LimitedFusion);
            if (abilities.Contains("A0QH")) signals.Add(OriginalMissionSignal.DistortedFusion);
            if (unitRawId == "h067" || unitRawId == "h08O" || unitRawId == "H0BK")
                signals.Add(OriginalMissionSignal.ItemRequiredFusion);
            if (abilities.Contains("A909")) signals.Add(OriginalMissionSignal.MysticFusion);
            if (isHighestTier) signals.Add(OriginalMissionSignal.HighestTierFusion);
            RecordBatch(signals);
        }

        private void Evaluate(OriginalMissionSignal raisedSignal, bool flush = true)
        {
            foreach (var slot in slots)
            {
                if (slot.Completed || slot.RewardPending) continue;
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
                slot.MarkRewardPending();
            }
            if (flush) RetryPendingRewards();
        }

        // An already committed sale/merge is never replayed to retry a reward.
        // Eligibility is retained even if a round/deadline changes meanwhile.
        public void RetryPendingRewards()
        {
            EnsureStarted();
            foreach (var slot in slots)
            {
                if (!slot.RewardPending) continue;
                rewardSink.Grant(sessionId + ":slot" + slot.Index + ":" +
                    slot.Mission.Id, slot.Mission.Rewards);
                slot.MarkCompleted();
            }
        }

        public OriginalMissionSessionState CaptureState()
        {
            EnsureStarted();
            var state = new OriginalMissionSessionState {
                SessionId = sessionId, Otherworld = otherworld,
                CurrentRound = CurrentRound, HasRerolled = rerolled
            };
            foreach (var slot in slots)
                state.Slots.Add(new OriginalMissionSlotState {
                    MissionId = slot.Mission.Id, Completed = slot.Completed,
                    RewardPending = slot.RewardPending
                });
            foreach (var count in counts)
                state.Counts.Add(new OriginalMissionCountState {
                    Signal = count.Key, Count = count.Value
                });
            state.Counts.Sort((a, b) => a.Signal.CompareTo(b.Signal));
            return state;
        }

        // Validate into a fresh session; a bad save cannot mutate the live one.
        public static OriginalPersonalMissionSession Restore(
            OriginalMissionSessionState state, IOriginalMissionRandom random,
            IOriginalMissionRewardSink rewards)
        {
            if (state == null || state.Version != 1 || state.Slots == null ||
                state.Slots.Count != 3 || state.Counts == null ||
                state.CurrentRound < 0 || state.CurrentRound > 65)
                throw new FormatException("개인 미션 저장 상태가 불완전합니다.");
            var result = new OriginalPersonalMissionSession(state.Otherworld,
                random, rewards, state.SessionId);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++)
            {
                var saved = state.Slots[i];
                var rule = saved == null ? null : OriginalPersonalMissionCatalog.Find(saved.MissionId);
                if (rule == null || !ids.Add(rule.Id) ||
                    (!state.Otherworld && rule.OtherworldOnly) ||
                    (saved.Completed && saved.RewardPending))
                    throw new FormatException("미션 슬롯이 중복되었거나 유효하지 않습니다.");
                result.slots[i] = new OriginalPersonalMissionSlot(i, rule);
                if (saved.Completed) result.slots[i].MarkCompleted();
                if (saved.RewardPending) result.slots[i].MarkRewardPending();
            }
            foreach (var count in state.Counts)
            {
                if (count == null || count.Count < 0 ||
                    !Enum.IsDefined(typeof(OriginalMissionSignal), count.Signal) ||
                    count.Signal == OriginalMissionSignal.RoundEnded ||
                    result.counts.ContainsKey(count.Signal))
                    throw new FormatException("개인 미션 진행 횟수가 잘못됐습니다.");
                result.counts.Add(count.Signal, count.Count);
            }
            result.CurrentRound = state.CurrentRound;
            result.rerolled = state.HasRerolled;
            result.started = true;
            foreach (var slot in result.slots)
                if ((slot.Completed || slot.RewardPending) &&
                    result.ProgressAt(slot.Index) < slot.Mission.RequiredCount)
                    throw new FormatException("목표 미달 미션이 완료 상태로 저장됐습니다.");
            return result;
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
