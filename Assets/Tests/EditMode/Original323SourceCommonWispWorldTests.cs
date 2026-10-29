using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323SourceCommonWispWorldTests
    {
        private sealed class FixedSourceBfi : IOriginalWispRandom
        {
            private readonly Queue<int> results = new Queue<int>();
            private readonly Queue<string> requestedRanges = new Queue<string>();

            public FixedSourceBfi(params int[] rolls)
            {
                foreach (int value in rolls) results.Enqueue(value);
            }

            public int NextInclusive(int minimum, int maximum)
            {
                requestedRanges.Enqueue(minimum + ".." + maximum);
                if (results.Count == 0)
                    throw new InvalidOperationException("예상하지 못한 추가 원본 BFi 호출");
                return results.Dequeue();
            }

            public void AssertCalls(params string[] expected)
            {
                CollectionAssert.AreEqual(expected, requestedRanges.ToArray());
                Assert.AreEqual(0, results.Count, "원본 BFi 호출 순서나 횟수 불일치");
            }
        }

        private sealed class Live
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public CharacterUnitCatalog Units;
            public OriginalMapBaseStatCatalog Objects;
        }

        private sealed class DifferentMobilePlacement : IOriginalPlacementRules
        {
            public bool TryChoosePosition(CatalogUnit unit, UnitState original,
                out double x, out double y)
            {
                x = 999;
                y = 888;
                return true;
            }

            public bool IsLegalPosition(CatalogUnit unit, UnitState original,
                double x, double y) => true;
        }

        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));

        private static Live Create(int wisps = 8)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var originals = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var noFormula = new RejectUnverifiedDamageFormula();
            var roster = new UnitRoster();
            var combat = new CharacterCombatRuntime(units, skills, noFormula,
                noFormula, new SeededNonOriginalProcRandom(29), roster);
            var shared = new SharedEconomySession(new EconomyWallet(), roster);
            combat.Start();
            combat.BeginWave(1);
            // Empty Wisp state is an empty wallet: ResourceBundle.Add forbids 0.
            if (wisps > 0)
                shared.Wallet.Grant(new ResourceBundle().Add(
                    WispResourceIds.Random, wisps));
            return new Live { Combat = combat, Economy = shared,
                Units = units, Objects = originals };
        }

        private static Original323CommonWispRecruitmentService Service(Live game,
            Original323SourceCommonWispWorld source) =>
            new Original323CommonWispRecruitmentService(game.Combat,
                game.Economy, game.Objects, source);

        [Test]
        public void SourcePoolUsesOriginalObjectOrderAndExactNineSpawnOffsets()
        {
            var game = Create();
            // BIU rare=10000; BIR chooses the first rawcode h003.
            var random = new FixedSourceBfi(10000, 0);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);
            var result = service.RecruitOne();

            Assert.AreEqual("C-01", result.CatalogId);
            Assert.AreEqual(1, result.SpentRandomWisps);
            Assert.AreEqual(1, game.Combat.CharacterCount);
            Assert.AreEqual(7, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1, source.ConfirmedCountForOriginal("h003"));
            Assert.AreEqual(1, source.ConfirmedCommonDraws);
            Assert.AreEqual(Original323SourceCommonWispWorld.OriginalCommonSpawnBaseX,
                game.Combat.MobileUnitSnapshot()[0].Position.X);
            Assert.AreEqual(Original323SourceCommonWispWorld.OriginalCommonSpawnY,
                game.Combat.MobileUnitSnapshot()[0].Position.Y);
            for (int i = 0; i < 9; i++)
                Assert.AreEqual(-4768 + 256 * i,
                    Original323SourceCommonWispWorld.OriginalCommonSpawnX(i));
            random.AssertCalls("1..10000", "0..8");
        }

        [Test]
        public void JassSpawnCoordinatesBeatUnrelatedMobileAutomaticPlacement()
        {
            var game = Create();
            game.Combat.MobileAutomation.Configure(
                new DifferentMobilePlacement(), null, null);
            var random = new FixedSourceBfi(10000, 1);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);

            Assert.AreEqual("C-02", Service(game, source).RecruitOne().CatalogId);
            var first = game.Combat.MobileUnitSnapshot()[0];
            Assert.AreEqual(-4512, first.Position.X);
            Assert.AreEqual(3232, first.Position.Y);

            // The ordinary non-source spawn is still subject to the mobile
            // placement adapter, so only the original draw is protected.
            long nonSource = game.Combat.SpawnCharacter("C-01", 12, 34);
            var second = game.Combat.Roster.Get(nonSource);
            Assert.AreEqual(999, second.X);
            Assert.AreEqual(888, second.Y);
            random.AssertCalls("1..10000", "0..8");
        }

        [Test]
        public void EverySecondOrdinaryDrawPrefersLessUsedOfTwoDistinctCandidates()
        {
            var game = Create();
            // First h003. Second proposes h003 and h002; h002 has fewer.
            // Third is the single h006 choice; fourth ties h003/h002 and
            // must choose the FIRST one, as BIS does (sd <= se).
            var random = new FixedSourceBfi(
                10000, 0,
                10000, 0, 1,
                10000, 8,
                10000, 0, 1);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);

            Assert.AreEqual("C-01", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-02", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-09", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-01", service.RecruitOne().CatalogId);
            Assert.AreEqual(4, source.ConfirmedCommonDraws);
            Assert.AreEqual(2, source.ConfirmedCountForOriginal("h003"));
            Assert.AreEqual(1, source.ConfirmedCountForOriginal("h002"));
            Assert.AreEqual(1, source.ConfirmedCountForOriginal("h006"));
            Assert.AreEqual(4, game.Combat.CharacterCount);
            Assert.AreEqual(4, game.Economy.Wallet.Balance(WispResourceIds.Random));
            random.AssertCalls("1..10000", "0..8",
                "1..10000", "0..8", "0..8",
                "1..10000", "0..8",
                "1..10000", "0..8", "0..8");
        }

        [Test]
        public void TwoCandidateDrawRerollsEqualIndicesWithoutCountingThemTwice()
        {
            var game = Create();
            var random = new FixedSourceBfi(10000, 5, 10000, 5, 5, 5, 2);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);

            Assert.AreEqual("C-06", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-03", service.RecruitOne().CatalogId);
            Assert.AreEqual(1, source.ConfirmedCountForOriginal("h008"));
            Assert.AreEqual(1, source.ConfirmedCountForOriginal("h001"));
            random.AssertCalls("1..10000", "0..8", "1..10000",
                "0..8", "0..8", "0..8", "0..8");
        }

        [TestCase(1)]
        [TestCase(24)]
        public void OriginalPirateShipBranchSpawnsOneDistinctSourceUnitAndSpendsOneWisp(int rareRoll)
        {
            var game = Create(2);
            var random = new FixedSourceBfi(rareRoll);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);

            var result = service.RecruitOne();
            Assert.IsNotNull(result);
            Assert.AreEqual("PS-01", result.CatalogId);
            Assert.AreEqual(1, result.SpentRandomWisps);
            Assert.AreEqual("해적선", game.Combat.MobileUnitSnapshot()[0].Unit.Name);
            Assert.AreEqual("해적선", game.Combat.MobileUnitSnapshot()[0].Unit.Grade);
            Assert.AreEqual(1, game.Combat.CharacterCount);
            Assert.AreEqual(1, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual("h060", game.Objects.Find("PS-01").OriginalRawcode);
            Assert.AreEqual(1, source.ConfirmedPirateShipDraws);
            Assert.AreEqual(0, source.ConfirmedCommonDraws);
            Assert.IsFalse(source.HasPendingPirateShip);
            Assert.IsTrue(service.CanRecruit);
            random.AssertCalls("1..10000");
        }

        [Test]
        public void AbortedPirateShipCreationRollsBackButRetryKeepsExactOriginalDraw()
        {
            var game = Create(2);
            var random = new FixedSourceBfi(1);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            using (var first = source.Begin())
            {
                long instanceId;
                string rawcode;
                Assert.IsTrue(first.TryCreate(out instanceId, out rawcode));
                Assert.AreEqual("h060", rawcode);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                // Commit deliberately not called.
            }
            Assert.IsTrue(source.HasPendingPirateShip);
            Assert.IsTrue(source.CanDraw);
            Assert.AreEqual(0, game.Combat.CharacterCount);
            Assert.AreEqual(2, game.Economy.Wallet.Balance(WispResourceIds.Random));
            var second = Service(game, source).RecruitOne();
            Assert.AreEqual("PS-01", second.CatalogId);
            Assert.AreEqual(1, source.ConfirmedPirateShipDraws);
            Assert.AreEqual(1, game.Combat.CharacterCount);
            Assert.AreEqual(1, game.Economy.Wallet.Balance(WispResourceIds.Random));
            random.AssertCalls("1..10000");
        }

        [Test]
        public void PirateShipDoesNotAdvanceNineCommonCountersOrSkipSecondDrawRule()
        {
            var game = Create(3);
            var random = new FixedSourceBfi(24, 10000, 0, 10000, 0, 1);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);
            Assert.AreEqual("PS-01", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-01", service.RecruitOne().CatalogId);
            Assert.AreEqual("C-02", service.RecruitOne().CatalogId);
            Assert.AreEqual(1, source.ConfirmedPirateShipDraws);
            Assert.AreEqual(2, source.ConfirmedCommonDraws);
            Assert.AreEqual(3, game.Combat.CharacterCount);
            random.AssertCalls("1..10000", "1..10000", "0..8",
                "1..10000", "0..8", "0..8");
        }

        [Test]
        public void PirateShipBaseAttackIsMappedFromSourceButNotClaimedAsFinalDamage()
        {
            var game = Create();
            var ship = game.Objects.Find("PS-01");
            Assert.AreEqual("verified-source-wisp-pirate-ship", ship.MappingStatus);
            Assert.AreEqual(510, ship.Attack1.BaseDamagePlus);
            Assert.AreEqual(2, ship.Attack1.DiceCount);
            Assert.AreEqual(4, ship.Attack1.DiceSides);
            Assert.AreEqual(512, ship.Attack1.MinimumBaseDamage);
            Assert.AreEqual(518, ship.Attack1.MaximumBaseDamage);
            Assert.AreEqual(0.9, ship.Attack1.AttackCooldownSeconds);
            Assert.AreEqual("missile", ship.Attack1.OriginalWeaponType);
            Assert.IsFalse(ship.CombatParityVerified);
        }

        [Test]
        public void RareThresholdIsInclusiveAndNextValueStillDrawsCommon()
        {
            var game = Create(1);
            var random = new FixedSourceBfi(25, 3);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            Assert.AreEqual("C-04", Service(game, source).RecruitOne().CatalogId);
            Assert.IsFalse(source.HasPendingPirateShip);
            random.AssertCalls("1..10000", "0..8");
        }

        [Test]
        public void NoWispDoesNotConsumeTheOriginalSharedRandomSequence()
        {
            var game = Create(0);
            var random = new FixedSourceBfi();
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var service = Service(game, source);
            Assert.IsFalse(service.CanRecruit);
            Assert.IsNull(service.RecruitOne());
            Assert.AreEqual(0, source.ConfirmedCommonDraws);
            random.AssertCalls();
        }

        [Test]
        public void CommonSummonAndResourceExchangeUseOneChronologicalBfiStream()
        {
            var game = Create(3);
            // BIU/BIR: h003; CSK: 70 => gold for round 1;
            // BIU/BIS: h003, h002 => h002 as the less-used candidate.
            var random = new FixedSourceBfi(10000, 0, 70, 10000, 0, 1);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var recruitment = Service(game, source);
            var resources = new OriginalRandomWispResourceExchange(
                game.Economy.Wallet, game.Combat.Game, random);

            Assert.AreEqual("C-01", recruitment.RecruitOne().CatalogId);
            var reward = resources.SpendOne();
            Assert.AreEqual("gold", reward.RewardResourceId);
            Assert.AreEqual(75, reward.RewardAmount);
            Assert.AreEqual("C-02", recruitment.RecruitOne().CatalogId);
            Assert.AreEqual(2, game.Combat.CharacterCount);
            Assert.AreEqual(0, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(75, game.Economy.Wallet.Balance("gold"));
            random.AssertCalls("1..10000", "0..8", "1..100",
                "1..10000", "0..8", "0..8");
        }

        [Test]
        public void SourceRollRejectsOutOfRangeResultAndDoesNotSpendWisp()
        {
            var game = Create();
            var random = new FixedSourceBfi(10001);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            Assert.Throws<InvalidOperationException>(() => Service(game, source).RecruitOne());
            Assert.AreEqual(8, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(0, game.Combat.CharacterCount);
            random.AssertCalls("1..10000");
        }

        [Test]
        public void OriginalRecruitmentTransactionIsExclusiveUntilDisposed()
        {
            var game = Create(2);
            var random = new FixedSourceBfi(10000, 0);
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, random);
            var first = source.Begin();
            Assert.IsFalse(source.CanDraw);
            Assert.Throws<InvalidOperationException>(() => source.Begin());
            first.Dispose();
            Assert.IsTrue(source.CanDraw);

            // A second transaction may re-open without consuming a fresh
            // BFi call, creating or debiting any unit in the aborted first.
            using (var second = source.Begin())
            {
                long id;
                string rawcode;
                Assert.IsTrue(second.TryCreate(out id, out rawcode));
                Assert.AreEqual("h003", rawcode);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                // Disposing without commit discards only the tentative unit.
            }
            Assert.AreEqual(0, game.Combat.CharacterCount);
            Assert.AreEqual(2, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.IsTrue(source.CanDraw);
            random.AssertCalls("1..10000", "0..8");
        }

        [Test]
        public void InvalidPoolIndexAndUnknownCounterRawcodeFailClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SourceCommonWispWorld.OriginalCommonSpawnX(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SourceCommonWispWorld.OriginalCommonSpawnX(9));
            var game = Create();
            var source = new Original323SourceCommonWispWorld(game.Combat,
                game.Units, game.Objects, new FixedSourceBfi());
            Assert.Throws<ArgumentException>(() => source.ConfirmedCountForOriginal("h060"));
        }
    }
}
