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
        private readonly Dictionary<long, Dictionary<string, Entry>> byOwner =
            new Dictionary<long, Dictionary<string, Entry>>();
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);

        public OriginalEquippedItemLedger(IOriginalEquippedItemWitness source)
        { witness = source ?? throw new ArgumentNullException(nameof(source)); }

        // A failed witness/underflow never changes a count or consumes a receipt.
        // Different equipment rawcodes remain separate even if stat/rarity match.
        public bool TryRecord(string transactionId, long ownerInstanceId,
            string originalItemRawcode, int signedCount)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || ownerInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(originalItemRawcode) ||
                originalItemRawcode.Length != 4 || signedCount == 0 ||
                signedCount == int.MinValue || receipts.Contains(transactionId)) return false;
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
        public void RemoveOwner(long ownerInstanceId) { byOwner.Remove(ownerInstanceId); }
    }
}
