using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // S03 unpublished restore fence. The wallet owns reward receipts;
    // the combat runtime owns the last completed serial. Both sides must
    // agree before a pending future proc can safely resume.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class BulkRewardReceiptRange
        {
            public long OwnerId;
            public string CatalogId;
            public long Count;
            public long Maximum;
        }

        private const string AnyaGiftReceiptPrefix = "practice-gift:MY-08:";
        private const string HigumaRobberyReceiptPrefix =
            "practice-robbery:MY-13:";
        private const string GabanArmorEffectSuffix =
            ":I-08:skill:4:armor-on-hit";
        private const string AnyaSpeedEffectMarker =
            ":MY-08:passive-ally-speed:";

        // Do not infer a new payout from the saved counter. A missing
        // receipt, a future serial or a gap in ANY historical owner receipt
        // set is a corrupt world, even after the owner has been sold.
        public static void ValidateSourceBulkWalletReceipts(
            SourceCombatRuntimeCheckpoint combat, EconomyWalletState wallet)
        {
            ValidateSourceBulkOwners(combat);
            if (wallet == null || wallet.CompletedRewards == null)
                throw new FormatException(
                    "추가 자동 보상과 결합할 공유 지갑 영수증이 없습니다.");
            var live = new Dictionary<long, string>();
            foreach (var actor in combat.Actors)
                live.Add(actor.InstanceId, actor.CatalogId);
            var groups = new Dictionary<string, BulkRewardReceiptRange>(
                StringComparer.Ordinal);
            var allReceipts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string receipt in wallet.CompletedRewards)
            {
                if (string.IsNullOrWhiteSpace(receipt) ||
                    !allReceipts.Add(receipt))
                    throw new FormatException(
                        "공유 지갑 영수증이 없거나 중복됐습니다.");
                bool gift = receipt.StartsWith(AnyaGiftReceiptPrefix,
                    StringComparison.Ordinal);
                bool robbery = receipt.StartsWith(HigumaRobberyReceiptPrefix,
                    StringComparison.Ordinal);
                if (!gift && !robbery) continue;
                string prefix = gift ? AnyaGiftReceiptPrefix :
                    HigumaRobberyReceiptPrefix;
                string catalog = gift ? "MY-08" : "MY-13";
                int split = receipt.IndexOf(':', prefix.Length);
                long owner, serial;
                if (split <= prefix.Length || split >= receipt.Length - 1 ||
                    !long.TryParse(receipt.Substring(prefix.Length,
                        split - prefix.Length), NumberStyles.None,
                        CultureInfo.InvariantCulture, out owner) ||
                    !long.TryParse(receipt.Substring(split + 1),
                        NumberStyles.None, CultureInfo.InvariantCulture,
                        out serial) || owner <= 0 || serial <= 0 ||
                    receipt != prefix +
                        owner.ToString(CultureInfo.InvariantCulture) + ":" +
                        serial.ToString(CultureInfo.InvariantCulture))
                    throw new FormatException(
                        "선물/강탈 영수증의 캐릭터 ID·순번이 잘못됐습니다.");
                string currentCatalog;
                if (live.TryGetValue(owner, out currentCatalog) &&
                    currentCatalog != catalog)
                    throw new FormatException(
                        "현재 실물에 다른 캐릭터 보상 영수증이 결합됐습니다.");
                string groupKey = prefix +
                    owner.ToString(CultureInfo.InvariantCulture);
                BulkRewardReceiptRange range;
                if (!groups.TryGetValue(groupKey, out range))
                    groups.Add(groupKey, range = new BulkRewardReceiptRange {
                        OwnerId = owner, CatalogId = catalog
                    });
                range.Count++;
                if (serial > range.Maximum) range.Maximum = serial;
            }
            var activeSerials = new Dictionary<long, long>();
            foreach (var record in combat.BulkOwners.GiftSerials)
            {
                string catalog = live[record.OwnerId];
                string prefix = catalog == "MY-08" ?
                    AnyaGiftReceiptPrefix : HigumaRobberyReceiptPrefix;
                string key = prefix +
                    record.OwnerId.ToString(CultureInfo.InvariantCulture);
                BulkRewardReceiptRange range;
                if (!groups.TryGetValue(key, out range) ||
                    range.Count != record.Value ||
                    range.Maximum != record.Value)
                    throw new FormatException(
                        "캐릭터별 지급 횟수와 실제 공유 지갑 영수증이 다릅니다.");
                activeSerials.Add(record.OwnerId, record.Value);
            }
            foreach (var range in groups.Values)
            {
                // Unique positive canonical serials of cardinality N
                // with maximum N must be exactly 1..N.
                if (range.Count != range.Maximum)
                    throw new FormatException(
                        "판매된 캐릭터를 포함한 보상 지급 순번이 끊겼습니다.");
                if (live.ContainsKey(range.OwnerId) &&
                    !activeSerials.ContainsKey(range.OwnerId))
                    throw new FormatException(
                        "지갑 지급 영수증이 있지만 전투 지급 순번이 없습니다.");
            }
            ValidateSourceBulkStatusReceipts(combat);
        }

        // Live statuses are a second owner ledger. A lone aura/debuff in
        // the restored HP/status graph must not outlive its indexed source.
        private static void ValidateSourceBulkStatusReceipts(
            SourceCombatRuntimeCheckpoint combat)
        {
            var anya = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in combat.BulkOwners.AnyaRecipients)
                anya.Add(AnyaAuraKey(row.OwnerId, row.RecipientId));
            var observedAnya = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actor in combat.Actors)
            {
                if (actor.Status == null || actor.Status.Effects == null) continue;
                foreach (var effect in actor.Status.Effects)
                {
                    if (effect == null || effect.SourceId == null ||
                        !effect.SourceId.StartsWith(
                            "practice-tooltip:bulk:", StringComparison.Ordinal) ||
                        effect.SourceId.IndexOf(AnyaSpeedEffectMarker,
                            StringComparison.Ordinal) < 0) continue;
                    if (!anya.Contains(effect.SourceId) ||
                        !observedAnya.Add(effect.SourceId) ||
                        effect.Type != CombatEffectType.AttackSpeedBuff ||
                        effect.Origin != CombatEffectOrigin.Aura ||
                        !effect.NeverExpires ||
                        effect.Amount != 30 || effect.DurationSeconds != 0)
                        throw new FormatException(
                            "소유자가 없는 아냐 오라 버프가 남아 있습니다.");
                }
            }
            if (observedAnya.Count != anya.Count)
                throw new FormatException(
                    "아냐 버프의 실제 대상과 수신자 장부가 다릅니다.");

            var gaban = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in combat.BulkOwners.GabanTargets)
                gaban.Add(
                    "practice-tooltip:bulk:" +
                        row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                        GabanArmorEffectSuffix, row.EnemyId);
            var observedGaban = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
            {
                if (enemy == null || enemy.Status == null ||
                    enemy.Status.Effects == null) continue;
                foreach (var effect in enemy.Status.Effects)
                {
                    if (effect == null || effect.SourceId == null ||
                        !effect.SourceId.StartsWith(
                            "practice-tooltip:bulk:", StringComparison.Ordinal) ||
                        !effect.SourceId.EndsWith(GabanArmorEffectSuffix,
                            StringComparison.Ordinal)) continue;
                    string target;
                    double expected = enemy.Kind == CharacterEnemyKind.Boss ||
                        enemy.Kind == CharacterEnemyKind.Enraged ? 70 : 60;
                    if (!gaban.TryGetValue(effect.SourceId, out target) ||
                        target != enemy.EnemyId ||
                        !observedGaban.Add(effect.SourceId) ||
                        effect.Type != CombatEffectType.ArmorReduction ||
                        effect.Origin != CombatEffectOrigin.Aura ||
                        !effect.NeverExpires ||
                        effect.Amount != expected ||
                        effect.DurationSeconds != 0)
                        throw new FormatException(
                            "가반 방어 감소의 실제 대상과 영수증이 다릅니다.");
                }
            }
            // A prior target may have died and been removed from the
            // enemy world without another hit; preserve that inert pointer.
        }
    }
}
