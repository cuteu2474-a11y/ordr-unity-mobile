using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // A one-tap mobile equivalent of the e0HP source beacon. All spawns use
    // the same live combat roster. Native/script RNG results are drawn once.
    public sealed class Original323EvolutionService
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession shared;
        private readonly IOriginalStartEvolutionWorld actorWorld;
        private readonly Original323EvolutionProgress progress;
        private readonly Original323EvolutionDrawSession draws;
        private double nextReleaseAttempt;

        public Original323EvolutionService(CharacterCombatRuntime runtime,
            SharedEconomySession session, IOriginalStartEvolutionWorld originalActor,
            Original323EvolutionProgress progressState,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            IOriginalWispRandom scriptRandom,
            IOriginal323NativeUnitPoolRandom nativeRandom)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            shared = session ?? throw new ArgumentNullException(nameof(session));
            actorWorld = originalActor ?? throw new ArgumentNullException(nameof(originalActor));
            progress = progressState ?? throw new ArgumentNullException(nameof(progressState));
            if (!ReferenceEquals(combat.Roster, shared.Units))
                throw new ArgumentException("진화체가 다른 세션 로스터를 사용합니다.");
            Original323EvolutionSourceRules.ValidateCatalog(units, objects);
            draws = new Original323EvolutionDrawSession(scriptRandom, nativeRandom);
        }

        public bool TryActivate(out string message)
        {
            message = "진화체를 사용할 수 없습니다.";
            if (!shared.HasStarted || combat.Game.Phase != SessionPhase.Running ||
                progress.IsActivated || progress.IsReleased) return false;
            try
            {
                if (combat.Roster.Get(progress.ActorId).UnitId !=
                    Original323SoloStartProfile.EvolutionRawcode)
                    return false;
            }
            catch (KeyNotFoundException) { return false; }

            // Keep a prepared outcome if native selection/spawn fails, so
            // retries never consume additional RNG or duplicate a reward.
            draws.Prepare();
            long uncommonId = 0;
            try
            {
                uncommonId = combat.SpawnVerifiedOriginalPositionCharacter(
                    draws.Uncommon.CatalogId,
                    Original323EvolutionSourceRules.UncommonX,
                    Original323EvolutionSourceRules.UncommonY);
                actorWorld.RemoveAndUntrack(shared, progress.ActorId);
                progress.Activate(uncommonId, draws.Special.CatalogId);
                message = "안흔함 1기 획득 · 진화체 성장 시작";
                return true;
            }
            catch
            {
                if (uncommonId > 0) combat.RollbackUncommittedSpawn(uncommonId);
                throw;
            }
        }

        public bool TryRelease(double now, out long resultId)
        {
            resultId = 0;
            if (!progress.CanRelease(now) ||
                combat.Game.Phase != SessionPhase.Running ||
                now < nextReleaseAttempt) return false;
            long spawned = 0;
            try
            {
                spawned = combat.SpawnVerifiedOriginalPositionCharacter(
                    progress.DormantSpecialCatalogId,
                    Original323EvolutionSourceRules.DormantX,
                    Original323EvolutionSourceRules.DormantY);
                progress.RecordSourceConfirmedRelease(spawned);
                resultId = spawned;
                return true;
            }
            catch
            {
                if (spawned > 0) combat.RollbackUncommittedSpawn(spawned);
                nextReleaseAttempt = now + 1;
                throw;
            }
        }
    }
}
