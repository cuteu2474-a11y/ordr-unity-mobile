using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Source: ORDR_S2_2.323[C].w3x / war3map.j, SHA-256
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // CVG / CVT / CVi / Bdf / CU7 / CU8 / CU9.
    // These known rules do NOT validate actual monster HP/armor or RNG.
    public static class Original323BossRoundRules
    {
        public const int FinalRound = 65;
        public const int FastLateBossKillThresholdSeconds = 16;
        public const int FastLateBossKillScore = 1000;
        public const int OtherLateBossKillScore = 500;

        public static bool IsBossRound(int wave)
        {
            return wave >= 1 && wave <= FinalRound &&
                ((wave <= 50 && wave % 10 == 0) ||
                 (wave > 50 && wave % 5 == 0));
        }

        // The original round countdown and the separate 0.5s spawn timer
        // are different timers. Round 50 also triggers a 70s transition.
        public static bool TryGetCountdownSeconds(int wave, out int seconds)
        {
            seconds = 0;
            if (wave < 1 || wave > FinalRound) return false;
            seconds = wave > 50 ? 32 :
                (IsBossRound(wave) ? 60 : 35);
            return true;
        }

        // Bdf: only NEW WORLD boss kills credit score points (not money).
        // The genuine "up" round timer elapsed value must be observed by
        // an independent source. Never substitute Unity wall-clock time.
        public static bool TryGetLateBossScore(int wave, double observedElapsed,
            out int score)
        {
            score = 0;
            if (wave <= 50 || !IsBossRound(wave) ||
                double.IsNaN(observedElapsed) ||
                double.IsInfinity(observedElapsed) || observedElapsed < 0)
                return false;
            score = observedElapsed < FastLateBossKillThresholdSeconds ?
                FastLateBossKillScore : OtherLateBossKillScore;
            return true;
        }

        // CU8/CU7 at the original inter-round trigger (l0). It checks
        // the remaining outstanding boss count un[player] != 0.
        // A mere empty board or simulated timeout is NOT this trigger.
        public static bool TryEvaluateObservedBossDeadline(
            VerifiedWaveProgress progress, out bool originalBossDefeat)
        {
            originalBossDefeat = false;
            if (progress == null || !IsBossRound(progress.WaveNumber) ||
                progress.BossPlanned != 1 || !progress.AllSpawnsIssued)
                return false;
            originalBossDefeat = !progress.AllBossesDefeated;
            return true;
        }

        public static bool TrustedRuntimeEvidence(string evidence)
        {
            return !string.IsNullOrWhiteSpace(evidence) &&
                evidence.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("TEST_FIXTURE", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MODEL_ONLY", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    // Trigger-backed observations must come from the same original source
    // semantics as Bdf / l0. A string alone is never game-state proof.
    public interface IOriginal323BossKillTimeWitness
    {
        bool TryConfirmDeathAndElapsed(int wave, string enemyId,
            string originalRawcode, out int playerSlot,
            out double originalRoundTimerElapsed, out string evidence);
    }

    public interface IOriginal323BossScorePort
    {
        // Must atomically award points once with a persistent unique key.
        // Other scoring sources also use this SAME total score ledger.
        bool TryGrantScoreOnce(string awardKey, int playerSlot,
            int points, string sourceEvidence);
    }

    public interface IOriginal323BossDeadlineWitness
    {
        // Confirm original l0 inter-round check (NOT a Unity local timer).
        bool TryConfirmInterRoundBossCheck(int wave, out string evidence);
    }

    // Retains one-event-at-a-time bookkeeping even when a boss ID is reused.
    // Credit is impossible without both a canonical defeat record and a
    // witnessed original round-timer value.
    public sealed class Original323BossScoreService
    {
        private readonly CombatWaveDirector director;
        private readonly IOriginal323BossKillTimeWitness witness;
        private readonly IOriginal323BossScorePort scores;
        private readonly HashSet<string> granted =
            new HashSet<string>(StringComparer.Ordinal);

        public Original323BossScoreService(CombatWaveDirector liveWaves,
            IOriginal323BossKillTimeWitness originalKills,
            IOriginal323BossScorePort originalScoreLedger)
        {
            director = liveWaves ?? throw new ArgumentNullException(nameof(liveWaves));
            witness = originalKills ?? throw new ArgumentNullException(nameof(originalKills));
            scores = originalScoreLedger ??
                throw new ArgumentNullException(nameof(originalScoreLedger));
        }

        public bool TryCreditDefeatedBoss(int wave, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) ||
                !Original323BossRoundRules.IsBossRound(wave) || wave <= 50)
                return false;
            string originalRawcode;
            if (!director.TryGetDefeatedBossRawcode(wave,
                instanceId, out originalRawcode) ||
                string.IsNullOrWhiteSpace(originalRawcode) ||
                originalRawcode.Length != 4)
                return false;
            string key = "ordr323:boss-score:" + wave + ":" + instanceId;
            if (granted.Contains(key)) return false;

            int slot;
            double elapsed;
            string evidence;
            if (!witness.TryConfirmDeathAndElapsed(wave, instanceId,
                originalRawcode, out slot, out elapsed, out evidence) ||
                slot < 0 || slot > 3 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            int points;
            if (!Original323BossRoundRules.TryGetLateBossScore(
                wave, elapsed, out points))
                return false;
            if (!scores.TryGrantScoreOnce(key, slot, points, evidence))
                return false;
            granted.Add(key);
            return true;
        }
    }
}
