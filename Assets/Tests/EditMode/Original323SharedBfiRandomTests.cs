using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Pinned arithmetic fixtures, NOT a live Warcraft seed observation.
    public sealed class Original323SharedBfiRandomTests
    {
        private sealed class Observer : IOriginal323ScriptRandomDrawWitness
        {
            public bool Approved = true;
            public string Evidence = "original JASS BFh draw sequence observation";
            public readonly List<string> Events = new List<string>();

            public bool TryConfirmOriginalDraw(long ordinal,
                Original323ScriptRandomPurpose purpose, int oldState,
                int nextState, int minimum, int maximum, int result,
                out string proof)
            {
                proof = Evidence;
                if (!Approved) return false;
                Events.Add(ordinal + ":" + purpose + ":" + oldState +
                    ">" + nextState + ":" + minimum + "-" + maximum +
                    "=" + result);
                return true;
            }
        }

        private sealed class NativePool : IOriginal323NativeUnitPoolRandom
        {
            public int Calls;
            public int NextInclusive(int minimum, int maximum)
            {
                Calls++;
                return minimum;
            }
        }

        private static string WispJson() => File.ReadAllText(Path.Combine(
            Application.dataPath, "..", "data", "original-2323C-wisp-pools.json"));

        private static Original323SharedBfiRandom Create(Observer observer,
            int originalSeed = 3)
        {
            return new Original323SharedBfiRandom(originalSeed,
                "original observed GetRandomInt native seed", observer);
        }

        [Test]
        public void ExactSharedJassIntegerArithmeticHasPinnedSeedThreeVector()
        {
            var observer = new Observer();
            var script = Create(observer);
            Assert.AreEqual(1, script.NextForPurpose(
                Original323ScriptRandomPurpose.RareWispBranch, 1, 100));
            Assert.AreEqual(207208, script.Snapshot().State);
            Assert.AreEqual(3, script.NextForPurpose(
                Original323ScriptRandomPurpose.MonsterDeathGold, 1, 4));
            Assert.AreEqual(1426747465, script.Snapshot().State);
            Assert.AreEqual(14, script.NextForPurpose(
                Original323ScriptRandomPurpose.RandomWispResource, 1, 100));
            Assert.AreEqual(291020662, script.Snapshot().State);
            Assert.AreEqual(1, script.NextForPurpose(
                Original323ScriptRandomPurpose.Gambling, 1, 4));
            Assert.AreEqual(59158399, script.Snapshot().State);
            Assert.AreEqual(4, script.DrawCount);
            Assert.AreEqual(4, observer.Events.Count);
        }

        [Test]
        public void ScriptWispBranchNativeSelectionGoldAndRandomWispShareOrder()
        {
            var observer = new Observer();
            var script = Create(observer);
            var nativePool = new NativePool();
            var catalog = OriginalWispRewardCatalog.FromOriginalJassJson(
                WispJson(),
                script.ForPurpose(Original323ScriptRandomPurpose.RareWispBranch),
                nativePool);
            var rare = catalog.Roll(WispResourceIds.Rare);
            Assert.AreEqual("h09X", rare.UnitRawcode);
            Assert.AreEqual("A0YE", rare.BonusAbilityRawcode);
            Assert.AreEqual(1, nativePool.Calls);
            Assert.AreEqual(1, script.DrawCount);

            int goldRoll = script.NextForPurpose(
                Original323ScriptRandomPurpose.MonsterDeathGold, 1, 4);
            Assert.AreEqual(3, goldRoll);
            int gold;
            Assert.IsTrue(Original323MonsterGoldRules.TryEvaluateObservedRoll(
                1, goldRoll, 0, out gold));
            Assert.AreEqual(2, gold);

            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 1));
            var game = new GameSession();
            game.Start();
            game.BeginWave(1);
            var exchange = new OriginalRandomWispResourceExchange(wallet, game,
                script.ForPurpose(Original323ScriptRandomPurpose.RandomWispResource));
            var outcome = exchange.SpendOne();
            Assert.AreEqual("wood", outcome.RewardResourceId);
            Assert.AreEqual(1, wallet.Balance("wood"));
            Assert.AreEqual(3, script.DrawCount);
            Assert.IsTrue(observer.Events[0].Contains("RareWispBranch"));
            Assert.IsTrue(observer.Events[1].Contains("MonsterDeathGold"));
            Assert.IsTrue(observer.Events[2].Contains("RandomWispResource"));
        }

        [Test]
        public void NormalWispSelectionUsesNativeOnlyAndNoBfiDraw()
        {
            var observer = new Observer();
            var script = Create(observer);
            var native = new NativePool();
            var catalog = OriginalWispRewardCatalog.FromOriginalJassJson(
                WispJson(),
                script.ForPurpose(Original323ScriptRandomPurpose.RareWispBranch),
                native);
            Assert.AreEqual("h00M", catalog.Roll(WispResourceIds.Uncommon).UnitRawcode);
            Assert.AreEqual(1, native.Calls);
            Assert.AreEqual(0, script.DrawCount);
        }

        [Test]
        public void FailedConfirmationNeverAdvancesTheSharedScriptState()
        {
            var observer = new Observer { Approved = false };
            var script = Create(observer);
            var initial = script.Snapshot();
            Assert.Throws<InvalidOperationException>(() => script.NextForPurpose(
                Original323ScriptRandomPurpose.Gambling, 1, 4));
            Assert.AreEqual(initial.State, script.Snapshot().State);
            Assert.AreEqual(initial.DrawCount, script.DrawCount);
            observer.Approved = true;
            observer.Evidence = "TEST_ONLY: guess";
            Assert.Throws<InvalidOperationException>(() => script.NextForPurpose(
                Original323ScriptRandomPurpose.Gambling, 1, 4));
            Assert.AreEqual(0, script.DrawCount);
            observer.Evidence = "observed original global draw";
            Assert.AreEqual(1, script.NextForPurpose(
                Original323ScriptRandomPurpose.Gambling, 1, 4));
            Assert.AreEqual(1, script.DrawCount);
        }

        [Test]
        public void NativePoolMustBeDistinctAndTwoLaneConstructorRequiresNativeMarker()
        {
            var script = Create(new Observer());
            var lane = script.ForPurpose(Original323ScriptRandomPurpose.RareWispBranch);
            Assert.IsNotNull(lane);
            var native = new NativePool();
            Assert.IsTrue(OriginalWispRewardCatalog.FromOriginalJassJson(
                WispJson(), lane, native).IsVerified(WispResourceIds.Rare));
            Assert.Throws<ArgumentNullException>(() =>
                OriginalWispRewardCatalog.FromOriginalJassJson(
                    WispJson(), lane, (IOriginal323NativeUnitPoolRandom)null));
            Assert.AreEqual(0, script.DrawCount);
        }

        [Test]
        public void ScriptOverflowAndNegativeResultAreNotSilentlyClamped()
        {
            var script = Create(new Observer(), 1);
            Assert.AreEqual(1, script.NextForPurpose(
                Original323ScriptRandomPurpose.OtherOriginalScriptCall, 1, 100));
            Assert.AreEqual(1, script.NextForPurpose(
                Original323ScriptRandomPurpose.OtherOriginalScriptCall, 1, 4));
            // JASS 32-bit signed arithmetic / signed remainder can be negative.
            // A consumer requiring 1..100 MUST reject rather than normalize.
            Assert.AreEqual(-46, script.NextForPurpose(
                Original323ScriptRandomPurpose.OtherOriginalScriptCall, 1, 100));
            Assert.AreEqual(-1017563188, script.Snapshot().State);
        }

        [Test]
        public void InvalidSeedOrStateOrDrawRangeIsRejectedWithoutConsumption()
        {
            Assert.Throws<ArgumentException>(() =>
                Create(new Observer(), 0));
            Assert.Throws<ArgumentException>(() =>
                Create(new Observer(), 1000001));
            Assert.Throws<ArgumentException>(() => new Original323SharedBfiRandom(
                3, "MODEL_ONLY guessed", new Observer()));
            var script = Create(new Observer());
            int prediction, futureState;
            Assert.IsFalse(script.TryPreviewNext(4, 3, out prediction, out futureState));
            Assert.IsFalse(script.TryPreviewNext(int.MinValue, int.MaxValue,
                out prediction, out futureState));
            Assert.AreEqual(0, script.DrawCount);
        }

        [Test]
        public void ResumeRequiresExternalOriginalStateAndContinuesSingleSequence()
        {
            var first = Create(new Observer());
            first.NextForPurpose(Original323ScriptRandomPurpose.RareWispBranch, 1, 100);
            var state = first.Snapshot();
            var second = Original323SharedBfiRandom.FromObservedState(state,
                "observed original H6 and call ordinal on session resume",
                new Observer());
            Assert.AreEqual(1, state.DrawCount);
            Assert.AreEqual(3, second.NextForPurpose(
                Original323ScriptRandomPurpose.MonsterDeathGold, 1, 4));
            Assert.AreEqual(2, second.DrawCount);
            Assert.AreEqual(1, first.DrawCount);
            Assert.Throws<ArgumentException>(() =>
                Original323SharedBfiRandom.FromObservedState(state,
                    "SYNTHETIC snapshot", new Observer()));
        }

        [Test]
        public void GamblingRequiresIndependentRulesDespiteSourceRandomAvailable()
        {
            var shared = Create(new Observer());
            int draw = shared.NextForPurpose(
                Original323ScriptRandomPurpose.Gambling, 1, 4);
            Assert.AreEqual(1, draw);
            var unverified = new GamblingTable("original-gambling");
            Assert.IsFalse(unverified.IsVerified);
            Assert.Throws<InvalidOperationException>(() => unverified.Play());
        }
    }
}
