using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceBulkOwnerSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true,
                BulkOwners = new SourceBulkOwnerCheckpoint()
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 4, CatalogId = "MY-07"
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 7, CatalogId = "MY-08",
                Status = new CombatStatusEngine().CaptureState()
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "I-08",
                Status = new CombatStatusEngine().CaptureState()
            });
            return state;
        }

        [Test]
        public void SansCountsAndBonesCannotMoveToAnotherOwner()
        {
            var state = Sample();
            state.BulkOwners.AttackCounts.Add(new SourceBulkIntState {
                OwnerId = 4, Value = 20
            });
            state.BulkOwners.SansBones.Add(new SourceBulkIntState {
                OwnerId = 4, Value = 30
            });
            CharacterCombatRuntime.ValidateSourceBulkOwners(state);
            state.BulkOwners.SansBones[0].OwnerId = 7;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
        }

        [Test]
        public void CooldownsRequireCorrectOwnerAndUniqueKey()
        {
            var state = Sample();
            state.BulkOwners.Cooldowns.Add(new SourceBulkCooldownState {
                OwnerId = 7, SkillKey = "MY-08:skill:6", DueAt = 85
            });
            CharacterCombatRuntime.ValidateSourceBulkOwners(state);
            state.BulkOwners.Cooldowns.Add(new SourceBulkCooldownState {
                OwnerId = 7, SkillKey = "MY-08:skill:6", DueAt = 85
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
            state.BulkOwners.Cooldowns.RemoveAt(1);
            state.BulkOwners.Cooldowns[0].SkillKey = "MY-13:skill:1";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
        }

        [Test]
        public void GabanAndGiftSerialsRetainCatalogOwnership()
        {
            var state = Sample();
            state.BulkOwners.GabanPower.Add(new SourceBulkDoubleState {
                OwnerId = 9, Value = 750000
            });
            state.BulkOwners.GiftSerials.Add(new SourceBulkLongState {
                OwnerId = 7, Value = 3
            });
            state.BulkOwners.GabanTargets.Add(new SourceBulkTargetState {
                OwnerId = 9, EnemyId = "line-3"
            });
            CharacterCombatRuntime.ValidateSourceBulkOwners(state);
            state.BulkOwners.GiftSerials[0].OwnerId = 4;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
        }

        [Test]
        public void AnyaLeaseNeedsMatchingLiveAura()
        {
            var state = Sample();
            state.BulkOwners.AnyaRecipients.Add(new SourceBulkRecipientState {
                OwnerId = 7, RecipientId = 9
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
            var status = new CombatStatusEngine();
            status.Apply(new CombatEffect(
                "practice-tooltip:bulk:7:MY-08:passive-ally-speed:9",
                CombatEffectType.AttackSpeedBuff, CombatEffectOrigin.Aura, 30, 0));
            state.Actors[2].Status = status.CaptureState();
            CharacterCombatRuntime.ValidateSourceBulkOwners(state);
            state.BulkOwners.AnyaRecipients[0].RecipientId = 4;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
        }

        [Test]
        public void AllyRegenExpiryAndModeAreValidated()
        {
            var state = Sample();
            state.BulkOwners.AllyRegen.Add(new SourceBulkDoubleState {
                OwnerId = 9, Value = 13
            });
            CharacterCombatRuntime.ValidateSourceBulkOwners(state);
            state.BulkOwners.AllyRegen[0].Value = 9;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
            state.BulkOwners.AllyRegen[0].Value = 13;
            state.HasSourceModelBasicAttacks = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkOwners(state));
        }
    }
}
