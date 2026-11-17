using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceSatelliteSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World()
        {
            var snapshot = new SourceCombatRuntimeCheckpoint {
                Time = 6, HasSourceModelBasicAttacks = true,
                SatelliteWorld = new SourceSatelliteWorldCheckpoint {
                    NextSatelliteId = 1
                },
                Attacks = new CombatAttackPipelineState {
                    Scheduler = new CombatSchedulerState { Time = 6 },
                    Skills = new CombatSkillTriggerState { Time = 6 }
                }
            };
            snapshot.SatelliteWorld.Satellites.Add(
                new SourceSatelliteAttackCheckpoint {
                    Id = 1, CreatorId = 97, OriginalRawcode = "H0AB",
                    CreatedAt = 2, X = 4, Y = 8, MaximumMana = 90,
                    Mana = 45,
                    RegenBeginsAt = 3, RegenEndsAt = 6,
                    RegenPerSecond = 25,
                    AttackStatus = new CombatStatusEngineState { Time = 6 }
                });
            snapshot.Attacks.Events.Add(new CombatAttackerSequenceState {
                UnitId = "practice-satellite:1", Sequence = 5
            });
            snapshot.Attacks.Scheduler.Attackers.Add(
                new CombatAttackReadyState {
                    UnitId = "practice-satellite:1", DueAt = 7.25
                });
            return snapshot;
        }

        [Test]
        public void DetachedCreatorSatelliteRetainsClockAndManaWithoutRosterCopy()
        {
            var snapshot = World(); // Creator 97 was sold, not in Actors.
            CharacterCombatRuntime.ValidateSourceSatelliteWorldState(snapshot);
            var wallet = new EconomyWalletState();
            wallet.CompletedRewards.Add(
                SourceModelNativeCharacterRules.SatelliteActionId("H0AB"));
            CharacterCombatRuntime.ValidateSatelliteWalletReceipts(
                snapshot, wallet);
            snapshot.SatelliteWorld.Satellites[0].Mana = 90;
            CharacterCombatRuntime.ValidateSourceSatelliteWorldState(snapshot);
        }

        [Test]
        public void MissingOrDuplicatedPurchasesCannotResurrectOrEraseSatellites()
        {
            var snapshot = World();
            var wallet = new EconomyWalletState();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSatelliteWalletReceipts(
                    snapshot, wallet));
            wallet.CompletedRewards.Add(
                SourceModelNativeCharacterRules.SatelliteActionId("H0AB"));
            snapshot.SatelliteWorld.Satellites.Clear();
            snapshot.Attacks.Events.Clear();
            snapshot.Attacks.Scheduler.Attackers.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSatelliteWalletReceipts(
                    snapshot, wallet));
        }

        [Test]
        public void TamperedManaRawcodeAuraAndAttackClockFailClosed()
        {
            var state = World();
            state.SatelliteWorld.Satellites[0].Mana = 91;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
            state = World();
            state.SatelliteWorld.Satellites[0].OriginalRawcode = "h00H";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
            state = World();
            state.SatelliteWorld.Satellites[0].RegenEndsAt = 12;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
            state = World();
            state.SatelliteWorld.NextSatelliteId = 2;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
            state = World();
            state.SatelliteWorld.Satellites.Add(
                new SourceSatelliteAttackCheckpoint {
                    Id = 2, CreatorId = 97, OriginalRawcode = "H0AB",
                    CreatedAt = 5, MaximumMana = 90,
                    AttackStatus = new CombatStatusEngineState { Time = 6 }
                });
            state.SatelliteWorld.NextSatelliteId = 2;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
            state = World();
            state.Attacks.Scheduler.Attackers.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
        }

        [Test]
        public void CreatorIdCollisionMustNotTurnSatelliteIntoForeignCharacter()
        {
            var state = World();
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 97, CatalogId = "TR-14"
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSatelliteWorldState(state));
        }
    }
}
