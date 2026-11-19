using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceNativeSummonSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint BlackArrow()
        {
            var status = new CombatStatusEngine();
            status.Advance(10);
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true, NextSummonId = 2
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 17, CatalogId = "S-09", SourceLaneRevision = 3
            });
            state.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 2, OwnerId = 17, LaneRevision = 3,
                SourceUnitRawcode = "h00H",
                SkillName = "모리아 · 블랙 애로우",
                Name = "모리아 일시 좀비 공격 효과",
                HasIndependentCombat = true, IsVirtualAttackEffect = true,
                ExpiresAt = 19, SpawnX = 200, SpawnY = 300,
                AttackStatus = status.CaptureState()
            });
            return state;
        }

        [Test]
        public void BlackArrowKeepsNativeAttackClockButNotInventory()
        {
            var state = BlackArrow();
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Summons[0].ExpiresAt = 20.00001;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].ExpiresAt = 19;
            state.Summons[0].HasIndependentCombat = false;
            state.Summons[0].AttackStatus = null;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void BlackArrowCannotChangeOwnerVisualIdentityOrLane()
        {
            var state = BlackArrow();
            state.Actors[0].CatalogId = "L-12";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Actors[0].CatalogId = "S-09";
            state.Summons[0].IsVirtualAttackEffect = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].IsVirtualAttackEffect = true;
            state.Summons[0].LaneRevision = 2;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].LaneRevision = 3;
            state.Summons[0].AttackStatus.Time = 11;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void NativeToyIdentityLifetimeAndUnresolvedLootAreGuarded()
        {
            var state = BlackArrow();
            var toy = state.Summons[0];
            state.Actors[0].CatalogId = "L-19";
            toy.SourceUnitRawcode = "h06N";
            toy.SkillName = "장난감 No.31";
            toy.Name = "장난감 No.31";
            toy.HasIndependentCombat = false;
            toy.IsVirtualAttackEffect = false;
            toy.AttackStatus = null;
            toy.ExpiresAt = 14.5;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            toy.ExpiresAt = 14.50001;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            toy.ExpiresAt = 14.5;
            toy.HasUnresolvedOriginalLootAbility = true;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void SelfDestructAndDollRemainNonCombatant()
        {
            var state = BlackArrow();
            var toy = state.Summons[0];
            state.Actors[0].CatalogId = "L-19";
            toy.SourceUnitRawcode = "h06P";
            toy.SkillName = "하비하비열매";
            toy.Name = "자폭 장난감";
            toy.HasIndependentCombat = false;
            toy.IsVirtualAttackEffect = false;
            toy.AttackStatus = null;
            toy.ExpiresAt = 13;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            toy.HasIndependentCombat = true;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            toy.HasIndependentCombat = false;
            state.Actors[0].CatalogId = "R-24";
            toy.SourceUnitRawcode = "h06O";
            toy.SkillName = "BpT · 장난감 자폭";
            toy.Name = "슈가 장난감 자폭";
            toy.IsVirtualAttackEffect = true;
            toy.ExpiresAt = 13.5;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            toy.ExpiresAt = 13.50001;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }
    }
}
