using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrPracticeResumeStateTests
    {
        [Test]
        public void RejectsDuplicateEnemyRewardsAndClockMismatchBeforePublication()
        {
            var state = new SourceModelPracticeBattleState {
                LastRound = 65, CurrentRound = 1, Started = true,
                MobileMainLane = true, RoundStartTime = 5,
                RoundLeadInSeconds = 3, RoundStartingGold = 100,
                GoldAwarded = 100
            };
            state.BornAt.Add(new SourceModelPracticeEnemyClockState {
                EnemyId = "first", Time = 7
            });
            state.RewardedEnemyIds.Add("first");
            SourceModelPracticeBattle.ValidateState(state, 65, 1, 10);
            state.RewardedEnemyIds.Add("first");
            Assert.Throws<FormatException>(() =>
                SourceModelPracticeBattle.ValidateState(state, 65, 1, 10));
            state.RewardedEnemyIds.RemoveAt(1);
            Assert.Throws<FormatException>(() =>
                SourceModelPracticeBattle.ValidateState(state, 65, 1, 4));
        }

        [Test]
        public void RejectsForeignRoundHistoryAndDistances()
        {
            var state = new SourceModelPracticeBattleState {
                LastRound = 65, CurrentRound = 2, Started = true,
                MobileMainLane = true, RoundStartTime = 15
            };
            state.BornAt.Add(new SourceModelPracticeEnemyClockState {
                EnemyId = "first", Time = 3
            });
            state.DistanceTravelled.Add(new SourceModelPracticeEnemyClockState {
                EnemyId = "other", Time = 40
            });
            Assert.Throws<FormatException>(() =>
                SourceModelPracticeBattle.ValidateState(state, 65, 2, 30));
            state.DistanceTravelled[0].EnemyId = "first";
            state.History.Add(new SourceModelPracticeRoundHistoryState { Wave = 1 });
            SourceModelPracticeBattle.ValidateState(state, 65, 2, 30);
            state.History[0].Wave = 3;
            Assert.Throws<FormatException>(() =>
                SourceModelPracticeBattle.ValidateState(state, 65, 2, 30));
        }
    }
}
