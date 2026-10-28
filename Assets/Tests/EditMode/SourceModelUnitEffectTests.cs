using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelUnitEffectTests
    {
        private sealed class Dice : ICombatProcRandom
        {
            private readonly bool proc;
            public Dice(bool activate) { proc = activate; }
            public int Next(int upper) => upper == 1000000 && !proc ? upper - 1 : 0;
        }
        private sealed class Gold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) => max; }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SourceModelPracticeBattle Battle;
            public SourceModelBasicCombatRules Rules;
            public CharacterUnitCatalog Units;
            public OriginalMapBaseStatCatalog Map;
            public void Dispose() { Battle.Dispose(); }
        }
        private static string Data(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create(bool proc = true)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var dice = new Dice(proc);
            var six = new Original323SpecialAuraCatalog(map);
            string audit = Data("original-2323C-all-unit-aura-audit.json");
            var combined = new Original323CompositeAuraCatalog(map,
                Original323ArmorAuraCatalog.FromJson(audit, map, six),
                Original323NativeSpeedAuraCatalog.FromJson(audit, map),
                Original323NativeCommandAuraCatalog.FromJson(audit, map));
            var rules = new SourceModelBasicCombatRules(map, dice);
            rules.ConfigureUnitEffects(new SourceModelUnitEffects(
                Original323NativePassiveCatalog.FromJson(Data("original-2323C-native-pure-passives.json"), map),
                combined, dice, Data("practice-native-inherited-defaults.json")));
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var economy = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            economy.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id,x,y) => economy.Units.Add(id,x,y), id => economy.Units.Remove(id));
            combat.Start(); rules.Enable(); combat.ConfigureSourceModelBasicAttacks(rules);
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(), economy,
                new SourceModelPracticeCatalog(Data("mobile-practice-battle-model.json")),
                Original323MonsterWaveIdentityCatalog.FromJson(Data("original-2323C-monster-wave-script-evidence.json")),
                rules, new Gold(), null);
            battle.Start();
            return new Live { Combat = combat, Battle = battle, Rules = rules, Units = units, Map = map };
        }
        [Test]
        public void MappedObjectAttacksCoverAllGradesWithoutFreeSpawnsOrRetailApproval()
        {
            using (var game = Create())
            {
                int count = 0;
                foreach (var row in game.Map.All)
                {
                    OriginalVerifiedAttackBinding binding;
                    if (game.Rules.TryResolve(game.Units.Require(row.UnitId), null, out binding)) count++;
                }
                Assert.AreEqual(271, count);
                Assert.AreEqual(0, game.Combat.CharacterCount);
                Assert.IsTrue(game.Combat.UsesSourceModelBasicAttacks);
            }
        }
        [Test]
        public void CriticalModifiesOneBasicPacketBeforeArmorAndReportsAnActivation()
        {
            long plain, critical;
            using (var game = Create(false))
            {
                long id = game.Combat.SpawnCharacter("U-04", -4000, 4000);
                var events = game.Battle.Advance(.6);
                plain = events.Single(e => e.Type == CharacterCombatEventType.Attack).Amount;
                Assert.IsFalse(game.Combat.HasVerifiedAttack(id));
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated));
            }
            using (var game = Create())
            {
                game.Combat.SpawnCharacter("U-04", -4000, 4000);
                var events = game.Battle.Advance(.6);
                critical = events.Single(e => e.Type == CharacterCombatEventType.Attack).Amount;
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated));
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage));
                Assert.AreEqual(338 - critical, game.Combat.MobileEnemySnapshot().Single().Health);
            }
            Assert.Greater(critical, plain);
        }
        [Test]
        public void BrookStunStopsMovementAndResumesAfterTheSourceDuration()
        {
            using (var game = Create())
            {
                game.Combat.SpawnCharacter("U-03", -4000, 4000);
                var events = game.Battle.Advance(.6);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated));
                var target = game.Combat.MobileEnemySnapshot().Single();
                Assert.IsTrue(game.Combat.StatusOf(target.Id).IsStunned());
                game.Battle.Advance(.1);
                Assert.AreEqual(target.Y, game.Combat.MobileEnemySnapshot().Single().Y, 1e-6);
                game.Battle.Advance(.2);
                Assert.IsFalse(game.Combat.StatusOf(target.Id).IsStunned());
                Assert.AreNotEqual(target.Y, game.Combat.MobileEnemySnapshot().Single().Y);
            }
        }
        [Test]
        public void RobinVolleyDamagesAtMostThreeDistinctLivingEnemies()
        {
            using (var game = Create())
            {
                game.Battle.Advance(2);
                game.Combat.SpawnCharacter("U-01", -4000, 4000);
                var events = game.Battle.Advance(.3);
                Assert.AreEqual(4, game.Combat.MobileEnemySnapshot().Count);
                Assert.AreEqual(3, game.Combat.MobileEnemySnapshot().Count(e => e.Health < e.MaxHealth));
                Assert.AreEqual(2, events.Count(e => e.Type == CharacterCombatEventType.SkillDamage));
                Assert.AreEqual(1, events.Count(e => e.Type == CharacterCombatEventType.Attack));
            }
        }
        private static long LuffyFirstDamage(params string[] auraUnits)
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("C-01", -4000, 4000);
                foreach (string unit in auraUnits) game.Combat.SpawnCharacter(unit, -4000, 4000);
                return game.Battle.Advance(.6).First(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == id.ToString()).Amount;
            }
        }
        [Test]
        public void ArmorAuraUsesInheritedFlatModeAndDuplicateCastersDoNotStack()
        {
            long plain = LuffyFirstDamage();
            long one = LuffyFirstDamage("S-10");
            long two = LuffyFirstDamage("S-10", "S-10");
            Assert.Greater(one, plain);
            Assert.AreEqual(one, two);
            // Different raw buff identities require a separate stacking audit.
            Assert.AreEqual(plain, LuffyFirstDamage("S-10", "S-26"));
        }
        [Test]
        public void CommandAuraChangesTheSameBasicDamagePacket()
        {
            Assert.Greater(LuffyFirstDamage("S-25"), LuffyFirstDamage());
        }
        [Test]
        public void SlowAuraChangesTravelAndIdenticalDuplicatesHaveOneEffect()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                var before = game.Combat.MobileEnemySnapshot().Single();
                game.Combat.SpawnCharacter("S-14", -4000, 4000);
                game.Combat.SpawnCharacter("S-14", -4000, 4000);
                game.Battle.Advance(.1); // Before Smoker's 0.17s first hit.
                var after = game.Combat.MobileEnemySnapshot().Single();
                double dx = after.X - before.X, dy = after.Y - before.Y;
                Assert.AreEqual(28.5, Math.Sqrt(dx * dx + dy * dy), 1e-5);
                Assert.AreEqual(1, game.Combat.StatusOf(after.Id).Snapshot().Count(e => e.Type == CombatEffectType.MovementSlowPercent));
            }
        }
        [Test]
        public void RemovedAuraOwnerCannotLeaveAnInfiniteBuffBehind()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                long owner = game.Combat.SpawnCharacter("S-14", -4000, 4000);
                var target = game.Combat.MobileEnemySnapshot().Single();
                Assert.IsTrue(game.Combat.StatusOf(target.Id).Snapshot().Any(e => e.Type == CombatEffectType.MovementSlowPercent));
                game.Combat.RemoveCharacter(owner);
                Assert.IsFalse(game.Combat.StatusOf(target.Id).Snapshot().Any(e => e.Type == CombatEffectType.MovementSlowPercent));
            }
        }
    }
}
