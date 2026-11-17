using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323NativeDummyResumeLedgerTests
    {
        private static SourceCombatRuntimeCheckpoint Snapshot()
        {
            var combat = new SourceCombatRuntimeCheckpoint { Time = 9.0 };
            var native = new SourceOriginalDummyLedgerCheckpoint {
                OwnerConfigured = true
            };
            native.AttackSequences.Add(
                new SourceOriginalDummyAttackSequenceCheckpoint {
                    AttackerId = "12", Sequence = 4
                });
            native.Pending.Add(new SourceOriginalDummyCastCheckpoint {
                CastId = "original-cast-12", AttackerId = "12",
                OriginalTargetId = "line-target", AttackerRawcode = "h00Q",
                AttackSequence = 4, StartedAt = 7.0, LastImpactAt = 8.0,
                OriginalCastEvidence = "observed original native cast",
                ImmuneSelections = 1,
                SelectedTargets = { "enemy-a", "enemy-b" },
                HitTargets = { "enemy-b" },
                ImpactIds = { "impact-12" }
            });
            native.FinishedCastIds.Add("original-cast-11");
            combat.OriginalNativeDummy = native;
            return combat;
        }

        private static Original323ScriptAttackProcState Attacked()
        {
            var receipts = new Original323ScriptAttackProcState();
            receipts.Receipts.Add(new Original323ScriptAttackProcReceipt {
                SourceFunction = "Bnr", AttackerId = "12",
                TargetId = "line-target", EventSequence = 4,
                GlobalBfiOrdinal = 17, Triggered = true,
                SourceEvidence = "observed original attacked event"
            });
            return receipts;
        }

        [Test]
        public void SourceAttackOwnerAndPendingNativeImpactHistoryValidate()
        {
            var snapshot = Snapshot();
            CharacterCombatRuntime.ValidateOriginalDummyState(snapshot);
            CharacterCombatRuntime.ValidateOriginalDummyAttackReceipts(
                snapshot, Attacked());
        }

        [Test]
        public void RestoredNativeOrdersCannotSilentlyReplayMissingProc()
        {
            var state = Snapshot();
            var proc = Attacked();
            proc.Receipts[0].Triggered = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyAttackReceipts(
                    state, proc));
            proc.Receipts[0].Triggered = true;
            proc.Receipts[0].EventSequence = 5;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyAttackReceipts(
                    state, proc));
        }

        [Test]
        public void TamperedTargetImmuneClockAndDuplicatedIdsFailClosed()
        {
            var state = Snapshot();
            state.OriginalNativeDummy.Pending[0].LastImpactAt = 10;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
            state = Snapshot();
            state.OriginalNativeDummy.Pending[0].SelectedTargets.Add("enemy-b");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
            state = Snapshot();
            state.OriginalNativeDummy.FinishedCastIds.Add("original-cast-12");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
            state = Snapshot();
            state.OriginalNativeDummy.Pending[0].ImmuneSelections = 3;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
            state = Snapshot();
            state.OriginalNativeDummy.AttackSequences[0].Sequence = 3;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
        }

        [Test]
        public void SourceOwnerIsRequiredForNativePendingEffect()
        {
            var state = Snapshot();
            state.OriginalNativeDummy.OwnerConfigured = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyState(state));
            state = Snapshot();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateOriginalDummyAttackReceipts(
                    state, null));
        }
    }
}
