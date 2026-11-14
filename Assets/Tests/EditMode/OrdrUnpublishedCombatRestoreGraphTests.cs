using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrUnpublishedCombatRestoreGraphTests
    {
        [Test]
        public void NeverPublishesPartialGraphWithoutOriginalCombatDependencies()
        {
            var save = new OrdrCoreCheckpointState();
            Assert.Throws<ArgumentNullException>(() =>
                OrdrCombatRestoreGraphBuilder.Prepare(save,
                    null, null, null, null, null, null, null));
        }
    }
}
