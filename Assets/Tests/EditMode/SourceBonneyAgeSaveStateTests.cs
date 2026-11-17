using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceBonneyAgeSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                HasSourceModelBasicAttacks = true,
                BonneyAgeOwners = new SourceBonneyAgeOwnerCheckpoint()
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "TR-21"
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 10, CatalogId = "TR-21"
            });
            state.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-1", Kind = CharacterEnemyKind.Line
            });
            state.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "boss-1", Kind = CharacterEnemyKind.Boss
            });
            return state;
        }

        private static void AddAge(SourceCombatRuntimeCheckpoint state,
            long owner, string enemyId, int amount, bool triggered = false)
        {
            state.BonneyAgeOwners.Stacks.Add(
                new SourceBonneyAgeStackCheckpoint {
                    OwnerId = owner, EnemyId = enemyId, Stacks = amount
                });
            if (triggered)
                state.BonneyAgeOwners.Triggered.Add(
                    new SourceBonneyAgeTriggerCheckpoint {
                        OwnerId = owner, EnemyId = enemyId
                    });
        }

        [Test]
        public void AgeReceiptsRemainDistinctForTwoBonneysAndOneEnemy()
        {
            var state = World();
            AddAge(state, 9, "line-1", 42, true);
            AddAge(state, 10, "line-1", 56, true);
            state.BonneyAgeOwners.Roots.Add(
                new SourceBonneyRootCheckpoint {
                    EnemyId = "line-1",
                    OwnerIds = new System.Collections.Generic.List<long> { 9, 10 }
                });
            CharacterCombatRuntime.ValidateBonneyAgeOwners(state);
            state.BonneyAgeOwners.Roots[0].OwnerIds.RemoveAt(1);
            CharacterCombatRuntime.ValidateBonneyAgeOwners(state);
            state.BonneyAgeOwners.Roots[0].OwnerIds.Add(9);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
        }

        [Test]
        public void AgeTriggerCannotBeForgedBeforeFortyStacks()
        {
            var state = World();
            AddAge(state, 9, "line-1", 39, true);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
            state.BonneyAgeOwners.Stacks[0].Stacks = 40;
            CharacterCombatRuntime.ValidateBonneyAgeOwners(state);
            state.BonneyAgeOwners.Triggered.Add(
                new SourceBonneyAgeTriggerCheckpoint {
                    OwnerId = 9, EnemyId = "line-1"
                });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
        }

        [Test]
        public void RootsOnlyBelongToLivingLineEnemyAndTriggeredOwner()
        {
            var state = World();
            AddAge(state, 9, "boss-1", 60, true);
            state.BonneyAgeOwners.Roots.Add(
                new SourceBonneyRootCheckpoint {
                    EnemyId = "boss-1",
                    OwnerIds = new System.Collections.Generic.List<long> { 9 }
                });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
            state.BonneyAgeOwners.Roots[0].EnemyId = "line-1";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
            state.BonneyAgeOwners.Roots.Clear();
            CharacterCombatRuntime.ValidateBonneyAgeOwners(state);
        }

        [Test]
        public void DeletedOwnerAndEnemyAreNotAssignedFreshAgeRights()
        {
            var state = World();
            AddAge(state, 9, "line-1", 20);
            state.Actors.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
            state = World();
            AddAge(state, 9, "line-1", 20);
            state.Enemies.RemoveAt(0);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
            state = World();
            AddAge(state, 9, "line-1", 20);
            state.HasSourceModelBasicAttacks = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateBonneyAgeOwners(state));
        }
    }
}
