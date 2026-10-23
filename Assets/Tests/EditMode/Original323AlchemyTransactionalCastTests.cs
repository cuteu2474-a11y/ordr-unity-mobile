using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323AlchemyTransactionalCastTests
    {
        private sealed class TestWorld : IOrdr323AlchemyTransactionalWorld
        {
            public readonly Ordr323OriginalHelperCowActor Helper;
            public Ordr323AlchemyTargetEvidence Target =
                new Ordr323AlchemyTargetEvidence(12, "h00O", 0, 1, 0);
            public bool Exists = true;
            public int Granted;
            public int Begun;
            public int Applied;
            public int Committed;
            public bool BlockNative;
            public bool RejectAfterMutation;
            public bool ThrowDuringCommit;
            public bool LastRecipeWasNull;
            public double LastRange;

            public TestWorld(Ordr323OriginalHelperCowActor helper) { Helper = helper; }

            public IOrdr323AlchemyTransaction BeginAlchemyTransaction()
            {
                Begun++;
                return new Transaction(this);
            }

            private sealed class Transaction : IOrdr323AlchemyTransaction
            {
                private readonly TestWorld world;
                private readonly double beforeMana;
                private readonly bool beforeExists;
                private readonly int beforeGranted;
                private bool committed;
                private bool disposed;

                public Transaction(TestWorld source)
                {
                    world = source;
                    beforeMana = source.Helper.CurrentMana;
                    beforeExists = source.Exists;
                    beforeGranted = source.Granted;
                }

                public bool TryInspectTarget(long id, out Ordr323AlchemyTargetEvidence target)
                {
                    target = world.Exists && world.Target.InstanceId == id ?
                        world.Target : null;
                    return target != null;
                }

                public bool TryVerifyOriginalNativeCast(IOriginalHelperCowCombatActor caster,
                    long id, double range, out string reason)
                {
                    world.LastRange = range;
                    reason = world.BlockNative ? "원본 스킬 봉인" : null;
                    return !world.BlockNative && ReferenceEquals(caster, world.Helper) &&
                        world.Exists && world.Target.InstanceId == id;
                }

                public bool TryApplyOriginalAlchemy(long id,
                    Ordr323AlchemyRefundRecipe recipe, double manaCost,
                    double cooldown, out string reason)
                {
                    world.Applied++;
                    Assert.AreEqual(100, manaCost);
                    Assert.AreEqual(5, cooldown);
                    Assert.IsTrue(world.Exists);
                    Assert.AreEqual(world.Target.InstanceId, id);
                    world.Helper.SetMana(world.Helper.CurrentMana - manaCost);
                    world.Exists = false;
                    world.LastRecipeWasNull = recipe == null;
                    if (recipe != null)
                        foreach (var entry in recipe.RefundUnits)
                            world.Granted += entry.Count;
                    reason = world.RejectAfterMutation ? "가상 생성 실패" : null;
                    return !world.RejectAfterMutation;
                }

                public void Commit()
                {
                    if (world.ThrowDuringCommit)
                        throw new InvalidOperationException("가상 최종 커밋 실패");
                    world.Committed++;
                    committed = true;
                }

                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    if (committed) return;
                    world.Helper.SetMana(beforeMana);
                    world.Exists = beforeExists;
                    world.Granted = beforeGranted;
                }
            }
        }

        private static Ordr323OriginalAlchemyRefundCatalog Refunds()
        {
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-2323C-helper-alchemy-recipes");
            Assert.IsNotNull(asset);
            return Ordr323OriginalAlchemyRefundCatalog.FromJson(asset.text);
        }

        private static GameSession StartAlchemy(Ordr323OriginalHelperCowActor helper)
        {
            var game = new GameSession();
            game.Start();
            for (int wave = 1; wave <= 21; wave++) game.BeginWave(wave);
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, helper).TryChooseAlchemy());
            helper.SetMana(400);
            return game;
        }

        [Test]
        public void ConfirmedOriginalTargetIsConsumedOnceAndCooldownUsesSameMana()
        {
            var helper = new Ordr323OriginalHelperCowActor(0);
            var game = StartAlchemy(helper);
            var world = new TestWorld(helper);
            double time = 30;
            var cast = new Ordr323OriginalAlchemyCastService(game, helper,
                Refunds(), world, () => time);

            Assert.IsTrue(cast.CanAttempt);
            Assert.AreEqual(ManualActionState.Succeeded, cast.TryCast(12).State);
            Assert.AreEqual(300, helper.CurrentMana);
            Assert.IsFalse(world.Exists);
            Assert.AreEqual(2, world.Granted); // h00O -> h001 and h004
            Assert.AreEqual(1, world.Applied);
            Assert.AreEqual(1, world.Committed);
            Assert.AreEqual(Ordr323OriginalHelperAlchemy.CastRange, world.LastRange);
            Assert.AreEqual(5, cast.CooldownRemaining);

            world.Target = new Ordr323AlchemyTargetEvidence(13, "h00O", 0, 1, 0);
            world.Exists = true;
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(13).State);
            Assert.AreEqual(1, world.Begun); // cooldown blocks even opening a transaction
            time = 35;
            Assert.AreEqual(ManualActionState.Succeeded, cast.TryCast(13).State);
            Assert.AreEqual(200, helper.CurrentMana);
            Assert.AreEqual(4, world.Granted);
        }

        [TestCase(1, 1, 0, "h00O", true)]
        [TestCase(0, 0, 0, "h00O", true)]
        [TestCase(0, 0, 0, "h015", false)]
        public void PreflightRejectsForeignOrNonGradeWithoutSpending(
            int owner, int a0ba, int a0b9, string rawcode, bool rejected)
        {
            var helper = new Ordr323OriginalHelperCowActor(0);
            var world = new TestWorld(helper);
            world.Target = new Ordr323AlchemyTargetEvidence(12, rawcode, owner, a0ba, a0b9);
            var cast = new Ordr323OriginalAlchemyCastService(
                StartAlchemy(helper), helper, Refunds(), world, () => 0);
            var result = cast.TryCast(12);
            Assert.AreEqual(rejected ? ManualActionState.Rejected :
                ManualActionState.Succeeded, result.State);
            if (rejected)
            {
                Assert.AreEqual(400, helper.CurrentMana);
                Assert.IsTrue(world.Exists);
                Assert.AreEqual(0, world.Applied);
            }
        }

        [Test]
        public void NativeGateAndPartialFailureLeaveNoSpendingOrCooldown()
        {
            var helper = new Ordr323OriginalHelperCowActor(0);
            var world = new TestWorld(helper) { BlockNative = true };
            double time = 10;
            var cast = new Ordr323OriginalAlchemyCastService(
                StartAlchemy(helper), helper, Refunds(), world, () => time);
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(12).State);
            Assert.AreEqual(0, world.Applied);
            world.BlockNative = false;
            world.RejectAfterMutation = true;
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(12).State);
            Assert.IsTrue(world.Exists);
            Assert.AreEqual(400, helper.CurrentMana);
            Assert.AreEqual(0, world.Granted);
            Assert.AreEqual(0, cast.CooldownRemaining);

            world.RejectAfterMutation = false;
            world.ThrowDuringCommit = true;
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(12).State);
            Assert.IsTrue(world.Exists);
            Assert.AreEqual(400, helper.CurrentMana);
            Assert.AreEqual(0, world.Granted);
            Assert.AreEqual(0, cast.CooldownRemaining);
            world.ThrowDuringCommit = false;
            Assert.AreEqual(ManualActionState.Succeeded, cast.TryCast(12).State);
        }

        [Test]
        public void MissingR3RecipeStillConsumesEligibleTargetWithoutFabricatingRefund()
        {
            var helper = new Ordr323OriginalHelperCowActor(0);
            var world = new TestWorld(helper);
            world.Target = new Ordr323AlchemyTargetEvidence(12, "zzzz", 0, 0, 1);
            var cast = new Ordr323OriginalAlchemyCastService(
                StartAlchemy(helper), helper, Refunds(), world, () => 5);
            Assert.AreEqual(ManualActionState.Succeeded, cast.TryCast(12).State);
            Assert.IsFalse(world.Exists);
            Assert.IsTrue(world.LastRecipeWasNull);
            Assert.AreEqual(0, world.Granted);
            Assert.AreEqual(300, helper.CurrentMana);
        }

        [Test]
        public void CannotCastBeforeAlchemyNavigationOrWithInsufficientSharedMana()
        {
            var helper = new Ordr323OriginalHelperCowActor(0);
            var game = new GameSession();
            game.Start();
            var world = new TestWorld(helper);
            var cast = new Ordr323OriginalAlchemyCastService(
                game, helper, Refunds(), world, () => 0);
            Assert.IsFalse(cast.CanAttempt);
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(12).State);
            for (int wave = 1; wave <= 21; wave++) game.BeginWave(wave);
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, helper).TryChooseAlchemy());
            helper.SetMana(99);
            Assert.AreEqual(ManualActionState.Rejected, cast.TryCast(12).State);
            Assert.AreEqual(0, world.Begun);
            helper.SetMana(100);
            Assert.AreEqual(ManualActionState.Succeeded, cast.TryCast(12).State);
            Assert.AreEqual(0, helper.CurrentMana);
        }
    }
}
