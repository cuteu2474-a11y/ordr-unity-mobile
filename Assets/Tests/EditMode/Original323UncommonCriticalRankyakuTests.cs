using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // TEST_ONLY native hit approval/armor quantization and Rankyaku geometry.
    // These tests prove correct integration, NOT complete WC3 simulation parity.
    public sealed class Original323UncommonCriticalRankyakuTests
    {
        private sealed class TestRng : ICombatProcRandom
        {
            public int Calls;
            public bool FailChance;
            public int Next(int upperExclusive)
            {
                Calls++;
                return upperExclusive == 1000000 && FailChance
                    ? upperExclusive - 1 : 0;
            }
        }

        private sealed class TestNative : IOriginalCommonNativeAttackVerification
        {
            private readonly OriginalMapBaseStatCatalog map;
            private readonly string enabled;
            public TestNative(OriginalMapBaseStatCatalog source, string id)
            {
                map = source; enabled = id;
            }
            public bool TryVerify(string id, string rawcode, out double firstDelay,
                out DamageSchool school, out string proof)
            {
                var original = map.Find(id);
                firstDelay = original == null || original.Attack1 == null
                    ? 0 : original.Attack1.AttackPointSeconds.Value;
                school = DamageSchool.Physical;
                bool allowed = id == enabled && original != null &&
                    original.OriginalRawcode == rawcode;
                proof = allowed ? "TEST_ONLY original object native path" : null;
                return allowed;
            }
        }

        private sealed class TestQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public Ordr323VerifiedDamageHit LastHit;
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                LastHit = hit;
                return (long)Math.Truncate(value);
            }
        }

        private sealed class TestCritPolicy : IOriginal323UncommonCriticalPrehitRules
        {
            public bool Approved = true;
            public int Calls;
            public bool TryAuthorize(OriginalUncommonProc proc,
                BasicDamageRequest request, CombatEnemy target,
                out string evidence)
            {
                Calls++;
                evidence = Approved ? "TEST_ONLY pre-armor crit" : null;
                return Approved && proc != null && target.IsAlive &&
                    target.Defenses.HasVerifiedArmor &&
                    request.UnitRawCode == proc.OriginalUnitRawcode;
            }
        }

        private sealed class TestLine : IOriginal323RankyakuVerifiedRules
        {
            public bool Approved = true;
            public readonly List<Original323RankyakuLineHit> Hits =
                new List<Original323RankyakuLineHit>();
            public int Preflight;
            public int Resolves;

            public bool CanAttempt(OriginalUncommonProc proc,
                string attackerId, CombatEnemy primary, out string evidence)
            {
                Preflight++;
                evidence = Approved ? "TEST_ONLY exact line target proof" : null;
                return Approved && primary != null && primary.IsAlive &&
                    primary.Defenses.HasVerifiedArmor;
            }

            public bool TryResolveCompleteLine(OriginalUncommonProc proc,
                CharacterCombatRuntime world, string attackerId,
                string primaryId, out IReadOnlyList<Original323RankyakuLineHit> hits,
                out string evidence)
            {
                Resolves++;
                evidence = Approved ? "TEST_ONLY full magic+geometry+HP conversion" : null;
                hits = Approved ? Hits.AsReadOnly() : null;
                return Approved;
            }
        }

        private static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath,
                "..", "data", name));
        }

        private static CharacterUnitCatalog Units()
        {
            return CharacterUnitCatalog.FromJson(Read("units.json"));
        }

        private static OriginalMapBaseStatCatalog Map(CharacterUnitCatalog units)
        {
            return OriginalMapBaseStatCatalog.FromJson(
                Read("original-2323C-map-base-stats.json"), units);
        }

        private static CharacterCombatRuntime World(string id,
            TestRng rng, TestQuantizer quantizer,
            TestCritPolicy crit, IOriginalUncommonVerifiedProcWorld effects)
        {
            var units = Units();
            var map = Map(units);
            var cat = new Original323UncommonAbilityCatalog(map);
            var native = new OriginalMapCommonNativeAttackBinding(
                map, new TestNative(map, id), rng);
            IOrdr323VerifiedHitSource hits = native;
            if (crit != null)
                hits = new Original323UncommonCriticalHitSource(
                    native, cat, crit, rng);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var world = Ordr323DamageWorldBinding.Create(
                units, skills, hits, null, quantizer, rng);
            world.ConfigureOriginalUncommonAbilities(cat, effects);
            world.ConfigureVerifiedUnitAttackProvider(native);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void Enemy(CharacterCombatRuntime world, string name,
            CharacterEnemyKind kind = CharacterEnemyKind.Line,
            double x = 10)
        {
            world.SpawnEnemyWithDefenses(name, 1000, kind, x, 0, 0,
                new EnemyDefenseProfile(kind, 0, null,
                    "normal", "TEST_ONLY original enemy armor/type"));
        }

        [TestCase("U-04", 177d, 265.5d, 331L)]
        [TestCase("U-13", 157d, 235.5d, 235L)]
        public void CritMultiplierAppliesToRawObjectDiceBeforeArmorAndHpTruncation(
            string id, double vanilla, double critical, long expectedHpLoss)
        {
            var rng = new TestRng();
            var quantizer = new TestQuantizer();
            var proof = new TestCritPolicy();
            var world = World(id, rng, quantizer, proof, null);
            long actor = world.SpawnCharacter(id, 0, 0);
            Enemy(world, "target");
            double delay = Map(Units()).Find(id).Attack1.AttackPointSeconds.Value;
            world.Step(delay);
            Assert.IsTrue(world.HasVerifiedAttack(actor));
            Assert.AreEqual(critical, quantizer.LastHit.RawDamage, 1e-9);
            Assert.IsTrue(quantizer.LastHit.Evidence.Contains("pre-armor x1.5"));
            Assert.AreEqual(1000L - expectedHpLoss,
                world.EnemyOf("target").Health);
            Assert.AreEqual(3, rng.Calls, "two original d4 + one 15% crit roll");
            Assert.AreEqual(1, proof.Calls);
            Assert.AreEqual(critical, vanilla * 1.5, 1e-9);
        }

        [TestCase("U-04", 177d, 221L)]
        [TestCase("U-13", 157d, 157L)]
        public void MissedOrUnapprovedCriticalKeepsOriginalBasicDamage(
            string id, double vanilla, long expectedHpLoss)
        {
            foreach (bool unapproved in new[] { false, true })
            {
                var rng = new TestRng { FailChance = true };
                var q = new TestQuantizer();
                var permission = new TestCritPolicy {
                    Approved = !unapproved
                };
                var world = World(id, rng, q, permission, null);
                world.SpawnCharacter(id, 0, 0);
                Enemy(world, "target");
                world.Step(Map(Units()).Find(id).Attack1.AttackPointSeconds.Value);
                Assert.AreEqual(vanilla, q.LastHit.RawDamage, 1e-9);
                Assert.AreEqual(1000L - expectedHpLoss,
                    world.EnemyOf("target").Health);
                Assert.AreEqual(unapproved ? 2 : 3, rng.Calls,
                    "Never roll an unapproved Warcraft native proc.");
            }
        }

        [TestCase("U-04", "h00M")]
        [TestCase("U-13", "h00A")]
        public void CriticalIsNeverAPostHitProcEvenIfEffectWorldApprovesIt(
            string id, string originalRawcode)
        {
            var cat = new Original323UncommonAbilityCatalog(Map(Units()));
            var effects = new Original323UncommonRankyakuWorld(new TestLine());
            var spec = CharacterCombatMapping.Build(id, null,
                new BasicAttackSpec(1, 1, DamageSchool.Physical,
                    DamageTraits.None), originalRawcode, null, cat, effects);
            bool rankyaku = false;
            foreach (var skill in spec.AttackSpec.Skills)
            {
                Assert.AreNotEqual("A07I:critical", skill.RawCode);
                if (skill.RawCode == "A07I:rankyaku")
                {
                    rankyaku = true;
                    Assert.AreEqual(SkillTriggerKind.OnHit, skill.Trigger);
                    Assert.AreEqual(100000, skill.ChancePerMillion);
                }
            }
            Assert.IsTrue(rankyaku, "10% rankyaku remains its own skill roll.");
        }

        [TestCase("U-04")]
        [TestCase("U-13")]
        public void VerifiedRankyakuCanApplyMultipleDistinctMainFieldHits(
            string id)
        {
            var line = new TestLine();
            line.Hits.Add(new Original323RankyakuLineHit(
                "target", 35, "TEST_ONLY target conversion"));
            line.Hits.Add(new Original323RankyakuLineHit(
                "splash", 48, "TEST_ONLY splash conversion"));
            var rng = new TestRng();
            var world = World(id, rng, new TestQuantizer(), null,
                new Original323UncommonRankyakuWorld(line));
            world.SpawnCharacter(id, 0, 0);
            Enemy(world, "target", CharacterEnemyKind.Line, 10);
            Enemy(world, "splash", CharacterEnemyKind.Line, 20);
            var events = world.Step(
                Map(Units()).Find(id).Attack1.AttackPointSeconds.Value);
            long baseHpLoss = id == "U-04" ? 221 : 157;
            Assert.AreEqual(1000L - baseHpLoss - 35,
                world.EnemyOf("target").Health);
            Assert.AreEqual(952L, world.EnemyOf("splash").Health);
            Assert.AreEqual(3, rng.Calls,
                "two native d4 rolls and one independent 10% rankyaku roll");
            Assert.AreEqual(1, line.Resolves);
            int effects = 0;
            foreach (var evt in events)
                if (evt.Type == CharacterCombatEventType.SkillDamage &&
                    evt.SkillName == "A07I:rankyaku") effects++;
            Assert.AreEqual(2, effects);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateOrCrossLaneLineRejectsEntireBatchWithoutPartialDamage(
            bool crossLane)
        {
            var line = new TestLine();
            line.Hits.Add(new Original323RankyakuLineHit(
                "splash", 48, "TEST_ONLY source line hit"));
            line.Hits.Add(new Original323RankyakuLineHit(
                crossLane ? "story" : "splash", 72,
                "TEST_ONLY invalid collision duplicate/lane"));
            var world = World("U-04", new TestRng(), new TestQuantizer(), null,
                new Original323UncommonRankyakuWorld(line));
            world.SpawnCharacter("U-04", 0, 0);
            Enemy(world, "target", CharacterEnemyKind.Line, 10);
            Enemy(world, "splash", CharacterEnemyKind.Line, 20);
            if (crossLane)
                Enemy(world, "story", CharacterEnemyKind.Story, 30);
            world.Step(.20);
            Assert.AreEqual(1000L, world.EnemyOf("splash").Health);
            if (crossLane)
                Assert.AreEqual(1000L, world.EnemyOf("story").Health);
            Assert.AreEqual(779L, world.EnemyOf("target").Health,
                "Only the original basic attack is allowed to commit.");
        }

        [Test]
        public void NoGeometryEvidenceMeansNoRankyakuRngOrDamage()
        {
            var line = new TestLine { Approved = false };
            var rng = new TestRng();
            var world = World("U-04", rng, new TestQuantizer(), null,
                new Original323UncommonRankyakuWorld(line));
            world.SpawnCharacter("U-04", 0, 0);
            Enemy(world, "target");
            world.Step(.20);
            Assert.AreEqual(2, rng.Calls);
            Assert.AreEqual(0, line.Resolves);
            Assert.AreEqual(779L, world.EnemyOf("target").Health);
        }

        [Test]
        public void UnsupportedLineHandlerCannotAuthorizeCriticalOrDifferentUncommon()
        {
            var cat = new Original323UncommonAbilityCatalog(Map(Units()));
            var rankyaku = new Original323UncommonRankyakuWorld(new TestLine());
            foreach (var id in new[] { "U-01", "U-03", "U-05", "U-09", "U-12" })
                foreach (var proc in cat.ForUnit(id))
                    Assert.IsFalse(rankyaku.SupportsCompleteEffect(proc));
            Assert.IsFalse(rankyaku.SupportsCompleteEffect(
                cat.FindProc("U-04", "A07I:critical")));
            Assert.IsFalse(rankyaku.SupportsCompleteEffect(
                cat.FindProc("U-13", "A07I:critical")));
            Assert.IsTrue(rankyaku.SupportsCompleteEffect(
                cat.FindProc("U-04", "A07I:rankyaku")));
            Assert.IsTrue(rankyaku.SupportsCompleteEffect(
                cat.FindProc("U-13", "A07I:rankyaku")));
        }
    }
}
