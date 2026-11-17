using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // The native CTq reward can create a REAL h060 pirate-ship actor.
    // This source witness must provide the actual spawn location; a map's
    // generic player-start point cannot be substituted for an unknown CTq site.
    public interface IOriginal323CreepPirateShipPlacement
    {
        bool TryGetCTqPirateShipSpawn(string creepEnemyId,
            out double x, out double y, out string sourceEvidence);
    }

    // Reuses the SINGLE shared economy/roster and original shared BFi RNG.
    // No new "creep currency", ghost ships, free rerolls or detached stack.
    public sealed class Original323CreepRewardWalletPort :
        IOriginal323CreepSourceRewardPort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginalWispRandom sourceRandom;
        private readonly IOriginal323CreepPirateShipPlacement shipPlacement;
        private readonly Dictionary<string, int> lockedCTqRolls =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private bool committing;

        public Original323CreepRewardWalletPort(CharacterCombatRuntime combat,
            SharedEconomySession economy, IOriginalWispRandom sharedRandom,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog originalObjects,
            IOriginal323CreepPirateShipPlacement verifiedShipPosition = null)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            sourceRandom = sharedRandom ?? throw new ArgumentNullException(nameof(sharedRandom));
            shipPlacement = verifiedShipPosition;
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                !economy.HasStarted || units == null || originalObjects == null)
                throw new ArgumentException("크립 보상은 전투와 동일한 시작된 소유 장부가 필요합니다.");
            var ship = units.Find(Original323SourceMaterialRewardWorld.PirateShipCatalogId);
            var source = originalObjects.Find(
                Original323SourceMaterialRewardWorld.PirateShipCatalogId);
            if (ship == null || source == null ||
                source.OriginalRawcode !=
                    Original323SourceMaterialRewardWorld.PirateShipRawcode)
                throw new ArgumentException("원본 h060 해적선 객체 신원이 필요합니다.");
        }

        public bool TryCommitOriginalDeathReward(Original323CreepSourceRewardClaim claim)
        {
            if (claim == null || committing || economy.Wallet == null ||
                combat.Game.Phase != SessionPhase.Running)
                return false;
            string source = claim.Stage == 1 ? "CTp" :
                claim.Stage == 2 ? "CTq" :
                claim.Stage == 3 ? "CTr" : null;
            if (source == null || claim.SourceFunction != source ||
                string.IsNullOrWhiteSpace(claim.ReceiptId) ||
                !claim.ReceiptId.StartsWith("original323:creep:" + source +
                    ":creep:source:", StringComparison.Ordinal))
                return false;
            if (economy.Wallet.HasCompletedTransaction(claim.ReceiptId))
                return true;
            // CTp distributes to all participants; in the ONE-player mode
            // the shared owner is the sole participant. CTq/CTr pay the
            // killing owner, which must have been witnessed on the fatal hit.
            long credited;
            if (claim.Stage >= 2 &&
                (!long.TryParse(claim.LastHitActorId, out credited) ||
                 credited <= 0))
                return false;

            committing = true;
            try
            {
                if (claim.Stage == 1)
                    return GrantOnce(claim.ReceiptId, new ResourceBundle()
                        .Add("gold", 1000).Add("wood", 1));
                if (claim.Stage == 3)
                {
                    // Bhd is one ordinary fZ excavation stack, NOT an
                    // immediately rolled/free inventory item.
                    if (economy.Wallet.Balance(
                        SourceModelItemExcavation.OrdinaryStacks) >=
                        SourceModelItemExcavation.MaximumStacks)
                        return false; // preserve pending for original cap decision
                    return GrantOnce(claim.ReceiptId, new ResourceBundle()
                        .Add("gold", 5000).Add("wood", 1)
                        .Add(SourceModelItemExcavation.OrdinaryStacks, 1));
                }

                int branch;
                if (!lockedCTqRolls.TryGetValue(claim.ReceiptId, out branch))
                {
                    branch = sourceRandom.NextInclusive(1, 2);
                    if (branch != 1 && branch != 2)
                        throw new InvalidOperationException("CTq BFi(1,2) 결과 오류");
                    lockedCTqRolls.Add(claim.ReceiptId, branch);
                }
                if (branch == 2)
                    return GrantOnce(claim.ReceiptId,
                        new ResourceBundle().Add("wood", 7));

                // The other 50% branch grants ONE physical h060 + wood2.
                // If the actual original placement is not yet proven, retain
                // the pending branch and do not re-roll next time.
                double x, y;
                string evidence;
                if (shipPlacement == null ||
                    !shipPlacement.TryGetCTqPirateShipSpawn(claim.EnemyId,
                        out x, out y, out evidence) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                    double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y))
                    return false;
                long id = 0;
                try
                {
                    id = combat.SpawnVerifiedOriginalPositionCharacter(
                        Original323SourceMaterialRewardWorld.PirateShipCatalogId,
                        x, y);
                    if (!GrantOnce(claim.ReceiptId,
                        new ResourceBundle().Add("wood", 2)))
                    {
                        combat.RollbackUncommittedSpawn(id);
                        return false;
                    }
                    return true;
                }
                catch
                {
                    // A wallet Granted observer can throw AFTER a durable
                    // payout. In that case the ship is already committed.
                    if (economy.Wallet.HasCompletedTransaction(claim.ReceiptId))
                        return true;
                    if (id > 0) combat.RollbackUncommittedSpawn(id);
                    throw;
                }
            }
            finally { committing = false; }
        }

        private bool GrantOnce(string receipt, ResourceBundle reward)
        {
            try
            {
                return economy.Wallet.TryGrantOnce(receipt, reward) ||
                    economy.Wallet.HasCompletedTransaction(receipt);
            }
            catch
            {
                if (economy.Wallet.HasCompletedTransaction(receipt))
                    return true;
                throw;
            }
        }
    }
}
