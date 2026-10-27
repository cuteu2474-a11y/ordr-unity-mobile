using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Must be backed by a real original round-start event/timer callback.
    // Neither defeating all monsters nor the mere passage of an inferred
    // number of seconds is sufficient to start another original wave.
    public interface IOriginal323ObservedRoundStartWitness
    {
        bool TryConfirmNextRoundStart(int previousRound, int requestedRound,
            out string originalEventEvidence);
    }

    public static class Original323ObservedRoundStartGate
    {
        // Pure preflight: do not even consult a potentially consuming native
        // event witness while the prior original spawn queue is unfinished.
        // Living enemies can carry across waves when the original allows it.
        public static bool MayObserveNext(
            GameSession session, CombatWaveDirector director,
            IOriginal323ObservedRoundStartWitness witness)
        {
            return session != null && director != null && witness != null &&
                session.Phase == SessionPhase.Running &&
                session.CurrentWave >= 1 && session.CurrentWave < 65 &&
                director.IsActive && director.RemainingSpawns == 0 &&
                director.CurrentProgress != null &&
                director.CurrentProgress.WaveNumber == session.CurrentWave;
        }

        public static bool TryAuthorize(GameSession session, int requestedRound,
            IOriginal323ObservedRoundStartWitness witness, out string evidence)
        {
            evidence = null;
            if (session == null || session.Phase != SessionPhase.Running ||
                requestedRound < 1 || requestedRound > 65 ||
                requestedRound != session.CurrentWave + 1)
                return false;
            // Session start permits the first wave only. Later transitions
            // need a fresh external source-backed observation.
            if (requestedRound == 1)
            {
                evidence = "session-start-first-wave";
                return true;
            }
            if (witness == null) return false;
            string original;
            if (!witness.TryConfirmNextRoundStart(session.CurrentWave,
                requestedRound, out original) || !Trusted(original))
                return false;
            evidence = original;
            return true;
        }

        private static bool Trusted(string source)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                source.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                source.IndexOf("TEST_FIXTURE", StringComparison.OrdinalIgnoreCase) < 0 &&
                source.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) < 0 &&
                source.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                source.IndexOf("MODEL_ONLY", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
