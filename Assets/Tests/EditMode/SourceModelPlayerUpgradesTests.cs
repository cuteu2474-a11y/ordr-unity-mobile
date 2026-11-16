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

        // Pinned W3X UnitWeapons.slk values, deliberately independent of
        // map-base-stats.json and the later web-catalog attack estimates.
        // This guards the early-game shared damage/tech path without turning
        // model-only hits into a retail Warcraft parity claim.
        private static readonly object[,] FirstTwentyTwoAttacks = {
            { "C-01", "h003", 29L, 31L, 1.02d, .35d, 420d, "siege" },
            { "C-02", "h002", 31L, 33L, 1.03d, .25d, 420d, "pierce" },
            { "C-03", "h001", 26L, 30L, .98d, .35d, 420d, "normal" },
            { "C-04", "h007", 25L, 27L, .84d, .20d, 420d, "normal" },
            { "C-05", "h004", 23L, 25L, .77d, .10d, 420d, "siege" },
            { "C-06", "h008", 22L, 24L, .57d, .20d, 420d, "normal" },
            { "C-07", "h005", 29L, 31L, .99d, .10d, 420d, "pierce" },
            { "C-08", "h009", 30L, 32L, .96d, .17d, 420d, "siege" },
            { "C-09", "h006", 28L, 30L, .94d, .17d, 420d, "pierce" },
            { "U-01", "h00O", 62L, 66L, 1.20d, .25d, 600d, "normal" },
            { "U-02", "h00C", 172L, 178L, 1.07d, .35d, 500d, "siege" },
            { "U-03", "h00D", 112L, 118L, .74d, .28d, 500d, "normal" },
            { "U-04", "h00M", 177L, 183L, 1.32d, .20d, 600d, "normal" },
            { "U-05", "h00K", 127L, 133L, 1.10d, .25d, 800d, "normal" },
            { "U-06", "h00I", 122L, 128L, .85d, .25d, 500d, "pierce" },
            { "U-07", "h00N", 222L, 228L, 1.40d, .25d, 1320d, "pierce" },
            { "U-08", "h00G", 172L, 178L, 1.00d, .25d, 600d, "siege" },
            { "U-09", "h00E", 132L, 138L, .96d, .25d, 500d, "pierce" },
            { "U-10", "h00J", 122L, 128L, .90d, .25d, 1000d, "normal" },
            { "U-11", "h00L", 142L, 148L, .89d, .25d, 600d, "siege" },
            { "U-12", "h00F", 207L, 213L, 1.60d, .25d, 550d, "pierce" },
            { "U-13", "h00A", 157L, 163L, 1.30d, .17d, 600d, "siege" }
        };

        [Test]
        public void AllNineCommonAndThirteenUncommonAttacksUseSourceDiceTimingRangeAndPaidTech()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var native = new OriginalMapMappedNativeAttackEvidenceCatalog(map);
            var model = Model();
            var rules = new SourceModelBasicCombatRules(map, new Dice());
            rules.ConfigurePlayerUpgrades(model);
            rules.Enable();
            Assert.AreEqual(22, FirstTwentyTwoAttacks.GetLength(0));
            Assert.AreEqual(0, model.LevelOf("R00F"));
            Assert.AreEqual(0, model.AttackSpeedPercent("h003"));
            var before = new long[22];
            for (int i = 0; i < FirstTwentyTwoAttacks.GetLength(0); i++)
            {
                string id = (string)FirstTwentyTwoAttacks[i, 0];
                string raw = (string)FirstTwentyTwoAttacks[i, 1];
                long minimum = (long)FirstTwentyTwoAttacks[i, 2];
                long maximum = (long)FirstTwentyTwoAttacks[i, 3];
                double cooldown = (double)FirstTwentyTwoAttacks[i, 4];
                double attackPoint = (double)FirstTwentyTwoAttacks[i, 5];
                double range = (double)FirstTwentyTwoAttacks[i, 6];
                string attackType = (string)FirstTwentyTwoAttacks[i, 7];

                var catalog = map.Find(id);
                Assert.IsNotNull(catalog, id);
                Assert.AreEqual(raw, catalog.OriginalRawcode, id);
                var source = catalog.Attack1;
                var objectSource = map.FindMapObjectAttack(raw);
                Assert.IsNotNull(objectSource, id);
                Assert.AreEqual(minimum, source.MinimumBaseDamage.Value, id);
                Assert.AreEqual(maximum, source.MaximumBaseDamage.Value, id);
                Assert.AreEqual(cooldown, source.AttackCooldownSeconds.Value, 1e-9, id);
                Assert.AreEqual(attackPoint, source.AttackPointSeconds.Value, 1e-9, id);
                Assert.AreEqual(range, source.OriginalRange.Value, 1e-9, id);
                Assert.AreEqual(attackType, source.OriginalAttackType, id);
                Assert.AreEqual(source.MinimumBaseDamage, objectSource.MinimumBaseDamage, id);
                Assert.AreEqual(source.MaximumBaseDamage, objectSource.MaximumBaseDamage, id);
                Assert.AreEqual(source.AttackCooldownSeconds, objectSource.AttackCooldownSeconds, id);
                Assert.AreEqual(source.OriginalRange, objectSource.OriginalRange, id);
                Assert.AreEqual(source.AttackPointSeconds, objectSource.AttackPointSeconds, id);
                Assert.AreEqual(minimum, source.BaseDamagePlus.Value + source.DiceCount.Value, id);
                Assert.AreEqual(maximum,
                    source.BaseDamagePlus.Value + source.DiceCount.Value * source.DiceSides.Value, id);

                var attack = native.Find(id);
                Assert.IsNotNull(attack, id);
                Assert.AreEqual(raw, attack.Rawcode, id);
                Assert.AreEqual(cooldown, attack.CooldownSeconds, 1e-9, id);
                Assert.AreEqual(maximum, attack.RollObjectBaseDamage(new Dice()), id);
                Assert.AreEqual(0, model.AttackBonus(raw), id);
                Assert.AreEqual(0, model.AttackSpeedPercent(raw), id);
                var spec = new BasicAttackSpec(minimum, cooldown, range,
                    DamageSchool.Physical, DamageTraits.None);
                var defense = new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: source attack regression");
                before[i] = rules.ResolveBasicDamage(
                    new BasicDamageRequest("existing:" + id, raw, spec, "before:" + id),
                    new CombatEnemy("before:" + id, 1000000, defense));
                var expected = (long)Math.Floor(
                    Ordr323MapDamageMath.ApplyVerifiedFactors(maximum, attack.NativeAttackType,
                        Ordr323ArmorType.Normal, true, true, 0, false, 0, 1));
                Assert.AreEqual(expected, before[i], id + " initial attack");
            }

            // Real wallet debit and upgrade step: no free +3 before purchase.
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 3));
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded,
                Service(model, wallet).Purchase("R00F"));
            Assert.AreEqual(0, wallet.Balance("gold"));
            Assert.AreEqual(1, model.LevelOf("R00F"));

            for (int i = 0; i < FirstTwentyTwoAttacks.GetLength(0); i++)
            {
                string id = (string)FirstTwentyTwoAttacks[i, 0];
                string raw = (string)FirstTwentyTwoAttacks[i, 1];
                long minimum = (long)FirstTwentyTwoAttacks[i, 2];
                long maximum = (long)FirstTwentyTwoAttacks[i, 3];
                double cooldown = (double)FirstTwentyTwoAttacks[i, 4];
                double range = (double)FirstTwentyTwoAttacks[i, 6];
                var attack = native.Find(id);
                var spec = new BasicAttackSpec(minimum, cooldown, range,
                    DamageSchool.Physical, DamageTraits.None);
                var defense = new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: paid upgrade regression");
                Assert.AreEqual(3, model.AttackBonus(raw), id);
                Assert.AreEqual(0, model.AttackSpeedPercent(raw), id);
                long existing = rules.ResolveBasicDamage(
                    new BasicDamageRequest("existing:" + id, raw, spec, "paid-existing:" + id),
                    new CombatEnemy("paid-existing:" + id, 1000000, defense));
                long newlyCreated = rules.ResolveBasicDamage(
                    new BasicDamageRequest("new:" + id, raw, spec, "paid-new:" + id),
                    new CombatEnemy("paid-new:" + id, 1000000, defense));
                long expected = (long)Math.Floor(
                    Ordr323MapDamageMath.ApplyVerifiedFactors(maximum + 3,
                        attack.NativeAttackType, Ordr323ArmorType.Normal,
                        true, true, 0, false, 0, 1));
                Assert.AreEqual(expected, existing, id + " existing attacker tech");
                Assert.AreEqual(expected, newlyCreated, id + " new attacker tech");
                Assert.GreaterOrEqual(existing, before[i], id);
            }
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
