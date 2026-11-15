using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Original CWR ITEM conditions for Marco A011/AI02, Nika ET02/AI01,
    // and Uta ET07/AI00. The 96201e80 JASS creates those inventory items
    // on successful A011, ET02, and ET07 respectively.
    // These are original inventory item rawcodes, never a free recipe unlock.
    // Source-witnessed pickups and the source-verified h0BR unique-box path
    // are separate authorized producers. Neither is a free recipe unlock.
    public interface IOriginal323FusionItemReceiptWitness
    {
        // Witness MUST confirm the original single player's actual item gain,
        // source transaction identity, and quantity (not merely UI selection).
        bool TryConfirmPlayerItemReceipt(string receiptId, string originalItemRawcode,
            int awardedCount, out string nativeEvidence);
    }

    // Inventory presentation only. A verified receipt or box redemption
    // grants the corresponding saved wallet right; the full item effects
    // are not automatically implemented by this shell.
    // Actual source item object effects can replace these shells later.
    public sealed class Original323FusionItemShell
    {
        public string Rawcode { get; }
        public string DisplayName { get; }
        public string OriginalFusionUse { get; }
        public bool HasImplementedUseEffect => false;

        internal Original323FusionItemShell(string rawcode, string use)
        {
            Rawcode = rawcode;
            DisplayName = "원본 아이템 " + rawcode;
            OriginalFusionUse = use;
        }
    }

    public static class Original323FusionSourceItemLedger
    {
        public const string MarcoItemRawcode = "AI02";
        public const string NikaItemRawcode = "AI01";
        public const string UtaItemRawcode = "AI00";
        public const string ItemWalletPrefix = "source:item:";
        // BhP/BhZ save a persistent acquired flag independently of item use.
        // This marker is shared by 5%-story drops, source pickups and h0BR.
        public const string EverObtainedPrefix = "source:unique-obtained:";
        // Original CRN/CRM A14A..A14J unique-box order. A successful
        // choice grants the unlocked AIxx flag once plus four lumber.
        // Item effects beyond the three CWR ingredient uses remain shells.
        private static readonly IReadOnlyList<Original323FusionItemShell> itemShells =
            Array.AsReadOnly(new[] {
                new Original323FusionItemShell(MarcoItemRawcode, "마르코(제한) 조합 재료"),
                new Original323FusionItemShell(UtaItemRawcode, "우타(영원) 조합 재료"),
                new Original323FusionItemShell(NikaItemRawcode, "니카(영원) 조합 재료"),
                new Original323FusionItemShell("AI03", "고유 아이템 후속 능력 연결 대기"),
                new Original323FusionItemShell("AI18", "도움소 마나 재생 +0.5/초 적용 (고유 획득 영구)"),
                new Original323FusionItemShell("AI21", "고유 아이템 후속 능력 연결 대기"),
                new Original323FusionItemShell("AI17", "고유 아이템 후속 능력 연결 대기"),
                new Original323FusionItemShell("AI19", "라인존 마법방어력 -5% 후속 효과 연결 대기"),
                new Original323FusionItemShell("AI16", "고유 아이템 후속 능력 연결 대기"),
                new Original323FusionItemShell("AI20", "라인존 폭발형 피해 +5% 적용 (고유 획득 영구)")
            });

        public static IReadOnlyList<Original323FusionItemShell> ItemShells => itemShells;

        public static Original323FusionItemShell FindShell(string originalItemRawcode)
        {
            foreach (var item in itemShells)
                if (item.Rawcode == originalItemRawcode) return item;
            return null;
        }

        public static long OwnedShellCount(EconomyWallet wallet, string originalItemRawcode)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (FindShell(originalItemRawcode) == null) return 0;
            return wallet.Balance(WalletId(originalItemRawcode));
        }

        public static string EverObtainedWalletId(string originalItemRawcode)
        {
            if (FindShell(originalItemRawcode) == null)
                throw new ArgumentException("원본 고유 아이템 ID가 아닙니다.", nameof(originalItemRawcode));
            return EverObtainedPrefix + originalItemRawcode;
        }

        public static bool WasEverObtained(EconomyWallet wallet, string originalItemRawcode)
        {
            if (wallet == null || FindShell(originalItemRawcode) == null) return false;
            // Old saves may have a source:item balance or a box receipt but no
            // explicit marker. Accept those proofs without duplicating drops.
            return wallet.Balance(EverObtainedWalletId(originalItemRawcode)) > 0 ||
                wallet.Balance(WalletId(originalItemRawcode)) > 0 ||
                wallet.HasCompletedTransaction("original323:unique-box-item:" + originalItemRawcode);
        }

        public static string WalletId(string originalItemRawcode)
        {
            if (FindShell(originalItemRawcode) == null)
                throw new ArgumentException("원본 조합에 등록되지 않은 아이템", nameof(originalItemRawcode));
            return ItemWalletPrefix + originalItemRawcode;
        }

        public static bool TryResolve(string ability, OriginalJassFusionInstruction condition,
            out string walletId, out string displayName)
        {
            walletId = null;
            displayName = null;
            if (condition == null || condition.Kind != "ITEM" || condition.Count != 1)
                return false;
            if ((ability == "A011" && condition.Rawcode == MarcoItemRawcode) ||
                (ability == "ET02" && condition.Rawcode == NikaItemRawcode) ||
                (ability == "ET07" && condition.Rawcode == UtaItemRawcode))
            {
                walletId = WalletId(condition.Rawcode);
                displayName = condition.Rawcode == MarcoItemRawcode ?
                    "마르코 원본 아이템(AI02)" :
                    condition.Rawcode == NikaItemRawcode ?
                    "니카 원본 아이템(AI01)" : "우타 원본 아이템(AI00)";
                return true;
            }
            return false;
        }

        public static bool IsApproved(string ability, OriginalJassFusionInstruction condition)
        {
            string wallet, name;
            return TryResolve(ability, condition, out wallet, out name);
        }

        public static bool CanSpend(EconomyWallet wallet, string ability,
            OriginalJassFusionInstruction condition, out string reason)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            string walletId, displayName;
            if (!TryResolve(ability, condition, out walletId, out displayName))
            {
                reason = "출처 확인 없는 원본 아이템 조건입니다.";
                return false;
            }
            long owned = wallet.Balance(walletId);
            bool ok = owned >= condition.Count;
            reason = ok ? null : displayName + " 부족 (필요 " +
                condition.Count + ", 보유 " + owned + ")";
            return ok;
        }

        // Public bridge for a future real source-loot/quest event. It does not
        // invent loot drops or run a random draw. The wallet receipt survives
        // capture/restore and cannot be replayed to mint additional items.
        public static bool TryRecordSourcePickup(EconomyWallet wallet,
            IOriginal323FusionItemReceiptWitness witness,
            string receiptId, string originalItemRawcode, int count)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (witness == null || string.IsNullOrWhiteSpace(receiptId) ||
                receiptId.Length > 160 || count <= 0 || count > 100 ||
                FindShell(originalItemRawcode) == null) return false;
            string receipt = "original323:item-pickup:" + receiptId;
            if (wallet.HasCompletedTransaction(receipt) ||
                WasEverObtained(wallet, originalItemRawcode)) return false;
            string evidence;
            if (!witness.TryConfirmPlayerItemReceipt(receiptId, originalItemRawcode,
                    count, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            try
            {
                return wallet.TryGrantOnce(receipt,
                    new ResourceBundle().Add(WalletId(originalItemRawcode), count)
                        .Add(EverObtainedWalletId(originalItemRawcode), 1));
            }
            catch
            {
                // The wallet receipt is durable even if a Granted observer fails
                // after the credit. Treat it as applied, never retry-mint.
                if (wallet.HasCompletedTransaction(receipt)) return true;
                throw;
            }
        }
    }

    // BhP -> BhL is fired at first source-verified item acquisition. These
    // bonuses are permanent player upgrades, not an equipped-slot buff:
    // consuming AI00/01/02 as fusion ingredients must not revoke them.
    // Original war3map.j 10509-10556, SHA-256 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d.
    public static class Original323SourceItemPassiveRules
    {
        public const double OrdinaryCowRegeneration = 0.2; // AI15 -> R035
        public const double UniqueCowRegeneration = 0.5;   // AI18 -> R03A
        public const int UniqueLineExplosionPercent = 5;  // AI20 -> LL[player] +5
        public const int OrdinaryWoodStockIncrease = 2;   // AI13 -> i5[player] +2
        public const float OrdinaryLineGoldFactorIncrease = 1f; // AI13 -> dL[player] +1.0
        public const int UniqueLineMagicDefenseDecrease = 5; // AI19 -> LK[player] +5
        public const int BasicTreasureAlarmSeconds = 97; // CJm level1 100 -> BJI 97
        public const int ImprovedTreasureAlarmSeconds = 77; // CJm level2 80 -> BJI 77

        public static bool HasOrdinary(EconomyWallet wallet, string rawcode)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            return wallet.Balance(SourceModelItemExcavation.OrdinaryItemWalletId(rawcode)) > 0;
        }

        public static int ExtraGraduatedWoodStock(EconomyWallet wallet) =>
            HasOrdinary(wallet, "AI13") ? OrdinaryWoodStockIncrease : 0;

        // Source JASS projections, not additional damage/gold grants:
        // native observed dL and A04Q already include these values.
        // A caller must not add them a second time to a witnessed result.
        public static float SourceLineGoldFactorIncrease(EconomyWallet wallet) =>
            HasOrdinary(wallet, "AI13") ? OrdinaryLineGoldFactorIncrease : 0f;

        public static int SourceLineMagicDefenseLevelIncrease(EconomyWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            int item=Original323FusionSourceItemLedger.WasEverObtained(wallet, "AI19")
                ? UniqueLineMagicDefenseDecrease : 0;
            return item + (int)Math.Min(
                Original323BountyTreasureWorld.OnePieceLineIncrease,
                wallet.Balance(Original323BountyTreasureWorld.LineMagicDefenseIncreaseId));
        }

        // Only record-guidance RV gets AI10's A05D level increase. Shinobu's
        // independent A05D is unchanged. BJI is an informational alarm timer,
        // not the native cast cooldown or a search probability.
        public static int TreasureAlarmSeconds(EconomyWallet wallet,
            bool castByRecordGuidance)
        {
            return castByRecordGuidance && HasOrdinary(wallet, "AI10") ?
                ImprovedTreasureAlarmSeconds : BasicTreasureAlarmSeconds;
        }

        public static double HelperCowExtraManaPerSecond(EconomyWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            double extra = wallet.Balance(
                SourceModelItemExcavation.OrdinaryItemWalletId("AI15")) > 0
                ? OrdinaryCowRegeneration : 0;
            if (Original323FusionSourceItemLedger.WasEverObtained(wallet, "AI18"))
                extra += UniqueCowRegeneration;
            return extra;
        }

        // Combat consumes a single read-only wallet projection. Legacy
        // source:item:AIxx receipts and unique-box transaction history are
        // equally valid, even if an older save lacks the newer acquired flag.
        public static long CombatReceiptBalance(EconomyWallet wallet, string receiptId)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (string.IsNullOrWhiteSpace(receiptId)) return 0;
            string prefix = Original323FusionSourceItemLedger.EverObtainedPrefix;
            if (receiptId.StartsWith(prefix, StringComparison.Ordinal))
            {
                string rawcode = receiptId.Substring(prefix.Length);
                if (Original323FusionSourceItemLedger.FindShell(rawcode) != null)
                    return Original323FusionSourceItemLedger.WasEverObtained(wallet, rawcode)
                        ? 1L : 0L;
            }
            return wallet.Balance(receiptId);
        }

        public static int SourceLineExplosionLevelIncrease(EconomyWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            int item=Original323FusionSourceItemLedger.WasEverObtained(wallet, "AI20")
                ? UniqueLineExplosionPercent : 0;
            return item + (int)Math.Min(
                Original323BountyTreasureWorld.OnePieceLineIncrease,
                wallet.Balance(Original323BountyTreasureWorld.LineExplosionIncreaseId));
        }

        public static bool HasLineExplosionBonus(EconomyWallet wallet) =>
            SourceLineExplosionLevelIncrease(wallet)>0;
    }
}
