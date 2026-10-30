using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323NativePassiveRuntimeTests
    {
        private sealed class FixedRandom : ICombatProcRandom
        {
            public int Value;
            public int Calls;
            public bool ClampForDice;
            public int Next(int bound) { Calls++; return ClampForDice ? Math.Min(Value, bound - 1) : Value; }
        }
        private sealed class NativePath : IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string id, string raw, out double delay,
                out DamageSchool school, out string evidence)
            {
                delay = 0; school = DamageSchool.Physical;
                evidence = "TEST_ONLY native attack approval, not a shipped source provider";
                return true;
            }
        }
        private sealed class TestQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public bool Fail;
            public Action BeforeConversion;
            public long Quantize(double amount, Ordr323VerifiedDamageHit hit)
            {
                BeforeConversion?.Invoke();
                if (Fail) throw new NotSupportedException("TEST_ONLY final HP conversion blocked");
                // Explicit test-only rounding. Not the production WC3 policy.
                return checked((long)Math.Floor(amount));
            }
        }
        private sealed class Fixture
        {
            public CharacterCombatRuntime World;
            public Original323NativePassiveCatalog Catalog;
            public OriginalNativePassive Skill;
            public FixedRandom Proc;
            public TestQuantizer Quantizer;
            public CombatEnemy Enemy;
        }

        private static Original323NativePassiveCatalog Load(out CharacterUnitCatalog units,
            out OriginalMapBaseStatCatalog map)
        {
            units = CharacterUnitCatalog.FromJson(Resources.Load<TextAsset>("OrdrData/units").text);
            map = OriginalMapBaseStatCatalog.FromJson(
                Resources.Load<TextAsset>("OrdrData/original-2323C-map-base-stats").text, units);
            return Original323NativePassiveCatalog.FromJson(Resources.Load<TextAsset>(
                "OrdrData/original-2323C-native-pure-passives").text, map);
        }

        private static Fixture Create(string raw = "h00M", int roll = 0,
            double armor = 0, bool? hero = false, long hp = 1000000000000,
            IEnumerable<string> handled = null, CharacterEnemyKind kind = CharacterEnemyKind.Line)
        {
            CharacterUnitCatalog units; OriginalMapBaseStatCatalog map;
            var catalog = Load(out units, out map);
            var skill = catalog.ForRawcode(raw);
            Assert.IsNotNull(skill);
            var dice = new FixedRandom { Value = 1, ClampForDice = true };
            var procs = new FixedRandom { Value = roll };
            var hits = new OriginalMapCommonNativeAttackBinding(map, new NativePath(), dice);
            var quantizer = new TestQuantizer();
            var world = Ordr323DamageWorldBinding.Create(units,
                CharacterSkillCatalog.FromJson("{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                hits, null, quantizer, procs, catalog, handled);
            world.ConfigureVerifiedUnitAttackProvider(hits);
            world.ConfigureOriginalUncommonAbilities(new Original323UncommonAbilityCatalog(map));
            world.SpawnCharacter(skill.CatalogId, 0, 0);
            world.Start(); world.BeginWave(1);
            world.SpawnEnemyWithDefenses("enemy", hp, kind, 1, 0, 0,
                new EnemyDefenseProfile(kind, armor, null, "large", "TEST_ONLY live armor"));
            var enemy = world.EnemyOf("enemy");
            if (hero.HasValue)
                Assert.IsTrue(enemy.TrySetNativePassiveTargetProfile(0,
                    Profile(hero.Value, skill.AbilityRawcode)));
            return new Fixture { World = world, Catalog = catalog, Skill = skill,
                Proc = procs, Quantizer = quantizer, Enemy = enemy };
        }

        private static Original323NativePassiveTargetProfile Profile(bool hero, params string[] allowed)
        {
            return new Original323NativePassiveTargetProfile("o00K", hero, allowed,
                Original323UncommonAbilityCatalog.SourceW3xSha256,
                "TEST_ONLY explicit native hero/immunity/target-mask fixture");
        }
        private static int Events(IReadOnlyList<CharacterCombatEvent> events, CharacterCombatEventType type)
        { int count = 0; foreach (var e in events) if (e.Type == type) count++; return count; }

        [Test]
        public void SourceCatalogIncludesTwentySixCompleteEffectsWithoutMixedOrMultiLevelAbilities()
        {
            CharacterUnitCatalog units; OriginalMapBaseStatCatalog map;
            var catalog = Load(out units, out map);
            Assert.AreEqual(26, catalog.Count);
            Assert.AreEqual(20, catalog.PracticeFirstLevelCount);
            Assert.AreEqual(1.5, catalog.ForRawcode("h00M").CriticalMultiplier);
            Assert.AreEqual(2, catalog.ForRawcode("h00R").CriticalMultiplier);
            Assert.AreEqual(700000, catalog.ForRawcode("h00X").ChancePerMillion);
            Assert.IsNull(catalog.ForRawcode("h00K"), "Ace's additional damage is not a pure crit/stun");
            Assert.IsNull(catalog.ForRawcode("h02S"), "multiple active AHbh abilities must not stack by assumption");
            Assert.IsNull(catalog.ForRawcode("H096"), "Law's multi-level ability is not silently fixed to level one");
        }

        [Test]
        public void EveryExtractedSkillActuallyActivatesInTheSharedCombatWorld()
        {
            CharacterUnitCatalog units; OriginalMapBaseStatCatalog map;
            var catalog = Load(out units, out map);
            foreach (var skill in catalog.All)
            {
                var fixture = Create(skill.OwnerRawcode);
                var events = fixture.World.Step(0);
                Assert.AreEqual(1, Events(events, CharacterCombatEventType.Attack), skill.Name);
                Assert.AreEqual(1, Events(events, CharacterCombatEventType.SkillActivated), skill.Name);
                Assert.Less(fixture.Enemy.Health, fixture.Enemy.MaxHealth, skill.Name);
                Assert.AreEqual(skill.Kind == OriginalNativePassiveKind.Stun,
                    fixture.Enemy.Status.IsStunned(), skill.Name);
            }
        }

        [Test]
        public void CriticalMultiplierPrecedesArmorAndFinalHpConversion()
        {
            var fixture = Create(armor: 50);
            var events = fixture.World.Step(0);
            // Source Blueno: 175 + (2 + 2) = 179; *1.5 / (1+.02*50)
            // => floor(134.25)=134. Multiplying already rounded base HP is 133.
            Assert.AreEqual(134, fixture.Enemy.MaxHealth - fixture.Enemy.Health);
            Assert.AreEqual(1, Events(events, CharacterCombatEventType.SkillActivated));
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillDamage));
            Assert.AreEqual(1, fixture.Proc.Calls);
        }

        [Test]
        public void ChanceBoundaryAndFailedRollKeepVanillaDamage()
        {
            var succeeds = Create(roll: 149999, armor: 50);
            succeeds.World.Step(0);
            Assert.AreEqual(134, succeeds.Enemy.MaxHealth - succeeds.Enemy.Health);
            var fails = Create(roll: 150000, armor: 50);
            var events = fails.World.Step(0);
            Assert.AreEqual(89, fails.Enemy.MaxHealth - fails.Enemy.Health);
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
        }

        [Test]
        public void TargetWithoutNativeEligibilityReceivesOnlyBaseAttackAndNoProcDraw()
        {
            var fixture = Create(armor: 50, hero: null);
            var events = fixture.World.Step(0);
            Assert.AreEqual(89, fixture.Enemy.MaxHealth - fixture.Enemy.Health);
            Assert.AreEqual(0, fixture.Proc.Calls);
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
        }

        [Test]
        public void ExplicitImmunityDoesNotConsumeTheSkillRandomStream()
        {
            var fixture = Create(raw: "h00D");
            Assert.IsTrue(fixture.Enemy.TrySetNativePassiveTargetProfile(1, Profile(false)));
            fixture.World.Step(0);
            Assert.IsFalse(fixture.Enemy.Status.IsStunned());
            Assert.AreEqual(0, fixture.Proc.Calls);
        }

        [Test]
        public void BrookStunUsesExplicitNativeHeroFlagAndExpires()
        {
            var normalBoss = Create(raw: "h00D", hero: false, kind: CharacterEnemyKind.Boss);
            normalBoss.World.Step(0);
            normalBoss.World.Step(.2);
            Assert.IsTrue(normalBoss.Enemy.Status.IsStunned(), "Boss label is not a WC3 hero flag");
            normalBoss.World.Step(.101);
            Assert.IsFalse(normalBoss.Enemy.Status.IsStunned());
            var hero = Create(raw: "h00D", hero: true);
            hero.World.Step(0);
            hero.World.Step(.151);
            Assert.IsFalse(hero.Enemy.Status.IsStunned(), "Hero duration is .15, not .3");
        }

        [Test]
        public void GuaranteedPassiveDoesNotRollAndLethalHitsNeverStunCorpses()
        {
            var guaranteed = Create(raw: "h07M"); // Kaido native .37 second stun
            var events = guaranteed.World.Step(0);
            Assert.IsTrue(guaranteed.Enemy.Status.IsStunned());
            Assert.AreEqual(0, guaranteed.Proc.Calls);
            Assert.AreEqual(1, Events(events, CharacterCombatEventType.SkillActivated));
            var lethal = Create(raw: "h00D", hp: 1);
            events = lethal.World.Step(0);
            Assert.IsFalse(lethal.Enemy.Status.IsStunned());
            Assert.AreEqual(1, lethal.World.Game.DefeatedEnemies);
            Assert.AreEqual(1, Events(events, CharacterCombatEventType.EnemyDefeated));
        }

        [Test]
        public void CriticalLethalHitPublishesOnlyOneDeathAndNoExtraDamagePacket()
        {
            var fixture = Create(hp: 100);
            var events = fixture.World.Step(0);
            Assert.AreEqual(1, fixture.World.Game.DefeatedEnemies);
            Assert.AreEqual(1, Events(events, CharacterCombatEventType.EnemyDefeated));
            Assert.AreEqual(1, Events(events, CharacterCombatEventType.SkillActivated));
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillDamage));
        }

        [Test]
        public void ExternalLegacyHandlerOwnsItsAbilityWithoutASecondProc()
        {
            var fixture = Create(armor: 50, handled: new[] { "A07I" });
            var events = fixture.World.Step(0);
            Assert.AreEqual(89, fixture.Enemy.MaxHealth - fixture.Enemy.Health);
            Assert.AreEqual(0, fixture.Proc.Calls);
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
        }

        [Test]
        public void FailedHpFormulaCannotPublishAnActivatedStun()
        {
            var fixture = Create(raw: "h00D");
            fixture.Quantizer.Fail = true;
            var events = fixture.World.Step(0);
            Assert.IsFalse(fixture.Enemy.Status.IsStunned());
            Assert.AreEqual(fixture.Enemy.MaxHealth, fixture.Enemy.Health);
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
        }

        [Test]
        public void ImmunityChangedDuringDamageConversionInvalidatesPreparedStun()
        {
            var fixture = Create(raw: "h00D");
            fixture.Quantizer.BeforeConversion = () => {
                fixture.Enemy.TrySetNativePassiveTargetProfile(1, Profile(false));
            };
            var events = fixture.World.Step(0);
            Assert.IsFalse(fixture.Enemy.Status.IsStunned());
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
            Assert.Less(fixture.Enemy.Health, fixture.Enemy.MaxHealth);
        }

        [Test]
        public void NativeProfileUpdatesRequireCurrentRevisionAndSameOriginalIdentity()
        {
            var fixture = Create();
            Assert.IsFalse(fixture.Enemy.TrySetNativePassiveTargetProfile(0, Profile(true, "A07I")));
            Assert.AreEqual(1, fixture.Enemy.NativePassiveTargetRevision);
            Assert.IsFalse(fixture.Enemy.TrySetNativePassiveTargetProfile(1,
                new Original323NativePassiveTargetProfile("o00L", false, new[] { "A07I" },
                    Original323UncommonAbilityCatalog.SourceW3xSha256, "TEST_ONLY other object")));
            Assert.IsTrue(fixture.Enemy.TrySetNativePassiveTargetProfile(1, Profile(true, "A07I")));
        }

        [Test]
        public void SplitFramesDoNotProduceDuplicateSkillActivations()
        {
            var fixture = Create(raw: "h00D");
            fixture.World.Step(0);
            int activations = 0;
            for (int i = 0; i < 10; i++)
                activations += Events(fixture.World.Step(.01), CharacterCombatEventType.SkillActivated);
            Assert.AreEqual(0, activations);
            Assert.AreEqual(1, fixture.Proc.Calls);
        }

        [Test]
        public void DifferentBattlefieldDoesNotCreateAnAttackOrProcDraw()
        {
            var fixture = Create(raw: "h00D", kind: CharacterEnemyKind.Story);
            var events = fixture.World.Step(0);
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.Attack));
            Assert.AreEqual(0, Events(events, CharacterCombatEventType.SkillActivated));
            Assert.AreEqual(0, fixture.Proc.Calls);
        }

        [Test]
        public void VerifiedWaveCarriesTargetStateToTheActualSpawnedEnemy()
        {
            var fixture = Create(raw: "h00D");
            var director = new CombatWaveDirector();
            var profile = Profile(false, "A07P");
            director.Begin(fixture.World, new VerifiedWavePlan(2, new[] {
                new VerifiedWaveEnemy(0, "new-enemy", 10000, CharacterEnemyKind.Line,
                    2, 0, 0, new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null,
                        "large", "TEST_ONLY"), "o00K", profile)
            }, "TEST_ONLY wave"));
            director.Step(fixture.World, 0);
            Assert.AreSame(profile, fixture.World.EnemyOf("new-enemy").NativePassiveTargetProfile);
            Assert.Throws<ArgumentException>(() => new VerifiedWaveEnemy(0, "bad", 1,
                CharacterEnemyKind.Line, 0, 0, 0, EnemyDefenseProfile.Unknown, "o00L", profile));
        }

        [Test]
        public void ConnectedCriticalDoesNotMisreportRankyakuWithTheSameAbilityCode()
        {
            var fixture = Create();
            long actor = fixture.World.MobileUnitSnapshot()[0].InstanceId;
            Assert.IsNotNull(fixture.World.ConnectedNativePassiveOf(actor));
            Assert.IsTrue(fixture.World.IsOriginalUncommonSkillConnected(actor, "A07I:critical"));
            Assert.IsFalse(fixture.World.IsOriginalUncommonSkillConnected(actor, "A07I:rankyaku"));
        }
    }
}
