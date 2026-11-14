using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
namespace Ordr.Mobile.Core
{
    [Serializable] public sealed class Original323StoryMobileProgressionState
    {
        public int Version = 1;
        public List<string> AppliedReceipts = new List<string>();
        public List<string> Researches = new List<string>();
        public bool GamblingChargesAlreadySettled;
        public long AwardedGamblingCharges;
    }
    public sealed partial class Original323StoryMobileProgression
    {
        public Original323StoryMobileProgressionState CaptureState(
            Original323StoryNativeRewardQueue rewards)
        {
            var state = new Original323StoryMobileProgressionState {
                GamblingChargesAlreadySettled = gamblingChargesAlreadySettled,
                AwardedGamblingCharges = awardedGamblingCharges
            };
            state.AppliedReceipts.AddRange(applied);
            state.AppliedReceipts.Sort(StringComparer.Ordinal);
            state.Researches.AddRange(research);
            state.Researches.Sort(StringComparer.Ordinal);
            ValidateState(state, rewards, sharedWallet);
            return state;
        }
        public static void ValidateState(Original323StoryMobileProgressionState state,
            Original323StoryNativeRewardQueue rewards, EconomyWallet wallet)
        {
            if (state == null || state.Version != 1 ||
                state.AppliedReceipts == null || state.Researches == null ||
                rewards == null || state.AppliedReceipts.Count > 40 ||
                state.Researches.Count > 5 || state.AwardedGamblingCharges < 0)
                throw new FormatException("원본 스토리 진행 저장본이 잘못됐습니다.");
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in state.AppliedReceipts)
                if (string.IsNullOrWhiteSpace(id) || !remaining.Add(id))
                    throw new FormatException("스토리 보상 영수증이 중복됐습니다.");
            var researchExpected = new HashSet<string>(StringComparer.Ordinal);
            int gamblingCount = 0;
            foreach (var award in rewards.Awards)
            {
                bool done = remaining.Remove(award.Receipt);
                if (done != award.Applied)
                    throw new FormatException("스토리 효과 상태가 원본 큐와 일치하지 않습니다.");
                if (!done) continue;
                if (award.Kind == Original323StoryNativeRewardKind.Research)
                    researchExpected.Add(award.OriginalRawcode);
                if (award.Kind == Original323StoryNativeRewardKind.GamblingCharges)
                {
                    gamblingCount++;
                    if (wallet != null && !wallet.HasCompletedTransaction(award.Receipt))
                        throw new FormatException("스토리 도박 충전 영수증이 지갑에 없습니다.");
                }
            }
            if (remaining.Count != 0 ||
                state.GamblingChargesAlreadySettled != (gamblingCount > 0) ||
                state.AwardedGamblingCharges < gamblingCount ||
                state.AwardedGamblingCharges > 2L * gamblingCount)
                throw new FormatException("원본 스토리 적용 보상 횟수가 올바르지 않습니다.");
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in state.Researches)
                if (!researchExpected.Contains(id) || !counted.Add(id))
                    throw new FormatException("원본 스토리 연구 상태가 잘못됐습니다.");
            if (counted.Count != researchExpected.Count)
                throw new FormatException("스토리 연구 효과가 누락됐습니다.");
        }
        public void RestoreState(Original323StoryMobileProgressionState state,
            Original323StoryNativeRewardQueue rewards)
        {
            if (applied.Count != 0 || research.Count != 0 ||
                gamblingChargesAlreadySettled || awardedGamblingCharges != 0)
                throw new InvalidOperationException("새 스토리 진행 세션에서만 복원합니다.");
            ValidateState(state, rewards, sharedWallet);
            applied.UnionWith(state.AppliedReceipts);
            research.UnionWith(state.Researches);
            gamblingChargesAlreadySettled = state.GamblingChargesAlreadySettled;
            awardedGamblingCharges = state.AwardedGamblingCharges;
        }
    }
}