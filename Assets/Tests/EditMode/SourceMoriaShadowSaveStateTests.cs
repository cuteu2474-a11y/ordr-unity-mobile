using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceMoriaShadowSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample()
        {
            var status = new CombatStatusEngine();
            status.Advance(10);
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10,
                HasSourceModelBasicAttacks = true,
                NextSummonId = 3
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 19, CatalogId = "L-12",
                SourceLaneRevision = 4
            });
            state.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 3, OwnerId = 19, LaneRevision = 4,
                SkillName = "BsL · 그림자 소환",
                Name = "모리아 그림자",
                SourceUnitRawcode = "h05D",
                HasIndependentCombat = true,
                IsVirtualAttackEffect = true,
                ExpiresAt = 25, SpawnX = 100, SpawnY = 200,
                AttackStatus = status.CaptureState()
            });
            return state;
        }

        [Test]
        public void IndependentMoriaShadowIsRestorableWithoutRosterIdentity()
        {
            var state = Sample();
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Summons[0].ExpiresAt = 28.00001;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].ExpiresAt = 25;
            state.Summons[0].IsVirtualAttackEffect = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].IsVirtualAttackEffect = true;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
        }

        [Test]
        public void NativeShadowWithoutAttackDiceKeepsOnlyItsVirtualLifecycle()
        {
            var state = Sample();
            state.Summons[0].HasIndependentCombat = false;
            state.Summons[0].AttackStatus = null;
            // Source object h05D may be known as a visual but lack a
            // verified attack profile. Restoring it must not create hits.
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Summons[0].IsVirtualAttackEffect = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].IsVirtualAttackEffect = true;
            state.Actors[0].CatalogId = "L-21";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void UnknownNativeLootCannotBeRestoredAsHarmlessSummon()
        {
            var state = Sample();
            state.Actors[0].CatalogId = "L-19";
            state.Summons[0].SourceUnitRawcode = "h06N";
            state.Summons[0].Name = "장난감 No.31";
            state.Summons[0].SkillName = "장난감 No.31";
            state.Summons[0].IsVirtualAttackEffect = false;
            state.Summons[0].HasIndependentCombat = false;
            state.Summons[0].AttackStatus = null;
            state.Summons[0].ExpiresAt = 13;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Summons[0].HasUnresolvedOriginalLootAbility = true;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void NativeHelperCannotChangeItsOwnerWhileInactive()
        {
            var state = Sample();
            var summon = state.Summons[0];
            summon.HasIndependentCombat = false;
            summon.AttackStatus = null;
            summon.SourceUnitRawcode = "h06O";
            summon.Name = "슈가 장난감 자폭";
            summon.SkillName = "BpT · 장난감 자폭";
            summon.ExpiresAt = 13;
            state.Actors[0].CatalogId = "R-24";
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Actors[0].CatalogId = "L-19";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void MoriaShadowRejectsForeignOwnerAndForgedNativeIdentity()
        {
            var state = Sample();
            state.Actors[0].CatalogId = "E-09";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Actors[0].CatalogId = "L-12";
            state.Summons[0].Name = "unverified actor";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].Name = "모리아 그림자";
            state.Summons[0].SourceUnitRawcode = "h00H";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void MoriaShadowStatusAndLaneMustMatchTheRestoredBattle()
        {
            var state = Sample();
            state.Summons[0].LaneRevision = 3;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].LaneRevision = 4;
            state.Summons[0].AttackStatus.Time = 11;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }
    }
}
