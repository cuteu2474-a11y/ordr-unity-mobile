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


    // Full source-equipment ledger state: real item rawcodes, duplicate-proof
    // receipts, and native inventory cursors are one indivisible checkpoint.
    [Serializable]
    public sealed class OriginalEquippedStackState
    {
        public string Rawcode;
        public int Count;
        public OriginalEquippedStat Stat;
        public OriginalEquippedRarity Rarity;
    }

    [Serializable]
    public sealed class OriginalEquippedOwnerState
    {
        public long OwnerId;
        public List<OriginalEquippedStackState> Stacks =
            new List<OriginalEquippedStackState>();
    }

    [Serializable]
    public sealed class OriginalEquippedNativeRevisionState
    {
        public long OwnerId;
        public long Revision;
    }

    [Serializable]
    public sealed class OriginalEquippedItemLedgerState
    {
        public int Version = 1;
        public List<OriginalEquippedOwnerState> Owners =
            new List<OriginalEquippedOwnerState>();
        public List<string> Receipts = new List<string>();
        public List<long> ReceiptOwners = new List<long>();
        public List<OriginalEquippedNativeRevisionState> NativeRevisions =
            new List<OriginalEquippedNativeRevisionState>();
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


        public static void ValidateState(OriginalEquippedItemLedgerState state)
        {
            if (state == null || state.Version != 1 || state.Owners == null ||
                state.Receipts == null || state.ReceiptOwners == null ||
                state.NativeRevisions == null)
                throw new FormatException("원본 장비 장부 저장 형식이 불완전합니다.");
            var uniqueReceipts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string receipt in state.Receipts)
                if (string.IsNullOrWhiteSpace(receipt) || !uniqueReceipts.Add(receipt))
                    throw new FormatException("중복되거나 잘못된 장비 거래 영수증입니다.");
            var deltaOwners = new HashSet<long>();
            foreach (long owner in state.ReceiptOwners)
                if (owner <= 0 || !deltaOwners.Add(owner))
                    throw new FormatException("중복된 장비 거래 소유자입니다.");
            if (uniqueReceipts.Count > 0 && deltaOwners.Count == 0)
                throw new FormatException("장비 영수증에 거래 소유자가 없습니다.");
            var nativeOwners = new HashSet<long>();
            foreach (var revision in state.NativeRevisions)
                if (revision == null || revision.OwnerId <= 0 ||
                    revision.Revision < 0 || !nativeOwners.Add(revision.OwnerId) ||
                    deltaOwners.Contains(revision.OwnerId))
                    throw new FormatException("원본 장비 인벤토리 리비전이 잘못됐습니다.");
            var owners = new HashSet<long>();
            foreach (var owner in state.Owners)
            {
                if (owner == null || owner.OwnerId <= 0 ||
                    owner.Stacks == null || owner.Stacks.Count == 0 ||
                    !owners.Add(owner.OwnerId) ||
                    (!deltaOwners.Contains(owner.OwnerId) &&
                     !nativeOwners.Contains(owner.OwnerId)))
                    throw new FormatException("장비 소유자·검증 경로가 잘못됐습니다.");
                var rawcodes = new HashSet<string>(StringComparer.Ordinal);
                long total = 0;
                foreach (var stack in owner.Stacks)
                {
                    if (stack == null || string.IsNullOrWhiteSpace(stack.Rawcode) ||
                        stack.Rawcode.Length != 4 || stack.Count <= 0 ||
                        !Enum.IsDefined(typeof(OriginalEquippedStat), stack.Stat) ||
                        !Enum.IsDefined(typeof(OriginalEquippedRarity), stack.Rarity) ||
                        !rawcodes.Add(stack.Rawcode))
                        throw new FormatException("원본 장비 종류·등급·수량이 잘못됐습니다.");
                    total += stack.Count;
                    if (total > int.MaxValue)
                        throw new FormatException("장비 보유 수량이 원본 한계를 초과합니다.");
                }
            }
        }

        public OriginalEquippedItemLedgerState CaptureState()
        {
            var state = new OriginalEquippedItemLedgerState();
            var ownerIds = new List<long>(byOwner.Keys);
            ownerIds.Sort();
            foreach (long id in ownerIds)
            {
                var row = new OriginalEquippedOwnerState { OwnerId = id };
                var rawcodes = new List<string>(byOwner[id].Keys);
                rawcodes.Sort(StringComparer.Ordinal);
                foreach (string rawcode in rawcodes)
                {
                    var entry = byOwner[id][rawcode];
                    row.Stacks.Add(new OriginalEquippedStackState {
                        Rawcode = rawcode, Count = entry.Count,
                        Stat = entry.Stat, Rarity = entry.Rarity
                    });
                }
                state.Owners.Add(row);
            }
            state.Receipts.AddRange(receipts);
            state.Receipts.Sort(StringComparer.Ordinal);
            state.ReceiptOwners.AddRange(receiptOwners);
            state.ReceiptOwners.Sort();
            var revisionIds = new List<long>(snapshotRevisions.Keys);
            revisionIds.Sort();
            foreach (long id in revisionIds)
                state.NativeRevisions.Add(new OriginalEquippedNativeRevisionState {
                    OwnerId = id, Revision = snapshotRevisions[id]
                });
            ValidateState(state);
            return state;
        }

        // Recreate only the ledger, never replay a native purchase/equip command.
        // Keep spent receipts and even removed owners' cursor tombstones.
        public void RestoreState(OriginalEquippedItemLedgerState state)
        {
            ValidateState(state);
            if (byOwner.Count != 0 || receipts.Count != 0 ||
                receiptOwners.Count != 0 || snapshotRevisions.Count != 0)
                throw new InvalidOperationException(
                    "원본 장비 장부는 새로운 빈 인스턴스에만 복원합니다.");
            if ((state.ReceiptOwners.Count != 0 && witness == null) ||
                (state.NativeRevisions.Count != 0 && snapshotWitness == null))
                throw new NotSupportedException(
                    "원본 장비 거래·전체 인벤토리 증거 공급자를 다시 연결해야 합니다.");
            // All shape, overflow and witness checks finish before mutation.
            foreach (var owner in state.Owners)
            {
                var stacks = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var item in owner.Stacks)
                    stacks.Add(item.Rawcode, new Entry {
                        Count = item.Count, Stat = item.Stat, Rarity = item.Rarity
                    });
                byOwner.Add(owner.OwnerId, stacks);
            }
            foreach (string receipt in state.Receipts) receipts.Add(receipt);
            foreach (long owner in state.ReceiptOwners) receiptOwners.Add(owner);
            foreach (var revision in state.NativeRevisions)
                snapshotRevisions.Add(revision.OwnerId, revision.Revision);
        }

        // Unit deletion/fusion consumes the owner's runtime equipment identity.
        // Receipts stay spent, so removing an actor cannot replay loot.
        // Keep receipt/revision tombstones to forbid stale replay after a
        // fusion or deletion, even if the numeric identity is seen again.
        public void RemoveOwner(long ownerInstanceId) { byOwner.Remove(ownerInstanceId); }
    }
}
