using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // This witness must identify an ACTUAL source BeN / CJm / CCQ cast.
    // A button click, map-coordinate estimate or preview is not a cast.
    public interface IOriginal323TreasureLiveCastWitness
    {
        bool TryConfirmOriginalCast(double centerX, double centerY,
            double halfExtent, bool collect, int originalRound,
            out string originalCastEvidence);
    }

    // Real shared-wallet accounting for treasure receipts. The original
    // native cast witness, original BFj chest positions and original BFi
    // reward stream are all separate prerequisites.
    public sealed class Original323TreasureWalletWorld :
        IOriginal323TreasureNativeWorld
    {
        private readonly EconomyWallet wallet;
        private readonly IOriginal323TreasureLiveCastWitness castWitness;
        private readonly string sourceBerryResourceId;
        private readonly HashSet<string> validatedCasts =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string,Original323TreasureReward> settled =
            new Dictionary<string,Original323TreasureReward>(StringComparer.Ordinal);
        private bool settling;

        public int SettledCount => settled.Count;

        // No default "berry" resource is assumed: the pinned original
        // resource identity must be explicitly supplied with live evidence.
        public Original323TreasureWalletWorld(EconomyWallet sharedWallet,
            IOriginal323TreasureLiveCastWitness verifiedNativeCasts,
            string verifiedBerryResourceId = null,
            string berryResourceEvidence = null)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            castWitness = verifiedNativeCasts ??
                throw new ArgumentNullException(nameof(verifiedNativeCasts));
            if ((verifiedBerryResourceId == null) != (berryResourceEvidence == null) ||
                (verifiedBerryResourceId != null &&
                 (string.IsNullOrWhiteSpace(verifiedBerryResourceId) ||
                  verifiedBerryResourceId == "gold" ||
                  verifiedBerryResourceId == "wood" ||
                  WispResourceIds.IsAuto(verifiedBerryResourceId) ||
                  verifiedBerryResourceId == WispResourceIds.Random ||
                  verifiedBerryResourceId == WispResourceIds.Choice ||
                  !Original323BossRoundRules.TrustedRuntimeEvidence(berryResourceEvidence))))
                throw new ArgumentException(
                    "베리는 검증된 별도 원작 재화 ID와 실제 연결 근거가 필요합니다.");
            sourceBerryResourceId = verifiedBerryResourceId;
        }

        public bool TryConfirmOriginalTreasureCast(double x, double y,
            double halfExtent, bool collect, int originalRound,
            out string originalCastEvidence)
        {
            originalCastEvidence = null;
            if (originalRound < 1 || originalRound > 65 ||
                !Original323TreasureRules.Finite(x) ||
                !Original323TreasureRules.Finite(y) ||
                !Original323TreasureRules.Finite(halfExtent) ||
                halfExtent < 0)
                return false;

            string observed;
            if (!castWitness.TryConfirmOriginalCast(x, y, halfExtent,
                    collect, originalRound, out observed) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(observed))
                return false;
            validatedCasts.Add(observed);
            originalCastEvidence = observed;
            return true;
        }

        public bool TrySettleTreasureOnce(string originalChestId,
            Original323TreasureReward originalReward,
            string originalCastEvidence, string sourceSpawnEvidence)
        {
            if (settling || string.IsNullOrWhiteSpace(originalChestId) ||
                originalReward == null ||
                !validatedCasts.Contains(originalCastEvidence ?? "") ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(sourceSpawnEvidence))
                return false;

            Original323TreasureReward previous;
            if (settled.TryGetValue(originalChestId, out previous))
                return previous.WispResourceId == originalReward.WispResourceId &&
                    previous.WispCount == originalReward.WispCount &&
                    previous.BonusBerry == originalReward.BonusBerry;

            // Without a verified berry identity, do NOT award a partial
            // wisp-only chest and silently drop the original 2.5% bonus.
            if (originalReward.BonusBerry &&
                sourceBerryResourceId == null)
                return false;

            var bundle = new ResourceBundle().Add(
                originalReward.WispResourceId, originalReward.WispCount);
            if (originalReward.BonusBerry)
                bundle.Add(sourceBerryResourceId, 1);

            string receiptId = "original323:treasure:" + originalChestId;
            settling = true;
            try
            {
                // TryGrantOnce owns both the balance mutation and saved
                // completed-reward receipt. Other automatic Wisp consumers
                // observe the same wallet. A foreign duplicate cannot count.
                bool granted = wallet.TryGrantOnce(receiptId, bundle);
                if (!granted) return false;
                settled.Add(originalChestId, originalReward);
                return true;
            }
            catch
            {
                // Wallet.Granted observers can throw AFTER TryGrantOnce has
                // committed. Detect the durable receipt before rethrowing;
                // the original search preserves the SAME pending BFi result,
                // and the next call can reconcile without another payout.
                if (wallet.CaptureState().CompletedRewards.Contains(receiptId) &&
                    !settled.ContainsKey(originalChestId))
                    settled.Add(originalChestId, originalReward);
                throw;
            }
            finally { settling = false; }
        }
    }
}
