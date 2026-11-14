using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Attaches separately owned source ledgers to an UNPUBLISHED combat
    // graph. No rewards, creep spawns or game-start events are replayed.
    public sealed class OrdrUnpublishedAuxiliaryRestoreGraph : IDisposable
    {
        public Original323PoneglyphSession Poneglyph { get; internal set; }
        public Original323EvolutionProgress Evolution { get; internal set; }
        public Original323PlayerResearchSession Research { get; internal set; }
        public Original323CreepPracticeWorld Creeps { get; internal set; }
        public Original323StoryBuildingPracticeWorld StoryBuildings { get; internal set; }
        public Original323SoloStoryScenario SoloStory { get; internal set; }
        public Original323StoryMobileProgression StoryProgression { get; internal set; }
        public void Dispose()
        {
            SoloStory?.Dispose();
            StoryBuildings?.Dispose();
            Creeps?.Dispose();
        }
    }

    public static class OrdrAuxiliaryRestoreGraphBuilder
    {
        public static OrdrUnpublishedAuxiliaryRestoreGraph Prepare(
            OrdrUnpublishedCombatRestoreGraph graph,
            OrdrCoreCheckpointState save,
            Original323CreepObjectCatalog creepCatalog,
            Func<string, bool> researchCanApply,
            Original323StoryBuildingCatalog storyCatalog = null,
            Func<int, int, int> storyRandom = null)
        {
            if (graph == null || graph.Core == null || graph.Combat == null ||
                save == null || researchCanApply == null)
                throw new ArgumentNullException("복원된 전투·코어·원본 연구 연결이 필요합니다.");
            OrdrCoreCheckpoint.Validate(save);
            var state = new OrdrUnpublishedAuxiliaryRestoreGraph();
            if (save.Poneglyph != null)
                state.Poneglyph = Original323PoneglyphSession.Restore(
                    save.Poneglyph, save.LogicalTime);
            if (save.Evolution != null)
                state.Evolution = Original323EvolutionProgress.Restore(
                    save.Evolution, save.LogicalTime);
            if (save.Research != null)
            {
                state.Research = new Original323PlayerResearchSession(
                    graph.Core.Economy.Wallet, graph.Core.Game, researchCanApply);
                state.Research.RestoreState(save.Research);
            }
            if (save.Creeps != null)
            {
                if (creepCatalog == null)
                    throw new ArgumentNullException(nameof(creepCatalog));
                try
                {
                    state.Creeps = new Original323CreepPracticeWorld(
                        graph.Combat, creepCatalog);
                    state.Creeps.RestoreState(save.Creeps);
                }
                catch
                {
                    state.Dispose();
                    throw;
                }
            }
            if (save.SoloStory != null)
            {
                if (storyCatalog == null || storyRandom == null)
                    throw new ArgumentNullException(
                        "원본 스토리 카탈로그와 복원된 BFi 난수열이 필요합니다.");
                try
                {
                    state.StoryBuildings = new Original323StoryBuildingPracticeWorld(
                        graph.Combat, storyCatalog);
                    state.StoryBuildings.RestoreState(save.StoryBuildings);
                    state.SoloStory = new Original323SoloStoryScenario(graph.Combat,
                        state.StoryBuildings, graph.Core.Economy, storyRandom);
                    state.SoloStory.RestoreState(save.SoloStory);
                    state.StoryProgression = new Original323StoryMobileProgression(
                        graph.Core.Economy.Wallet);
                    state.StoryProgression.RestoreState(save.StoryProgression,
                        state.SoloStory.NativeRewards);
                    // Do not call ConfigureNativeAwardPort: it applies pending
                    // rewards and must wait until all other owners are restored.
                }
                catch
                {
                    state.Dispose();
                    throw;
                }
            }
            // Ownership is not published to controller/scene at this stage.
            return state;
        }
    }
}
