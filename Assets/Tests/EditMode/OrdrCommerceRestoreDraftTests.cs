using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrCommerceRestoreDraftTests
    {
        [Test]
        public void MissingCommerceOrNativePoolStateCannotBecomeAFreeNewShop()
        {
            var state = new OrdrCoreCheckpointState();
            Assert.Throws<FormatException>(() =>
                OrdrCommerceRestoreGraphBuilder.ValidateCommerceDraft(state));
        }

        [Test]
        public void RollbackStreamReplayDoesNotConsumeSavedAdditionalRandomDraws()
        {
            var script = new MobileRollbackWispRandom(43);
            var native = new MobileRollbackWispRandom(97);
            var graduated = new MobileRollbackWispRandom(19);
            var wisp = new MobileRollbackWispRandom(71);
            script.NextInclusive(1, 1000);
            native.NextInclusive(0, 41);
            graduated.NextInclusive(0, 49);
            wisp.NextInclusive(0, 59);
            var restoredScript = MobileRollbackWispRandom.Restore(script.CaptureState());
            var restoredNative = MobileRollbackWispRandom.Restore(native.CaptureState());
            var restoredGraduated = MobileRollbackWispRandom.Restore(
                graduated.CaptureState());
            var restoredWisp = MobileRollbackWispRandom.Restore(wisp.CaptureState());
            Assert.AreEqual(script.NextInclusive(1, 100), restoredScript.NextInclusive(1, 100));
            Assert.AreEqual(native.NextInclusive(0, 12), restoredNative.NextInclusive(0, 12));
            Assert.AreEqual(graduated.NextInclusive(0, 49),
                restoredGraduated.NextInclusive(0, 49));
            Assert.AreEqual(wisp.NextInclusive(0, 59),
                restoredWisp.NextInclusive(0, 59));
        }
    }
}
