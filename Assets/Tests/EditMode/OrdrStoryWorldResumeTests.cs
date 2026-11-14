using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrStoryWorldResumeTests
    {
        [Test]
        public void StoryWaitStateRejectsForgedStageAndReward()
        {
            var state = new Original323SoloStoryState {
                ActiveStage = 1, HighestDestroyedStage = 0,
                LastNotice = "TEST_ONLY source", EggheadSpawnAt = -1,
                NativeRewards = new Original323StoryNativeRewardQueueState()
            };
            state.IssuedStages.Add(1);
            Original323SoloStoryScenario.ValidateState(state, 0);
            state.PendingStage = 3;
            Assert.Throws<FormatException>(() =>
                Original323SoloStoryScenario.ValidateState(state, 0));
            state.PendingStage = 0;
            state.NativeRewards.AppliedReceipts.Add("STORY_FAKE_RECEIPT");
            Assert.Throws<FormatException>(() =>
                Original323SoloStoryScenario.ValidateState(state, 0));
        }

        [Test]
        public void StoryBuildingRejectsForgedObservedSpawn()
        {
            var state = new Original323StoryBuildingPracticeState { Serial = 1 };
            state.Encounters.Add(new Original323StoryBuildingCheckpoint {
                EnemyId = "story:building:practice:h002:1", Rawcode = "h002",
                Stage = 1, State = Original323StoryBuildingState.Alive
            });
            Original323StoryBuildingPracticeWorld.ValidateState(state);
            state.Encounters[0].SourceVerifiedSpawn = true;
            Assert.Throws<FormatException>(() =>
                Original323StoryBuildingPracticeWorld.ValidateState(state));
        }
    }
}
