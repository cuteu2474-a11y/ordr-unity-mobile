using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrUnpublishedWorldRestoreGraphTests
    {
        [Test]
        public void IncompleteWorldFailsBeforeAnyCombatOrRewardFactoryRuns()
        {
            bool called = false;
            var callbacks = new OrdrUnpublishedWorldRestoreInputs {
                ConfigureCombat = combat => called = true,
                CreateAbilities = combat => {
                    called = true;
                    return null;
                }
            };
            Assert.Throws<FormatException>(() =>
                OrdrWorldRestoreGraphBuilder.Prepare(
                    new OrdrCoreCheckpointState(), callbacks));
            Assert.IsFalse(called,
                "손상된 전체 저장본은 새 전투/보상 콜백까지 도달하면 안 됩니다.");
        }

        [Test]
        public void UnpublishedAuxiliarySubscriptionsCanBeReleasedRepeatedly()
        {
            // A partially reconstructed auxiliary owner must release each
            // subscription once and cannot be published after failure.
            var graph = new OrdrUnpublishedAuxiliaryRestoreGraph();
            Assert.DoesNotThrow(() => graph.Dispose());
            Assert.DoesNotThrow(() => graph.Dispose());
        }

        [Test]
        public void MalformedCompositionNeverBecomesAPlayableContinue()
        {
            Assert.Throws<FormatException>(() =>
                OrdrWorldRestoreGraphBuilder.ValidateReconstructedState(
                    null, null, null, null));
            // Read-only sidecar builder must require a complete world and
            // cannot manufacture missing commerce or source witnesses.
            Assert.Throws<FormatException>(() =>
                OrdrCommerceRestoreGraphBuilder.ValidateCommerceDraft(
                    new OrdrCoreCheckpointState()));
        }
    }
}
