using System;
using System.Collections.Generic;
namespace Ordr.Mobile.Combat
{
    [Serializable] public sealed class Original323StoryNativeRewardQueueState
    {
        public int Version = 1;
        public List<int> RecordedStages = new List<int>();
        public List<string> AppliedReceipts = new List<string>();
    }
    public sealed partial class Original323StoryNativeRewardQueue
    {
        public Original323StoryNativeRewardQueueState CaptureState()
        {
            var state = new Original323StoryNativeRewardQueueState();
            state.RecordedStages.AddRange(recordedStages);
            state.RecordedStages.Sort();
            foreach (var award in awards)
                if (award.Applied) state.AppliedReceipts.Add(award.Receipt);
            state.AppliedReceipts.Sort(StringComparer.Ordinal);
            ValidateState(state);
            return state;
        }
        public static void ValidateState(Original323StoryNativeRewardQueueState state)
        { BuildRestored(state); }
        private static Original323StoryNativeRewardQueue BuildRestored(
            Original323StoryNativeRewardQueueState state)
        {
            if (state == null || state.Version != 1 ||
                state.RecordedStages == null || state.AppliedReceipts == null ||
                state.RecordedStages.Count > 14 || state.AppliedReceipts.Count > 40)
                throw new FormatException("스토리 네이티브 보상 저장 형식이 잘못됐습니다.");
            var staged = new Original323StoryNativeRewardQueue();
            foreach (int stage in state.RecordedStages)
            {
                if (stage < 1 || stage > 14 || staged.recordedStages.Contains(stage))
                    throw new FormatException("원본 스토리 단계가 중복됐습니다.");
                staged.RecordCommittedClear(stage);
            }
            var pending = new HashSet<string>(StringComparer.Ordinal);
            foreach (string receipt in state.AppliedReceipts)
                if (string.IsNullOrWhiteSpace(receipt) || !pending.Add(receipt))
                    throw new FormatException("스토리 효과 적용 영수증이 중복됐습니다.");
            foreach (var award in staged.awards)
                if (pending.Remove(award.Receipt)) award.Applied = true;
            if (pending.Count != 0)
                throw new FormatException("원본 보상표에 없는 적용 영수증입니다.");
            return staged;
        }
        public void RestoreState(Original323StoryNativeRewardQueueState state)
        {
            if (awards.Count != 0 || recordedStages.Count != 0)
                throw new InvalidOperationException("새 보상 큐에서만 원본 상태를 복구합니다.");
            var staged = BuildRestored(state);
            awards.AddRange(staged.awards);
            foreach (var stage in staged.recordedStages) recordedStages.Add(stage);
        }
    }
}