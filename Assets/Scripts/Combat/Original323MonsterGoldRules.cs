using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Source: ORDR S2 2.323[C] original war3map.j, SHA-256
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // CVM (line 59398), BFi (3596), BFh (3589), Bcf (8236), CVo/CVp/CVX/CVY/CVZ.
    // Gold is awarded on original monster DEATH with A901 ownership, not
    // on escaped monsters, nor for user-driven "kill" notifications.
    public static class Original323MonsterGoldRules
    {
        public const int FinalRound = 65;
        public const int WinningGoldRoll = 3;
        public const int GoldRollMin = 1;
        public const int GoldRollMax = 4;
        public const string CurrencyId = "gold";

        // Warcraft native R2I truncates toward zero; do not use Math.Round.
        // Warcraft real expressions are evaluated in single precision.
        // The original uses global X3, i.e., round AT DEATH, not spawn round.
        public static bool TryComputeAward(int deathRound,
            float originalPlayerGoldBonus, out int originalGold)
        {
            originalGold = 0;
            if (deathRound < 1 || deathRound > FinalRound ||
                float.IsNaN(originalPlayerGoldBonus) ||
                float.IsInfinity(originalPlayerGoldBonus) ||
                originalPlayerGoldBonus < 0)
                return false;
            float round = deathRound;
            float baseReal = 1f + 2f * (round / 5f) +
                3f * (round / 6f) - round / 10f;
            if (float.IsNaN(baseReal) || float.IsInfinity(baseReal) ||
                baseReal < 1 || baseReal >= int.MaxValue)
                return false;
            int baseGold = (int)baseReal;
            float modifiedGold = baseGold * 2f * (1f + originalPlayerGoldBonus);
            if (float.IsNaN(modifiedGold) || float.IsInfinity(modifiedGold) ||
                modifiedGold < 1 || modifiedGold >= int.MaxValue)
                return false;
            originalGold = (int)modifiedGold;
            return originalGold > 0;
        }

        public static bool TryEvaluateObservedRoll(int deathRound,
            int originalRoll, float goldBonus, out int awarded)
        {
            awarded = 0;
            if (originalRoll < GoldRollMin || originalRoll > GoldRollMax)
                return false;
            int calculated;
            if (!TryComputeAward(deathRound, goldBonus, out calculated))
                return false;
            awarded = originalRoll == WinningGoldRoll ? calculated : 0;
            return true;
        }
    }

    // MUST come from the shared BFh/BFi stream, AFTER any bonus conditional
    // RNG draws in CVM, with A901 active player ownership and the original
    // dL player-gold bonus observed at the exact death event.
    // ObservedOriginalGold = 0 on a non-winning roll; otherwise the native
    // Warcraft player's actual gold increment from the same callback.
    public interface IOriginal323MonsterGoldDeathWitness
    {
        bool TryConfirmOriginalGoldDeath(int spawnWave, string instanceId,
            string originalUnitCode, out int playerSlot,
            out int originalRoundAtDeath, out int originalRoll,
            out float playerGoldBonus, out int observedOriginalGold,
            out string sourceEvidence);
    }

    // Reads the exact same EconomyWallet as shop, crafting and Wisp systems.
    // Records both winning and losing dice outcomes in the wallet's durable
    // reward-id history; a reloaded save cannot re-roll a processed corpse.
    public sealed class Original323MonsterGoldService
    {
        private readonly CombatWaveDirector waves;
        private readonly GameSession session;
        private readonly EconomyWallet wallet;
        private readonly IOriginal323MonsterGoldDeathWitness source;
        private readonly int localPlayerSlot;
        private readonly HashSet<string> processed =
            new HashSet<string>(StringComparer.Ordinal);

        public Original323MonsterGoldService(CombatWaveDirector verifiedWaves,
            GameSession liveSession, EconomyWallet sharedWallet,
            int playerSlot, IOriginal323MonsterGoldDeathWitness originalDeath)
        {
            if (playerSlot < 0 || playerSlot > 3)
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            waves = verifiedWaves ?? throw new ArgumentNullException(nameof(verifiedWaves));
            session = liveSession ?? throw new ArgumentNullException(nameof(liveSession));
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            source = originalDeath ?? throw new ArgumentNullException(nameof(originalDeath));
            localPlayerSlot = playerSlot;
        }

        // False: source not verified, not dead, invalid amount, duplicate,
        // or wallet overflow. True: native death RNG event accounted for,
        // including a correct non-winning roll (0 gold).
        public bool TryProcessObservedDeath(int spawnWave, string instanceId,
            out int awardedGold)
        {
            awardedGold = 0;
            if (session.Phase != SessionPhase.Running ||
                spawnWave < 1 || spawnWave > session.CurrentWave ||
                session.CurrentWave > Original323MonsterGoldRules.FinalRound ||
                string.IsNullOrWhiteSpace(instanceId))
                return false;
            string rawcode;
            if (!waves.TryGetDefeatedMonsterRawcode(spawnWave,
                instanceId, out rawcode) ||
                string.IsNullOrWhiteSpace(rawcode) || rawcode.Length != 4)
                return false;

            string key = "ordr323:monster-gold:" + spawnWave + ":" + instanceId;
            if (processed.Contains(key)) return false;

            int player, originalRound, roll, observedGold;
            float bonus;
            string evidence;
            if (!source.TryConfirmOriginalGoldDeath(spawnWave, instanceId,
                    rawcode, out player, out originalRound, out roll,
                    out bonus, out observedGold, out evidence) ||
                player != localPlayerSlot ||
                originalRound != session.CurrentWave ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;

            int calculated;
            if (!Original323MonsterGoldRules.TryEvaluateObservedRoll(
                    originalRound, roll, bonus, out calculated) ||
                calculated != observedGold)
                return false;

            var grant = new ResourceBundle();
            if (calculated > 0)
                grant.Add(Original323MonsterGoldRules.CurrencyId, calculated);
            bool accepted;
            try
            {
                // The wallet checks for duplicate reward IDs BEFORE any change.
                accepted = wallet.TryGrantOnce(key, grant);
            }
            catch (OverflowException)
            {
                // Overflow must not consume an unprocessed observed reward.
                return false;
            }
            if (!accepted) return false;
            processed.Add(key);
            awardedGold = calculated;
            return true;
        }
    }
}
