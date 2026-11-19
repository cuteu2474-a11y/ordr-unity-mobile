using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceBulkWalletSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true,
                BulkOwners = new SourceBulkOwnerCheckpoint()
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 4, CatalogId = "MY-08",
                Status = new CombatStatusEngine().CaptureState()
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "MY-13",
                Status = new CombatStatusEngine().CaptureState()
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 12, CatalogId = "I-08",
                Status = new CombatStatusEngine().CaptureState()
            });
            return state;
        }

        private static EconomyWalletState Wallet(params string[] receipts)
        {
            var wallet = new EconomyWalletState();
            wallet.CompletedRewards.AddRange(receipts);
            return wallet;
        }

        [Test]
        public void LiveGiftAndRobberyNeedAllCommittedReceipts()
        {
            var state = Sample();
            state.BulkOwners.GiftSerials.Add(new SourceBulkLongState {
                OwnerId = 4, Value = 2
            });
            state.BulkOwners.GiftSerials.Add(new SourceBulkLongState {
                OwnerId = 9, Value = 1
            });
            var wallet = Wallet(
                "practice-gift:MY-08:4:1", "practice-gift:MY-08:4:2",
                "practice-robbery:MY-13:9:1");
            CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                state, wallet);
            wallet.CompletedRewards.Remove("practice-gift:MY-08:4:1");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, wallet));
        }

        [Test]
        public void ExtraReceiptWithoutCurrentCounterMustNotCreateFreeGrant()
        {
            var state = Sample();
            var wallet = Wallet("practice-gift:MY-08:4:1");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, wallet));
            state.BulkOwners.GiftSerials.Add(new SourceBulkLongState {
                OwnerId = 4, Value = 1
            });
            CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                state, wallet);
            wallet.CompletedRewards.Add("practice-gift:MY-08:4:2");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, wallet));
        }

        [Test]
        public void HistoricalSoldOwnerMustKeepContiguousReceipts()
        {
            var state = Sample();
            var wallet = Wallet("practice-gift:MY-08:1000:1",
                "practice-gift:MY-08:1000:2");
            CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                state, wallet);
            wallet.CompletedRewards.RemoveAt(0);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, wallet));
        }

        [Test]
        public void ReceiptIdentityMustBeCanonicalAndUnique()
        {
            var state = Sample();
            foreach (string receipt in new[] {
                "practice-gift:MY-08:04:1",
                "practice-robbery:MY-13:9:01",
                "practice-gift:MY-08:4:0",
                "practice-gift:MY-08:4:1:extra" })
                Assert.Throws<FormatException>(() =>
                    CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                        state, Wallet(receipt)));
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, Wallet("practice-gift:MY-08:1000:1",
                        "practice-gift:MY-08:1000:1")));
        }

        [Test]
        public void WrongCatalogCannotInheritGiftHistory()
        {
            var state = Sample();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, Wallet("practice-gift:MY-08:9:1")));
        }

        [Test]
        public void AnyaAuraMustBeIndexedInBothDirections()
        {
            var state = Sample();
            var aura = new CombatStatusEngine();
            aura.Apply(new CombatEffect(
                "practice-tooltip:bulk:4:MY-08:passive-ally-speed:12",
                CombatEffectType.AttackSpeedBuff, CombatEffectOrigin.Aura,
                30, 0));
            state.Actors[2].Status = aura.CaptureState();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, Wallet()));
            state.BulkOwners.AnyaRecipients.Add(
                new SourceBulkRecipientState { OwnerId = 4, RecipientId = 12 });
            CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                state, Wallet());
        }

        [Test]
        public void GabanLiveArmorDebuffNeedsMatchingRecordedTarget()
        {
            var state = Sample();
            var status = new CombatStatusEngine();
            status.Apply(new CombatEffect(
                "practice-tooltip:bulk:12:I-08:skill:4:armor-on-hit",
                CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura, 60, 0));
            state.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-3", Kind = CharacterEnemyKind.Line,
                Status = status.CaptureState()
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, Wallet()));
            state.BulkOwners.GabanTargets.Add(new SourceBulkTargetState {
                OwnerId = 12, EnemyId = "line-3"
            });
            CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                state, Wallet());
            state.BulkOwners.GabanTargets[0].EnemyId = "line-4";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkWalletReceipts(
                    state, Wallet()));
        }
    }
}
