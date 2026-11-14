using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class Original323SoloStoryDamageState
    {
        public string EnemyId;
        public long Damage;
    }

    [Serializable]
    public sealed class Original323SoloStoryState
    {
        public int Version = 1;
        public int ActiveStage, HighestDestroyedStage, PendingStage;
        public bool CompletedAllBuildings, LostByStory;
        public string LastNotice, LastUniqueItemAward;
        public int AwardedGold, AwardedWood;
        public double NextSpawnAt, EggheadSpawnAt, LastRegenAt;
        public List<int> IssuedStages = new List<int>();
        public List<int> CompletedStages = new List<int>();
        public List<Original323SoloStoryDamageState> OwnedDamage =
            new List<Original323SoloStoryDamageState>();
        public Original323StoryNativeRewardQueueState NativeRewards;
    }

    public sealed partial class Original323SoloStoryScenario
    {
        public Original323SoloStoryState CaptureState()
        {
            if (disposed || ActiveStage < 1)
                throw new InvalidOperationException("시작하지 않은 스토리는 저장할 수 없습니다.");
            var state = new Original323SoloStoryState {
                ActiveStage = ActiveStage,
                HighestDestroyedStage = HighestDestroyedStage,
                PendingStage = pendingStage, CompletedAllBuildings = CompletedAllBuildings,
                LostByStory = LostByStory, LastNotice = LastNotice,
                LastUniqueItemAward = LastUniqueItemAward,
                AwardedGold = AwardedGold, AwardedWood = AwardedWood,
                NextSpawnAt = nextSpawnAt, EggheadSpawnAt = eggheadSpawnAt,
                LastRegenAt = lastRegenAt,
                NativeRewards = NativeRewards.CaptureState()
            };
            state.IssuedStages.AddRange(issued);
            state.IssuedStages.Sort();
            state.CompletedStages.AddRange(completed);
            state.CompletedStages.Sort();
            foreach (var item in ownedDamage)
                state.OwnedDamage.Add(new Original323SoloStoryDamageState {
                    EnemyId = item.Key, Damage = item.Value
                });
            state.OwnedDamage.Sort((a,b) =>
                string.CompareOrdinal(a.EnemyId, b.EnemyId));
            ValidateState(state, combat.Time);
            ValidateBuildingWorld(state);
            return state;
        }

        public static void ValidateState(Original323SoloStoryState state, double now)
        {
            if (state == null || state.Version != 1 ||
                state.ActiveStage < 1 || state.ActiveStage > 14 ||
                state.HighestDestroyedStage < 0 ||
                state.HighestDestroyedStage > state.ActiveStage ||
                state.IssuedStages == null || state.CompletedStages == null ||
                state.OwnedDamage == null || state.IssuedStages.Count != state.ActiveStage ||
                state.CompletedStages.Count != state.HighestDestroyedStage ||
                state.OwnedDamage.Count > 10000 ||
                state.ActiveStage > state.HighestDestroyedStage + 1 ||
                state.PendingStage < 0 || state.PendingStage > 14 ||
                (state.PendingStage > 0 &&
                    (state.PendingStage != state.HighestDestroyedStage + 1 ||
                     state.ActiveStage != state.HighestDestroyedStage)) ||
                state.CompletedAllBuildings != (state.HighestDestroyedStage == 14) ||
                state.AwardedGold < 0 || state.AwardedWood < 0 ||
                string.IsNullOrWhiteSpace(state.LastNotice) ||
                double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                double.IsNaN(state.NextSpawnAt) || double.IsInfinity(state.NextSpawnAt) ||
                state.NextSpawnAt < 0 ||
                double.IsNaN(state.LastRegenAt) || double.IsInfinity(state.LastRegenAt) ||
                state.LastRegenAt < 0 || state.LastRegenAt > now ||
                double.IsNaN(state.EggheadSpawnAt) ||
                double.IsInfinity(state.EggheadSpawnAt) ||
                (state.ActiveStage < 14 && state.EggheadSpawnAt != -1) ||
                (state.ActiveStage == 14 &&
                    (state.EggheadSpawnAt < 0 || state.EggheadSpawnAt > now)) ||
                (state.LastUniqueItemAward != null &&
                    state.LastUniqueItemAward != "AI00" &&
                    state.LastUniqueItemAward != "AI02" &&
                    state.LastUniqueItemAward != "AI03") ||
                state.NativeRewards == null)
                throw new FormatException("스토리 진행/건물 대기 시계가 잘못됐습니다.");
            for (int i=0;i<state.IssuedStages.Count;i++)
                if (state.IssuedStages[i] != i+1)
                    throw new FormatException("스토리 건물 출현 이력이 중복됐습니다.");
            for (int i=0;i<state.CompletedStages.Count;i++)
                if (state.CompletedStages[i] != i+1)
                    throw new FormatException("스토리 건물 파괴 이력이 중복됐습니다.");
            Original323StoryNativeRewardQueue.ValidateState(state.NativeRewards);
            if (state.NativeRewards.RecordedStages.Count != state.CompletedStages.Count)
                throw new FormatException("스토리 원본 보상 단계가 파괴 기록과 다릅니다.");
            for (int i = 0; i < state.CompletedStages.Count; i++)
                if (state.NativeRewards.RecordedStages[i] != state.CompletedStages[i])
                    throw new FormatException("스토리 보상과 건물 파괴 단계가 다릅니다.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in state.OwnedDamage)
                if (entry == null || string.IsNullOrWhiteSpace(entry.EnemyId) ||
                    entry.Damage <= 0 || !unique.Add(entry.EnemyId))
                    throw new FormatException("스토리 피해 기여 기록이 잘못됐습니다.");
        }

        private void ValidateBuildingWorld(Original323SoloStoryState state)
        {
            if (!ReferenceEquals(world.Encounters, null))
            {
                var stages = new HashSet<int>();
                foreach (var entry in world.Encounters)
                {
                    if (entry.Building.Stage > state.ActiveStage)
                        throw new FormatException("스토리 진행보다 앞선 건물이 있습니다.");
                    stages.Add(entry.Building.Stage);
                }
                foreach (int stage in state.IssuedStages)
                    if (!stages.Contains(stage))
                        throw new FormatException("스토리 실제 건물이 출현 장부에 없습니다.");
                foreach (int stage in state.CompletedStages)
                    if (!economy.Wallet.HasCompletedTransaction(
                            "practice:story-stage:" + stage))
                        throw new FormatException("스토리 완료 영수증이 공유 지갑에 없습니다.");
                foreach (var damage in state.OwnedDamage)
                {
                    var target = world.FindEncounter(damage.EnemyId);
                    if (target == null ||
                        target.State != Original323StoryBuildingState.Alive)
                        throw new FormatException("스토리 기여도가 죽은 건물을 가리킵니다.");
                }
            }
        }

        // Reinstate timers and the queue without calling Start(), SpawnNext,
        // TryGrantOnce, NativeRewards.ApplyPending or the original item RNG.
        public void RestoreState(Original323SoloStoryState state)
        {
            ValidateState(state, combat.Time);
            if (disposed || ActiveStage != 0 || issued.Count != 0 ||
                completed.Count != 0 || ownedDamage.Count != 0 ||
                NativeRewards.Awards.Count != 0 ||
                combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("새 스토리 월드에서만 복원합니다.");
            ValidateBuildingWorld(state);
            NativeRewards.RestoreState(state.NativeRewards);
            ActiveStage = state.ActiveStage;
            HighestDestroyedStage = state.HighestDestroyedStage;
            pendingStage = state.PendingStage;
            CompletedAllBuildings = state.CompletedAllBuildings;
            LostByStory = state.LostByStory;
            LastNotice = state.LastNotice;
            LastUniqueItemAward = state.LastUniqueItemAward;
            AwardedGold = state.AwardedGold;
            AwardedWood = state.AwardedWood;
            nextSpawnAt = state.NextSpawnAt;
            eggheadSpawnAt = state.EggheadSpawnAt;
            lastRegenAt = state.LastRegenAt;
            foreach (int stage in state.IssuedStages) issued.Add(stage);
            foreach (int stage in state.CompletedStages) completed.Add(stage);
            foreach (var item in state.OwnedDamage)
                ownedDamage.Add(item.EnemyId, item.Damage);
        }
    }
}
