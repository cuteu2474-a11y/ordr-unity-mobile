using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    // All actor mana/cast ranges/cooldowns/final damage rules in this file
    // are TEST_ONLY fixtures, never approved Warcraft object data.
    public sealed class OriginalHelperCowLiveCombatWorldTests
    {
        private sealed class TestCow : IOriginalHelperCowCombatActor
        {
            public string OriginalRawcode { get; set; } = "h08A";
            public int OwnerPlayerId { get; set; }
            public bool IsAlive { get; set; } = true;
            public double X { get; set; }
            public double Y { get; set; }
            public double CurrentMana { get; private set; }
            public double MaximumMana { get; private set; }
            public int BurstLevel = 1;
            public int QuakeLevel;
            public bool Ready = true;
            public TestCow(double mana = 200, double max = 200)
            { CurrentMana = mana; MaximumMana = max; }
            public int GetAbilityLevel(string ability)
            {
                if (ability == "A0BZ") return BurstLevel;
                if (ability == "A07T") return QuakeLevel;
                return 0;
            }
            public bool IsOriginalCastReady(string ability) { return Ready; }
            public void SetMana(double mana)
            {
                if (mana < 0 || mana > MaximumMana)
                    throw new ArgumentOutOfRangeException(nameof(mana));
                CurrentMana = mana;
            }
        }

        private sealed class TestData : IOriginalHelperCowVerifiedCastDataSource
        {
            public bool Valid = true;
            public double CastRange = 1000;
            public double ManaCost = 20;
            public double Cooldown = 3;
            public bool TryRead(string id, int level,
                out OriginalHelperCowVerifiedCastData data)
            {
                data = null;
                if (!Valid || (id != "A0BZ" && id != "A07T")) return false;
                data = new OriginalHelperCowVerifiedCastData(
                    id, level, CastRange, ManaCost, Cooldown, "TEST_ONLY");
                return true;
            }
        }

        private sealed class TestResolver : IOriginalHelperCowVerifiedAreaDamage
        {
            public bool Permit = true;
            public bool SupportsFinalDamage = true;
            public string RejectedId;
            public bool CanHit(string ability, MobileEnemySnapshot s, CombatEnemy enemy)
            {
                return Permit && s.Id != RejectedId;
            }
            public bool CanCastPoint(string id, double x, double y) { return Permit; }
            public bool TryFinalDamage(string id, int level, CombatEnemy enemy,
                IReadOnlyList<CombatEffect> effects, long raw, out long damage)
            {
                damage = 0;
                if (!SupportsFinalDamage) return false;
                damage = raw; // TEST_ONLY; never use raw as production final damage
                return true;
            }
        }

        // TEST_ONLY: verifies that the runtime invokes a real A04R ability
        // modifier provider, rather than changing only a decorative counter.
        private sealed class TestA04REffects : IOriginalA04REffectEngine
        {
            public int Applied;
            public bool Allow = true;
            public bool CanApplyOriginalA04RLevel(CombatEnemy enemy, int level, double raw)
            {
                return Allow && enemy != null && raw < 0;
            }
            public void ApplyOriginalA04RLevel(CombatEnemy enemy, int level, double raw)
            {
                if (!Allow || enemy.OriginalA04RLevel != level)
                    throw new InvalidOperationException("TEST_ONLY");
                Applied++;
            }
        }

        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
                "{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"도움소 테스트\",\"roleTags\":[]}]}");
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
                "{\"unitId\":\"TEST-01\",\"sourceUrl\":\"TEST_FIXTURE\"," +
                "\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{}," +
                "\"passives\":[],\"skills\":[]}]}");
            var missing = new RejectUnverifiedDamageFormula();
            var runtime = new CharacterCombatRuntime(units, profiles, missing,
                missing, new SeededNonOriginalProcRandom(7));
            runtime.Start();
            runtime.BeginWave(1);
            return runtime;
        }

        [Test]
        public void OriginalHelperAreaUsesOneCombatRuntimeForHpAndKillCounting()
        {
            var runtime = World();
            runtime.SpawnEnemy("a", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            runtime.SpawnEnemy("b", 20000000, CharacterEnemyKind.Boss, 90, 0, 0);
            runtime.SpawnEnemy("story", 20000000, CharacterEnemyKind.Story, 55, 0, 0);
            var cow = new TestCow();
            var data = new TestData();
            var resolver = new TestResolver();
            var live = new OriginalHelperCowLiveCombatWorld(
                runtime, () => cow, data, resolver);

            Assert.AreEqual(1, live.AvailableReadyAbilities().Count);
            Assert.IsTrue(live.TryCommitCast("A0BZ", 70, 0));
            Assert.AreEqual(180, cow.CurrentMana);
            Assert.AreEqual(13000000, runtime.EnemyOf("a").Health);
            Assert.AreEqual(13000000, runtime.EnemyOf("b").Health);
            Assert.AreEqual(20000000, runtime.EnemyOf("story").Health);
            Assert.AreEqual(0, runtime.Game.DefeatedEnemies);
            Assert.IsFalse(live.TryCommitCast("A0BZ", 70, 0));
            Assert.AreEqual(180, cow.CurrentMana);

            runtime.Step(3.1);
            Assert.IsTrue(live.TryCommitCast("A0BZ", 70, 0));
            Assert.AreEqual(160, cow.CurrentMana);
            Assert.AreEqual(6000000, runtime.EnemyOf("a").Health);

            runtime.Step(3.1);
            Assert.IsTrue(live.TryCommitCast("A0BZ", 70, 0));
            Assert.IsNull(runtime.EnemyOf("a"));
            Assert.IsNull(runtime.EnemyOf("b"));
            Assert.AreEqual(1, runtime.Game.AliveEnemies); // story remains
            Assert.AreEqual(2, runtime.Game.DefeatedEnemies);
        }

        [Test]
        public void EarthquakeCannotPretendA04RLevelsAreStunSeconds()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 10000000, CharacterEnemyKind.Line, 100, 0, 0);
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver());

            // Verified JASS has Bd8(Cm,5), i.e. +5 A04R ability LEVELS.
            // Without a SOURCE-VERIFIED baseline on the affected survivor,
            // committing the spell is forbidden and its mana is untouched.
            Assert.AreEqual(5, OriginalHelperCowAreaEvidence.JassA04RLevelIncrease(
                "A07T", 1));
            Assert.AreEqual(0, live.AvailableReadyAbilities().Count);
            Assert.IsFalse(live.TryCommitCast("A07T", 90, 0));
            Assert.IsFalse(runtime.TryApplyOriginalHelperCowAreaEffects(
                "A07T", 1, new[] {
                    new OriginalHelperCowResolvedHit("line", 1000000)
                }));
            Assert.AreEqual(200, cow.CurrentMana);
            Assert.AreEqual(10000000, runtime.EnemyOf("line").Health);
            Assert.IsFalse(runtime.StatusOf("line").IsStunned());
        }

        [Test]
        public void A04RMechanicsProviderMustPreflightBeforeManaOrHpChanges()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 100, 0, 0);
            runtime.ConfigureVerifiedEnemyOriginalA04R("line", 0, "TEST_ONLY");
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var effects = new TestA04REffects { Allow = false };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: effects);
            Assert.IsFalse(live.TryCommitCast("A07T", 90, 0));
            Assert.AreEqual(200, cow.CurrentMana);
            Assert.AreEqual(0, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.AreEqual(20000000L, runtime.EnemyOf("line").Health);
            effects.Allow = true;
            Assert.IsTrue(live.TryCommitCast("A07T", 90, 0));
            Assert.AreEqual(6, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.AreEqual(1, effects.Applied);
        }

        [Test]
        public void MissingAbilityObjectDataAndUnverifiedDamageCannotSpendMana()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 30, 0, 0);
            var cow = new TestCow();
            var data = new TestData { Valid = false };
            var resolver = new TestResolver();
            var live = new OriginalHelperCowLiveCombatWorld(
                runtime, () => cow, data, resolver);
            Assert.AreEqual(0, live.AvailableReadyAbilities().Count);
            Assert.IsFalse(live.TryCommitCast("A0BZ", 30, 0));
            data.Valid = true;
            resolver.SupportsFinalDamage = false;
            Assert.IsFalse(live.TryCommitCast("A0BZ", 30, 0));
            Assert.AreEqual(200, cow.CurrentMana);
            Assert.AreEqual(20000000, runtime.EnemyOf("line").Health);
            Assert.AreEqual(0, runtime.Game.DefeatedEnemies);
        }

        [Test]
        public void RestrictsCastPositionManaActorIdentityAndOwner()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 800, 0, 0);
            var cow = new TestCow(10, 200);
            var data = new TestData { CastRange = 50, ManaCost = 20 };
            var live = new OriginalHelperCowLiveCombatWorld(
                runtime, () => cow, data, new TestResolver());
            Assert.IsFalse(live.TryCommitCast("A0BZ", 800, 0)); // range
            Assert.AreEqual(0, live.AvailableReadyAbilities().Count); // mana
            cow.SetMana(150);
            Assert.IsFalse(live.TryCommitCast("A0BZ", 800, 0)); // range, despite enough mana
            cow.OwnerPlayerId = 1;
            Assert.AreEqual(0, live.AvailableReadyAbilities().Count);
            cow.OwnerPlayerId = 0;
            cow.OriginalRawcode = "h003";
            Assert.IsFalse(live.TryCommitCast("A0BZ", 0, 0));
            Assert.AreEqual(150, cow.CurrentMana);
        }

        [Test]
        public void SameAuthoritativeManaPowersBothWispRechargeAndAreaSpells()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 30, 0, 0);
            var cow = new TestCow(30, 200);
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 1));
            var mana = new OriginalHelperCowManaWorld(() => cow);
            var recharge = new OriginalHelperCowManaRecharge(wallet, runtime.Game, mana);
            Assert.AreEqual(51, recharge.SpendOne().CurrentMana);
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Random));

            var live = new OriginalHelperCowLiveCombatWorld(
                runtime, () => cow, new TestData(), new TestResolver());
            Assert.IsTrue(live.TryCommitCast("A0BZ", 30, 0));
            Assert.AreEqual(31, cow.CurrentMana);
        }

        [Test]
        public void CannotDamageStoryEnemyOrUnauthorisedTarget()
        {
            var runtime = World();
            runtime.SpawnEnemy("story", 20000000, CharacterEnemyKind.Story, 30, 0, 0);
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            var cow = new TestCow();
            var resolver = new TestResolver { RejectedId = "line" };
            var live = new OriginalHelperCowLiveCombatWorld(
                runtime, () => cow, new TestData(), resolver);
            Assert.IsFalse(live.TryCommitCast("A0BZ", 30, 0));
            Assert.AreEqual(20000000, runtime.EnemyOf("line").Health);
            Assert.AreEqual(20000000, runtime.EnemyOf("story").Health);
            Assert.AreEqual(200, cow.CurrentMana);
        }

        [Test]
        public void QuakeAppliesPersistentA04RLevelsInsteadOfInventedStun()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            runtime.ConfigureVerifiedEnemyOriginalA04R("line", 0, "TEST_ONLY baseline");

            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: new TestA04REffects());

            Assert.IsTrue(live.TryCommitCast("A07T", 50, 0));
            Assert.AreEqual(19000000L, runtime.EnemyOf("line").Health);
            Assert.AreEqual(6, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.IsFalse(runtime.StatusOf("line").IsStunned());
            Assert.AreEqual(180, cow.CurrentMana);

            runtime.Step(3.1);
            Assert.IsTrue(live.TryCommitCast("A07T", 50, 0));
            Assert.AreEqual(18000000L, runtime.EnemyOf("line").Health);
            Assert.AreEqual(11, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.AreEqual(160, cow.CurrentMana);
            Assert.IsFalse(runtime.StatusOf("line").IsStunned());
            double isr2;
            Assert.IsTrue(Ordr323OriginalA04R.TryReadIsr2(
                runtime.EnemyOf("line").OriginalA04RLevel.Value, out isr2));
            Assert.AreEqual(-0.10, isr2, 0.0000001);
        }

        [Test]
        public void EnhancedQuakeAddsEightA04RLevelsAndUsesVerifiedBaseDamage()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            runtime.ConfigureVerifiedEnemyOriginalA04R("line", 6, "TEST_ONLY baseline");
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 2 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: new TestA04REffects());
            Assert.IsTrue(live.TryCommitCast("A07T", 50, 0));
            Assert.AreEqual(18000000L, runtime.EnemyOf("line").Health);
            Assert.AreEqual(14, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.IsFalse(runtime.EnemyOf("line").Status.IsStunned());
        }

        [Test]
        public void UnknownA04ROnOneSurvivorRejectsWholeAreaWithoutManaCost()
        {
            var runtime = World();
            runtime.SpawnEnemy("known", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            runtime.SpawnEnemy("unknown", 20000000, CharacterEnemyKind.Line, 80, 0, 0);
            runtime.ConfigureVerifiedEnemyOriginalA04R("known", 0, "TEST_ONLY baseline");
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: new TestA04REffects());

            Assert.IsFalse(live.TryCommitCast("A07T", 50, 0));
            Assert.AreEqual(200, cow.CurrentMana);
            Assert.AreEqual(20000000L, runtime.EnemyOf("known").Health);
            Assert.AreEqual(20000000L, runtime.EnemyOf("unknown").Health);
            Assert.AreEqual(0, runtime.EnemyOf("known").OriginalA04RLevel);
            Assert.IsNull(runtime.EnemyOf("unknown").OriginalA04RLevel);
        }

        [Test]
        public void A04RBeyondHighestSourceProvenLevelRejectsSpell()
        {
            var runtime = World();
            runtime.SpawnEnemy("line", 20000000, CharacterEnemyKind.Line, 50, 0, 0);
            runtime.ConfigureVerifiedEnemyOriginalA04R("line", 24, "TEST_ONLY baseline");
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: new TestA04REffects());
            Assert.IsFalse(live.TryCommitCast("A07T", 50, 0));
            Assert.AreEqual(24, runtime.EnemyOf("line").OriginalA04RLevel);
            Assert.AreEqual(200, cow.CurrentMana);
        }

        [Test]
        public void DeadFromQuakeDoesNotNeedPersistentAbilityBaseline()
        {
            var runtime = World();
            runtime.SpawnEnemy("weak", 500000, CharacterEnemyKind.Line, 40, 0, 0);
            var cow = new TestCow { BurstLevel = 0, QuakeLevel = 1 };
            var live = new OriginalHelperCowLiveCombatWorld(runtime,
                () => cow, new TestData(), new TestResolver(),
                originalA04RMechanics: new TestA04REffects());
            Assert.IsTrue(live.TryCommitCast("A07T", 40, 0));
            Assert.IsNull(runtime.EnemyOf("weak"));
            Assert.AreEqual(1, runtime.Game.DefeatedEnemies);
        }
    }
}
