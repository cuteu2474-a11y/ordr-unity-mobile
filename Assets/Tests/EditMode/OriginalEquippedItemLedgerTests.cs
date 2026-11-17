using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalEquippedItemLedgerTests
    {
        private sealed class Witness : IOriginalEquippedItemWitness
        {
            public bool Confirm = true;
            public string Evidence = "native-equipment-purchase:confirmed";
            public OriginalEquippedStat Stat = OriginalEquippedStat.Attack;
            public OriginalEquippedRarity Rarity = OriginalEquippedRarity.Unique;
            public bool TryConfirmEquipmentChange(string receipt, long owner,
                string rawcode, int count, out OriginalEquippedStat stat,
                out OriginalEquippedRarity rarity, out string evidence)
            {
                stat = Stat;
                rarity = Rarity;
                evidence = Evidence;
                return Confirm;
            }
        }


        private sealed class NativeInventory : IOriginalEquippedInventorySnapshotWitness
        {
            public bool Confirm = true;
            public long Revision = 1;
            public string Evidence = "native-inventory:complete-observation";
            public IReadOnlyList<OriginalNativeEquippedStack> Stacks =
                new List<OriginalNativeEquippedStack>();
            public bool TryReadCompleteInventory(long owner,
                out long nativeRevision,
                out IReadOnlyList<OriginalNativeEquippedStack> nativeStacks,
                out string sourceEvidence)
            {
                nativeRevision = Revision;
                nativeStacks = Stacks;
                sourceEvidence = Evidence;
                return Confirm;
            }
        }

        [Test]
        public void ConfirmedPurchaseUsesOneReceiptAndRemovesOnVerifiedUnequip()
        {
            var witness = new Witness();
            var items = new OriginalEquippedItemLedger(witness);
            Assert.IsTrue(items.TryRecord("purchase-1", 7, "Iabc", 2));
            Assert.IsFalse(items.TryRecord("purchase-1", 7, "Iabc", 2));
            Assert.AreEqual(2, items.Snapshot(7).Attack);
            Assert.AreEqual(2, items.Snapshot(7).UniqueAttack);
            Assert.IsFalse(items.Snapshot(7).AllUnique);
            Assert.IsFalse(items.TryRecord("bad-underflow", 7, "Iabc", -3));
            Assert.IsTrue(items.TryRecord("unequip-1", 7, "Iabc", -1));
            Assert.AreEqual(1, items.Snapshot(7).Total);
            items.RemoveOwner(7);
            Assert.AreEqual(0, items.Snapshot(7).Total);
            Assert.IsFalse(items.TryRecord("purchase-1", 7, "Iabc", 2));
        }

        [Test]
        public void RejectedOrSyntheticWitnessCannotCreateItems()
        {
            var witness = new Witness { Confirm = false };
            var items = new OriginalEquippedItemLedger(witness);
            Assert.IsFalse(items.TryRecord("purchase-1", 7, "Iabc", 2));
            witness.Confirm = true;
            witness.Evidence = "MODEL_ONLY equipment preview";
            Assert.IsFalse(items.TryRecord("purchase-1", 7, "Iabc", 2));
            witness.Evidence = "native-equipment-purchase:confirmed";
            Assert.IsTrue(items.TryRecord("purchase-1", 7, "Iabc", 2));
            witness.Rarity = OriginalEquippedRarity.Legendary;
            Assert.IsFalse(items.TryRecord("rarity-swap", 7, "Iabc", 1));
            Assert.AreEqual(2, items.Snapshot(7).Total);
        }

        [Test]
        public void MultiStatRaritySnapshotNeedsAllThreeVerifiedClasses()
        {
            var witness = new Witness();
            var items = new OriginalEquippedItemLedger(witness);
            Assert.IsTrue(items.TryRecord("a", 13, "Iatk", 1));
            witness.Stat = OriginalEquippedStat.AttackSpeed;
            Assert.IsTrue(items.TryRecord("b", 13, "Ispd", 1));
            witness.Stat = OriginalEquippedStat.Armor;
            Assert.IsTrue(items.TryRecord("c", 13, "Iarm", 1));
            Assert.IsTrue(items.Snapshot(13).AllUnique);
            Assert.IsFalse(items.Snapshot(13).AllLegendary);
            Assert.AreEqual(0, items.Snapshot(12).Total);
        }


        [Test]
        public void NativeInventorySupportsPreexistingGearAndNewerCompleteRevisions()
        {
            var native = new NativeInventory { Stacks = new[] {
                new OriginalNativeEquippedStack("Iatk", 3,
                    OriginalEquippedStat.Attack, OriginalEquippedRarity.Unique),
                new OriginalNativeEquippedStack("Ispd", 2,
                    OriginalEquippedStat.AttackSpeed, OriginalEquippedRarity.Legendary),
                new OriginalNativeEquippedStack("Iarm", 1,
                    OriginalEquippedStat.Armor, OriginalEquippedRarity.Legendary)
            } };
            var items = new OriginalEquippedItemLedger(null, native);
            Assert.IsTrue(items.TrySynchronizeFromNative(9));
            Assert.AreEqual(6, items.Snapshot(9).Total);
            Assert.IsTrue(items.Snapshot(9).AllUnique);
            Assert.IsFalse(items.Snapshot(9).AllLegendary);
            Assert.IsFalse(items.TrySynchronizeFromNative(9));
            native.Revision = 2;
            native.Stacks = new[] { new OriginalNativeEquippedStack("Iarm", 1,
                OriginalEquippedStat.Armor, OriginalEquippedRarity.Legendary) };
            Assert.IsTrue(items.TrySynchronizeFromNative(9));
            Assert.AreEqual(1, items.Snapshot(9).Total);
            Assert.AreEqual(0, items.Snapshot(9).Attack);
            native.Revision = 3;
            native.Stacks = new OriginalNativeEquippedStack[0];
            Assert.IsTrue(items.TrySynchronizeFromNative(9));
            Assert.AreEqual(0, items.Snapshot(9).Total);
        }


        [Test]
        public void InvalidSnapshotIsAtomicAndCannotReplayOlderRevision()
        {
            var native = new NativeInventory { Revision = 8, Stacks = new[] {
                new OriginalNativeEquippedStack("Iatk", 2,
                    OriginalEquippedStat.Attack, OriginalEquippedRarity.Ordinary)
            } };
            var items = new OriginalEquippedItemLedger(null, native);
            Assert.IsTrue(items.TrySynchronizeFromNative(17));
            native.Revision = 9;
            native.Stacks = new[] {
                new OriginalNativeEquippedStack("Ispd", 10,
                    OriginalEquippedStat.AttackSpeed, OriginalEquippedRarity.Unique),
                new OriginalNativeEquippedStack("Ispd", 1,
                    OriginalEquippedStat.Armor, OriginalEquippedRarity.Legendary)
            };
            Assert.IsFalse(items.TrySynchronizeFromNative(17));
            Assert.AreEqual(2, items.Snapshot(17).Attack);
            native.Stacks = new[] { new OriginalNativeEquippedStack("Ispd", int.MaxValue,
                OriginalEquippedStat.AttackSpeed, OriginalEquippedRarity.Unique),
                new OriginalNativeEquippedStack("Iarm", 1,
                OriginalEquippedStat.Armor, OriginalEquippedRarity.Unique) };
            Assert.IsFalse(items.TrySynchronizeFromNative(17));
            native.Evidence = "MODEL_ONLY UI preview";
            native.Stacks = new OriginalNativeEquippedStack[0];
            Assert.IsFalse(items.TrySynchronizeFromNative(17));
            native.Evidence = "native-inventory:complete-observation";
            native.Revision = 7;
            Assert.IsFalse(items.TrySynchronizeFromNative(17));
            Assert.AreEqual(2, items.Snapshot(17).Total);
        }


        [Test]
        public void CompleteSnapshotsAndDeltaReceiptsCannotDoubleCredit()
        {
            var receipt = new Witness();
            var native = new NativeInventory { Stacks = new[] {
                new OriginalNativeEquippedStack("Iatk", 2,
                    OriginalEquippedStat.Attack, OriginalEquippedRarity.Unique)
            } };
            var items = new OriginalEquippedItemLedger(receipt, native);
            Assert.IsTrue(items.TrySynchronizeFromNative(1));
            Assert.IsFalse(items.TryRecord("old-native-receipt", 1, "Iatk", 2));
            Assert.AreEqual(2, items.Snapshot(1).Total);
            Assert.IsTrue(items.TryRecord("native-delta", 2, "Iatk", 1));
            Assert.IsFalse(items.TrySynchronizeFromNative(2));
            items.RemoveOwner(1);
            Assert.AreEqual(0, items.Snapshot(1).Total);
            Assert.IsFalse(items.TryRecord("replayed-after-removal", 1, "Iatk", 1));
            native.Revision = 2;
            // A witnessed newer complete revision may rehydrate a surviving
            // runtime actor, but the runtime itself rejects removed actors.
            Assert.IsTrue(items.TrySynchronizeFromNative(1));
        }



        [Test]
        public void CheckpointPreservesReceiptsAndFullNativeRevisionWithoutRegrantingGear()
        {
            var witness = new Witness();
            var native = new NativeInventory { Revision = 8, Stacks = new[] {
                new OriginalNativeEquippedStack("Iarm", 3,
                    OriginalEquippedStat.Armor, OriginalEquippedRarity.Legendary)
            }};
            var original = new OriginalEquippedItemLedger(witness, native);
            Assert.IsTrue(original.TryRecord("equip-7", 7, "Iatk", 2));
            Assert.IsTrue(original.TrySynchronizeFromNative(9));
            original.RemoveOwner(7);
            var state = original.CaptureState();
            var recovered = new OriginalEquippedItemLedger(witness, native);
            recovered.RestoreState(state);
            Assert.AreEqual(0, recovered.Snapshot(7).Total);
            Assert.AreEqual(3, recovered.Snapshot(9).LegendaryArmor);
            Assert.IsFalse(recovered.TryRecord("equip-7", 7, "Iatk", 2));
            Assert.IsFalse(recovered.TrySynchronizeFromNative(9));
            Assert.IsFalse(recovered.TryRecord("equip-other", 9, "Iatk", 1));
            native.Revision = 9;
            native.Stacks = new[] { new OriginalNativeEquippedStack("Iarm", 1,
                OriginalEquippedStat.Armor, OriginalEquippedRarity.Legendary) };
            Assert.IsTrue(recovered.TrySynchronizeFromNative(9));
            Assert.AreEqual(1, recovered.Snapshot(9).Total);
        }

        [Test]
        public void TamperedOrUnwitnessedEquipmentSaveFailsBeforeAnyMutation()
        {
            var receipt = new Witness();
            var native = new NativeInventory { Stacks = new[] {
                new OriginalNativeEquippedStack("Ispd", 1,
                    OriginalEquippedStat.AttackSpeed, OriginalEquippedRarity.Unique)
            }};
            var original = new OriginalEquippedItemLedger(receipt, native);
            Assert.IsTrue(original.TryRecord("equip-1", 2, "Iatk", 1));
            Assert.IsTrue(original.TrySynchronizeFromNative(3));
            var state = original.CaptureState();
            var empty = new OriginalEquippedItemLedger(receipt, native);
            state.Owners[0].Stacks[0].Count = -10;
            Assert.Throws<FormatException>(() => empty.RestoreState(state));
            Assert.AreEqual(0, empty.CaptureState().Owners.Count);
            state.Owners[0].Stacks[0].Count = 1;
            Assert.Throws<NotSupportedException>(() =>
                new OriginalEquippedItemLedger(receipt).RestoreState(state));
            Assert.Throws<NotSupportedException>(() =>
                new OriginalEquippedItemLedger(null, native).RestoreState(state));
            state.NativeRevisions[0].OwnerId = 2;
            Assert.Throws<FormatException>(() => empty.RestoreState(state));
            Assert.AreEqual(0, empty.CaptureState().Owners.Count);
        }

        [Test]
        public void DeltaAcrossDifferentRawcodesCannotOverflowTotal()
        {
            var witness = new Witness();
            var items = new OriginalEquippedItemLedger(witness);
            Assert.IsTrue(items.TryRecord("full-1", 20, "Iatk", int.MaxValue));
            witness.Stat = OriginalEquippedStat.Armor;
            Assert.IsFalse(items.TryRecord("over-limit", 20, "Iarm", 1));
            Assert.AreEqual(int.MaxValue, items.Snapshot(20).Total);
        }


        [Test]
        public void FullInventoryRejectsInvalidItemMetadataWithoutChangingPreviousRevision()
        {
            var native = new NativeInventory { Stacks = new[] {
                new OriginalNativeEquippedStack("Iatk", 1,
                    OriginalEquippedStat.Attack, OriginalEquippedRarity.Unique)
            } };
            var items = new OriginalEquippedItemLedger(null, native);
            Assert.IsTrue(items.TrySynchronizeFromNative(23));
            native.Revision = 2;
            native.Stacks = new[] { new OriginalNativeEquippedStack("X", 1,
                OriginalEquippedStat.Attack, OriginalEquippedRarity.Unique) };
            Assert.IsFalse(items.TrySynchronizeFromNative(23));
            native.Stacks = new[] { new OriginalNativeEquippedStack("Ispd", 1,
                (OriginalEquippedStat)99, OriginalEquippedRarity.Legendary) };
            Assert.IsFalse(items.TrySynchronizeFromNative(23));
            native.Stacks = null;
            Assert.IsFalse(items.TrySynchronizeFromNative(23));
            Assert.AreEqual(1, items.Snapshot(23).UniqueAttack);
            native.Stacks = new[] { new OriginalNativeEquippedStack("Ispd", 1,
                OriginalEquippedStat.AttackSpeed, OriginalEquippedRarity.Legendary) };
            Assert.IsTrue(items.TrySynchronizeFromNative(23));
            Assert.AreEqual(1, items.Snapshot(23).LegendarySpeed);
        }
    }
}
