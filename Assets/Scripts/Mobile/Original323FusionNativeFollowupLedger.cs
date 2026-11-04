using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // JASS sha256 96201e80: Bvg/B8Y/B99/B7z/B7W/B8G/B8I/CEN and
    // Bwm/B7w. These durable receipts are the transactional *state* half
    // of mobile native follow-ups. A receipt does not itself simulate the
    // original h04V dummy's abilities, timer, or an original item slot.
    // Consumers must not label those combat/visual callbacks as verified.
    public static class Original323FusionNativeFollowupLedger
    {
        public const string RyumaOnceId = "source:native-fusion:ET04:once";
        public const string DragonLineExplosionBonusId =
            "source:native-fusion:IM04:line-explosion-percent";
        public const string RebeccaHelperId = "source:native-fusion:A007:helper";
        public const string RogerHelperId = "source:native-fusion:IM01:helper";
        public const string TesoroHelperId = "source:native-fusion:ET12:helper";
        public const string SharkCenId = "source:native-fusion:SPH3:cen";
        public const string UtaSongId = "source:native-fusion:ET07:song";

        public static bool CanCraft(EconomyWallet wallet, string ability,
            out string reason)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (ability == "ET04" && wallet.Balance(RyumaOnceId) != 0)
            {
                reason = "원본 규칙: 류마 - 영원은 플레이어당 한 번만 조합할 수 있습니다.";
                return false;
            }
            reason = null;
            return true;
        }

        public static ResourceBundle BuildReward(EconomyWallet wallet, string ability)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            var result = new ResourceBundle();
            switch (ability)
            {
                // Bwm: A011 gains AI02 after source fusion succeeds.
                case "A011":
                    return result.Add(Original323FusionSourceItemLedger.WalletId("AI02"), 1);
                // B7w: ET02 gains AI00. Its input AI01 stays separately spent.
                case "ET02":
                    return result.Add(Original323FusionSourceItemLedger.WalletId("AI00"), 1);
                // B7W: ET07 gains AI01, starts a repeating 60-second song timer.
                case "ET07":
                    return result.Add(Original323FusionSourceItemLedger.WalletId("AI01"), 1)
                        .Add(UtaSongId, 1);
                case "A007":
                    if (wallet.Balance(RebeccaHelperId) == 0)
                        result.Add(RebeccaHelperId, 1);
                    return result;
                case "IM01":
                    if (wallet.Balance(RogerHelperId) == 0)
                        result.Add(RogerHelperId, 1);
                    return result;
                // B99 adds 20 per success; never replace the prior value.
                case "IM04":
                    return result.Add(DragonLineExplosionBonusId, 20);
                case "ET04":
                    return result.Add(RyumaOnceId, 1);
                case "ET12":
                    if (wallet.Balance(TesoroHelperId) == 0)
                        result.Add(TesoroHelperId, 1);
                    return result;
                case "SPH3":
                    return result.Add(SharkCenId, 1);
                default:
                    return null;
            }
        }
    }
}
