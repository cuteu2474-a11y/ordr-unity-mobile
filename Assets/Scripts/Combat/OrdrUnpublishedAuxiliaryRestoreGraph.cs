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
        public void Dispose() { Creeps?.Dispose(); }
    }

    public static class OrdrAuxiliaryRestoreGraphBuilder
    {
        public static OrdrUnpublishedAuxiliaryRestoreGraph Prepare(
            OrdrUnpublishedCombatRestoreGraph graph,
            OrdrCoreCheckpointState save,
            Original323CreepObjectCatalog creepCatalog,
            Func<string, bool> researchCanApply)
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
            // Ownership is not published to controller/scene at this stage.
            return state;
        }
    }
}
