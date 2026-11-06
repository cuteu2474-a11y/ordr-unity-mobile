using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelPlayerUpgradesTests
    {
        private static string Data(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static SourceModelPlayerUpgrades Model() => new SourceModelPlayerUpgrades(Data("original-2323C-player-upgrade-runtime.json"));
        private sealed class Dice : ICombatProcRandom { public int Next(int bound) => bound - 1; }
        private static Original323PlayerUpgradeSession Service(SourceModelPlayerUpgrades model, EconomyWallet wallet, bool start = true)
        {
            var game = new GameSession();
            if (start) game.Start();
            return new Original323PlayerUpgradeSession(wallet, game, () => 0, sourceModel: model);
        }

        [Test]
        public void SourceStartupLevelsAndFirstPaidLevelAreNotConfused()
        {
            var model = Model(); var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 100));
            var service = Service(model, wallet);
            Assert.AreEqual(0, model.LevelOf("R00F"));
            Assert.AreEqual(1, model.LevelOf("R008"));
            Assert.AreEqual(1, model.LevelOf("R01V"));
            Assert.AreEqual(0, model.LevelOf("R032"));
            Assert.AreEqual(30, service.Quote("R008").NextCost.Entries.Single().Value);
            Assert.IsNull(service.Quote("R008").NextStep, "Model must not fabricate a native verified step");
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded, service.Purchase("R008"));
            Assert.AreEqual(2, model.LevelOf("R008"));
            Assert.AreEqual(70, wallet.Balance("gold"));
            Assert.IsTrue(wallet.HasCompletedTransaction("original323-upgrade:R008:2"));
        }

        [Test]
        public void CommonHasNoInventedSpeedAndMaximumNeverChargesAgain()
        {
            var model = Model(); var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 100));
            var service = Service(model, wallet);
            for (int n = 0; n < 7; n++)
                Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded, service.Purchase("R00F"));
            Assert.AreEqual(21, model.AttackBonus("h003"));
            Assert.AreEqual(0, model.AttackSpeedPercent("h003"));
            Assert.AreEqual(37, wallet.Balance("gold"));
            Assert.AreEqual(Original323UpgradePurchaseStatus.MaximumLevel, service.Purchase("R00F"));
            Assert.AreEqual(37, wallet.Balance("gold"));
        }

        [Test]
        public void InsufficientFundsAndStoppedSessionHaveNoLevelOrReceiptSideEffects()
        {
            var model = Model(); var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 2));
            Assert.AreEqual(Original323UpgradePurchaseStatus.NotRunning, Service(model, wallet, false).Purchase("R00F"));
            Assert.AreEqual(Original323UpgradePurchaseStatus.InsufficientResources, Service(model, wallet).Purchase("R00F"));
            Assert.AreEqual(2, wallet.Balance("gold")); Assert.AreEqual(0, model.LevelOf("R00F"));
            Assert.IsFalse(wallet.HasCompletedTransaction("original323-upgrade:R00F:1"));
        }

        [Test]
        public void LevelTwelveRequiresActualCrocodileDefeatAndPreservesOrderedCv8Links()
        {
            var model = Model(); var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 100000));
            var service = Service(model, wallet);
            service.Purchase("R003");
            Assert.AreEqual(2, model.LevelOf("R005"));
            Assert.AreEqual(3, model.LevelOf("R02W"), "CV8 R02W reads the already incremented R005");
            for (int level = 2; level < 11; level++) service.Purchase("R003");
            long before = wallet.Balance("gold");
            model.ObserveOriginalBossDefeated("o01V");
            Assert.AreEqual(Original323UpgradePurchaseStatus.PrerequisiteMissing, service.Purchase("R003"));
            Assert.AreEqual(11, model.LevelOf("R003")); Assert.AreEqual(before, wallet.Balance("gold"));
            model.ObserveOriginalBossDefeated("o01W");
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded, service.Purchase("R003"));
            Assert.AreEqual(12, model.LevelOf("R003"));
        }

        [Test]
        public void ActiveAndLaterFormObjectsReadSameTechWithoutRepeatedBuffAccumulation()
        {
            var model = Model();
            Assert.AreEqual(22500, model.AttackBonus("H08U"));
            Assert.AreEqual(215, model.AttackSpeedPercent("H08U"), .00001);
            var step = model.NextStep("R003");
            Assert.IsTrue(model.TryApply(step, 1));
            Assert.AreEqual(24650, model.AttackBonus("H08U"));
            Assert.AreEqual(24650, model.AttackBonus("H0B6"));
            Assert.AreEqual(28500, model.AttackBonus("H0AE"), "Secondary R00C: 22500 + 6000");
            Assert.IsFalse(model.TryApply(step, 1), "Stale step cannot increment twice");
            Assert.AreEqual(24650, model.AttackBonus("H08U"));
            Assert.AreEqual(67500, model.AttackBonus("h0AD"), "Explicit R023 mapping receives the original initial upgrade");
            Assert.AreEqual(0, model.AttackBonus("ZZZZ"), "Unlisted object gets no grade-based guess");
        }

        [Test]
        public void RestoreValidatesEverythingBeforeChangingLiveEffects()
        {
            var model = Model(); var before = model.CaptureState();
            model.TryApply(model.NextStep("R003"), 1);
            var bad = model.CaptureState(); bad.Levels["R00F"] = 8;
            Assert.Throws<ArgumentException>(() => model.RestoreState(bad));
            Assert.AreEqual(2, model.LevelOf("R003"));
            model.RestoreState(before);
            Assert.AreEqual(1, model.LevelOf("R003"));
            Assert.AreEqual(22500, model.AttackBonus("H08U"));
        }

        [Test]
        public void PaidCommonUpgradeChangesRealBasicDamageForOldAndNewAttackerIds()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var model = Model(); var rules = new SourceModelBasicCombatRules(map, new Dice());
            rules.ConfigurePlayerUpgrades(model); rules.Enable();
            var enemy = new CombatEnemy("enemy", 100000, new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null, "normal", "TEST_ONLY"));
            var attack = new BasicAttackSpec(29, 1.02, DamageSchool.Physical, DamageTraits.None);
            long old = rules.ResolveBasicDamage(new BasicDamageRequest("1", "h003", attack, "enemy"), enemy);
            var wallet = new EconomyWallet(); wallet.Grant(new ResourceBundle().Add("gold", 3));
            Service(model, wallet).Purchase("R00F");
            long same = rules.ResolveBasicDamage(new BasicDamageRequest("1", "h003", attack, "enemy"), enemy);
            long later = rules.ResolveBasicDamage(new BasicDamageRequest("2", "h003", attack, "enemy"), enemy);
            Assert.Greater(same, old); Assert.AreEqual(same, later);
        }

        [Test]
        public void InitialRareSpeedReachesActualAttackScheduler()
        {
            int Count(bool upgrades)
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
                var dice = new Dice(); var rules = new SourceModelBasicCombatRules(map, dice);
                if (upgrades) rules.ConfigurePlayerUpgrades(Model());
                var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
                combat.Start(); rules.Enable(); combat.ConfigureSourceModelBasicAttacks(rules);
                combat.SpawnCharacter("R-01", 0, 0); combat.BeginWave(1);
                combat.SpawnEnemyWithDefenses("enemy", 100000000, CharacterEnemyKind.Line, 10, 0, 0,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null, "normal", "TEST_ONLY"));
                return combat.Step(5).Count(x => x.Type == CharacterCombatEventType.Attack);
            }
            Assert.Greater(Count(true), Count(false));
        }
    }
}
