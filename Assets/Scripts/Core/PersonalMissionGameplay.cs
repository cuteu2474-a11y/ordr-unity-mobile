using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    [Serializable]
    public sealed class OriginalMissionSlotState
    {
        public string MissionId;
        public bool Completed;
        public bool RewardPending;
    }

    [Serializable]
    public sealed class OriginalMissionCountState
    {
        public OriginalMissionSignal Signal;
        public long Count;
    }

    [Serializable]
    public sealed class OriginalMissionSessionState
    {
        public int Version = 1;
        public string SessionId;
        public bool Otherworld;
        public int CurrentRound;
        public bool HasRerolled;
        public List<OriginalMissionSlotState> Slots = new List<OriginalMissionSlotState>();
        public List<OriginalMissionCountState> Counts = new List<OriginalMissionCountState>();
    }

    // Mobile-local uniform draws, independent of the combat BFh/BFi stream.
    // This is NOT a replay of Warcraft's original PRNG. State is saveable so
    // reopening a save does not change the next reroll.
    public sealed class MobileMissionRandom : IOriginalMissionRandom
    {
        public uint State { get; private set; }
        public MobileMissionRandom(uint seed) { State = seed == 0 ? 0x9e3779b9u : seed; }
        public int NextIndex(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            uint bound = (uint)exclusiveUpperBound;
            // xorshift's cycle contains every NONZERO uint; reject the short
            // tail after mapping to the zero-based interval [0, 2^32-2].
            ulong size = uint.MaxValue;
            ulong limit = size - size % bound;
            uint value;
            do
            {
                uint next = State;
                next ^= next << 13; next ^= next >> 17; next ^= next << 5;
                State = next;
                value = next - 1;
            } while ((ulong)value >= limit);
            return (int)(value % bound);
        }
    }

    // Every reward is committed to the SAME wallet, with its receipt.
    // Ability levels are recorded here; their combat effect requires its
    // separate original ability implementation and is not guessed here.
    public sealed class OriginalMissionWalletRewards : IOriginalMissionRewardSink
    {
        public const string TraitPointsId = "trait-points";
        private readonly EconomyWallet wallet;
        public OriginalMissionWalletRewards(EconomyWallet shared)
        { wallet = shared ?? throw new ArgumentNullException(nameof(shared)); }

        public static string AbilityId(string rawcode) { return "mission:ability:" + rawcode; }
        public static string ReceiptId(string transactionKey) { return "mission:" + transactionKey; }
        public static string ItemResourceId(string rawcode)
        {
            switch (rawcode)
            {
                case "e018": return WispResourceIds.Choice;
                case "e0IX": return WispResourceIds.Random;
                case "e017": return WispResourceIds.Uncommon;
                case "e019": return WispResourceIds.Rare;
                default: throw new ArgumentException("미션 보상의 원본 아이템 대응이 없습니다.");
            }
        }

        public void Grant(string transactionKey, IReadOnlyList<OriginalMissionReward> rewards)
        {
            if (string.IsNullOrWhiteSpace(transactionKey) || rewards == null)
                throw new ArgumentException("미션 보상 거래가 불완전합니다.");
            var additions = new ResourceBundle();
            var levels = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var reward in rewards)
            {
                if (reward == null) throw new ArgumentException("미션 보상이 비어 있습니다.");
                switch (reward.Kind)
                {
                    case OriginalMissionRewardKind.Item:
                        additions.Add(ItemResourceId(reward.RawId), reward.Count); break;
                    case OriginalMissionRewardKind.Lumber:
                        additions.Add("wood", reward.Count); break;
                    case OriginalMissionRewardKind.TraitPoint:
                        additions.Add(TraitPointsId, reward.Count); break;
                    case OriginalMissionRewardKind.AddAbilityLevel:
                        additions.Add(AbilityId(reward.RawId), reward.Count); break;
                    case OriginalMissionRewardKind.SetAbilityLevel:
                        if (levels.ContainsKey(AbilityId(reward.RawId)))
                            throw new ArgumentException("능력 레벨 설정 보상이 중복됐습니다.");
                        levels.Add(AbilityId(reward.RawId), reward.Count); break;
                    default: throw new ArgumentOutOfRangeException(nameof(reward.Kind));
                }
            }
            wallet.TryGrantOnce(ReceiptId(transactionKey), additions, levels);
        }
    }

    [Serializable]
    public sealed class PersonalMissionGameplayState
    {
        public int Version = 1;
        public OriginalMissionSessionState Missions;
        public EconomyWalletState Wallet;
        public List<string> ProcessedEvents = new List<string>();
        public int LastEndedRound;
        public long ChoiceUses;
        public uint MobileRandomState;
    }

    // Only completed gameplay operations enter here; button taps, previews,
    // rejected transactions, summons and story dispatch are NOT mission events.
    public sealed class PersonalMissionGameplay
    {
        private readonly GameSession game;
        private readonly EconomyWallet wallet;
        private readonly IOriginalMissionRandom random;
        private readonly HashSet<string> processed = new HashSet<string>(StringComparer.Ordinal);
        private int lastEndedRound;
        private long choiceUses;
        public OriginalPersonalMissionSession Missions { get; private set; }
        public string PendingRewardError { get; private set; }

        private PersonalMissionGameplay(GameSession liveGame, EconomyWallet sharedWallet,
            IOriginalMissionRandom missionRandom)
        {
            game = liveGame ?? throw new ArgumentNullException(nameof(liveGame));
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            random = missionRandom ?? throw new ArgumentNullException(nameof(missionRandom));
        }

        public static PersonalMissionGameplay Start(GameSession game, EconomyWallet wallet,
            IOriginalMissionRandom random, string sessionId)
        {
            if (game == null || game.Phase != SessionPhase.Running || game.CurrentWave != 0)
                throw new InvalidOperationException("게임 시작 직후 개인 미션을 배정하세요.");
            var result = new PersonalMissionGameplay(game, wallet, random);
            result.Missions = new OriginalPersonalMissionSession(true, random,
                new OriginalMissionWalletRewards(wallet), sessionId);
            result.Missions.Start();
            return result;
        }

        // Draws can fail before any starting resources or units are committed.
        public static PersonalMissionGameplay Prepare(GameSession game, EconomyWallet wallet,
            IOriginalMissionRandom random, string sessionId)
        {
            if (game == null || game.Phase != SessionPhase.NotStarted || game.CurrentWave != 0)
                throw new InvalidOperationException("게임 시작 전에만 미션을 준비할 수 있습니다.");
            var result = new PersonalMissionGameplay(game, wallet, random);
            result.Missions = new OriginalPersonalMissionSession(true, random,
                new OriginalMissionWalletRewards(wallet), sessionId);
            result.Missions.Start();
            return result;
        }

        public bool CanReroll(int index)
        {
            return game.Phase == SessionPhase.Running && index >= 0 && index < 3 &&
                !Missions.HasRerolled && Missions.CurrentRound < 10 &&
                !Missions.Slots[index].Completed && !Missions.Slots[index].RewardPending;
        }
        public bool TryReroll(int index)
        { return CanReroll(index) && Missions.TryReroll(index); }

        // Called only AFTER the verified director accepted the new round.
        // The previous round ends on this source signal, never on enemy count.
        public void OnRoundStarted()
        {
            if (game.Phase != SessionPhase.Running || game.CurrentWave != Missions.CurrentRound + 1)
                throw new InvalidOperationException("실제 게임과 미션 라운드 순서가 다릅니다.");
            EndPreviousRound();
            Missions.BeginRound(game.CurrentWave);
        }

        private void EndPreviousRound()
        {
            if (Missions.CurrentRound <= lastEndedRound) return;
            lastEndedRound = Missions.CurrentRound;
            ApplyRewardWork(() => Missions.EndRound());
        }

        public void OnGameFinished()
        {
            if (game.Phase == SessionPhase.Won) EndPreviousRound();
            // Eligibility only: persistent berry settlement is a different
            // system. Losing or repeatedly opening UI grants no berries.
        }

        public bool RecordOnce(string committedEventId, OriginalMissionSignal signal, int quantity = 1)
        {
            if (!Enum.IsDefined(typeof(OriginalMissionSignal), signal) ||
                signal == OriginalMissionSignal.RoundEnded || quantity <= 0)
                throw new ArgumentException("실제 게임 이벤트 종류·횟수가 필요합니다.");
            return ApplyEvent(committedEventId, () => Missions.Record(signal, quantity));
        }

        public bool RecordCommonSale(long soldInstanceId)
        {
            if (soldInstanceId <= 0) return false;
            return RecordOnce("common-sale:" + soldInstanceId, OriginalMissionSignal.CommonUnitSold);
        }

        public bool RecordChoiceWispUse()
        {
            if (game.Phase != SessionPhase.Running) return false;
            long next = checked(choiceUses + 1);
            bool accepted = RecordOnce("choice-use:" + next, OriginalMissionSignal.SelectionWispUsed);
            if (accepted) choiceUses = next;
            return accepted;
        }

        public bool RecordGamble(string eventId, OriginalGambleTier tier, bool won)
        {
            if (!Enum.IsDefined(typeof(OriginalGambleTier), tier)) throw new ArgumentOutOfRangeException(nameof(tier));
            return ApplyEvent(eventId, () => Missions.RecordGamble(tier, won));
        }

        // The source fusion engine supplies its ACTUAL hero flag and Q7
        // eligibility. Catalog grade alone cannot establish these two facts.
        public bool RecordFusion(string eventId, string rawcode, bool isHero,
            IEnumerable<string> abilities, bool isHighestTier)
        {
            if (rawcode == null || rawcode.Length != 4 || abilities == null)
                throw new ArgumentException("실제 조합 결과 객체가 필요합니다.");
            var copy = new List<string>(abilities);
            return ApplyEvent(eventId, () => Missions.RecordSuccessfulFusion(rawcode,
                isHero, copy, isHighestTier));
        }

        private bool ApplyEvent(string id, Action committedEvent)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
                throw new ArgumentException("게임 이벤트 식별자가 필요합니다.");
            if (game.Phase != SessionPhase.Running ||
                game.CurrentWave != Missions.CurrentRound || !processed.Add(id)) return false;
            // The underlying operation already committed. Preserve its count
            // if a reward consumer fails; retry the reward, never the operation.
            ApplyRewardWork(committedEvent);
            return true;
        }

        public void RetryRewards() { ApplyRewardWork(() => Missions.RetryPendingRewards()); }
        private void ApplyRewardWork(Action work)
        {
            try { work(); PendingRewardError = null; }
            catch (Exception error) { PendingRewardError = error.Message; }
        }

        // Component snapshot; restore ONLY alongside the matching full game
        // save. This is not an independent combat/roster save file.
        public PersonalMissionGameplayState CaptureState()
        {
            var local = random as MobileMissionRandom;
            var state = new PersonalMissionGameplayState {
                Missions = Missions.CaptureState(), Wallet = wallet.CaptureState(),
                LastEndedRound = lastEndedRound, ChoiceUses = choiceUses,
                MobileRandomState = local == null ? 0 : local.State
            };
            state.ProcessedEvents.AddRange(processed);
            state.ProcessedEvents.Sort(StringComparer.Ordinal);
            return state;
        }

        public static PersonalMissionGameplay Restore(GameSession game, EconomyWallet sharedWallet,
            PersonalMissionGameplayState state, IOriginalMissionRandom externalRandom = null)
        {
            if (game == null || game.Phase == SessionPhase.NotStarted || state == null ||
                state.Version != 1 || state.Missions == null || !state.Missions.Otherworld ||
                state.Missions.CurrentRound != game.CurrentWave || state.ProcessedEvents == null ||
                state.LastEndedRound < 0 || state.LastEndedRound > game.CurrentWave ||
                state.ChoiceUses < 0 || (externalRandom == null && state.MobileRandomState == 0))
                throw new FormatException("게임과 개인 미션 저장 시점이 일치하지 않습니다.");
            var validatedWallet = new EconomyWallet();
            validatedWallet.RestoreState(state.Wallet);
            var result = new PersonalMissionGameplay(game, sharedWallet,
                externalRandom == null || externalRandom is MobileMissionRandom
                    ? new MobileMissionRandom(state.MobileRandomState) : externalRandom);
            result.Missions = OriginalPersonalMissionSession.Restore(state.Missions,
                result.random, new OriginalMissionWalletRewards(sharedWallet));
            foreach (var id in state.ProcessedEvents)
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || !result.processed.Add(id))
                    throw new FormatException("미션 이벤트 저장 이력이 잘못됐습니다.");
            foreach (var slot in result.Missions.Slots)
                if (slot.Completed && !state.Wallet.CompletedRewards.Contains(
                    OriginalMissionWalletRewards.ReceiptId(state.Missions.SessionId +
                        ":slot" + slot.Index + ":" + slot.Mission.Id)))
                    throw new FormatException("완료 미션의 보상 지급 이력이 없습니다.");
            result.lastEndedRound = state.LastEndedRound;
            result.choiceUses = state.ChoiceUses;
            sharedWallet.RestoreState(state.Wallet);
            return result;
        }
    }
}
