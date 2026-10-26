using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323SpecialRareObservedSkillTests
    {
        // Test-only: synthetic witnessed final HP, NEVER production Warcraft
        // native skill damage, triggering probability or status immunity.
        private sealed class Witness : IOriginal323SpecialRareImpactWitness
        {
            public Original323SpecialRareObservedImpact Event;
            public bool IdentityVerified = true;
            public bool Available = true;
            public int ObserveCalls;
            public int IdentityCalls;
            public bool TryObserveNativeImpact(string sourceEventId,
                CharacterCombatRuntime world,
                out Original323SpecialRareObservedImpact observed)
            {
                ObserveCalls++;
                observed = Available ? Event : null;
                return observed != null;
            }
            public bool TryVerifyCurrentCasterIdentity(long id, string raw,
                out string evidence)
            {
                IdentityCalls++;
                evidence = IdentityVerified
                    ? "TEST_ONLY live native actor rawcode proof" : null;
                return IdentityVerified;
            }
        }

        private sealed class Setup
        {
            public CharacterCombatRuntime World;
            public Witness Witness;
            public long Caster;
            public string UnitId;
            public string Rawcode;
            public string Ability;
        }

        private static string Data(string file)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", file));
        }

        private static Setup Create(string catalogId, string originalRawcode,
            string actualAbility)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var source = map.Find(catalogId);
            Assert.IsNotNull(source);
            Assert.AreEqual(originalRawcode, source.OriginalRawcode);
            CollectionAssert.Contains(source.OriginalAbilityRawcodes, actualAbility);
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var failFormula = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(
                units, profiles, failFormula, failFormula,
                new SeededNonOriginalProcRandom(31));
            var witness = new Witness();
            world.ConfigureOriginal323SpecialRareSkillImpacts(map, witness);
            world.Start();
            world.BeginWave(1);
            long caster = world.SpawnCharacter(catalogId, 0, 0);
            world.SpawnEnemy("primary", 1000, CharacterEnemyKind.Line, 10, 0, 0);
            world.SpawnEnemy("second", 900, CharacterEnemyKind.Line, 20, 0, 0);
            world.SpawnEnemy("story", 800, CharacterEnemyKind.Story, 5, 0, 0);
            return new Setup {
                World=world, Witness=witness, Caster=caster,
                UnitId=catalogId, Rawcode=originalRawcode,
                Ability=actualAbility
            };
        }

        private static Original323SpecialRareObservedHit Damage(
            string target, long value, double? stun = null)
        {
            return new Original323SpecialRareObservedHit(target, value,
                "TEST_ONLY native target + defense + FINAL HP proof", stun,
                stun.HasValue ? "TEST_ONLY native unresisted Bash status" : null);
        }

        private static void Observe(Setup game, string sourceId,
            params Original323SpecialRareObservedHit[] hits)
        {
            game.Witness.Event = new Original323SpecialRareObservedImpact(
                sourceId, game.Caster, game.UnitId, game.Rawcode,
                game.Ability, "TEST_ONLY verified original ability",
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                game.World.Time, "TEST_ONLY complete cast timing/geometry/mask",
                hits);
        }

        [Test]
        public void SpecialEnelNativeAbilityAppliesTwoFinalHpHitsWithoutBasicAttack()
        {
            // Real W3X unit h00Q / ability A084 association. Values 41/53
            // are made-up TEST_ONLY final HP evidence, not original damage.
            var game = Create("S-18", "h00Q", "A084");
            Assert.IsNull(game.World.ProfileOf(game.Caster));
            Observe(game, "source-enel-1", Damage("primary", 41),
                Damage("second", 53));
            Assert.IsTrue(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "source-enel-1"));
            Assert.AreEqual(959, game.World.EnemyOf("primary").Health);
            Assert.AreEqual(847, game.World.EnemyOf("second").Health);
            Assert.AreEqual(0, game.World.Game.DefeatedEnemies);

            int activations = 0, hits = 0;
            foreach (var evt in game.World.Step(0))
            {
                if (evt.Type == CharacterCombatEventType.SkillActivated)
                    activations++;
                if (evt.Type == CharacterCombatEventType.SkillDamage)
                    hits++;
            }
            Assert.AreEqual(1, activations);
            Assert.AreEqual(2, hits);
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "source-enel-1"), "One native source event cannot double-hit.");
            Assert.AreEqual(959, game.World.EnemyOf("primary").Health);
            Assert.AreEqual(1, game.Witness.ObserveCalls);
        }

        [Test]
        public void RareDoflamingoSkillCanDealObservedDamageAndNativeStun()
        {
            var game = Create("R-04", "h01L", "A057");
            Assert.IsNull(game.World.ProfileOf(game.Caster));
            Observe(game, "rare-effect-1", Damage("primary", 37, 0.4));
            Assert.IsTrue(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "rare-effect-1"));
            Assert.AreEqual(963, game.World.EnemyOf("primary").Health);
            Assert.IsTrue(game.World.StatusOf("primary").IsStunned());
            Assert.IsFalse(game.World.TryMoveEnemy("primary", 40, 0, 0));
            game.World.RemoveCharacter(game.Caster);
            game.World.Step(0.39);
            Assert.IsTrue(game.World.StatusOf("primary").IsStunned());
            game.World.Step(0.02);
            Assert.IsFalse(game.World.StatusOf("primary").IsStunned());
            Assert.IsTrue(game.World.TryMoveEnemy("primary", 40, 0, 0));
        }

        [Test]
        public void NativeSkillBatchRejectsCrossLaneDuplicateOrUnverifiedHpBeforeAnyDamage()
        {
            var game = Create("S-18", "h00Q", "A084");
            Observe(game, "bad-lane", Damage("primary", 45),
                Damage("story", 10));
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "bad-lane"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);
            Assert.AreEqual(800, game.World.EnemyOf("story").Health);

            Observe(game, "duplicate", Damage("primary", 15),
                Damage("primary", 20));
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "duplicate"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);

            Observe(game, "overkill-unobserved", Damage("primary", 1001));
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "overkill-unobserved"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);

            Observe(game, "good-after-reject", Damage("primary", 45));
            Assert.IsTrue(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "good-after-reject"));
            Assert.AreEqual(955, game.World.EnemyOf("primary").Health);
        }

        [Test]
        public void CurrentCasterRawcodeAndSourceAbilityCannotBeForged()
        {
            var game = Create("R-04", "h01L", "A057");
            Observe(game, "real-1", Damage("primary", 10));
            game.Witness.IdentityVerified = false;
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "real-1"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);
            game.Witness.IdentityVerified = true;

            game.Witness.Event = new Original323SpecialRareObservedImpact(
                "wrong-owner", game.Caster, game.UnitId, "h00Q", "A057",
                "TEST_ONLY spell", Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                game.World.Time, "TEST_ONLY", new[] {Damage("primary", 10)});
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "wrong-owner"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);

            game.Witness.Event = new Original323SpecialRareObservedImpact(
                "wrong-ability", game.Caster, game.UnitId, game.Rawcode, "ZZZZ",
                "TEST_ONLY spell", Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                game.World.Time, "TEST_ONLY", new[] {Damage("primary", 10)});
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "wrong-ability"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);
        }

        [Test]
        public void OriginalDummyLightningAndUntrustedSourceVersionRemainBlocked()
        {
            var game = Create("S-18", "h00Q", "A084");
            game.Ability = "A086"; // Existing JASS native dummy-spell world owns this.
            Observe(game, "dummy-duplicate", Damage("primary", 47));
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "dummy-duplicate"));
            game.Ability = "A084";
            game.Witness.Event = new Original323SpecialRareObservedImpact(
                "wrong-version", game.Caster, game.UnitId, game.Rawcode,
                game.Ability, "TEST_ONLY spell", new string('0', 64),
                game.World.Time, "TEST_ONLY", new[] { Damage("primary", 10) });
            Assert.IsFalse(game.World.TryApplyObservedOriginal323SpecialRareImpact(
                "wrong-version"));
            Assert.AreEqual(1000, game.World.EnemyOf("primary").Health);
        }

        [Test]
        public void NoNativeSourceWitnessCannotActivateAnySpeculativeTooltipSkill()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var reject = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(
                units, skills, reject, reject, new SeededNonOriginalProcRandom(5));
            world.Start();
            world.BeginWave(1);
            world.SpawnCharacter("R-04", 0, 0);
            world.SpawnEnemy("primary", 1000,
                CharacterEnemyKind.Line, 0, 0, 0);
            Assert.IsFalse(world.TryApplyObservedOriginal323SpecialRareImpact(
                "unseen-native-effect"));
            Assert.AreEqual(1000, world.EnemyOf("primary").Health);
        }
    }
}
