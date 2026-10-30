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
        {
            private readonly bool winningRoll;
            public Gold(bool winningRoll) { this.winningRoll = winningRoll; }
            public int NextInclusive(int min, int max) =>
                winningRoll ? Original323MonsterGoldRules.WinningGoldRoll : max;
        }
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
        private static Live Create(bool proc = true, bool durableEnemy = false,
            int lastRound = 10, bool winningGoldRoll = false)
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
            string model = Data(lastRound == 65 ? "mobile-standalone-enemy-model.json" : "mobile-practice-battle-model.json");
            if (durableEnemy) model = model.Replace("\"health\": 338,", "\"health\": 1000000000000,");
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(), economy,
                new SourceModelPracticeCatalog(model, lastRound),
                Original323MonsterWaveIdentityCatalog.FromJson(Data("original-2323C-monster-wave-script-evidence.json")),
                rules, new Gold(winningGoldRoll), null);
            battle.Start();
            return new Live { Combat = combat, Battle = battle, Rules = rules, Units = units, Map = map };
        }
        [TestCase("X-02")][TestCase("R-18")][TestCase("L-04")][TestCase("L-05")]
        [TestCase("L-29")][TestCase("L-35")][TestCase("H-08")][TestCase("TR-02")]
        [TestCase("TR-13")][TestCase("TR-14")][TestCase("TR-22")][TestCase("TR-23")]
        [TestCase("TR-30")][TestCase("E-01")][TestCase("E-09")][TestCase("E-11")]
        [TestCase("I-02")][TestCase("I-03")][TestCase("RN-12")][TestCase("RN-14")]
        [TestCase("MY-06")][TestCase("MY-08")]
        public void TwentyTwoSpeedAurasReachTheirSourceAllowedRecipients(string id)
        {
            using (var game = Create(true, true))
            {
                long caster = game.Combat.SpawnCharacter(id, -4000, 4000);
                long ally = game.Combat.SpawnCharacter("C-01", -4000, 4000);
                var aura = game.Rules.UnitEffects.AurasFor(id).Single(a => a.Level == 1 && a.Type == CombatEffectType.AttackSpeedBuff);
                game.Battle.Advance(.5);
                bool casterHas = game.Combat.UnitStatusOf(caster).Snapshot().Any(e => e.Type == CombatEffectType.AttackSpeedBuff);
                bool allyHas = game.Combat.UnitStatusOf(ally).Snapshot().Any(e => e.Type == CombatEffectType.AttackSpeedBuff);
                Assert.AreEqual(aura.AllowsAllyRole(true), casterHas, id);
                Assert.AreEqual(aura.AllowsAllyRole(false), allyHas, id);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(caster), id);
            }
        }
        [Test]
        public void PracticeSpeedAuraChangesNextAttackAndRemovalRestoresBaseCadence()
        {
            using (var game = Create(true, true))
            {
                long caster = game.Combat.SpawnCharacter("X-02", -4000, 4000);
                long ally = game.Combat.SpawnCharacter("C-01", -4000, 4000);
                game.Battle.Advance(.5);
                var boosted = game.Combat.InspectBasicAttack(ally).NextAttackAtSeconds;
                Assert.IsTrue(boosted.HasValue);
                // First line monster appears at 0.5s; the ready attacker hits then.
                Assert.AreEqual(.5 + 1.02 / 1.11, boosted.Value, 1e-6);
                game.Combat.RemoveCharacter(caster);
                game.Battle.Advance(1.1);
                var restored = game.Combat.InspectBasicAttack(ally).NextAttackAtSeconds;
                Assert.IsTrue(restored.HasValue);
                Assert.AreEqual(boosted.Value + 1.02, restored.Value, 1e-6);
            }
        }
        [Test]
        public void DifferentSpeedAurasAreDeferredWithoutBlockingBasicAttacks()
        {
            using (var game = Create(true, true))
            {
                game.Combat.SpawnCharacter("X-02", -4000, 4000);
                game.Combat.SpawnCharacter("R-18", -4000, 4000);
                long ally = game.Combat.SpawnCharacter("C-01", -4000, 4000);
                var events = game.Battle.Advance(.5);
                Assert.IsFalse(game.Combat.UnitStatusOf(ally).Snapshot().Any(e => e.Type == CombatEffectType.AttackSpeedBuff));
                Assert.Greater(game.Rules.UnitEffects.DeferredAuraGroups, 0);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == ally.ToString()));
            }
        }
        [Test]
        public void AllPublishedPracticeSpeedDescriptionsMatchLiveCatalog()
        {
            using (var game = Create())
            {
                var root = OrdrJson.Object(OrdrJson.Parse(Data("unit-runtime-status.json")), "status");
                int count = 0;
                foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
                {
                    var row = OrdrJson.Object(item, "unit");
                    string id = OrdrJson.RequiredString(row, "catalogId");
                    var descriptions = game.Rules.UnitEffects.Describe(id, game.Map.Find(id).OriginalRawcode);
                    if (!descriptions.Any(text => text.Contains("공격속도 증가(%)"))) continue;
                    var published = OrdrJson.Array(OrdrJson.Value(row, "connectedModelEffects"), "effects")
                        .Select(value => (string)value).ToArray();
                    CollectionAssert.AreEqual(published, descriptions, id);
                    count++;
                }
                Assert.AreEqual(22, count);
            }
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
                Assert.AreEqual(273, count);
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
        [TestCase("X-01")][TestCase("R-22")][TestCase("R-28")]
        [TestCase("L-10")][TestCase("L-30")][TestCase("H-02")][TestCase("H-21")]
        [TestCase("TR-05")][TestCase("TR-12")][TestCase("TR-14")][TestCase("TR-26")]
        [TestCase("E-01")][TestCase("I-01")][TestCase("I-05")]
        [TestCase("SR-01")][TestCase("SR-02")][TestCase("SR-03")][TestCase("SR-04")]
        [TestCase("RN-06")][TestCase("RN-11")]
        public void PracticeFirstLevelEffectActuallyActivatesForAllTwentyOwners(string unitId)
        {
            using (var game = Create(true, true))
            {
                long actor = game.Combat.SpawnCharacter(unitId, -4000, 4000);
                var passive = game.Rules.UnitEffects.PassiveFor(game.Map.Find(unitId).OriginalRawcode);
                Assert.IsNotNull(passive, unitId);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(actor));
                var events = game.Battle.Advance(2);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == actor.ToString()), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated && e.ActorId == actor.ToString()), unitId);
                Assert.IsTrue(game.Rules.UnitEffects.Describe(unitId, game.Map.Find(unitId).OriginalRawcode)
                    .Any(e => e.Contains("연습 1단계")), unitId);
            }
        }
        [Test]
        public void CombinedCriticalAndStunShareTheRollAndDoNotRunOnFailure()
        {
            long ordinary;
            using (var game = Create(false, true))
            {
                game.Combat.SpawnCharacter("X-01", -4000, 4000);
                var events = game.Battle.Advance(.6);
                ordinary = events.First(e => e.Type == CharacterCombatEventType.Attack).Amount;
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated));
                Assert.IsFalse(game.Combat.MobileEnemySnapshot().Any(e => game.Combat.StatusOf(e.Id).IsStunned()));
            }
            using (var game = Create(true, true))
            {
                game.Combat.SpawnCharacter("X-01", -4000, 4000);
                var events = game.Battle.Advance(.6);
                Assert.Greater(events.First(e => e.Type == CharacterCombatEventType.Attack).Amount, ordinary);
                Assert.IsTrue(game.Combat.MobileEnemySnapshot().Any(e => game.Combat.StatusOf(e.Id).IsStunned()));
            }
        }
        [Test]
        public void TwentyPracticeDescriptionsMatchThePublishedRuntimeStatus()
        {
            using (var game = Create())
            {
                var root = OrdrJson.Object(OrdrJson.Parse(Data("unit-runtime-status.json")), "status");
                int checkedUnits = 0;
                foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
                {
                    var row = OrdrJson.Object(item, "unit");
                    string id = OrdrJson.RequiredString(row, "catalogId");
                    var raw = game.Map.Find(id).OriginalRawcode;
                    var passive = game.Rules.UnitEffects.PassiveFor(raw);
                    if (passive == null || !game.Rules.UnitEffects.Describe(id, raw)
                        .Any(text => text.Contains("연습 1단계"))) continue;
                    var expected = OrdrJson.Array(OrdrJson.Value(row, "connectedModelEffects"), "effects")
                        .Select(value => (string)value).ToArray();
                    CollectionAssert.AreEqual(expected, game.Rules.UnitEffects.Describe(id, raw), id);
                    checkedUnits++;
                }
                Assert.AreEqual(21, checkedUnits);
            }
        }
        [Test]
        public void MixedCriticalBonusHasBothEventsOnOneSuccessfulAttack()
        {
            using (var game = Create(true, true))
            {
                long actor = game.Combat.SpawnCharacter("S-31", -4000, 4000);
                var events = game.Battle.Advance(1.6);
                Assert.IsTrue(events.Any(e => e.ActorId == actor.ToString() && e.Detail.Contains("크리티컬")));
                Assert.IsTrue(events.Any(e => e.ActorId == actor.ToString() && e.Detail.Contains("추가 피해")));
                Assert.IsTrue(events.Any(e => e.ActorId == actor.ToString() && e.Detail.Contains("기절")));
            }
            using (var game = Create(false, true))
            {
                long actor = game.Combat.SpawnCharacter("S-31", -4000, 4000);
                var events = game.Battle.Advance(1.6);
                Assert.IsFalse(events.Any(e => e.ActorId == actor.ToString() && e.Type == CharacterCombatEventType.SkillActivated));
            }
        }
        [Test]
        public void AllFiftyThreeBonusOwnersApplyOneSharedAttackPacket()
        {
            var root = OrdrJson.Object(OrdrJson.Parse(Data("original-2323C-native-pure-passives.json")), "passives");
            int count = 0;
            foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "practiceBonusEntries"), "bonus"))
            {
                var row = OrdrJson.Object(item, "bonus");
                string id = OrdrJson.RequiredString(row, "catalogId");
                long boosted;
                using (var game = Create(true, true))
                {
                    long actor = game.Combat.SpawnCharacter(id, -4000, 4000);
                    var events = game.Battle.Advance(1.6);
                    boosted = events.First(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == actor.ToString()).Amount;
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.ActorId == actor.ToString() && e.Detail.Contains("추가 피해")), id);
                    Assert.IsFalse(game.Combat.HasVerifiedAttack(actor), id);
                    Assert.IsTrue(game.Rules.UnitEffects.Describe(id, game.Map.Find(id).OriginalRawcode)
                        .Any(e => e.Contains("객체 추가 피해")), id);
                }
                if (OrdrJson.Number(row, "chancePerMillion") < 1000000)
                using (var game = Create(false, true))
                {
                    long actor = game.Combat.SpawnCharacter(id, -4000, 4000);
                    var events = game.Battle.Advance(1.6);
                    long normal = events.First(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == actor.ToString()).Amount;
                    Assert.Greater(boosted, normal, id);
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.ActorId == actor.ToString() && e.Detail.Contains("추가 피해")), id);
                }
                count++;
            }
            Assert.AreEqual(53, count);
        }
        [Test]
        public void BonusDescriptionsMatchPublishedRuntimeStatus()
        {
            using (var game = Create())
            {
                var root = OrdrJson.Object(OrdrJson.Parse(Data("unit-runtime-status.json")), "status");
                int count = 0;
                foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
                {
                    var row = OrdrJson.Object(item, "unit");
                    string id = OrdrJson.RequiredString(row, "catalogId");
                    string raw = game.Map.Find(id).OriginalRawcode;
                    var passive = game.Rules.UnitEffects.PassiveFor(raw);
                    if (passive == null || passive.BonusDamage <= 0) continue;
                    var expected = OrdrJson.Array(OrdrJson.Value(row, "connectedModelEffects"), "effects")
                        .Select(value => (string)value).ToArray();
                    CollectionAssert.AreEqual(expected, game.Rules.UnitEffects.Describe(id, raw), id);
                    count++;
                }
                Assert.AreEqual(53, count);
            }
        }
        [Test]
        public void BonusKillUsesNormalDefeatAndGoldPath()
        {
            using (var game = Create(winningGoldRoll: true))
            {
                game.Combat.SpawnCharacter("U-05", -4000, 4000);
                var events = game.Battle.Advance(1);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated && e.Detail.Contains("추가 피해")));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.EnemyDefeated));
                Assert.Greater(game.Combat.Game.DefeatedEnemies, 0);
                Assert.Greater(game.Battle.GoldAwarded, 0);
            }
        }
        [Test]
        public void BonusKillWithoutWinningGoldRollStillCountsAsDefeat()
        {
            using (var game = Create())
            {
                game.Combat.SpawnCharacter("U-05", -4000, 4000);
                var events = game.Battle.Advance(1);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.EnemyDefeated));
                Assert.Greater(game.Combat.Game.DefeatedEnemies, 0);
                Assert.AreEqual(0, game.Battle.GoldAwarded);
            }
        }
        [Test]
        public void BonusPassiveAlsoRunsInStandaloneSixtyFiveRoundBattle()
        {
            using (var game = Create(true, true, 65))
            {
                game.Combat.SpawnCharacter("U-09", -4000, 4000);
                Assert.AreEqual(65, game.Battle.LastRound);
                double leadInSeconds = game.Battle.PreparationSecondsRemaining;
                Assert.AreEqual(Original323BossRoundRules.FirstRoundWaitSeconds, leadInSeconds);
                var preparationEvents = game.Battle.Advance(leadInSeconds);
                Assert.AreEqual(0, game.Combat.EnemyCount);
                Assert.IsFalse(preparationEvents.Any(e => e.Type == CharacterCombatEventType.SkillActivated));
                var events = game.Battle.Advance(2);
                Assert.Greater(game.Combat.EnemyCount, 0);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated && e.Detail.Contains("추가 피해")));
            }
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

