using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalWispRewardCatalogTests
    {
        private sealed class FixedRandom : IOriginalWispRandom
        {
            private readonly Queue<int> rolls = new Queue<int>();
            public int Calls { get; private set; }
            public FixedRandom(params int[] values)
            {
                foreach (var value in values) rolls.Enqueue(value);
            }
            public int NextInclusive(int min, int max)
            {
                Calls++;
                return rolls.Count == 0 ? min : rolls.Dequeue();
            }
        }

        private static string Source() => File.ReadAllText(Path.Combine(
            Application.dataPath, "..", "data", "original-2323C-wisp-pools.json"));

        [Test]
        public void AllFourPoolsLoadDirectlyFromPinnedOriginalJass()
        {
            var roller = OriginalWispRewardCatalog.FromOriginalJassJson(Source(), new FixedRandom());
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Uncommon));
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Special));
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Rare));
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Legendary));
            Assert.IsFalse(roller.IsVerified(WispResourceIds.Random));
            Assert.IsFalse(roller.IsVerified(WispResourceIds.Choice));
            Assert.AreEqual("h00M", roller.Roll(WispResourceIds.Uncommon).UnitRawcode);
            Assert.AreEqual("h01K", roller.Roll(WispResourceIds.Special).UnitRawcode);
            Assert.AreEqual("h03K", roller.Roll(WispResourceIds.Legendary).UnitRawcode);
        }

        [Test]
        public void RareWisp99Versus1BranchAppliesA0YEOnlyToBasicPool()
        {
            var basic = OriginalWispRewardCatalog.FromOriginalJassJson(Source(),
                new FixedRandom(99, 0)).Roll(WispResourceIds.Rare);
            Assert.AreEqual("h09X", basic.UnitRawcode);
            Assert.AreEqual("A0YE", basic.BonusAbilityRawcode);
            var special = OriginalWispRewardCatalog.FromOriginalJassJson(Source(),
                new FixedRandom(100, 0)).Roll(WispResourceIds.Rare);
            Assert.AreEqual("h05H", special.UnitRawcode);
            Assert.IsNull(special.BonusAbilityRawcode);
        }

        [Test]
        public void OriginalRareWispOnePercentBranchContainsGaimonAsEighthUnit()
        {
            // CSI: BFi(1,100)==100 selects sw pool, whose 8th entry is h0BG.
            // The separate native PlaceRandomUnit chooses index 7.
            var rare = OriginalWispRewardCatalog.FromOriginalJassJson(Source(),
                new FixedRandom(100, 7)).Roll(WispResourceIds.Rare);
            Assert.AreEqual("h0BG", rare.UnitRawcode);
            Assert.IsNull(rare.BonusAbilityRawcode);
        }

        [Test]
        public void NonRareWispDoesNotDrawAnExtraPercentageRoll()
        {
            var random = new FixedRandom(0);
            var catalog = OriginalWispRewardCatalog.FromOriginalJassJson(Source(), random);
            Assert.AreEqual("h00M", catalog.Roll(WispResourceIds.Uncommon).UnitRawcode);
            Assert.AreEqual(1, random.Calls);
        }

        [Test]
        public void AlteredPoolOrOutOfBoundsRngNeverCreatesRewards()
        {
            var source = Source();
            Assert.Throws<FormatException>(() => OriginalWispRewardCatalog.FromOriginalJassJson(
                source.Replace("h00M", "hXYZ"), new FixedRandom()));
            var roller = OriginalWispRewardCatalog.FromOriginalJassJson(source,
                new FixedRandom(101));
            Assert.Throws<InvalidOperationException>(() => roller.Roll(WispResourceIds.Uncommon));
        }
    }
}
