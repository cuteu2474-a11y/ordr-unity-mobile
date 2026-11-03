using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Item rawcodes, ownership and rarity are NOT derived from a character tooltip.
    // Only a native-world inventory/equipment witness may authorize a receipt.
    public enum OriginalEquippedStat { Attack, AttackSpeed, Armor }
    public enum OriginalEquippedRarity { Ordinary, Unique, Legendary }

    public interface IOriginalEquippedItemWitness
    {
        bool TryConfirmEquipmentChange(string transactionId, long ownerInstanceId,
            string originalItemRawcode, int signedCount,
            out OriginalEquippedStat stat, out OriginalEquippedRarity rarity,
            out string sourceEvidence);
    }

    // An authoritative, COMPLETE native inventory observation, not a save
    // file, UI list or a count reconstructed from tooltip descriptions.
    // Revisions must increase for each inventory change in this native world.
    public sealed class OriginalNativeEquippedStack
    {
        public string Rawcode { get; }
        public int Count { get; }
        public OriginalEquippedStat Stat { get; }
        public OriginalEquippedRarity Rarity { get; }

        public OriginalNativeEquippedStack(string rawcode, int count,
            OriginalEquippedStat stat, OriginalEquippedRarity rarity)
        {
            Rawcode = rawcode;
            Count = count;
            Stat = stat;
            Rarity = rarity;
        }
    }

    public interface IOriginalEquippedInventorySnapshotWitness
    {
        bool TryReadCompleteInventory(long ownerInstanceId, out long nativeRevision,
            out IReadOnlyList<OriginalNativeEquippedStack> nativeStacks,
            out string sourceEvidence);
    }

    public sealed class OriginalEquippedItemSnapshot
    {
        public int Attack { get; internal set; }
        public int AttackSpeed { get; internal set; }
        public int Armor { get; internal set; }
        public int UniqueAttack { get; internal set; }
        public int UniqueSpeed { get; internal set; }
        public int UniqueArmor { get; internal set; }
        public int LegendaryAttack { get; internal set; }
        public int LegendarySpeed { get; internal set; }
        public int LegendaryArmor { get; internal set; }
        public bool HasUnique => UniqueAttack + UniqueSpeed + UniqueArmor > 0;
        public bool HasLegendary => LegendaryAttack + LegendarySpeed + LegendaryArmor > 0;
        public bool AllUnique => UniqueAttack > 0 && UniqueSpeed > 0 && UniqueArmor > 0;
        public bool AllLegendary => LegendaryAttack > 0 && LegendarySpeed > 0 && LegendaryArmor > 0;
        public int Total => checked(Attack + AttackSpeed + Armor);
    }

    public sealed class OriginalEquippedItemLedger
    {
        private sealed class Entry
        {
            public OriginalEquippedStat Stat;
            public OriginalEquippedRarity Rarity;
            public int Count;
        }

        private readonly IOriginalEquippedItemWitness witness;
        private readonly IOriginalEquippedInventorySnapshotWitness snapshotWitness;
        private readonly Dictionary<long, Dictionary<string, Entry>> byOwner =
            new Dictionary<long, Dictionary<string, Entry>>();
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
        // A delta-only owner must not later import a complete snapshot, nor
        // may a snapshot-backed owner replay an old delta into the balance.
        private readonly HashSet<long> receiptOwners = new HashSet<long>();
        private readonly Dictionary<long, long> snapshotRevisions =
            new Dictionary<long, long>();

        public OriginalEquippedItemLedger(IOriginalEquippedItemWitness source)
            : this(source, null) { }

        public OriginalEquippedItemLedger(IOriginalEquippedItemWitness deltaSource,
            IOriginalEquippedInventorySnapshotWitness completeSource)
        {
            if (deltaSource == null && completeSource == null)
                throw new ArgumentException("원본 아이템 거래 또는 전체 보유 증거가 필요합니다.");
            witness = deltaSource;
            snapshotWitness = completeSource;
        }

        // A failed witness/underflow never changes a count or consumes a receipt.
        // Different equipment rawcodes remain separate even if stat/rarity match.
        public bool TryRecord(string transactionId, long ownerInstanceId,
            string originalItemRawcode, int signedCount)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || ownerInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(originalItemRawcode) ||
                originalItemRawcode.Length != 4 || signedCount == 0 ||
                signedCount == int.MinValue || witness == null ||
                snapshotRevisions.ContainsKey(ownerInstanceId) ||
                receipts.Contains(transactionId)) return false;
            OriginalEquippedStat stat;
            OriginalEquippedRarity rarity;
            string evidence;
            if (!witness.TryConfirmEquipmentChange(transactionId, ownerInstanceId,
                    originalItemRawcode, signedCount, out stat, out rarity,
                    out evidence) ||
                !Enum.IsDefined(typeof(OriginalEquippedStat), stat) ||
                !Enum.IsDefined(typeof(OriginalEquippedRarity), rarity) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence)) return false;
            Dictionary<string, Entry> items;
            byOwner.TryGetValue(ownerInstanceId, out items);
            Entry existing;
            if (items == null || !items.TryGetValue(originalItemRawcode, out existing))
                existing = null;
            if (existing != null &&
                (existing.Stat != stat || existing.Rarity != rarity)) return false;
            long next = (long)(existing == null ? 0 : existing.Count) + signedCount;
            if (next < 0 || next > int.MaxValue) return false;
            long total = next;
            if (items != null)
                foreach (var pair in items)
                {
                    if (pair.Key == originalItemRawcode) continue;
                    total += pair.Value.Count;
                    if (total > int.MaxValue) return false;
                }
            if (items == null)
            {
                items = new Dictionary<string, Entry>(StringComparer.Ordinal);
                byOwner.Add(ownerInstanceId, items);
            }
            if (next == 0) items.Remove(originalItemRawcode);
            else items[originalItemRawcode] = new Entry { Stat = stat, Rarity = rarity,
                Count = (int)next };
            if (items.Count == 0) byOwner.Remove(ownerInstanceId);
            receipts.Add(transactionId);
            receiptOwners.Add(ownerInstanceId);
            return true;
        }

        // Restores a PREEXISTING equipped inventory after a genuine native
        // load or reconnect, and replaces the complete state atomically.
        // Do not mix this full-state stream with incremental receipt events:
        // without a shared event cursor they could double-credit an item.
        public bool TrySynchronizeFromNative(long ownerInstanceId)
        {
            if (ownerInstanceId <= 0 || snapshotWitness == null ||
                receiptOwners.Contains(ownerInstanceId)) return false;
            long revision;
            IReadOnlyList<OriginalNativeEquippedStack> stacks;
            string evidence;
            if (!snapshotWitness.TryReadCompleteInventory(ownerInstanceId,
                    out revision, out stacks, out evidence) ||
                revision < 0 || stacks == null ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence)) return false;
            long previous;
            if (snapshotRevisions.TryGetValue(ownerInstanceId, out previous) &&
                revision <= previous) return false;

            var replacement = new Dictionary<string, Entry>(StringComparer.Ordinal);
            long aggregate = 0;
            foreach (var stack in stacks)
            {
                if (stack == null || string.IsNullOrWhiteSpace(stack.Rawcode) ||
                    stack.Rawcode.Length != 4 || stack.Count <= 0 ||
                    !Enum.IsDefined(typeof(OriginalEquippedStat), stack.Stat) ||
                    !Enum.IsDefined(typeof(OriginalEquippedRarity), stack.Rarity) ||
                    replacement.ContainsKey(stack.Rawcode)) return false;
                aggregate += stack.Count;
                if (aggregate > int.MaxValue) return false;
                replacement.Add(stack.Rawcode, new Entry { Stat = stack.Stat,
                    Rarity = stack.Rarity, Count = stack.Count });
            }
            // No mutations occur until *every* rawcode and count is checked.
            if (replacement.Count == 0) byOwner.Remove(ownerInstanceId);
            else byOwner[ownerInstanceId] = replacement;
            snapshotRevisions[ownerInstanceId] = revision;
            return true;
        }

        public OriginalEquippedItemSnapshot Snapshot(long ownerInstanceId)
        {
            var snapshot = new OriginalEquippedItemSnapshot();
            Dictionary<string, Entry> items;
            if (!byOwner.TryGetValue(ownerInstanceId, out items)) return snapshot;
            foreach (var entry in items.Values)
            {
                switch (entry.Stat)
                {
                    case OriginalEquippedStat.Attack:
                        snapshot.Attack = checked(snapshot.Attack + entry.Count);
                        if (entry.Rarity >= OriginalEquippedRarity.Unique)
                            snapshot.UniqueAttack = checked(snapshot.UniqueAttack + entry.Count);
                        if (entry.Rarity == OriginalEquippedRarity.Legendary)
                            snapshot.LegendaryAttack = checked(snapshot.LegendaryAttack + entry.Count);
                        break;
                    case OriginalEquippedStat.AttackSpeed:
                        snapshot.AttackSpeed = checked(snapshot.AttackSpeed + entry.Count);
                        if (entry.Rarity >= OriginalEquippedRarity.Unique)
                            snapshot.UniqueSpeed = checked(snapshot.UniqueSpeed + entry.Count);
                        if (entry.Rarity == OriginalEquippedRarity.Legendary)
                            snapshot.LegendarySpeed = checked(snapshot.LegendarySpeed + entry.Count);
                        break;
                    case OriginalEquippedStat.Armor:
                        snapshot.Armor = checked(snapshot.Armor + entry.Count);
                        if (entry.Rarity >= OriginalEquippedRarity.Unique)
                            snapshot.UniqueArmor = checked(snapshot.UniqueArmor + entry.Count);
                        if (entry.Rarity == OriginalEquippedRarity.Legendary)
                            snapshot.LegendaryArmor = checked(snapshot.LegendaryArmor + entry.Count);
                        break;
                }
            }
            return snapshot;
        }

        // Unit deletion/fusion consumes the owner's runtime equipment identity.
        // Receipts stay spent, so removing an actor cannot replay loot.
        // Keep receipt/revision tombstones to forbid stale replay after a
        // fusion or deletion, even if the numeric identity is seen again.
        public void RemoveOwner(long ownerInstanceId) { byOwner.Remove(ownerInstanceId); }
    }
}
