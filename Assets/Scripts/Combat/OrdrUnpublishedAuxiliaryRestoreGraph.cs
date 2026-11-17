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
        public SourceModelPracticeBattle Practice { get; internal set; }
        public void Dispose()
        {
            Practice?.Dispose();
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
            Func<int, int, int> storyRandom = null,
            SourceModelPracticeCatalog practiceCatalog = null,
            Original323MonsterWaveIdentityCatalog originalMonsterCatalog = null,
            SourceModelBasicCombatRules sourceDamage = null,
            IOriginalWispRandom originalScriptRandom = null,
            Action roundStarted = null,
            IOriginal323CreepDispatchAuthority creepDispatchAuthority = null)
        {
            if (graph == null || graph.Core == null || graph.Combat == null ||
                save == null || researchCanApply == null)
                throw new ArgumentNullException("복원된 전투·코어·원본 연구 연결이 필요합니다.");
            OrdrCoreCheckpoint.Validate(save);
            if (save.Creeps != null && creepCatalog == null)
                throw new ArgumentNullException(nameof(creepCatalog));
            if (save.SoloStory != null && (storyCatalog == null || storyRandom == null))
                throw new ArgumentNullException(
                    "원본 스토리 카탈로그와 복원 가능한 BFi 난수가 필요합니다.");
            if (save.Practice != null &&
                (practiceCatalog == null || originalMonsterCatalog == null ||
                 sourceDamage == null || originalScriptRandom == null))
                throw new ArgumentNullException(
                    "65R 원본 출현 데이터·전투 모델·난수 복원이 필요합니다.");
            var state = new OrdrUnpublishedAuxiliaryRestoreGraph();
            try
            {
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
                    // Stable, already-paid sorties rebind without issuing
                    // dispatch, recasting a mobility skill or re-crediting
                    // a creep encounter. Missing source authority fails closed.
                    graph.Combat.RestoreCreepDeploymentsDraft(
                        save.Combat, save.Creeps, state.Creeps,
                        creepDispatchAuthority);
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
                if (save.Practice != null)
                {
                    state.Practice = new SourceModelPracticeBattle(
                        graph.Combat, graph.Waves, graph.Core.Economy,
                        practiceCatalog, originalMonsterCatalog, sourceDamage,
                        originalScriptRandom, roundStarted,
                        mobileMainLane: save.Practice.MobileMainLane);
                    state.Practice.RestoreState(save.Practice);
                }
                // No Start/BeginRound/ApplyPending calls occur on resume.
                // Controller publication remains blocked until the rest
                // of source-native effects and saved RNG owners are restored.
                return state;
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }
    }
}
