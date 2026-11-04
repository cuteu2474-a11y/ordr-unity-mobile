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
    // Native item acquisition has no verified source producer in this port:
    // only a witnessed, unique inventory receipt may credit the saved wallet.
    public interface IOriginal323FusionItemReceiptWitness
    {
        // Witness MUST confirm the original single player's actual item gain,
        // source transaction identity, and quantity (not merely UI selection).
        bool TryConfirmPlayerItemReceipt(string receiptId, string originalItemRawcode,
            int awardedCount, out string nativeEvidence);
    }

    // Temporary inventory presentation only. It does not grant an item,
    // define a usable spell, alter combat stats, or invent a loot producer.
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
        private static readonly IReadOnlyList<Original323FusionItemShell> itemShells =
            Array.AsReadOnly(new[] {
                new Original323FusionItemShell(UtaItemRawcode, "우타(영원) 조합 재료"),
                new Original323FusionItemShell(NikaItemRawcode, "니카(영원) 조합 재료"),
                new Original323FusionItemShell(MarcoItemRawcode, "마르코(제한) 조합 재료")
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
            if (wallet.HasCompletedTransaction(receipt)) return false;
            string evidence;
            if (!witness.TryConfirmPlayerItemReceipt(receiptId, originalItemRawcode,
                    count, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            try
            {
                return wallet.TryGrantOnce(receipt,
                    new ResourceBundle().Add(WalletId(originalItemRawcode), count));
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
}
