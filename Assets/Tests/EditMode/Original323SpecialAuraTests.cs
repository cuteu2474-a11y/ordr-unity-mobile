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
    public sealed class Original323SpecialAuraTests
    {
        private sealed class Random : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive) { Calls++; return 0; }
        }

        // Synthetic native path, target masks and HP truncation ONLY.
        // These fixtures do not approve real Warcraft immunity/linger/buff math.
        private sealed class Native : IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string id, string code, out double delay,
                out DamageSchool school, out string evidence)
            {
                delay = 0; school = DamageSchool.Physical;
                evidence = id == "C-01" ? "TEST_ONLY: native path" : null;
                return evidence != null;
            }
        }

        private sealed class Quantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double value, Ordr323VerifiedDamageHit hit) => (long)value;
        }

        private sealed class Targets : IOriginal323SpecialAuraTargetRules
        {
            public bool Approved = true;
            public bool Evidence = true;
            public string ExcludedEnemy;
            public long ExcludedAlly;
            public double UntilTime = double.PositiveInfinity;
            public bool CanAffectEnemy(Original323SpecialAura aura, UnitState caster,
                SpatialCombatEnemy target, double time, out string evidence)
            {
                evidence = Evidence ? "TEST_ONLY: native mask/immunity/current buff tick" : null;
                return Approved && time <= UntilTime && target.Id != ExcludedEnemy;
            }
            public bool CanAffectAlly(Original323SpecialAura aura, UnitState caster,
                UnitState target, double time, out string evidence)
            {
                evidence = Evidence ? "TEST_ONLY: organic/friend/self target eligibility" : null;
                return Approved && time <= UntilTime && target.InstanceId != ExcludedAlly;
            }
        }

        private sealed class Story : IOriginalStoryDispatchPort
        {
            public readonly HashSet<long> Deployed = new HashSet<long>();
            public bool IsDeployed(long id) => Deployed.Contains(id);
            public bool CanDispatch(long id) => true;
            public bool CanRecall(long id) => true;
            public ManualActionResult Dispatch(long id)
            { Deployed.Add(id); return new ManualActionResult(ManualActionState.Succeeded, "TEST_ONLY"); }
            public ManualActionResult Recall(long id)
            { Deployed.Remove(id); return new ManualActionResult(ManualActionState.Succeeded, "TEST_ONLY"); }
        }

        private sealed class Buff : IOriginal323NativeAttackBuffRules
        {
            public bool Approved = true;
            public int Preflights;
            public double OriginalRaw;
            public double ModifiedRaw;
            public int EffectCount;
            public bool CanResolve(BasicDamageRequest request, CombatEnemy target,
                out string evidence)
            {
                Preflights++;
                evidence = Approved ? "TEST_ONLY: synthetic pre-armor buff calculation" : null;
                return Approved;
            }
            public bool TryApply(BasicDamageRequest request, CombatEnemy target,
                Ordr323VerifiedDamageHit original, out Ordr323VerifiedDamageHit modified)
            {
                OriginalRaw = original.RawDamage;
                EffectCount = request.AttackerEffects.Count;
                // Deliberately synthetic, not a production ACac formula.
                ModifiedRaw = original.RawDamage * 1.07;
                modified = new Ordr323VerifiedDamageHit(ModifiedRaw, original.AttackType,
                    original.UseAttackTable, original.UseArmorRating,
                    original.UseMagicResistance, original.Traits,
                    "TEST_ONLY: raw damage with synthetic buff", original.School);
                return true;
            }
        }

        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));
        private static CharacterUnitCatalog Units() => CharacterUnitCatalog.FromJson(Data("units.json"));
        private static OriginalMapBaseStatCatalog Map() => OriginalMapBaseStatCatalog.FromJson(
            Data("original-2323C-map-base-stats.json"), Units());

        private static CharacterCombatRuntime World(Targets targets = null,
            Random rng = null, Buff buff = null, bool allowNative = false)
        {
            var map = Map();
            var native = new OriginalMapCommonNativeAttackBinding(map, new Native(),
                rng ?? new Random(), buff);
            var world = Ordr323DamageWorldBinding.Create(Units(),
                CharacterSkillCatalog.FromJson("{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                native, null, new Quantizer(), rng ?? new Random());
            world.ConfigureOriginalSpecialAuras(new Original323SpecialAuraCatalog(map), targets);
            if (allowNative) world.ConfigureVerifiedUnitAttackProvider(native);
            world.Start(); world.BeginWave(1);
            return world;
        }

        private static void Enemy(CharacterCombatRuntime world, string id = "enemy",
            double x = 10, CharacterEnemyKind kind = CharacterEnemyKind.Line)
        {
            world.SpawnEnemyWithDefenses(id, 10000, kind, x, 0, 0,
                new EnemyDefenseProfile(kind, 10, null, "normal", "TEST_ONLY: armor"));
        }

        [TestCase("S-10", "h016", "A08N", "B011", 3, 800)]
        [TestCase("S-26", "h01E", "A0EO", "B02G", 3, 800)]
        [TestCase("S-14", "h01F", "A08F", "B012", 5, 800)]
        [TestCase("S-29", "h01A", "A08E", "B001", 5, 850)]
        [TestCase("S-30", "h00Y", "A08H", "B004", 5, 800)]
        [TestCase("S-25", "h01D", "A0EM", "B00V", 7, 850)]
        public void SixSourceIdentitiesAndValuesArePinned(string id, string raw,
            string ability, string buff, double amount, double radius)
        {
            var catalog = new Original323SpecialAuraCatalog(Map());
            Assert.AreEqual(6, catalog.Count);
            var aura = catalog.Find(id);
            Assert.AreEqual(raw, aura.UnitRawcode);
            Assert.AreEqual(ability, aura.AbilityRawcode);
            Assert.AreEqual(buff, aura.BuffRawcode);
            Assert.AreEqual(amount, aura.Amount);
            Assert.AreEqual(radius, aura.Radius);
            Assert.IsNull(catalog.Find("U-01"));
            if (id == "S-25")
            {
                Assert.AreEqual("ACac", aura.ParentAbility);
                Assert.AreEqual(.07000000029802322, aura.SignedRawObjectValue);
                Assert.IsTrue(aura.TargetsAllies);
            }
        }

        [TestCase("S-10", 800)]
        [TestCase("S-26", 800)]
        [TestCase("S-14", 800)]
        [TestCase("S-29", 850)]
        [TestCase("S-30", 800)]
        public void EnemyAurasRefreshAtBoundaryMoveAndCasterRemoval(string id, double radius)
        {
            var world = World(new Targets());
            long caster = world.SpawnCharacter(id, 0, 0);
            Enemy(world, x: radius);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            world.Step(0); world.Step(0);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount, "No refresh duplicates.");
            world.ForceMoveEnemy("enemy", radius + .01, 0, 0);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            world.MoveCharacter(caster, 1, 0);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            world.RemoveCharacter(caster);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DeniedOrUnprovenTargetsNeverAcquireEffects(bool approved, bool proof)
        {
            var world = World(new Targets { Approved = approved, Evidence = proof });
            world.SpawnCharacter("S-10", 0, 0);
            world.SpawnCharacter("S-14", 0, 0);
            long guard = world.SpawnCharacter("S-25", 0, 0);
            Enemy(world);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            Assert.AreEqual(0, world.UnitStatusOf(guard).ActiveCount);
        }

        [Test]
        public void MissingTargetRulesPreserveMetadataButDoNotApplyAuras()
        {
            var world = World();
            long caster = world.SpawnCharacter("S-10", 0, 0);
            Enemy(world);
            Assert.AreEqual("A08N", world.OriginalSpecialAuraOf(caster).AbilityRawcode);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
        }

        [Test]
        public void StoryDeploymentSeparatesEnemyAndAllyAurasAtSameCoordinates()
        {
            var world = World(new Targets());
            var story = new Story(); world.ConfigureStoryDeployment(story);
            long smoker = world.SpawnCharacter("S-14", 0, 0);
            long guard = world.SpawnCharacter("S-25", 0, 0);
            long ally = world.SpawnCharacter("C-01", 0, 0);
            story.Deployed.Add(smoker); story.Deployed.Add(guard);
            Enemy(world); Enemy(world, "story", 10, CharacterEnemyKind.Story);
            world.Step(0);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            Assert.AreEqual(1, world.StatusOf("story").ActiveCount);
            Assert.AreEqual(0, world.UnitStatusOf(ally).ActiveCount);
            Assert.AreEqual(1, world.UnitStatusOf(guard).ActiveCount, "Self is an approved mask fixture.");
            story.Deployed.Clear(); world.Step(0);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.AreEqual(0, world.StatusOf("story").ActiveCount);
            Assert.AreEqual(1, world.UnitStatusOf(ally).ActiveCount);
        }

        [Test]
        public void CasterAfterEnemyAndRecipientBeforeCasterBothGainAura()
        {
            var world = World(new Targets());
            Enemy(world);
            long ally = world.SpawnCharacter("C-01", 850, 0);
            long hawkins = world.SpawnCharacter("S-10", 0, 0);
            long guard = world.SpawnCharacter("S-25", 0, 0);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.AreEqual(1, world.UnitStatusOf(ally).ActiveCount);
            world.MoveCharacter(ally, 850.01, 0);
            Assert.AreEqual(0, world.UnitStatusOf(ally).ActiveCount);
            world.RemoveCharacter(guard);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            world.RemoveCharacter(hawkins);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
        }

        [Test]
        public void DistinctBuffsAndDuplicateCastersKeepSeparateProvenanceWithoutAssumedStacking()
        {
            var world = World(new Targets());
            world.SpawnCharacter("S-10", 0, 0);
            world.SpawnCharacter("S-10", 0, 0);
            world.SpawnCharacter("S-26", 0, 0);
            Enemy(world);
            var effects = world.StatusOf("enemy").Snapshot();
            Assert.AreEqual(3, effects.Count);
            var keys = new HashSet<string>();
            foreach (var effect in effects) Assert.IsTrue(keys.Add(effect.SourceId));
            var rules = new Ordr323VerifiedPartialModifierRules(
                verifiedSpecialAuraProvenance: world.IsVerifiedOriginalSpecialAuraEffect);
            double armor;
            Assert.IsFalse(rules.TryResolveArmor(world.EnemyOf("enemy"), effects, 0, out armor));
        }

        [TestCase("S-10")]
        [TestCase("S-26")]
        public void SingleProvenArmorAuraChangesActualCombatDamage(string id)
        {
            var world = World(new Targets(), allowNative: true);
            world.SpawnCharacter(id, 0, 0);
            world.SpawnCharacter("C-01", 0, 0);
            Enemy(world);
            var rules = new Ordr323VerifiedPartialModifierRules(
                verifiedSpecialAuraProvenance: world.IsVerifiedOriginalSpecialAuraEffect);
            double armor;
            var effects = world.StatusOf("enemy").Snapshot();
            Assert.IsTrue(rules.TryResolveArmor(world.EnemyOf("enemy"), effects, 0, out armor));
            Assert.AreEqual(7, armor);
            var forged = new CombatEffect(effects[0].SourceId, CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura, 3, 0);
            Assert.IsFalse(rules.TryResolveArmor(world.EnemyOf("enemy"), new[] { forged }, 0, out armor));
            world.Step(0);
            var control = World(allowNative: true);
            control.SpawnCharacter("C-01", 0, 0); Enemy(control); control.Step(0);
            Assert.Less(world.EnemyOf("enemy").Health, control.EnemyOf("enemy").Health);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GuardBuffCannotBeSilentlyIgnoredAndLeavingRangeRestoresVanillaAttack(bool rejectedPolicy)
        {
            var rng = new Random();
            var world = World(new Targets(), rng,
                rejectedPolicy ? new Buff { Approved = false } : null, true);
            long guard = world.SpawnCharacter("S-25", 0, 0);
            world.SpawnCharacter("C-01", 0, 0); Enemy(world);
            world.Step(0);
            Assert.AreEqual(10000, world.EnemyOf("enemy").Health);
            Assert.AreEqual(0, rng.Calls, "Missing buff formula fails BEFORE dice.");
            world.MoveCharacter(guard, 851, 0); world.Step(0);
            Assert.Less(world.EnemyOf("enemy").Health, 10000);
            Assert.AreEqual(1, rng.Calls);
        }

        [Test]
        public void TargetMaskApprovalIsPerEnemyAndPerAlly()
        {
            var targets = new Targets { ExcludedEnemy = "immune" };
            var world = World(targets);
            world.SpawnCharacter("S-10", 0, 0);
            long ally = world.SpawnCharacter("C-01", 0, 0);
            targets.ExcludedAlly = ally;
            long guard = world.SpawnCharacter("S-25", 0, 0);
            Enemy(world, "immune"); Enemy(world, "eligible");
            Assert.AreEqual(0, world.StatusOf("immune").ActiveCount);
            Assert.AreEqual(1, world.StatusOf("eligible").ActiveCount);
            Assert.AreEqual(0, world.UnitStatusOf(ally).ActiveCount);
            Assert.AreEqual(1, world.UnitStatusOf(guard).ActiveCount);
        }

        [Test]
        public void AuraCleanupPreservesUnrelatedTimedEffects()
        {
            var world = World(new Targets());
            long caster = world.SpawnCharacter("S-14", 0, 0); Enemy(world);
            world.StatusOf("enemy").Apply(new CombatEffect("TEST:stun",
                CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill, 1, 1));
            world.RemoveCharacter(caster);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.IsTrue(world.StatusOf("enemy").IsStunned());
        }

        [Test]
        public void BuffPresenceIsRecheckedAtInternalAttackTimesDuringLargeStep()
        {
            var world = World(new Targets { UntilTime = .5 }, allowNative: true);
            world.SpawnCharacter("S-10", 0, 0);
            world.SpawnCharacter("C-01", 0, 0); Enemy(world);
            world.Step(1.02);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            Assert.Less(world.EnemyOf("enemy").Health, 10000);
        }

        [Test]
        public void ApprovedGuardBuffReachesNativeRawHitBeforeArmor()
        {
            var buff = new Buff(); var rng = new Random();
            var world = World(new Targets(), rng, buff, true);
            world.SpawnCharacter("S-25", 0, 0);
            world.SpawnCharacter("C-01", 0, 0); Enemy(world);
            world.Step(0);
            Assert.AreEqual(1, buff.Preflights);
            Assert.AreEqual(29, buff.OriginalRaw);
            Assert.AreEqual(31.03, buff.ModifiedRaw, 1e-9);
            Assert.AreEqual(1, buff.EffectCount);
            Assert.AreEqual(1, rng.Calls);
            Assert.Less(world.EnemyOf("enemy").Health, 10000);
        }

        private sealed class Movement : IOriginalEnemyMovementRules
        {
            public bool SawSlow;
            public bool TryResolveSpeed(CombatEnemy enemy, IReadOnlyList<CombatEffect> effects,
                out double speed)
            {
                speed = 100; // TEST_ONLY base speed and single-aura formula.
                foreach (var effect in effects)
                    if (effect.Type == CombatEffectType.MovementSlowPercent)
                    { SawSlow = true; speed = 95; }
                return true;
            }
        }

        [TestCase("S-14")]
        [TestCase("S-29")]
        [TestCase("S-30")]
        public void SlowAurasReachTheMovementEngine(string id)
        {
            var world = World(new Targets()); world.SpawnCharacter(id, 0, 0); Enemy(world, x: 0);
            var driver = new CombatEnemyPathDriver();
            driver.Attach("enemy", new VerifiedCombatPath(new[] {
                new CombatPathPoint(0, 0), new CombatPathPoint(1000, 0)
            }, "TEST_ONLY path"));
            var movement = new Movement(); driver.Advance(world, movement, 1);
            Assert.IsTrue(movement.SawSlow);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
        }
    }
}
