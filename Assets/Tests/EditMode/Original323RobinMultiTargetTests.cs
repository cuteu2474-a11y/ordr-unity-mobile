using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Synthetic Warcraft path approval + armor/HP conversion are fixtures,
    // not evidence that real ORDR A07M missile timing has been reproduced.
    public sealed class Original323RobinMultiTargetTests
    {
        private sealed class CountingRoll : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive) { Calls++; return 0; }
        }

        private sealed class TestNative : IOriginalCommonNativeAttackVerification
        {
            public bool Approved = true;
            public bool TryVerify(string id, string code, out double delay,
                out DamageSchool school, out string evidence)
            {
                delay = 0.25;
                school = DamageSchool.Physical;
                bool match = Approved && id == "U-01" && code == "h00O";
                evidence = match ? "TEST_ONLY: independent native timing approval" : null;
                return match;
            }
        }

        private sealed class TestQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(value);
            }
        }

        private sealed class TestVolley : IOriginal323RobinVerifiedVolleyRules
        {
            public bool Approved = true;
            public bool ResolveApproved = true;
            public int Preflights;
            public int Resolves;
            public readonly List<Original323RobinSecondaryHit> Additional =
                new List<Original323RobinSecondaryHit>();

            public bool CanAttempt(OriginalUncommonProc proc, string attackerId,
                CombatEnemy primary, out string evidence)
            {
                Preflights++;
                evidence = Approved ? "TEST_ONLY: original A07M complete eligibility" : null;
                return Approved && proc.ProcId == "A07M:multi3" &&
                    primary != null && primary.IsAlive &&
                    primary.Defenses.HasVerifiedArmor;
            }

            public bool TryResolveAdditionalHits(OriginalUncommonProc proc,
                CharacterCombatRuntime world, string attackerId,
                string primaryId,
                out IReadOnlyList<Original323RobinSecondaryHit> additionalHits,
                out string evidence)
            {
                Resolves++;
                evidence = ResolveApproved
                    ? "TEST_ONLY: original extra target order/dice/armor/HP proof"
                    : null;
                additionalHits = ResolveApproved ? Additional.AsReadOnly() : null;
                return ResolveApproved;
            }
        }

        private static string Data(string file)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath,
                "..", "data", file));
        }

        private static CharacterUnitCatalog Units()
        {
            return CharacterUnitCatalog.FromJson(Data("units.json"));
        }

        private static Original323UncommonAbilityCatalog Skills(
            CharacterUnitCatalog units)
        {
            return new Original323UncommonAbilityCatalog(
                OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units));
        }

        private static CharacterCombatRuntime World(
            CountingRoll random, TestVolley volley,
            bool verifiedNative = true)
        {
            var units = Units();
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var source = new TestNative { Approved = verifiedNative };
            var native = new OriginalMapCommonNativeAttackBinding(
                map, source, random);
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var world = Ordr323DamageWorldBinding.Create(units, profiles,
                native, null, new TestQuantizer(), random);
            world.ConfigureOriginalUncommonAbilities(
                new Original323UncommonAbilityCatalog(map),
                volley == null ? null : new Original323RobinMultiTargetWorld(volley));
            world.ConfigureVerifiedUnitAttackProvider(native);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void Enemy(CharacterCombatRuntime world,
            string id, long hp = 1000,
            CharacterEnemyKind kind = CharacterEnemyKind.Line, double x = 10)
        {
            world.SpawnEnemyWithDefenses(id, hp, kind, x, 0, 0,
                new EnemyDefenseProfile(kind, 0, null, "normal",
                    "TEST_ONLY: original attack eligibility and target armor"));
        }

        private static Original323RobinSecondaryHit Shot(
            string target, long damage)
        {
            return new Original323RobinSecondaryHit(target, damage,
                "TEST_ONLY: separate native missile + final HP");
        }

        [Test]
        public void SourceDefinesOnePrimaryAndAtMostTwoAdditionalAttacks()
        {
            var unit = Units();
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), unit);
            var robin = map.Find("U-01");
            Assert.AreEqual("h00O", robin.OriginalRawcode);
            Assert.AreEqual("A07Y,A0JE,A0B8,A07M,A03E,Avul",
                string.Join(",", robin.OriginalAbilityRawcodes));
            Assert.AreEqual(60, robin.Attack1.BaseDamagePlus.Value);
            Assert.AreEqual(2, robin.Attack1.DiceCount.Value);
            Assert.AreEqual(3, robin.Attack1.DiceSides.Value);
            Assert.AreEqual(0.25, robin.Attack1.AttackPointSeconds.Value, 1e-9);
            Assert.AreEqual(1.2, robin.Attack1.AttackCooldownSeconds.Value, 1e-9);
            var skill = Skills(unit).FindProc("U-01", "A07M:multi3");
            Assert.AreEqual(OriginalUncommonProcKind.MultiTarget, skill.Kind);
            Assert.AreEqual(3, skill.NativeTargetCount);
            Assert.AreEqual(1000000, skill.ChancePerMillion);
            var handler = new Original323RobinMultiTargetWorld(new TestVolley());
            Assert.IsTrue(handler.SupportsCompleteEffect(skill));
            Assert.IsFalse(handler.SupportsCompleteEffect(
                Skills(unit).FindProc("U-03", "A07P:bash")));
        }

        [Test]
        public void ThreeTargetsReceiveOneNativePrimaryAndTwoVerifiedExtraHits()
        {
            var rng = new CountingRoll();
            var volley = new TestVolley();
            volley.Additional.Add(Shot("extra-a", 45));
            volley.Additional.Add(Shot("extra-b", 61));
            var world = World(rng, volley);
            long robin = world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary", 1000, CharacterEnemyKind.Line, 10);
            Enemy(world, "extra-a", 1000, CharacterEnemyKind.Line, 20);
            Enemy(world, "extra-b", 1000, CharacterEnemyKind.Line, 30);
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(robin, "A07M:multi3"));
            var events = world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health,
                "TEST_ONLY: (60+2d3 minimum) * normal armor type 1.25 -> 77 HP");
            Assert.AreEqual(955L, world.EnemyOf("extra-a").Health);
            Assert.AreEqual(939L, world.EnemyOf("extra-b").Health);
            Assert.AreEqual(2, rng.Calls,
                "The 100% volley trigger must not consume an extra RNG roll.");
            Assert.AreEqual(1, volley.Preflights);
            Assert.AreEqual(1, volley.Resolves);
            int basics = 0, secondary = 0;
            foreach (var evt in events)
            {
                if (evt.Type == CharacterCombatEventType.Attack) basics++;
                if (evt.Type == CharacterCombatEventType.SkillDamage &&
                    evt.SkillName == "A07M:multi3") secondary++;
            }
            Assert.AreEqual(1, basics, "Only one basic cooldown is committed.");
            Assert.AreEqual(2, secondary);
        }

        [Test]
        public void OnlyTwoEnemiesMeansExactlyOneAdditionalShot()
        {
            var volley = new TestVolley();
            volley.Additional.Add(Shot("second", 32));
            var world = World(new CountingRoll(), volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary", 1000, CharacterEnemyKind.Line, 10);
            Enemy(world, "second", 1000, CharacterEnemyKind.Line, 20);
            world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health);
            Assert.AreEqual(968L, world.EnemyOf("second").Health);
        }

        [Test]
        public void SingleEnemyAcceptsZeroAdditionalShotsWithoutDuplicatingPrimary()
        {
            var volley = new TestVolley();
            var rng = new CountingRoll();
            var world = World(rng, volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "only");
            world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("only").Health);
            Assert.AreEqual(1, volley.Resolves);
            Assert.AreEqual(2, rng.Calls);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DuplicatePrimaryCrossLaneOrTooManyTargetsRejectWholeExtraBatch(
            int invalidCase)
        {
            var volley = new TestVolley();
            volley.Additional.Add(Shot("extra", 45));
            volley.Additional.Add(Shot(invalidCase == 0 ? "extra" :
                invalidCase == 1 ? "primary" :
                invalidCase == 2 ? "story" : "third", 31));
            if (invalidCase == 3)
                volley.Additional.Add(Shot("fourth", 19));
            var world = World(new CountingRoll(), volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary", 1000, CharacterEnemyKind.Line, 10);
            Enemy(world, "extra", 1000, CharacterEnemyKind.Line, 20);
            Enemy(world, "story", 1000, CharacterEnemyKind.Story, 30);
            Enemy(world, "third", 1000, CharacterEnemyKind.Line, 40);
            Enemy(world, "fourth", 1000, CharacterEnemyKind.Line, 50);
            var events = world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health);
            foreach (var id in new[] { "extra", "story", "third", "fourth" })
                Assert.AreEqual(1000L, world.EnemyOf(id).Health,
                    "Unverified native volley must never partly hit.");
            int deferred = 0;
            foreach (var evt in events)
                if (evt.Type == CharacterCombatEventType.EffectDeferred &&
                    evt.SkillName == "꽃꽃 열매: 3명 동시 공격") deferred++;
            Assert.AreEqual(1, deferred);
        }

        [Test]
        public void UnknownSecondaryArmorCannotSilentlyTakeDamage()
        {
            var volley = new TestVolley();
            volley.Additional.Add(Shot("good", 31));
            volley.Additional.Add(Shot("unknown", 42));
            var world = World(new CountingRoll(), volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary");
            Enemy(world, "good", 1000, CharacterEnemyKind.Line, 20);
            world.SpawnEnemy("unknown", 1000, CharacterEnemyKind.Line,
                40, 0, 0);
            world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health);
            Assert.AreEqual(1000L, world.EnemyOf("good").Health);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
        }

        [Test]
        public void PrimaryDeathStillAllowsSourceVerifiedSecondAndThirdTargets()
        {
            var volley = new TestVolley();
            volley.Additional.Add(Shot("extra-a", 1000));
            volley.Additional.Add(Shot("extra-b", 35));
            var world = World(new CountingRoll(), volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary", 77, CharacterEnemyKind.Line, 10);
            Enemy(world, "extra-a", 50, CharacterEnemyKind.Line, 20);
            Enemy(world, "extra-b", 1000, CharacterEnemyKind.Line, 30);
            var events = world.Step(.25);
            Assert.IsNull(world.EnemyOf("primary"));
            Assert.IsNull(world.EnemyOf("extra-a"));
            Assert.AreEqual(965L, world.EnemyOf("extra-b").Health);
            Assert.AreEqual(2, world.Game.DefeatedEnemies,
                "Primary and additional hit kills are counted once each.");
            int secondaryKills = 0;
            foreach (var evt in events)
                if (evt.Type == CharacterCombatEventType.EnemyDefeated &&
                    evt.SkillName == "A07M:multi3") secondaryKills++;
            Assert.AreEqual(1, secondaryKills);
        }

        [Test]
        public void UnverifiedNativePathOrTargetPolicyNeverCausesBonusVolley()
        {
            foreach (bool nativeUnapproved in new[] { false, true })
            {
                var rng = new CountingRoll();
                // Either the native basic path or the independent volley
                // approval is missing, never both approved at once.
                var volley = new TestVolley { Approved = nativeUnapproved };
                var world = World(rng, volley, !nativeUnapproved);
                long robin = world.SpawnCharacter("U-01", 0, 0);
                Enemy(world, "primary");
                Enemy(world, "extra", 1000, CharacterEnemyKind.Line, 20);
                volley.Additional.Add(Shot("extra", 55));
                world.Step(2.0);
                Assert.AreEqual(1000L, world.EnemyOf("extra").Health);
                Assert.AreEqual(0, volley.Resolves);
                Assert.AreEqual(0, world.Game.DefeatedEnemies);
                if (nativeUnapproved)
                    Assert.IsFalse(world.HasVerifiedAttack(robin),
                        "Original-native approval is required to deal any damage.");
                else
                    Assert.IsFalse(world.IsOriginalUncommonSkillConnected(robin,
                        "A07M:multi3"),
                        "Unapproved volley must not enter the proc trigger engine.");
            }
        }

        [Test]
        public void ResolverWithMissingIndependentEvidenceFailsClosed()
        {
            var volley = new TestVolley { ResolveApproved = false };
            volley.Additional.Add(Shot("extra", 77));
            var world = World(new CountingRoll(), volley);
            world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary");
            Enemy(world, "extra", 1000, CharacterEnemyKind.Line, 20);
            world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health);
            Assert.AreEqual(1000L, world.EnemyOf("extra").Health);
        }

        [Test]
        public void NoVolleyHandlerMaintainsPlainNativeBasicAttack()
        {
            var rng = new CountingRoll();
            var world = World(rng, null);
            long robin = world.SpawnCharacter("U-01", 0, 0);
            Enemy(world, "primary");
            Enemy(world, "extra", 1000, CharacterEnemyKind.Line, 20);
            Assert.IsFalse(world.IsOriginalUncommonSkillConnected(robin,
                "A07M:multi3"));
            world.Step(.25);
            Assert.AreEqual(923L, world.EnemyOf("primary").Health);
            Assert.AreEqual(1000L, world.EnemyOf("extra").Health);
            Assert.AreEqual(2, rng.Calls);
        }

        [Test]
        public void UnrelatedUncommonAbilitiesAreNeverAuthorizedAsRobinShots()
        {
            var skillCatalog = Skills(Units());
            var handler = new Original323RobinMultiTargetWorld(new TestVolley());
            foreach (var id in new[] { "U-03", "U-04", "U-05", "U-09",
                "U-12", "U-13", "U-14" })
                foreach (var proc in skillCatalog.ForUnit(id))
                    Assert.IsFalse(handler.SupportsCompleteEffect(proc));
        }
    }
}
