using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrStoryNativeResumeStateTests
    {
        [Test]
        public void NativeRewardQueueRehydratesWithoutReplayingAward()
        {
            var first = new Original323StoryNativeRewardQueue();
            first.RecordCommittedClear(5);
            first.RecordCommittedClear(8);
            var state = first.CaptureState();
            var restored = new Original323StoryNativeRewardQueue();
            restored.RestoreState(state);
            Assert.AreEqual(first.PendingCount, restored.PendingCount);
            Assert.IsTrue(restored.HasRecordedStage(5));
            Assert.IsTrue(restored.HasRecordedStage(8));
            state.AppliedReceipts.Add("story:native:9:AncientShip:h05Y");
            Assert.Throws<FormatException>(() =>
                new Original323StoryNativeRewardQueue().RestoreState(state));
        }

        [Test]
        public void ProgressionRejectsAppliedReceiptWithoutQueueCommit()
        {
            var queue = new Original323StoryNativeRewardQueue();
            queue.RecordCommittedClear(5);
            var state = new Original323StoryMobileProgressionState();
            var progression = new Original323StoryMobileProgression();
            progression.RestoreState(state, queue);
            state.AppliedReceipts.Add("story:native:5:Research:R01N");
            state.Researches.Add("R01N");
            Assert.Throws<FormatException>(() =>
                Original323StoryMobileProgression.ValidateState(state,
                    queue, null));
        }
    }
}