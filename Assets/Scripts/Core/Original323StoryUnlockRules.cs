using System;

namespace Ordr.Mobile.Core
{
    // Original S2 2.323[C] JASS story-stage source rules.
    // Stage buildings are static combat targets; this class does NOT grant
    // resources, invent final HP, or authorize a source-unverified spawn.
    public static class Original323StoryUnlockRules
    {
        public const int FinalStage = 14;
        public const int NightmareDifficulty = 6;
        public const int CheckpointRound = 35;
        public const int NightmareRequiredDestroyedStage = 13;
        public const double EggheadDelayAfterWanoKillSeconds = 120;
        public const double EggheadKillDeadlineSeconds = 240;

        // Original JASS BDB death callbacks immediately evaluate the next
        // stage trigger (1..12). Wano -> Egghead is a 120s delayed exception
        // on God/Nightmare only (CQ0/CQz, JASS lines 55269..55283).
        public static bool TryNextStageOnDestroy(int destroyedStage,
            int difficulty, out int nextStage, out double delaySeconds)
        {
            if (difficulty < 1 || difficulty > NightmareDifficulty)
                throw new ArgumentOutOfRangeException(nameof(difficulty));
            nextStage = 0;
            delaySeconds = 0;
            if (destroyedStage < 1 || destroyedStage > FinalStage)
                throw new ArgumentOutOfRangeException(nameof(destroyedStage));
            if (destroyedStage <= 12)
            {
                nextStage = destroyedStage + 1;
                return true;
            }
            if (destroyedStage == 13 && difficulty >= 5)
            {
                nextStage = FinalStage;
                delaySeconds = EggheadDelayAfterWanoKillSeconds;
                return true;
            }
            return false;
        }

        // The original 'on' counter increments for story spawns 1..13,
        // and once more on Wano destruction. Thus the round-35 JASS
        // on<=9/11/12/13 tests mean stages 9/11/12/13 not yet destroyed.
        public static int RequiredDestroyedStageAtCheckpoint(int difficulty)
        {
            switch (difficulty)
            {
                case 1:
                case 2: return 0;
                case 3: return 9;
                case 4: return 11;
                case 5: return 12;
                case 6: return NightmareRequiredDestroyedStage;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        // Evaluate at the ACTUAL start event of round 35, not a guessed
        // wall-clock timestamp (CVD, JASS lines 59237..59285).
        public static bool ShouldLoseAtRoundStart(int startedRound,
            int difficulty, int highestDestroyedStage)
        {
            if (startedRound < 1)
                throw new ArgumentOutOfRangeException(nameof(startedRound));
            if (highestDestroyedStage < 0 || highestDestroyedStage > FinalStage)
                throw new ArgumentOutOfRangeException(nameof(highestDestroyedStage));
            return startedRound == CheckpointRound &&
                highestDestroyedStage < RequiredDestroyedStageAtCheckpoint(difficulty);
        }

        // Egghead is a real building that is only subject to this timer
        // after it actually appears (CRB/CQ9, JASS lines 55446..55460).
        public static bool EggheadTimedOut(int difficulty,
            double secondsSinceEggheadSpawn, bool destroyed)
        {
            if (double.IsNaN(secondsSinceEggheadSpawn) ||
                double.IsInfinity(secondsSinceEggheadSpawn) ||
                secondsSinceEggheadSpawn < 0)
                throw new ArgumentOutOfRangeException(nameof(secondsSinceEggheadSpawn));
            if (difficulty < 1 || difficulty > NightmareDifficulty)
                throw new ArgumentOutOfRangeException(nameof(difficulty));
            return difficulty >= 5 && !destroyed &&
                secondsSinceEggheadSpawn >= EggheadKillDeadlineSeconds;
        }
    }
}
