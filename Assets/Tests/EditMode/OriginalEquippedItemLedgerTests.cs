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
    }
}
