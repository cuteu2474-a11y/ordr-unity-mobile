using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelScriptAttackSkillTests
    {
        private sealed class Dice : ICombatProcRandom
        {
            public bool Trigger;
            public bool ForceSugarSuicide;
            public bool BlockKaidoMorph;
            public bool ForceKaidoSecondBranch;
            public Dice(bool trigger) { Trigger = trigger; }
            public int Next(int bound) => ForceSugarSuicide && bound == 1000
                ? 9 : ForceKaidoSecondBranch && bound == 100
                    ? 14 : BlockKaidoMorph && bound == 100
                    ? 99 : Trigger ? 0 : bound - 1;
        }
        private sealed class NoGold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) => max; }
        private sealed class MutableStoryLane : IOriginalStoryDispatchPort
        {
            public bool Deployed;
            public readonly HashSet<long> DeployedIds = new HashSet<long>();
            public bool CanDispatch(long id) => false;
            public ManualActionResult Dispatch(long id) => null;
            public bool IsDeployed(long id) => Deployed || DeployedIds.Contains(id);
            public bool CanRecall(long id) => false;
            public ManualActionResult Recall(long id) => null;
        }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SourceModelPracticeBattle Battle;
            public long ActorId;
            public Dice Rolls;
            public void Dispose() { Battle.Dispose(); }
        }
        private static string Data(string path) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", path));

        private static Live Create(string unitId, bool trigger,
            double x = -4000, double y = 4000, bool enormousHealth = true,
            bool withNativeAuras = false, bool withSourceResources = false)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            // Pinned W3X stats are validated against the source 277 catalog
            // before the nine source/wikilink identities are overlaid.
            if (unitId == "MY-14" || unitId == "TR-31" || unitId == "R-44")
                units.ApplyWikiExtension(Data("wiki-unit-catalog-extension.json"));
            var dice = new Dice(trigger);
            var rules = new SourceModelBasicCombatRules(map, dice);
            if (withNativeAuras)
            {
                string audit = Data("original-2323C-all-unit-aura-audit.json");
                var six = new Original323SpecialAuraCatalog(map);
                var combined = new Original323CompositeAuraCatalog(map,
                    Original323ArmorAuraCatalog.FromJson(audit, map, six),
                    Original323NativeSpeedAuraCatalog.FromJson(audit, map),
                    Original323NativeCommandAuraCatalog.FromJson(audit, map));
                rules.ConfigureUnitEffects(new SourceModelUnitEffects(
                    Original323NativePassiveCatalog.FromJson(
                        Data("original-2323C-native-pure-passives.json"), map),
                    combined, dice, Data("practice-native-inherited-defaults.json"),
                    Original323MapObjectAuraCatalog.FromJson(audit, map)));
            }
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var shared = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y), id => shared.Units.Remove(id));
            combat.Start(); rules.Enable(); combat.ConfigureSourceModelBasicAttacks(rules);
            if (withSourceResources)
                combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
            long actorId = combat.SpawnCharacter(unitId, x, y);
            string practice = Data("mobile-practice-battle-model.json");
            if (enormousHealth)
                practice = practice.Replace("\"health\": 338,",
                    "\"health\": 1000000000000,");
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(),
                shared, new SourceModelPracticeCatalog(practice, 10),
                Original323MonsterWaveIdentityCatalog.FromJson(
                    Data("original-2323C-monster-wave-script-evidence.json")),
                rules, new NoGold(), null);
            battle.Start();
            return new Live { Combat = combat, Battle = battle,
                ActorId = actorId, Rolls = dice };
        }

        [Test]
        public void HibariUsesRawUnitDataPointValuesNotRarityProxies()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var rules = new SourceModelBasicCombatRules(map, new Dice(false));
            Assert.AreEqual(90, rules.OriginalUnitPointValue("R-07", "h025"));
            Assert.AreEqual(101, rules.OriginalUnitPointValue("L-01", "h02U"));
            Assert.AreEqual(131, rules.OriginalUnitPointValue("TR-24", "H0CO"));
            Assert.IsNull(rules.OriginalUnitPointValue("L-01", "h0CM"),
                "Do not apply another rawcode's point value.");
            using (var live = Create("L-39", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                long rare = live.Combat.SpawnCharacter("R-07", -4900, 5400);
                long legendary = live.Combat.SpawnCharacter("L-01", -4900, 5400);
                long koby = live.Combat.SpawnCharacter("TR-24", -4900, 5400);
                Assert.IsTrue(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                Assert.IsFalse(live.Combat.CanUseSourceModelHibariBento(
                    live.ActorId, rare, wallet));
                Assert.IsTrue(live.Combat.CanUseSourceModelHibariBento(
                    live.ActorId, legendary, wallet));
                Assert.IsTrue(live.Combat.CanUseSourceModelHibariBento(
                    live.ActorId, koby, wallet));
                Assert.AreEqual(1, wallet.Balance(
                    Original323SourceNativeActionKeys.HibariItem(live.ActorId)));
            }
        }

        [Test]
        public void SourceBlackbeardJassStrengthScalingAddsDamageOnlyWithBaseStatWitness()
        {
            double baseline, witnessed;
            using (var first = Create("TR-01", true, -4900, 5400,
                       withSourceResources: true))
            {
                baseline = first.Battle.Advance(2).Where(evt =>
                    evt.Type == CharacterCombatEventType.SkillDamage)
                    .Sum(evt => evt.Amount);
            }
            using (var second = Create("TR-01", true, -4900, 5400,
                       withSourceResources: true))
            {
                second.Combat.ConfigureSourceModelHeroUnbonusedStrengthProvider(
                    id => id == second.ActorId ? (double?)10 : null);
                witnessed = second.Battle.Advance(2).Where(evt =>
                    evt.Type == CharacterCombatEventType.SkillDamage)
                    .Sum(evt => evt.Amount);
            }
            Assert.Greater(baseline, 0);
            Assert.Greater(witnessed, baseline,
                "Bzz/Bzr/Bzx must apply source STR*60000/5000/5000 " +
                "within their existing packets, with no second proc.");
        }

        [Test]
        public void SourceNamiJassIntelligenceScalingRequiresTrueHeroStatWitness()
        {
            double baseline, witnessed;
            using (var first = Create("TR-02", true, -4900, 5400))
            {
                baseline = first.Battle.Advance(3).Where(evt =>
                    evt.Type == CharacterCombatEventType.SkillDamage &&
                    evt.SkillName == "낙뢰").Sum(evt => evt.Amount);
            }
            using (var second = Create("TR-02", true, -4900, 5400))
            {
                second.Combat.ConfigureSourceModelHeroIntelligenceProvider(
                    id => id == second.ActorId ? (double?)8 : null);
                witnessed = second.Battle.Advance(3).Where(evt =>
                    evt.Type == CharacterCombatEventType.SkillDamage &&
                    evt.SkillName == "낙뢰").Sum(evt => evt.Amount);
            }
            Assert.Greater(baseline, 0);
            Assert.Greater(witnessed, baseline,
                "Nami Bxp snapshots the witnessed INT including bonuses " +
                "and adds 3500 per eight damage hits.");
        }

        [Test]
        public void NewMysticHanmaUsesOriginalObjectAttackInsteadOfEmptyShell()
        {
            var source = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var original = source.FindMapObjectAttack("h0C2");
            Assert.NotNull(original);
            Assert.AreEqual(152501d, original.MinimumBaseDamage.Value);
            Assert.AreEqual(152504d, original.MaximumBaseDamage.Value);
            Assert.AreEqual(0.71d, original.AttackCooldownSeconds.Value, 0.00001d);
            using (var live = Create("MY-14", true))
            {
                var status = live.Combat.InspectBasicAttack(live.ActorId);
                Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedSource, status.Kind);
                var results = live.Battle.Advance(5);
                Assert.IsTrue(results.Any(x => x.Type == CharacterCombatEventType.Attack),
                    "Hanma's original source attack must reach the normal battle pipeline");
            }
        }

        [Test]
        public void WikiSupportDoesNotInventDamageAndFormsCannotSpawnAlone()
        {
            using (var live = Create("R-44", true))
            {
                Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource,
                    live.Combat.InspectBasicAttack(live.ActorId).Kind);
            }
            Assert.Throws<InvalidOperationException>(() => Create("TR-31", true),
                "The extra Zoro form must never become a free independent unit");
        }

        [Test]
        public void ExactTwentyCallbacksMatchSourceAuditAndAreModelOnly()
        {
            var root = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-attack-skill-batch.json")), "attack audit");
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                OrdrJson.RequiredString(root, "sourceJassSha256"));
            Assert.AreEqual(false, OrdrJson.Value(root, "runtimeAuthorization"));
            var audited = OrdrJson.Array(OrdrJson.Value(root, "units"), "units");
            Assert.AreEqual(20, SourceModelScriptAttackCatalog.All.Count);
            Assert.AreEqual(audited.Count, SourceModelScriptAttackCatalog.All.Count);
            foreach (var source in audited)
            {
                var row = OrdrJson.Object(source, "source attack");
                var cb = OrdrJson.Object(OrdrJson.Value(row, "callback"), "callback");
                string raw = OrdrJson.RequiredString(row, "rawcode");
                SourceScriptAttackSpec spec;
                Assert.IsTrue(SourceModelScriptAttackCatalog.TryGet(raw, out spec), raw);
                Assert.AreEqual(OrdrJson.RequiredString(row, "catalogId"), spec.CatalogId);
                Assert.AreEqual(OrdrJson.RequiredString(cb, "function"), spec.JassFunction);
                Assert.IsFalse(spec.OnProc.Count == 0);
            }
        }

        [TestCase("h00M", "U-04", "Bnm")]
        [TestCase("h00A", "U-13", "Bnp")]
        public void RankyakuUsesEightHundredTravelAndTaperedCasterFacing(
            string rawcode, string unitId, string callback)
        {
            SourceScriptAttackSpec spec;
            Assert.IsTrue(SourceModelScriptAttackCatalog.TryGet(rawcode, out spec));
            Assert.AreEqual(unitId, spec.CatalogId);
            Assert.AreEqual(callback, spec.JassFunction);
            Assert.AreEqual(100, spec.RollSides);
            Assert.AreEqual(10, spec.RollSuccess);
            var wave = spec.OnProc.Single();
            Assert.AreEqual(SourceScriptHitShape.RankyakuLine, wave.Shape);
            Assert.AreEqual(150, wave.Damage);
            Assert.AreEqual(800, wave.Length, "AUcs DataC, not 1700 Rng");
            Assert.AreEqual(200, wave.Radius);
            Assert.AreEqual(250, wave.EndRadius);
            Assert.IsTrue(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 0, 400, 225));
            Assert.IsFalse(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 0, 400, 226));
            Assert.IsTrue(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 0, 800, 250));
            Assert.IsFalse(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 0, 801, 0));
            Assert.IsFalse(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 0, -1, 0));
            Assert.IsTrue(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 90, 0, 400));
            Assert.IsFalse(SourceModelRankyakuGeometry.Contains(wave, 0, 0, 90, 400, 0));
            Assert.IsFalse(SourceModelRankyakuGeometry.Contains(
                wave, 0, 0, double.NaN, 0, 400));
        }

        [TestCase("U-04")][TestCase("U-13")][TestCase("S-03")][TestCase("S-13")]
        [TestCase("R-11")][TestCase("R-12")][TestCase("R-13")][TestCase("R-21")]
        [TestCase("R-23")][TestCase("R-26")][TestCase("R-30")][TestCase("R-31")]
        [TestCase("R-35")][TestCase("R-38")][TestCase("H-11")][TestCase("H-16")]
        [TestCase("H-18")][TestCase("L-29")][TestCase("L-33")][TestCase("RN-04")]
        public void SourceAttackCallbackIsInvokedInRealPracticeCombat(string unitId)
        {
            using (var live = Create(unitId, true))
            {
                var source = SourceModelScriptAttackCatalog.All.Single(x => x.CatalogId == unitId);
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack), unitId);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.Detail.Contains(source.JassFunction)), unitId);
                // Native spell follow-ups remain explicitly deferred, not
                // silently claimed to have spawned real Warcraft actors.
                if (source.DeferredNativeEffect != null)
                    Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EffectDeferred &&
                        x.Detail.Contains(source.DeferredNativeEffect)), unitId);
            }
        }

        [Test]
        public void FailedProcDoesNotGrantSkillDamageButCrackerAlwaysHits()
        {
            using (var ordinary = Create("R-21", false))
            {
                var events = ordinary.Battle.Advance(5);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BpM")));
            }
            using (var cracker = Create("L-33", false))
            {
                var events = cracker.Battle.Advance(5);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("Btd")));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("Btd")));
            }
        }

        [Test]
        public void AdditionalSixCallbacksMatchPinnedMapAndRemainModelOnly()
        {
            var root = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-next-20-native-source-audit.json")), "next 20 audit");
            Assert.AreEqual(SourceModelNextAttackCatalog.SourceJassSha256,
                OrdrJson.RequiredString(root, "sourceJassSha256"));
            Assert.AreEqual(false, OrdrJson.Value(root, "runtimeAuthorization"));
            Assert.AreEqual(6, SourceModelNextAttackCatalog.All.Count);
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "units"), "units"))
            {
                var row = OrdrJson.Object(raw, "unit");
                var callbacks = OrdrJson.Array(OrdrJson.Value(row, "w5AttackCallbackEvidence"),
                    "registered callbacks");
                if (callbacks.Count == 0) continue;
                Assert.AreEqual(1, callbacks.Count);
                var callback = OrdrJson.Object(callbacks[0], "callback");
                SourceScriptAttackSpec spec;
                Assert.IsTrue(SourceModelNextAttackCatalog.TryGet(
                    OrdrJson.RequiredString(row, "originalUnitRawcode"), out spec));
                Assert.AreEqual(OrdrJson.RequiredString(row, "catalogId"), spec.CatalogId);
                Assert.AreEqual(OrdrJson.RequiredString(callback, "function"), spec.JassFunction);
                Assert.AreEqual(OrdrJson.Number(callback, "attackEventChancePercent"),
                    100.0 * spec.RollSuccess / spec.RollSides);
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.DeferredNativeEffect));
            }
        }

        [TestCase("S-08")][TestCase("S-12")][TestCase("S-15")]
        [TestCase("S-18")][TestCase("S-19")][TestCase("S-22")]
        public void SixAdditionalSkillBranchesActuallyFireInCombat(string unitId)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var source = SourceModelNextAttackCatalog.All.Single(x => x.CatalogId == unitId);
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains(source.JassFunction)), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains(source.DeferredNativeEffect)), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains(source.JassFunction)), unitId);
            }
        }

        [Test]
        public void SquardDirectJassAreaDamageOccursAfterSourcePointTwoNineDelay()
        {
            using (var live = Create("S-15", true, -4900, 5400))
            {
                bool activated = false;
                for (int i = 0; i < 150; i++)
                {
                    var events = live.Battle.Advance(.05);
                    if (!events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.Detail.Contains("BoC"))) continue;
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("BoC")));
                    activated = true;
                    break;
                }
                Assert.IsTrue(activated, "First attack should dispatch BoC");
                // The declaration may occur part-way through the .05 step:
                // the .29 second hit must not be visible after just .20s.
                var before = live.Battle.Advance(.20);
                Assert.IsFalse(before.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BoC")));
                var after = live.Battle.Advance(.15);
                Assert.IsTrue(after.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BoC")));
            }
        }

        [TestCase("S-05", "언홀리 프렌지")]
        [TestCase("S-20", "리퀴드 파이어")]
        [TestCase("S-28", "피닉스 파이어")]
        public void SourceNativeBuffAndBurnActuallyActivate(string unitId, string ability)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(8);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName != null && e.SkillName.Contains(ability)), unitId);
                if (unitId == "S-20" || unitId == "S-28")
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains(unitId == "S-20" ? "Aliq" : "A0EA")),
                        "At least one native object modeled damage tick");
            }
        }

        [Test]
        public void MoriaBlackArrowCreatesVirtualAttackNotAnOwnedZombie()
        {
            using (var live = Create("S-09", true, -4900, 5400, false))
            {
                var events = live.Battle.Advance(2);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("A087/ANba")));
                Assert.IsFalse(live.Combat.MobileUnitSnapshot().Any(x =>
                    x.Unit.Id == "U-14"),
                    "Only Absalom's original permanent acquisition may own a U-14.");
                Assert.IsEmpty(live.Combat.PracticeSummonsOf(live.ActorId),
                    "Moria's 10-second native attack effect is not a selectable summon.");
                Assert.AreEqual(1, live.Combat.CharacterCount);
                live.Battle.Advance(11);
                Assert.IsFalse(live.Combat.MobileUnitSnapshot().Any(x =>
                    x.Unit.Id == "U-14"));
            }
        }

        private static List<SourceModelSummonState> InternalSourceEffects(
            CharacterCombatRuntime combat)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "sourceTemporarySummons",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (List<SourceModelSummonState>)field.GetValue(combat);
        }

        [Test]
        public void SugarR24SelfDestructIsEffectOnlyAndRetiresWithOriginalTimedLife()
        {
            using (var live = Create("R-24", true, -4900, 5400))
            {
                var events = live.Battle.Advance(1.2);
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "슈가 · 장난감 투사체"));
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "슈가 · 장난감 자폭 사망"));
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.Amount > 0), "Only the existing A03A damage packet may hit.");
                Assert.IsFalse(InternalSourceEffects(live.Combat).Any(x =>
                    x.SourceUnitRawcode == "h06O" && !x.IsVirtualAttackEffect));
                Assert.IsEmpty(live.Combat.PracticeSummonsOf(live.ActorId));
                Assert.AreEqual(1, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void LegendMoriaShadowHasNoInventoryAndOnlySourceVerifiedAutonomousHit()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var native = map.FindMapObjectAttack("h05D");
            bool sourceCanAttack = native != null &&
                native.BaseDamagePlus.HasValue && native.DiceCount.HasValue &&
                native.DiceSides.HasValue && native.AttackCooldownSeconds.HasValue &&
                native.AttackPointSeconds.HasValue &&
                native.AttackPointSeconds.Value <= native.AttackCooldownSeconds.Value;
            using (var live = Create("L-12", true, -4900, 5400))
            {
                var events = live.Battle.Advance(1);
                Assert.IsTrue(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillActivated &&
                    x.Detail.Contains("h05D 그림자")));
                var shadows = InternalSourceEffects(live.Combat).Where(x =>
                    x.OwnerId == live.ActorId && x.SourceUnitRawcode == "h05D").ToList();
                Assert.IsNotEmpty(shadows);
                Assert.IsTrue(shadows.All(x => x.IsVirtualAttackEffect &&
                    x.HasIndependentCombat == sourceCanAttack));
                Assert.IsEmpty(live.Combat.PracticeSummonsOf(live.ActorId));
                Assert.AreEqual(1, live.Combat.CharacterCount);
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsFalse(InternalSourceEffects(live.Combat)
                    .Any(x => x.SourceUnitRawcode == "h05D"));
            }
        }

        [Test]
        public void UtaSongUsesCommonSevenSecondEffectClockAndCancelsOnLaneChange()
        {
            using (var live = Create("E-09", true, -4900, 5400,
                withSourceResources: true))
            {
                live.Combat.ConfigureSourceModelFusionNativeReceipts(id =>
                    id == Original323FusionNativeFollowupLedger.UtaSongId ? 1 : 0);
                for (int i = 0; i < 200 &&
                    live.Combat.PracticeSongSoldierCount(live.ActorId) == 0; i++)
                    live.Battle.Advance(.1);
                Assert.Greater(live.Combat.PracticeSongSoldierCount(live.ActorId), 0);
                var songs = InternalSourceEffects(live.Combat)
                    .Where(x => x.OwnerId == live.ActorId &&
                        x.SourceUnitRawcode == "h04O").ToList();
                Assert.IsNotEmpty(songs);
                Assert.IsTrue(songs.All(x => x.IsVirtualAttackEffect &&
                    x.HasIndependentCombat && x.ExpiresAt > live.Combat.Time));
                Assert.IsEmpty(live.Combat.PracticeSummonsOf(live.ActorId));
                string raw;
                double x, y;
                Assert.IsTrue(live.Combat.TryGetSourceTransientProjectileOrigin(
                    "practice-summon:" + songs[0].Id, out x, out y, out raw));
                Assert.AreEqual("h04O", raw);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.AreEqual(0, live.Combat.PracticeSongSoldierCount(live.ActorId));
                Assert.IsFalse(InternalSourceEffects(live.Combat).Any(s =>
                    s.OwnerId == live.ActorId && s.SourceUnitRawcode == "h04O"));
                Assert.IsFalse(live.Combat.TryGetSourceTransientProjectileOrigin(
                    "practice-summon:" + songs[0].Id, out x, out y, out raw));
            }
        }

        [TestCase("R-13", "A14X", .9)]
        [TestCase("R-26", "A14Y", .95)]
        [TestCase("R-30", "A150", 1.15)]
        [TestCase("R-31", "A151", 1.2)]
        [TestCase("H-16", "A156", 1.7)]
        public void FiveInfernoFollowupsApplyPinnedNativeStunNotExtraDamage(
            string unitId, string ability, double duration)
        {
            var spec = SourceModelLegacyNativeFollowupCatalog.All
                .Single(x => x.CatalogId == unitId);
            Assert.AreEqual(ability, spec.AbilityRawcode);
            Assert.AreEqual(duration, spec.NormalStunSeconds, 1e-9);
            Assert.AreEqual(.01, spec.ImpactDelaySeconds, 1e-9);
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains(ability) && e.Detail.Contains("기절")), unitId);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains(ability)), "AUin DataA1 is ZERO; native stun is not extra damage");
            }
        }

        [Test]
        public void NamiBntPreservesUniversalDamageAndOriginalAttackType()
        {
            var spec = SourceModelScriptAttackCatalog.All.Single(x => x.CatalogId == "S-03");
            Assert.AreEqual("Bnt", spec.JassFunction);
            Assert.AreEqual(100, spec.RollSides);
            Assert.AreEqual(10, spec.RollSuccess);
            Assert.AreEqual(1, spec.OnProc.Count);
            var hit = spec.OnProc[0];
            Assert.AreEqual(SourceScriptHitShape.Circle, hit.Shape);
            Assert.AreEqual(400d, hit.Radius);
            Assert.AreEqual(800d, hit.Damage);
            // In this project Spells is JASS ATTACK_TYPE_NORMAL, not magic attack.
            Assert.AreEqual(Ordr323AttackType.Spells, hit.Attack);
            Assert.AreEqual(SourceScriptDamageType.Universal, hit.DamageType);
            Assert.IsFalse(hit.UsesArmorRating);
            // Universal ignores the numeric armor, but keeps its attack-type
            // versus armor-class row (Medium is 1x in this map).
            double amount = Ordr323MapDamageMath.ApplyVerifiedFactors(
                hit.Damage, hit.Attack, Ordr323ArmorType.Medium,
                true, hit.UsesArmorRating, 1000, false, 0, 1);
            Assert.AreEqual(800d, amount, .00001);
        }

        [Test]
        public void NamiDummyPlacesAnActualFivePercentSlowZone()
        {
            using (var live = Create("S-03", true, -4900, 5400))
            {
                var events = live.Battle.Advance(2.5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("e00K/A089")));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(status =>
                        status.SourceId.StartsWith("practice-nami:A089:") &&
                        status.Type == CombatEffectType.MovementSlowPercent &&
                        status.Amount == 5)));
            }
        }

        [Test]
        public void NamiAftershockExpiresAfterThreeSecondsWithoutStaleSlow()
        {
            using (var live = Create("S-03", true, -4900, 5400))
            {
                var triggered = live.Battle.Advance(2.5);
                Assert.IsTrue(triggered.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("e00K/A089")));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(status =>
                        status.SourceId.StartsWith("practice-nami:A089:"))));
                // Future ordinary attacks still occur, but cannot refresh
                // the already cast zone after the fixed 3s expires.
                live.Rolls.Trigger = false;
                live.Battle.Advance(
                    SourceModelLegacyNativeFollowupCatalog.NamiAftershockDurationSeconds + .1);
                Assert.IsFalse(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(status =>
                        status.SourceId.StartsWith("practice-nami:A089:"))),
                    "Expired e00K aura must not permanently slow enemies.");
            }
        }

        [Test]
        public void SentinelAppliesFifteenNativeA04RLevelsAndSixtySecondFaerieFire()
        {
            using (var live = Create("RN-04", true, -4900, 5400))
            {
                var events = live.Battle.Advance(5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("Bd8(A04R): 1→16")));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("e0LE/A10I")));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(effect =>
                        effect.SourceId == "practice-native:A10I" &&
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.Amount == 50 && effect.DurationSeconds == 60)));
            }
        }

        [Test]
        public void CrackerVisualDummyAddsNoUnverifiedSecondDamageSource()
        {
            var cracker = SourceModelScriptAttackCatalog.All.Single(x => x.CatalogId == "L-33");
            Assert.IsNull(cracker.DeferredNativeEffect,
                "e04X contains only Aloc and Avul; no native combat damage");
            Assert.AreEqual(1, cracker.Always.Count);
            Assert.AreEqual(20000, cracker.Always[0].Damage);
            Assert.AreEqual(250000, cracker.OnProc.Single().Damage);
        }

        [Test]
        public void BonneyBaseAttackDeclaresA04PBeforeHitWithoutInventingConditionalProcs()
        {
            using (var live = Create("TR-21", true, -4900, 5400))
            {
                var events = live.Battle.Advance(5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "보니 · A04P" && e.Detail.Contains("B4C/Bd2")));
                Assert.IsFalse(events.Any(e => e.SkillName == "보니 · A04P" &&
                    (e.Detail.Contains("SingleProc") || e.Detail.Contains("AreaProc"))));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(effect =>
                        effect.SourceId == "practice-native:A04P" &&
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.Amount >= 10 && effect.Amount <= 40)));
            }
        }

        [Test]
        public void DeferredZoroCircleCannotJumpAcrossStoryLaneOrBackOnReturn()
        {
            using (var live = Create("L-27", true, -4900, 5400))
            {
                var lane = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lane);
                long id = live.Combat.MobileUnitSnapshot()
                    .Single(x => x.Unit.Id == "L-27").InstanceId;
                bool declared = false;
                for (int i = 0; i < 1000; ++i)
                {
                    var events = live.Battle.Advance(.02);
                    if (!events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.Detail.Contains("Bsk/Bsj:"))) continue;
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("Bsk/Bsj")));
                    declared = true;
                    break;
                }
                Assert.IsTrue(declared, "The JASS Bsk/Bsj path must declare its delayed hit.");
                lane.Deployed = true;
                live.Combat.RefreshAfterCommittedStoryLaneChange(id);
                lane.Deployed = false;
                live.Combat.RefreshAfterCommittedStoryLaneChange(id);
                var after = live.Battle.Advance(.06);
                Assert.IsFalse(after.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("Bsk/Bsj")),
                    "A returning caster must not revive the old main-lane blast");
            }
        }

        [Test]
        public void SecondThirtyModelsHaveExactRosterAndOnlyWholeTooltipPackets()
        {
            var roster = OrdrJson.Object(OrdrJson.Parse(Data("unit-runtime-status.json")),
                "source roster");
            var actual = new Dictionary<string, string>();
            foreach (var value in OrdrJson.Array(OrdrJson.Value(roster, "entries"), "entries"))
            {
                var row = OrdrJson.Object(value, "unit");
                actual.Add(OrdrJson.RequiredString(row, "catalogId"),
                    OrdrJson.String(row, "originalRawcode"));
            }
            var firstThirty = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-next-30-runtime-coverage.json")), "first thirty");
            var previous = new HashSet<string>(OrdrJson.Array(
                OrdrJson.Value(firstThirty, "units"), "units")
                .Select(x => OrdrJson.RequiredString(OrdrJson.Object(x, "unit"), "id")));
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            Assert.AreEqual(33, SourceModelNextThirtySkillCatalog.All.Count);
            Assert.AreEqual(33, SourceModelNextThirtySkillCatalog.All
                .Select(x => x.CatalogId).Distinct().Count());
            int enabled = 0, activeEnabled = 0;
            foreach (var row in SourceModelNextThirtySkillCatalog.All)
            {
                // E-03 was intentionally extended after its initial callback
                // coverage; no other older-batch unit may overlap this list.
                if (previous.Contains(row.CatalogId))
                    Assert.AreEqual("E-03", row.CatalogId,
                        "Unexpected owner already covered by the first batch: " + row.CatalogId);
                Assert.AreEqual(actual[row.CatalogId], row.Rawcode);
                SourceModelNextThirtyUnit matched;
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.TryGet(row.Rawcode, out matched));
                Assert.AreSame(row, matched);
                var profile = profiles.Require(row.CatalogId);
                foreach (string name in row.PracticeAttackSkills)
                {
                    var skill = profile.Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill),
                        row.CatalogId + ": " + name);
                    enabled++;
                }
                foreach (string name in row.PracticeActiveSkills)
                {
                    var skill = profile.Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                        row.CatalogId + ": " + name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                        row.CatalogId, row.PracticeActiveSkills, skill));
                    activeEnabled++;
                }
            }
            Assert.AreEqual(51, enabled, "Only whole attack tooltip packets are allowed");
            Assert.AreEqual(18, activeEnabled, "Resource-gated source and composite actives");
        }

        [TestCase("L-04", "바람바람 열매")]
        [TestCase("L-13", "배리어 볼")]
        [TestCase("L-15", "왼손은 수면제")]
        [TestCase("TR-02", "미라쥬 템포")]
        [TestCase("TR-04", "범위 마법 스턴")]
        [TestCase("H-04", "숲 물리")]
        [TestCase("H-22", "마리조아붕괴")]
        [TestCase("E-01", "혼합 광역 타격")]
        [TestCase("E-04", "매 타격 추가 마법")]
        [TestCase("E-08", "범위 물리")]
        [TestCase("I-04", "광역 물리 타격")]
        [TestCase("L-03", "전격")]
        [TestCase("L-07", "명왕의 검술")]
        [TestCase("L-13", "배리어 피스트")]
        [TestCase("L-15", "타격 스턴")]
        [TestCase("L-20", "화이트블로우")]
        [TestCase("H-02", "보스·광폭화 함포")]
        [TestCase("H-12", "용의 발톱")]
        [TestCase("TR-09", "광역 3연타")]
        [TestCase("TR-10", "타격 스턴")]
        [TestCase("E-04", "연속 범위 타격")]
        [TestCase("E-08", "단일 스턴")]
        [TestCase("I-04", "타격·단일 스턴")]
        public void SecondThirtySupportedTooltipAttacksExecuteInPracticeOnly(
            string unitId, string name)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == name && e.Detail.Contains("TOOLTIP_MODEL_ONLY")), unitId);
                if (name != "타격 스턴" && name != "단일 스턴")
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.SkillName == name && e.Amount > 0), unitId);
                else
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.StatusApplied &&
                        e.SkillName == name), unitId);
            }
        }

        [TestCase("H-07", "갑판 포격", "H-07:skill:0", 95, false)]
        [TestCase("TR-04", "체력 소모 광역 공격", "TR-04:skill:0", 40, true)]
        [TestCase("TR-09", "마나 광역 공격", "TR-09:skill:1", 100, false)]
        [TestCase("TR-14", "킹코브라", "TR-14:skill:0", 85, false)]
        [TestCase("E-01", "마나 광역기", "E-01:skill:1", 150, false)]
        [TestCase("E-04", "마나 고정 피해", "E-04:skill:0", 175, false)]
        [TestCase("E-08", "마나 광역 스턴", "E-08:skill:0", 145, false)]
        public void SourceModelActiveConsumesOnlyConfiguredResourcesAndHits(
            string unitId, string name, string skillId, int cost, bool healthCost)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                Assert.AreEqual(AbilityCastResult.NoResourceState,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id));
                var resources = new CharacterResources(1000, 1000, 1000, 1000);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.MissingTarget,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, "missing"));
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id));
                Assert.AreEqual(healthCost ? 1000 - cost : 1000, resources.Health);
                Assert.AreEqual(healthCost ? 1000 : 1000 - cost, resources.Mana);
                Assert.IsTrue(live.Combat.LastEvents.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == name && x.Amount > 0), unitId);
            }
        }

        [Test]
        public void SourceSlkBaselineMatchesAllSevenPreviouslyUnusableActivePools()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var resourceCatalog = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            Assert.Greater(resourceCatalog.Count, 200);
            var expected = new[] {
                new object[] { "H-07", "h03R", 10d, 95d, 0d, 0d },
                new object[] { "TR-04", "H098", 40d, 0d, 0.5d, 0d },
                new object[] { "TR-09", "H094", 40d, 100d, 0.33d, 0d },
                new object[] { "TR-14", "H0B2", 10d, 85d, 0d, 0d },
                new object[] { "E-01", "H0BK", 115d, 150d, 0.25d, 0d },
                new object[] { "E-04", "h058", 10d, 175d, 0d, 2d },
                new object[] { "E-08", "h08R", 10d, 145d, 0d, 0d },
                new object[] { "TR-18", "H0BD", 95d, 0d, 0.33d, 0d }
            };
            foreach (var row in expected)
            {
                var source = resourceCatalog.Find((string)row[0]);
                Assert.IsNotNull(source, (string)row[0]);
                Assert.AreEqual(row[1], source.OriginalRawcode);
                Assert.AreEqual((double)row[2], source.MaximumHealth, 1e-9);
                Assert.AreEqual((double)row[3], source.MaximumMana, 1e-9);
                Assert.AreEqual((double)row[4], source.HealthRegeneration, 1e-9);
                Assert.AreEqual((double)row[5], source.ManaRegeneration, 1e-9);
            }
            Assert.Throws<InvalidOperationException>(() =>
                SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                    Data("original-2323C-unit-object-stats.json")
                        .Replace("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5",
                            "not-source-matched"), map));
        }

        [Test]
        public void FullCostHealthIsSpendableOnlyInExplicitSourceMode()
        {
            var verified = new CharacterResources(40, 40, 0, 0);
            Assert.IsFalse(verified.CanSpend(40, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => verified.SetHealth(0));
            var source = new CharacterResources(40, 40, 0, 0, true);
            Assert.IsTrue(source.CanSpend(40, 0));
            source.Spend(40, 0);
            Assert.AreEqual(0, source.Health);
            Assert.IsFalse(source.CanSpend(1, 0));
            source.RegenerateSourceModel(0.5, 0, 2);
            Assert.AreEqual(1, source.Health, 1e-9);
            source.RegenerateSourceModel(0.5, 0, 500);
            Assert.AreEqual(40, source.Health);
            Assert.Throws<InvalidOperationException>(() =>
                verified.RegenerateSourceModel(0.5, 0, 1));
        }

        [TestCase("H-07", "갑판 포격", 95d, false)]
        [TestCase("TR-04", "체력 소모 광역 공격", 40d, true)]
        [TestCase("TR-09", "마나 광역 공격", 100d, false)]
        [TestCase("TR-14", "킹코브라", 85d, false)]
        [TestCase("E-01", "마나 광역기", 150d, false)]
        [TestCase("E-04", "마나 고정 피해", 175d, false)]
        [TestCase("E-08", "마나 광역 스턴", 145d, false)]
        public void RealSourceBaselineEnablesOneAutomaticActiveWithExactDebit(
            string unitId, string skillName, double cost, bool spendsHealth)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var resources = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(resources);
                Assert.AreEqual(spendsHealth ? cost : cost,
                    spendsHealth ? resources.MaximumHealth : resources.MaximumMana);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                    unitId + " source model cast");
                bool twoActives = unitId == "TR-09" || unitId == "E-01";
                Assert.AreEqual(spendsHealth || unitId == "E-01" ? 0 :
                    resources.MaximumHealth, resources.Health, 1e-8);
                Assert.AreEqual(spendsHealth || unitId == "E-01" ?
                    resources.MaximumMana : 0, resources.Mana, 1e-8);
                if (twoActives)
                {
                    Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                        "Each source-approved second active must debit only its own resource");
                    Assert.AreEqual(0, resources.Health, 1e-8);
                    Assert.AreEqual(0, resources.Mana, 1e-8);
                }
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "Casting without replenished resources must not duplicate rewards.");
                // External source model events settle only on the next
                // combat step, using the existing central event queue.
                var events = live.Battle.Advance(.1);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == skillName), unitId);
            }
        }

        // Thirteen source-statted models, deliberately reusing the existing
        // resource ledger and tooltip effect executor. They are not Warcraft
        // native-cast parity and must never run without original target data.
        [TestCase("MY-07", "가스터 블래스터", 100d, false)]
        [TestCase("MY-08", "요르 포저", 75d, false)]
        // Kuma's 115-mana attack is dispatched through its original JASS
        // callback; it must not double-cast as a generic tooltip active.
        [TestCase("TR-11", "마나 범위 타격", 125d, false)]
        [TestCase("TR-12", "유성소바 킥", 115d, false)]
        [TestCase("TR-15", "해왕류 다이브", 120d, false)]
        [TestCase("TR-19", "파이어 버드 스타", 140d, false)]
        [TestCase("TR-24", "어니스티 임팩트", 150d, true)]
        [TestCase("I-01", "특제 대포환", 160d, false)]
        [TestCase("I-06", "대자재천신", 115d, true)]
        [TestCase("MY-04", "나선섬광초륜무후삼식", 100d, false)]
        [TestCase("MY-05", "종경백제검", 145d, false)]
        [TestCase("MY-06", "이성의 창조물", 145d, false)]
        public void AdditionalThirteenCharactersCastSourceStattedActiveInRealPracticeBattle(
            string unitId, string skillName, double cost, bool spendsHealth)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
                var profile = skills.Find(unitId);
                Assert.IsNotNull(profile);
                var skill = profile.Skills.Single(x => x.Name == skillName);
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                    "All documented components and cost fields must be supported.");
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                var source = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                    Data("original-2323C-unit-object-stats.json"), map);
                live.Combat.ConfigureSourceModelUnitResources(source);
                var resources = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(resources, unitId);
                Assert.AreEqual(cost,
                    spendsHealth ? resources.MaximumHealth : resources.MaximumMana, 1e-8);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                    unitId + " / " + skillName + " active cast");
                Assert.AreEqual(0, spendsHealth ? resources.Health : resources.Mana, 1e-8);
                if (unitId == "MY-08")
                {
                    // Previously approved Yor is still first, and the new
                    // health-cost Lloyd follows on the next auto-cast frame.
                    Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives());
                    Assert.AreEqual(0, resources.Health, 1e-8);
                }
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "No double debit or free repeated cast");
                var events = live.Battle.Advance(.1);
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == skillName),
                    unitId + " active must reach the real shared enemy HP pipeline");
            }
        }

        [TestCase("MY-08", "로이드 포저", 45d, true)]
        [TestCase("I-04", "광역 혼합 피해", 150d, false)]
        [TestCase("TR-10", "마나 광역 공격", 115d, false)]
        [TestCase("I-09", "마나 광역 스턴", 125d, false)]
        [TestCase("LI-07", "뇌영", 145d, false)]
        [TestCase("L-28", "어인가라데", 85d, false)]
        [TestCase("RN-13", "슈퍼 페이몬 선풍", 100d, false)]
        [TestCase("H-04", "거대 숲 물리", 85d, false)]
        [TestCase("L-25", "미티어 울두건", 35d, true)]
        [TestCase("D-03", "브라키오 붐버", 65d, false)]
        public void FirstTenCompositeActivesSpendSourceResourcesAndHitActualEnemies(
            string unitId, string skillName, double cost, bool spendsHealth)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                    .Require(unitId).Skills.Single(x => x.Name == skillName);
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsFirstCompositeActive(skill));
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(pool, unitId);
                Assert.AreEqual(cost,
                    spendsHealth ? pool.MaximumHealth : pool.MaximumMana, 1e-8);
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, skill.Id, target.Id), unitId);
                Assert.AreEqual(0, spendsHealth ? pool.Health : pool.Mana, 1e-8);
                Assert.IsTrue(live.Combat.LastEvents.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == skillName && x.Amount > 0), unitId);
                CombatEffectType? expectedStatus = null;
                if (unitId == "MY-08" || unitId == "I-04")
                    expectedStatus = CombatEffectType.MovementSlowPercent;
                else if (unitId == "TR-10" || unitId == "I-09" || unitId == "RN-13")
                    expectedStatus = CombatEffectType.MagicDamageAmplification;
                else if (unitId == "LI-07")
                    expectedStatus = CombatEffectType.MagicResistanceReduction;
                else if (unitId == "H-04")
                    expectedStatus = CombatEffectType.ArmorReduction;
                if (expectedStatus.HasValue)
                    Assert.IsTrue(live.Combat.StatusOf(target.Id).Snapshot().Any(x =>
                        x.Type == expectedStatus.Value &&
                        x.SourceId.StartsWith("practice-tooltip:")), unitId);
                if (unitId == "L-25" || unitId == "D-03")
                    Assert.AreEqual(unitId == "L-25" ? 1 : 3,
                        live.Combat.ArmorBreakStacksOf(target.Id), unitId);
            }
        }

        [TestCase("E-09", "광역 노래 마법", 100d, false, 0)]
        [TestCase("SR-03", "체력 소모 광역공격", 60d, true, 3)]
        [TestCase("TR-08", "레드호크", 160d, false, 7)]
        [TestCase("TR-22", "어인가라데", 100d, false, 3)]
        [TestCase("TR-29", "씽크홀", 140d, false, 9)]
        [TestCase("LI-06", "범위 광역 스턴", 55d, false, 10)]
        [TestCase("MY-12", "염동 회오리", 45d, true, 10)]
        [TestCase("MY-12", "염동 운석낙하", 75d, false, 15)]
        public void SecondBatchEightCompositeActivesUseSharedResourceAndDefenseLedger(
            string unitId, string name, double cost, bool health, int stacks)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsSecondCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(pool);
                Assert.GreaterOrEqual(health ? pool.MaximumHealth : pool.MaximumMana, cost);
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                double before = health ? pool.Health : pool.Mana;
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, skill.Id, target.Id), name);
                Assert.AreEqual(before - cost, health ? pool.Health : pool.Mana, 1e-8);
                Assert.IsTrue(live.Combat.LastEvents.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == name && e.Amount > 0), name);
                if (stacks != 0)
                    Assert.AreEqual(stacks, live.Combat.ArmorBreakStacksOf(target.Id), name);
                if (unitId == "E-09")
                    Assert.IsTrue(live.Combat.StatusOf(target.Id).Snapshot().Any(e =>
                        e.Type == CombatEffectType.ExplosiveDamageAmplification &&
                        e.Amount == 10), name);
                if (unitId == "LI-06")
                    Assert.IsTrue(live.Combat.UnitStatusOf(live.ActorId).Snapshot().Any(e =>
                        e.Type == CombatEffectType.AttackPowerBuff &&
                        e.Amount == 33 && e.DurationSeconds == 4.5), name);
            }
        }

        [TestCase("H-19", "범위 마법·증폭", CombatEffectType.MagicDamageAmplification, 1d)]
        [TestCase("LI-02", "광역 물리 둔화", CombatEffectType.MovementSlowPercent, 50d)]
        public void SecondBatchTwoAttackProcsApplyDamageAndStatusWithoutAnActiveCast(
            string unitId, string name, CombatEffectType type, double amount)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsSecondCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == name && e.Amount > 0), name);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == type && effect.Amount == amount &&
                        effect.SourceId.StartsWith("practice-tooltip:"))), name);
            }
        }

        [Test]
        public void SentinelSecondBatchReusesOneNativeProcAndAddsOnlyMagicAmplification()
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require("RN-04").Skills.Single(x => x.Name == "페인 브링어");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            Assert.IsFalse(SourceModelUnconnectedSkillCatalog.All.Any(row =>
                row.CatalogId == "RN-04"), "Native CCg damage must not roll twice");
            using (var live = Create("RN-04", true, -4900, 5400))
            {
                live.Battle.Advance(5);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.MagicDamageAmplification &&
                        effect.Amount == 18 && effect.SourceId.Contains("RN-04:skill:0"))));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.SourceId == "practice-native:A10I" &&
                        effect.DurationSeconds == 60)),
                    "The primary armor decrease still uses the existing 60-second native path");
            }
        }

        [TestCase("TR-08", "평타 광역 연계", 17.5d)]
        [TestCase("TR-08", "기간트 개틀링", 1.25d)]
        [TestCase("TR-22", "해류 공격", 6.25d)]
        [TestCase("TR-22", "진각", 14.25d)]
        [TestCase("TR-29", "중력장", 4.5d)]
        [TestCase("TR-29", "중력검", 15d)]
        [TestCase("SR-03", "단일 타격", 12.5d)]
        [TestCase("H-19", "라인 체력 비례", 15d)]
        [TestCase("LI-02", "물리 연계", 10d)]
        [TestCase("E-09", "음표 병사 연계", 25d)]
        [TestCase("E-09", "3연타 마법", 4d)]
        [TestCase("E-09", "라인·보스 추가 피해", 10d)]
        public void ThirdBatchTwelveAttackPacketsEnterExistingCombatLedger(
            string unitId, string name, double chance)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.AreEqual(chance, skill.ChancePercent.Value, 1e-8);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill), name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill), name);
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(7);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == name), name + " did not activate");
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == name && e.Amount > 0),
                    name + " must reach the shared enemy HP ledger");
            }
        }

        [Test]
        public void ThirdBatchUtaSoldierContributionExpiresAndNeverBecomesAFreeUnit()
        {
            using (var live = Create("E-09", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                live.Combat.ConfigureSourceModelFusionNativeReceipts(id =>
                    id == Original323FusionNativeFollowupLedger.UtaSongId ? 1 : 0);
                // Inspect the summon immediately after its successful
                // attack: an expired 3-second source soldier is no longer
                // supposed to count as a live combat contributor.
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 200 &&
                    live.Combat.PracticeSongSoldierCount(live.ActorId) == 0; tick++)
                    events.AddRange(live.Battle.Advance(.1));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "3연타 마법"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "음표 병사 연계" && e.Amount > 0));
                Assert.Greater(live.Combat.PracticeSongSoldierCount(live.ActorId), 0);
                Assert.IsFalse(live.Combat.MobileUnitSnapshot().Any(u =>
                    u.Unit.Id == "SOLDIER"), "Unknown W3X summon rawcode must not spawn a fake unit");
                live.Rolls.Trigger = false;
                live.Battle.Advance(8);
                Assert.AreEqual(0, live.Combat.PracticeSongSoldierCount(live.ActorId));
            }
        }

        [Test]
        public void ThirdBatchJinbeAttributeScaleAppliesOnlyWhenARealProviderIsConfigured()
        {
            long unscaled, scaled;
            using (var live = Create("TR-22", true, -4900, 5400))
                unscaled = live.Battle.Advance(5)
                    .Where(e => e.Type == CharacterCombatEventType.SkillDamage &&
                                e.SkillName == "진각")
                    .Sum(e => e.Amount);
            using (var live = Create("TR-22", true, -4900, 5400))
            {
                live.Combat.ConfigureSourceModelHeroStrengthProvider(id => 10d);
                scaled = live.Battle.Advance(5)
                    .Where(e => e.Type == CharacterCombatEventType.SkillDamage &&
                                e.SkillName == "진각")
                    .Sum(e => e.Amount);
            }
            Assert.Greater(scaled, unscaled,
                "Only the supplied hero strength may increase Jinbe's fixed packet");
        }

        [TestCase("L-09", "육왕건")]
        [TestCase("L-09", "육식 Lv4")]
        [TestCase("TR-07", "격투 스턴")]
        [TestCase("R-42", "단일 타격·스턴")]
        [TestCase("L-25", "울두건")]
        [TestCase("L-20", "화이트바인")]
        [TestCase("MY-08", "스타캐치 애로우")]
        [TestCase("I-09", "범위 마법 스턴")]
        [TestCase("I-09", "물리 마법 혼합")]
        [TestCase("D-03", "빙귀")]
        [TestCase("TR-19", "화염성")]
        [TestCase("E-07", "적 중심 마법")]
        [TestCase("E-07", "불주먹")]
        [TestCase("TR-26", "팔지경")]
        [TestCase("TR-26", "레이저")]
        public void FourthBatchFifteenExactAttacksReachExistingDamageLedger(
            string unitId, string name)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsFourthCompositeAttack(skill), name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill), name);
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(7);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == name), name + " must trigger an approved proc");
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == name && e.Amount > 0),
                    name + " must decrease real practice enemy HP");
            }
        }

        [Test]
        public void FourthBatchFireFistWaitsForRealAbsoluteCooldown()
        {
            using (var live = Create("E-07", true, -4900, 5400))
            {
                var first = live.Battle.Advance(3);
                Assert.AreEqual(1, first.Count(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "불주먹"),
                    "The absolute cooldown must block repeated successful rolls");
                var later = live.Battle.Advance(4);
                Assert.IsTrue(later.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "불주먹"),
                    "The same owner must rearm after 4.25 seconds");
            }
        }

        [Test]
        public void FourthBatchKizaruAgilityRequiresAnActualProvider()
        {
            long baseDamage, scaledDamage;
            using (var live = Create("TR-26", true, -4900, 5400))
                baseDamage = live.Battle.Advance(5)
                    .Where(e => e.Type == CharacterCombatEventType.SkillDamage &&
                                e.SkillName == "팔지경")
                    .Sum(e => e.Amount);
            using (var live = Create("TR-26", true, -4900, 5400))
            {
                live.Combat.ConfigureSourceModelHeroAgilityProvider(id => 10d);
                scaledDamage = live.Battle.Advance(5)
                    .Where(e => e.Type == CharacterCombatEventType.SkillDamage &&
                                e.SkillName == "팔지경")
                    .Sum(e => e.Amount);
            }
            Assert.Greater(scaledDamage, baseDamage,
                "Only a real agility source may add Kizaru's documented 3500 per point");
        }

        [TestCase("TR-07", "람각")]
        [TestCase("TR-07", "지건")]
        [TestCase("TR-07", "육왕건")]
        [TestCase("TR-26", "마나 연타 광역기")]
        [TestCase("TR-26", "단일 치명타")]
        [TestCase("D-03", "역재")]
        [TestCase("TR-04", "멀티샷 추가 피해")]
        public void FifthBatchSevenStrictAttackPacketsReachCommonDamageLedger(
            string unitId, string name)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsFifthCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            using (var live = Create(unitId, true, -4900, 5400))
            {
                if (unitId == "TR-26" && name == "마나 연타 광역기")
                {
                    var map = OriginalMapBaseStatCatalog.FromJson(
                        Data("original-2323C-map-base-stats.json"),
                        CharacterUnitCatalog.FromJson(Data("units.json")));
                    live.Combat.ConfigureSourceModelUnitResources(
                        SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                            Data("original-2323C-unit-object-stats.json"), map));
                    Assert.AreEqual(100, live.Combat.ResourcesOf(live.ActorId).MaximumMana);
                }
                var events = live.Battle.Advance(7);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == name), name);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == name && x.Amount > 0), name);
            }
        }

        [Test]
        public void FifthBatchAceUltimateDelaysDamageAndSpendsExactly185Mana()
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require("E-07").Skills.Single(x => x.Name == "대염계 염제");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            using (var live = Create("E-07", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, skill.Id, target.Id));
                Assert.AreEqual(0, live.Combat.ResourcesOf(live.ActorId).Mana);
                Assert.IsFalse(live.Combat.LastEvents.Any(x =>
                    x.SkillName == skill.Name && x.Type == CharacterCombatEventType.SkillDamage));
                var early = live.Battle.Advance(1);
                Assert.IsFalse(early.Any(x => x.SkillName == skill.Name &&
                    x.Type == CharacterCombatEventType.SkillDamage));
                var landed = live.Battle.Advance(1.1);
                Assert.IsTrue(landed.Any(x => x.SkillName == skill.Name &&
                    x.Type == CharacterCombatEventType.SkillDamage && x.Amount > 0));
            }
        }

        [Test]
        public void FifthBatchLucciClearScalingDoesNotInventPersistentWins()
        {
            var profile = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require("TR-07");
            Assert.AreEqual(5.25d, profile.Skills.Single(x => x.Name == "육왕건")
                .ChancePercent.Value);
            using (var live = Create("TR-07", false))
            {
                live.Combat.ConfigureSourceModelAccumulatedClearCount(() => 1000);
                // No persistent career history is assumed by the game loop;
                // capped progression can be supplied by an actual save later.
                Assert.IsNotNull(live.Combat);
            }
        }

        [TestCase("TR-09", "평타 광역 추가")]
        [TestCase("TR-09", "크리티컬")]
        [TestCase("TR-09", "단일 스턴 타격")]
        [TestCase("TR-10", "범위 마법타격")]
        [TestCase("TR-11", "불꽃 주먹")]
        [TestCase("TR-11", "넓은 주먹")]
        [TestCase("TR-12", "검은다리")]
        [TestCase("TR-12", "무통 샷")]
        [TestCase("TR-13", "원형 마법 스턴")]
        [TestCase("TR-13", "자신중심 마법 스턴")]
        [TestCase("TR-14", "무장색 패기")]
        [TestCase("TR-14", "컬버린")]
        [TestCase("TR-15", "고대병기 포세이돈")]
        [TestCase("TR-19", "저격")]
        [TestCase("TR-19", "치명타")]
        [TestCase("TR-24", "기본 공격 특수 피해")]
        [TestCase("E-12", "혼합 공격")]
        [TestCase("E-12", "혼합 스턴 타격")]
        [TestCase("I-03", "기본 공격 광역 추가")]
        public void SixthBatchNineteenAttackPacketsHaveExactOwnershipAndCompleteFields(
            string unitId, string name)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsSixthCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
        }

        [Test]
        public void SixthBatchHawkinsHealthActiveIsGatedByOriginalResources()
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require("TR-09").Skills.Single(x => x.Name == "체력 소모 연타");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsSixthCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            Assert.AreEqual(40d, skill.Number("healthCost").Value);
        }

        [TestCase("TR-11", "H092", "화염용왕", false)]
        [TestCase("TR-16", "H097", "아이스 에이지", false)]
        [TestCase("TR-20", "H09F", "1080 번뇌봉", true)]
        [TestCase("TR-24", "H0CO", "콤비네이션 어택", false)]
        public void SeventhBatchFourFullPacketsRequireExactOwnerAndCompositeFields(
            string unitId, string rawcode, string name, bool active)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsSeventhCompositeSkill(skill));
            Assert.IsTrue(active ? SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill) :
                SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet(rawcode, out row));
            Assert.AreEqual(unitId, row.CatalogId);
            CollectionAssert.Contains(active ? row.PracticeActiveSkills : row.PracticeAttackSkills,
                name);
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-18", row.PracticeActiveSkills, skill),
                "Another character must not be able to cast this packet");
        }

        [TestCase("TR-20", "사자의 노래 Lv.2")]
        [TestCase("TR-20", "삼도류 - 극 호랑이 사냥")]
        [TestCase("TR-18", "마환인")]
        [TestCase("TR-18", "미환인")]
        [TestCase("TR-18", "무시빙아")]
        [TestCase("D-01", "13타 고정피해")]
        [TestCase("H-20", "눈보라")]
        [TestCase("H-14", "흉포한 검술")]
        public void EighthBatchEightCompositeAttacksNeedExactCompleteTooltipPacket(
            string unitId, string name)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill),
                "No unknown component may be silently activated for " + unitId + "/" + name);
        }

        [TestCase("L-39", "백발백중")]
        [TestCase("L-28", "해류 한판 업어치기")]
        [TestCase("I-01", "기본 공격 범위 추가")]
        [TestCase("I-06", "기본 공격 추가")]
        [TestCase("MY-02", "오니")]
        [TestCase("MY-02", "할퀴기")]
        [TestCase("MY-02", "휩쓸기")]
        [TestCase("LI-06", "확정 단일 스턴")]
        [TestCase("SR-01", "피스톨 키스")]
        [TestCase("SR-01", "피스톨 키스 6회 발동 보너스")]
        public void NinthBatchTenPacketsHaveExplicitOwnerAndCompleteFields(
            string unitId, string name)
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require(unitId).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelNinthSkillCatalog.Approved(skill),
                "The ninth batch cannot activate an incomplete or changed source packet: " +
                unitId + "/" + name);
        }

        [TestCase("L-39", "백발백중")]
        [TestCase("L-28", "해류 한판 업어치기")]
        [TestCase("I-01", "기본 공격 범위 추가")]
        [TestCase("I-06", "기본 공격 추가")]
        [TestCase("MY-02", "휩쓸기")]
        [TestCase("LI-06", "확정 단일 스턴")]
        [TestCase("SR-01", "피스톨 키스")]
        public void NinthBatchRunsThroughRealPracticeAttackPipeline(
            string unitId, string expectedSkill)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(15);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack),
                    unitId + " must commit a basic attack");
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == expectedSkill),
                    unitId + " must execute the ninth-batch skill in combat");
            }
        }

        [Test]
        public void NinthBatchRejectsFrankyGeneralSkillWithoutConfirmedForm()
        {
            var skill = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"))
                .Require("TR-28").Skills.Single(x => x.Name == "제너럴 소드");
            Assert.IsFalse(SourceModelNinthSkillCatalog.Approved(skill));
        }

        [Test]
        public void SeventhBatchKobyCloneHasFiveSecondCombatLifeButNoFusionRosterEntry()
        {
            using (var live = Create("TR-24", true, -4900, 5400))
            {
                int before = live.Combat.CharacterCount;
                bool observed = false;
                for (int step = 0; step < 100; step++)
                {
                    live.Battle.Advance(.2);
                    if (!live.Combat.PracticeSummonsOf(live.ActorId).Any(x =>
                        x.SkillName == "콤비네이션 어택")) continue;
                    observed = true;
                    break;
                }
                Assert.IsTrue(observed, "The five-percent clone proc must use a live summon");
                Assert.AreEqual(before, live.Combat.CharacterCount,
                    "The clone must not enter the fusion-material character roster");
                Assert.IsTrue(live.Combat.PracticeSummonsOf(live.ActorId).Any(x =>
                    x.Name == "코비 분신" && x.HasIndependentCombat));
                live.Rolls.Trigger = false;
                live.Battle.Advance(6);
                Assert.IsFalse(live.Combat.PracticeSummonsOf(live.ActorId).Any(x =>
                    x.SkillName == "콤비네이션 어택"),
                    "The clone cannot remain after its documented five-second lifetime");
            }
        }

        [Test]
        public void ZephyrMissingManaSkillUsesOriginal160ManaAndExistingHpDamageLedger()
        {
            using (var live = Create("I-10", false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var resource = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(resource);
                Assert.AreEqual(160d, resource.MaximumMana);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives());
                Assert.AreEqual(0d, resource.Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "No duplicate debit or free recast from the same mana cap");
                var settled = live.Battle.Advance(.1);
                Assert.IsTrue(settled.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "마나 광역 공격"),
                    "The missing-health bonus and area packet must enter the shared HP ledger");
                Assert.IsFalse(settled.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    (x.SkillName == "직선 마법 공격" || x.SkillName == "차지 버스트" ||
                     x.SkillName == "매 타격 스턴")),
                    "Unsupported mixed/native packets must not be double-executed");
            }
        }

        [Test]
        public void RedfieldMobileEnemyCenteredAreaStillHitsWhenCasterIsFarAway()
        {
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var caster = skills.Find("LI-01");
            var packet = caster.Skills.Single(x => x.Name == "핏빛 범위 공격");
            Assert.AreEqual("casterArea", packet.Target);
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(packet));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet("h05G", out row));
            Assert.AreEqual("LI-01", row.CatalogId);
            Assert.IsTrue(SpecialGradeMobilePolicy.UsesEnemyCenteredArea("LI-01"));
            Assert.IsTrue(SpecialGradeMobilePolicy.UsesEnemyCenteredArea("LI-02"));
            CollectionAssert.AreEquivalent(
                new[] { "핏빛 범위 공격", "광역 마법 공격",
                    "단일 스턴", "평타 단일 고정" }, row.PracticeAttackSkills);

            using (var near = Create("LI-01", true, -4900, 5400))
            {
                var events = near.Battle.Advance(6);
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "핏빛 범위 공격"),
                    "Mobile target-centered 750-radius model reaches enemy HP nearby");
            }
            using (var far = Create("LI-01", true, 50000, 50000))
            {
                var events = far.Battle.Advance(6);
                Assert.IsTrue(events.Any(x =>
                    x.SkillName == "핏빛 범위 공격" &&
                    x.Type == CharacterCombatEventType.SkillDamage),
                    "Unlimited reach must retain Redfield's target-centered packet even when far away");
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "광역 마법 공격"),
                    "The preexisting target-centered 5% packet still executes");
            }
        }

        [Test]
        public void KobySourceManaRegenWithoutMaximumCannotAbortEntireCharacterResourceIndex()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var index = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            var koby = index.Find("TR-24");
            Assert.IsNotNull(koby);
            Assert.AreEqual("H0CO", koby.OriginalRawcode);
            Assert.AreEqual(150, koby.MaximumHealth);
            Assert.AreEqual(0, koby.MaximumMana,
                "Unspecified source mana capacity is not a free guessed MP pool.");
            Assert.AreEqual(0, koby.ManaRegeneration);
            Assert.AreEqual(0.5, koby.DeferredSourceManaRegeneration, 1e-9);
            Assert.AreEqual(0, koby.CreateStartingPool().Mana);
        }

        [Test]
        public void AnyaSlowIsExplicitlyApprovedButOtherNonAllowlistedEffectsRemainDeferred()
        {
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var profile = skills.Find("MY-08");
            var unverified = profile.Skills.Single(x => x.Name == "로이드 포저");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(unverified));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet("h08M", out row));
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.AllowsActive(row, "로이드 포저"));
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.AllowsActive(row, "요르 포저"));
            Assert.IsFalse(SourceModelUnconnectedSkillCatalog.AllowsActive(row, "독심술"));
            CollectionAssert.AreEqual(new[] { "스타캐치 애로우" },
                row.PracticeAttackSkills,
                "Only the explicitly approved source attack is allowed; " +
                "unrelated active effects cannot create extra procs.");
        }

        [Test]
        public void SourceResourceLifecycleHandlesLateSpawnAndExplicitOverride()
        {
            using (var live = Create("C-01", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.IsNull(live.Combat.ResourcesOf(live.ActorId),
                    "Unapproved unit must not obtain artificial spell resources.");
                long late = live.Combat.SpawnCharacter("E-04", -4900, 5400);
                Assert.AreEqual(175, live.Combat.ResourcesOf(late).Mana);
                var external = new CharacterResources(500, 490, 500, 450);
                live.Combat.ConfigureCharacterResources(late, external);
                live.Battle.Advance(2);
                Assert.AreSame(external, live.Combat.ResourcesOf(late));
                Assert.AreEqual(490, external.Health);
                Assert.AreEqual(450, external.Mana,
                    "Externally provided resources must not receive model regen.");
                live.Combat.RemoveCharacter(late);
                Assert.Throws<KeyNotFoundException>(() => live.Combat.ResourcesOf(late));
            }
        }

        [Test]
        public void OriginalNativeFormsAndSatelliteCatalogUsePinnedJass()
        {
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                SourceModelNativeCharacterRules.JassSha256);
            Assert.AreEqual(150, SourceModelNativeCharacterRules.ChopperTransformMana);
            Assert.AreEqual(200, SourceModelNativeCharacterRules.VegapunkMainMana);
            Assert.AreEqual(1.23, SourceModelNativeCharacterRules.VegapunkMainDelaySeconds);
            Assert.AreEqual(14, SourceModelNativeCharacterRules.KaidoTransformRollPercent);
            string mapped;
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("H091", out mapped));
            Assert.AreEqual("TR-23", mapped);
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("H0AA", out mapped));
            Assert.AreEqual("TR-30", mapped);
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("h07M", out mapped));
            Assert.AreEqual("I-11", mapped);
            Assert.IsFalse(SourceModelNativeCharacterRules.TryGetNativeAttack("h01G", out mapped),
                "Docking5 activation is unverified and must not be auto-invented.");
        }

        [TestCase("H0AB", 0, 2, 7)]
        [TestCase("H0AE", 1, 4, 14)]
        [TestCase("H0AM", 0, 4, 0)]
        [TestCase("H0AM", 2, 4, 0)]
        public void VegapunkSatelliteCostsFollowB4aStage(
            string rawcode, int previous, long expectedTraits, long expectedWood)
        {
            long traits, wood;
            Assert.IsTrue(SourceModelNativeCharacterRules.TryPlanSatelliteCost(
                rawcode, previous, out traits, out wood));
            Assert.AreEqual(expectedTraits, traits);
            Assert.AreEqual(expectedWood, wood);
        }

        [Test]
        public void OriginalNativeFormsUseMapObjectAttackAndKeepSplashModelOnly()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var catalog = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            Assert.AreEqual(150, catalog.Find("TR-23").MaximumMana);
            Assert.AreEqual(200, catalog.Find("TR-30").MaximumMana);
            Assert.AreEqual(100, catalog.Find("I-11").MaximumHealth);
            Assert.AreEqual(285001, map.FindMapObjectAttack("H0B6").MinimumBaseDamage.Value);
            Assert.AreEqual(1.2, map.FindMapObjectAttack("H0B6").AttackCooldownSeconds.Value);
            Assert.AreEqual(250001, map.FindMapObjectAttack("h0AD").MinimumBaseDamage.Value);
            using (var chopper = Create("TR-23", true, -4900, 5400))
            {
                chopper.Combat.ConfigureSourceModelUnitResources(catalog);
                var events = chopper.Battle.Advance(2);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "쵸파 럼블볼 폭주"));
                Assert.IsNotNull(chopper.Combat.PracticeFormOf(chopper.ActorId));
                Assert.IsTrue(chopper.Combat.PracticeFormOf(chopper.ActorId)
                    .Contains("H0B6"));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == chopper.ActorId.ToString() && e.Amount > 0));
            }
            using (var vegapunk = Create("TR-30", true, -4900, 5400))
            {
                vegapunk.Combat.ConfigureSourceModelUnitResources(catalog);
                var events = vegapunk.Battle.Advance(4);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "베가펑크 마나 공격"));
                Assert.Less(vegapunk.Combat.ResourcesOf(vegapunk.ActorId).Mana, 200);
            }
            using (var kaido = Create("I-11", true, -4900, 5400))
            {
                var events = kaido.Battle.Advance(6);
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "카이도 변신" && e.Detail.Contains("h0AD")));
                Assert.IsNull(kaido.Combat.PracticeFormOf(kaido.ActorId),
                    "Original h07M lacks A0PS: CBL must not roll 14% auto metamorphosis.");
                Assert.IsTrue(events.Any(e => e.SkillName == "카이도 100 체력 공격" &&
                    e.Type == CharacterCombatEventType.SkillDamage && e.Amount > 0));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == kaido.ActorId.ToString() && e.Amount > 0));
            }
        }

        [Test]
        public void TransformedChopperRawObjectAurasAreTimedByFormAndNotExtraBaseDamage()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var resources = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            using (var live = Create("TR-23", true, -4900, 5400,
                withNativeAuras: true))
            {
                live.Combat.ConfigureSourceModelUnitResources(resources);
                long farAlly = live.Combat.SpawnCharacter("C-01", 4900, -5400);
                live.Battle.Advance(4);
                Assert.IsNotNull(live.Combat.PracticeNativeFormOf(live.ActorId));
                var effects = live.Combat.UnitStatusOf(live.ActorId).Snapshot();
                Assert.AreEqual(1, effects.Count(e =>
                    e.Type == CombatEffectType.AttackPowerBuff &&
                    e.SourceId.StartsWith("practice-aura:") && e.Amount == 40));
                Assert.AreEqual(1, effects.Count(e =>
                    e.Type == CombatEffectType.AttackSpeedBuff &&
                    e.SourceId.StartsWith("practice-aura:") && e.Amount == 40));
                Assert.IsTrue(live.Combat.UnitStatusOf(farAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff && e.Amount == 40),
                    "Mobile native auras cover the same lane regardless of W3X radius.");
                live.Combat.RemoveCharacter(live.ActorId);
            }
        }

        [Test]
        public void ChopperTooltipBurstEndsAfterFourSecondsWithoutInventingManaPayment()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var resources = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            using (var live = Create("TR-23", true, -4900, 5400,
                withNativeAuras: true))
            {
                live.Combat.ConfigureSourceModelUnitResources(resources);
                long ally = live.Combat.SpawnCharacter("C-01", 4900, -5400);
                live.Battle.Advance(2);
                Assert.IsNotNull(live.Combat.PracticeNativeFormOf(live.ActorId));
                Assert.AreEqual(150, live.Combat.ResourcesOf(live.ActorId).Mana,
                    "Byb does not prove a native mana payment; the model must not fabricate one.");
                live.Battle.Advance(6);
                Assert.IsNull(live.Combat.PracticeNativeFormOf(live.ActorId));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.IsTrue(live.Combat.UnitStatusOf(ally).Snapshot().Any(e =>
                    e.SourceId.StartsWith("practice-aura:") &&
                    e.Type == CombatEffectType.AttackPowerBuff && e.Amount == 40),
                    "Expiration removes the form, not Chopper's native base-object aura.");
                // Full mana cannot immediately make the 4-second tooltip
                // form permanent. A source-managed drop is needed to rearm.
                live.Battle.Advance(3);
                Assert.IsNull(live.Combat.PracticeNativeFormOf(live.ActorId));
            }
        }

        [Test]
        public void ChopperBurstAndTemporaryCloneStopAtCommittedLaneChange()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var resources = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            using (var live = Create("TR-23", true, -4900, 5400))
            {
                live.Combat.ConfigureSourceModelUnitResources(resources);
                live.Battle.Advance(2);
                Assert.IsNotNull(live.Combat.PracticeNativeFormOf(live.ActorId));
                var lanes = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lanes);
                lanes.DeployedIds.Add(live.ActorId);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                Assert.IsNull(live.Combat.PracticeNativeFormOf(live.ActorId));
            }
            using (var live = Create("L-21", true, -4900, 5400))
            {
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeSummonsOf(live.ActorId).Count == 0; tick++)
                    live.Battle.Advance(.2);
                Assert.Greater(live.Combat.PracticeSummonsOf(live.ActorId).Count, 0);
                var lanes = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lanes);
                lanes.DeployedIds.Add(live.ActorId);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }

        [Test]
        public void PinnedMapObjectAurasUseOriginalObjectIdentityAndLevel()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var objects = Original323MapObjectAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            var lilith = objects.FindAll("H0AB");
            Assert.AreEqual(1, lilith.Count);
            Assert.AreEqual("A034", lilith[0].AbilityRawcode);
            Assert.AreEqual("B088", lilith[0].BuffRawcode);
            Assert.AreEqual(CombatEffectType.ArmorReduction, lilith[0].Type);
            Assert.AreEqual(40, lilith[0].Amount);
            Assert.AreEqual(1, lilith[0].Level);
            var form = objects.FindAll("H0B6");
            CollectionAssert.AreEquivalent(new[] { "A0OK", "A0OJ", "A0OI", "A0OL" },
                form.Select(a => a.AbilityRawcode).ToArray());
            Assert.IsEmpty(objects.FindAll("not-a-map-object"));
        }

        [Test]
        public void LilithArmorAuraSurvivesCreatorRemovalAtUnlimitedSameLaneReach()
        {
            using (var live = Create("TR-30", false, -4000, 4000,
                withNativeAuras: true))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                live.Battle.Advance(.5);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:") && effect.Amount == 40)),
                    "Satellite object aura is independent of the creator and of its native 800 radius.");
            }
        }

        [Test]
        public void DifferentCharacterAndSatelliteArmorAurasDeferInsteadOfSilentlyStacking()
        {
            using (var live = Create("TR-23", false, -4000, 4000,
                withNativeAuras: true))
            {
                long vegapunk = live.Combat.SpawnCharacter("TR-30", -4000, 4000);
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    vegapunk, "H0AB", wallet));
                live.Battle.Advance(.5);
                Assert.Greater(live.Combat.PracticeUnitEffects.DeferredAuraGroups, 0);
                Assert.IsFalse(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:"))));
                live.Combat.RemoveCharacter(live.ActorId);
                live.Battle.Advance(.1);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:") && effect.Amount == 40)));
            }
        }

        [Test]
        public void NativeObjectAuraDoesNotLeakAcrossStoryAndMainLanes()
        {
            using (var live = Create("TR-23", false, -4900, 5400,
                withNativeAuras: true))
            {
                long storyAlly = live.Combat.SpawnCharacter("C-01", 4500, -4000);
                long mainAlly = live.Combat.SpawnCharacter("C-02", 4500, -4000);
                var lanes = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lanes);
                lanes.DeployedIds.Add(live.ActorId);
                lanes.DeployedIds.Add(storyAlly);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                live.Battle.Advance(.5);
                Assert.IsTrue(live.Combat.UnitStatusOf(storyAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff && e.Amount == 40));
                Assert.IsFalse(live.Combat.UnitStatusOf(mainAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff));
            }
        }

        [Test]
        public void VegapunkSatellitePurchaseIsAtomicUniqueAndNotACombatRosterUnit()
        {
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                Assert.IsFalse(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 10).Add("wood", 21));
                int before = live.Combat.CharacterCount;
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                Assert.AreEqual(8, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(14, wallet.Balance("wood"));
                Assert.IsFalse(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AE", wallet));
                Assert.AreEqual(4, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AM", wallet));
                Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(3, live.Combat.PracticeSatellites().Count);
                Assert.AreEqual(before, live.Combat.CharacterCount);
                Assert.IsFalse(live.Combat.PracticeSatellites()[0].IndependentCombatVerified);
                var sources = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                Assert.AreEqual(27001, sources.FindMapObjectAttack("H0AB").MinimumBaseDamage.Value);
                var autonomous = live.Battle.Advance(2.5);
                Assert.IsTrue(autonomous.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId.StartsWith("practice-satellite:") && e.Amount > 0),
                    "Purchased satellites must commit real model HP damage.");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(3, live.Combat.PracticeSatellites().Count,
                    "Original B4a satellites survive removal of their creator.");
                autonomous = live.Battle.Advance(2);
                Assert.IsTrue(autonomous.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId.StartsWith("practice-satellite:") && e.Amount > 0),
                    "Satellite attacks cannot silently depend on creator existence.");
            }
        }

        [TestCase("H0AB", 90d, "릴리스 마나 공격", 2200000d)]
        [TestCase("H0AE", 110d, "아틀라스 마나 공격", 2000000d)]
        public void PurchasedSatelliteUsesPinnedManaAndExecutesOneIndependentActive(
            string rawcode, double maximum, string skill, double amount)
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            Assert.AreEqual(maximum, map.FindMapObjectResources(rawcode).MaximumMana);
            Assert.AreEqual(0, map.FindMapObjectResources(rawcode).ManaRegeneration);
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, rawcode, wallet));
                var satellite = live.Combat.PracticeSatellites().Single();
                Assert.AreEqual(maximum,
                    live.Combat.PracticeSatelliteMaximumManaOf(satellite.Id).Value);
                Assert.AreEqual(maximum,
                    live.Combat.PracticeSatelliteManaOf(satellite.Id).Value);
                var actions = live.Battle.Advance(3);
                Assert.AreEqual(1, actions.Count(e =>
                    e.Type == CharacterCombatEventType.SkillActivated && e.SkillName == skill),
                    "An SLK pool with no mana regeneration funds exactly one active.");
                Assert.IsTrue(actions.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == skill && e.Amount > 0));
                Assert.AreEqual(0, live.Combat.PracticeSatelliteManaOf(satellite.Id).Value);
                actions = live.Battle.Advance(3);
                Assert.IsFalse(actions.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated && e.SkillName == skill));
                Assert.AreEqual(0, live.Combat.PracticeSatelliteManaOf(satellite.Id).Value);
            }
        }

        [TestCase("L-26", "무장색의 패기", false)]
        [TestCase("L-28", "단일 타격 스턴", false)]
        [TestCase("L-31", "육식 - 체", false)]
        [TestCase("TR-17", "범위 스턴 타격", false)]
        [TestCase("I-10", "매 타격 스턴", false)]
        [TestCase("T-05", "고속전격", false)]
        [TestCase("MY-11", "마왕", false)]
        [TestCase("R-17", "단일 물리 스턴", false)]
        [TestCase("X-04", "조선공의 망치", false)]
        [TestCase("TR-03", "범위 물리 타격", false)]
        [TestCase("TR-27", "육식 체", false)]
        [TestCase("T-01", "오버히트", false)]
        [TestCase("LI-05", "2연타 마법", false)]
        [TestCase("L-18", "사람사람열매", false)]
        [TestCase("H-21", "돌격 연타", false)]
        [TestCase("RN-14", "고양이 발톱", false)]
        [TestCase("I-12", "체력 소모 범위 공격", true)]
        [TestCase("H-14", "혈우", true)]
        [TestCase("H-20", "잔설검", true)]
        [TestCase("I-08", "전체체력 비례 고정피해", true)]
        public void FifteenthTwentySourceSkillsTriggerThroughLivePracticeCombat(
            string unitId, string skill, bool resourceActive)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                if (resourceActive)
                {
                    var map = OriginalMapBaseStatCatalog.FromJson(
                        Data("original-2323C-map-base-stats.json"),
                        CharacterUnitCatalog.FromJson(Data("units.json")));
                    live.Combat.ConfigureSourceModelUnitResources(
                        SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                            Data("original-2323C-unit-object-stats.json"), map));
                }
                var earlyEvents = live.Battle.Advance(2);
                if (resourceActive)
                    Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                        unitId + " approved source active must debit its real pool");
                var events = earlyEvents.Concat(live.Battle.Advance(4)).ToList();
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == skill), unitId + " skill must actually activate");
                Assert.IsTrue(events.Any(e => e.SkillName == skill &&
                    (e.Type == CharacterCombatEventType.SkillDamage && e.Amount > 0 ||
                     e.Type == CharacterCombatEventType.StatusApplied)),
                    unitId + " must affect real enemy HP/status, not just log a name");
            }
        }

        [Test]
        public void YorkHasSourceManaButNoInventedManaSkill()
        {
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add(
                    OriginalMissionWalletRewards.TraitPointsId, 4));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AM", wallet));
                long id = live.Combat.PracticeSatellites().Single().Id;
                Assert.AreEqual(100, live.Combat.PracticeSatelliteManaOf(id).Value);
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == "practice-satellite:" + id));
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.ActorId == "practice-satellite:" + id));
                Assert.AreEqual(100, live.Combat.PracticeSatelliteManaOf(id).Value);
            }
        }

        [Test]
        public void VegapunkMainCastRegeneratesOnlyPurchasedSatellitesForThreeSeconds()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var resources = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                live.Combat.ConfigureSourceModelUnitResources(resources);
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add(
                    OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                long id = live.Combat.PracticeSatellites().Single().Id;
                var events = live.Battle.Advance(8);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "베가펑크 마나 공격"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "릴리스 마나 공격"));
                double regenerated = live.Combat.PracticeSatelliteManaOf(id).Value;
                Assert.Greater(regenerated, 0,
                    "Only a witnessed Vegapunk main cast may regenerate satellite mana.");
                Assert.LessOrEqual(regenerated, 75,
                    "25 mana/s for three seconds, capped by the pinned 90 MP pool.");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(regenerated, live.Combat.PracticeSatelliteManaOf(id).Value, .001,
                    "Owned satellites and their mana must survive creator removal.");
                live.Battle.Advance(2);
                Assert.AreEqual(regenerated, live.Combat.PracticeSatelliteManaOf(id).Value, .001);
            }
        }

        [Test]
        public void StoryRewardsNeverMintTraitPointsAndEarnedPointsUnlockVegapunkPurchase()
        {
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add("wood", 7));
                var port = new Original323SourceDedicatedSkillPort(live.Combat, wallet);
                Assert.IsFalse(port.AvailableSkills().First(x =>
                    x.Id == SourceModelNativeCharacterRules.SatelliteActionId("H0AB"))
                    .CanExecute);
                var queue = new Original323StoryNativeRewardQueue();
                var story = new Original323StoryMobileProgression(wallet);
                queue.RecordCommittedClear(8);
                queue.RecordCommittedClear(9);
                Assert.AreEqual(4, queue.ApplyPending(story)); // 2 research, 2 BdL
                Assert.AreEqual(0, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId),
                    "Story gambling charges must not become trait points.");
                Assert.AreEqual(2, wallet.Balance(
                    Original323OtherworldGamblingRules.ChargesResource));
                Assert.IsFalse(port.AvailableSkills().First(x =>
                    x.Id == SourceModelNativeCharacterRules.SatelliteActionId("H0AB"))
                    .CanExecute);
                // Real trait points (such as mission earnings) use the same
                // shared wallet, not a fabricated story currency conversion.
                wallet.Grant(new ResourceBundle().Add(
                    OriginalMissionWalletRewards.TraitPointsId, 2));
                Assert.IsTrue(port.AvailableSkills().First(x =>
                    x.Id == SourceModelNativeCharacterRules.SatelliteActionId("H0AB"))
                    .CanExecute);
                Assert.AreEqual(ManualActionState.Succeeded,
                    port.Execute(SourceModelNativeCharacterRules.SatelliteActionId("H0AB")).State);
                Assert.AreEqual(0, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.AreEqual(1, live.Combat.PracticeSatellites().Count);
                Assert.AreEqual(2, wallet.Balance(
                    Original323OtherworldGamblingRules.ChargesResource),
                    "Satellite purchase must not spend story gambling charges.");
                Assert.AreEqual(0, queue.ApplyPending(story));
                Assert.AreEqual(0, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId));
            }
        }

        [Test]
        public void KaidoHundredLifeAttackUsesPinnedElevenSourceAreaPackets()
        {
            using (var live = Create("I-11", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.SkillName == "카이도 100 체력 공격"));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "카이도 100 체력 공격" && e.Amount > 0));
                Assert.Greater(live.Combat.PracticeKaidoAttackCounterOf(live.ActorId), 0);
            }
        }

        [Test]
        public void OriginalSpecialSourcesMatchThePinnedJassAndCatalogRawcodes()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                SourceModelOriginalSpecialCallbackRules.JassSha256);
            foreach (var id in new[] { "L-19", "R-03", "R-07", "L-27", "L-32" })
            {
                var unit = map.Find(id);
                Assert.IsNotNull(unit);
                string mapped;
                Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.TryGetUnit(
                    unit.OriginalRawcode, out mapped), id);
                Assert.AreEqual(id, mapped);
            }
            Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.SugarToyBranch(8));
            Assert.IsFalse(SourceModelOriginalSpecialCallbackRules.SugarToyBranch(9));
            Assert.IsFalse(SourceModelOriginalSpecialCallbackRules.ZoroSourceExecuteCondition(
                false, 10, 100));
            Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.ZoroSourceExecuteCondition(
                true, 15, 100));
            Assert.AreEqual(7.9,
                SourceModelOriginalSpecialCallbackRules.RyumaHealthBonus(0, 100),
                0.0001);
        }

        [TestCase("L-19", "장난감 No.31")]
        [TestCase("R-03", "검은수염 어둠")]
        [TestCase("R-07", "흑도 슈스이")]
        [TestCase("L-27", "사자의노래")]
        [TestCase("L-32", "우르스스 쇼크")]
        public void SourceJassSpecialAttackedCallbackReachesLivePractice(
            string id, string skill)
        {
            using (var live = Create(id, true, -4900, 5400))
            {
                if (id == "L-32")
                {
                    var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                    var map = OriginalMapBaseStatCatalog.FromJson(
                        Data("original-2323C-map-base-stats.json"), units);
                    live.Combat.ConfigureSourceModelUnitResources(
                        SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                            Data("original-2323C-unit-object-stats.json"), map));
                }
                var all = live.Battle.Advance(6);
                Assert.IsTrue(all.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == skill), id + " " + skill);
            }
        }

        [Test]
        public void SugarToyUsesSourceSpawnLifetimeNotFusionRoster()
        {
            using (var live = Create("L-19", true, -4900, 5400))
            {
                int before = live.Combat.Roster.Count;
                var attacks = live.Battle.Advance(3);
                var toys = live.Combat.PracticeSummonsOf(live.ActorId);
                Assert.IsTrue(toys.Any(x => x.SourceUnitRawcode == "h06N" &&
                    x.ExpiresAt > live.Combat.Time && x.HasIndependentCombat &&
                    x.SpawnX == -4900 && x.SpawnY == 5400));
                Assert.IsTrue(attacks.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:") && x.Amount > 0));
                Assert.AreEqual(before, live.Combat.Roster.Count,
                    "A temporary toy is never a fusion material.");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }

        [Test]
        public void SugarSuicideUsesSingleElseIfBranchAndAppliesTooltipAreaPacket()
        {
            using (var live = Create("L-19", true, -4900, 5400))
            {
                live.Rolls.ForceSugarSuicide = true;
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "하비하비열매 자폭" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains("타이밍 미검증")));
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "장난감 No.31"));
                Assert.IsTrue(live.Combat.PracticeSummonsOf(live.ActorId).Any(x =>
                    x.SourceUnitRawcode == "h06P" &&
                    x.SpawnX == -4900 && x.SpawnY == 5400));
            }
        }

        [Test]
        public void LilithSatelliteProcsSurviveCreatorRemovalWithoutFusionOwnership()
        {
            using (var live = Create("TR-30", true, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2)
                    .Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "릴리스 단일 스턴"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "릴리스 광역 마법" && e.Amount > 0));
            }
        }

        [Test]
        public void AtlasSelfHasteUsesOwnIndependentStatusNotCreatorStatus()
        {
            using (var live = Create("TR-30", true, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2)
                    .Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AE", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "아틀라스 날뛰기"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "아틀라스 평타·치명타" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "아틀라스 날뛰기" && e.Amount > 0));
            }
        }

        [Test]
        public void KaidoTenthAttackModelsOnlyPinnedFlatPacketAndDefersScaling()
        {
            using (var live = Create("I-11", false, -4900, 5400))
            {
                var events = live.Battle.Advance(30);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "인간형 열 번째 공격" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains("평타 비례 추가피해")));
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "카이도 변신"));
            }
        }

        [Test]
        public void KaidoTwoOrderedSourceRollsAndUserDataIncrementsAreIndependent()
        {
            var audit = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-source-character-callback-evidence.json")), "source");
            var original = OrdrJson.Object(OrdrJson.Value(audit, "kaido"), "kaido");
            Assert.AreEqual(false, OrdrJson.Value(original, "humanObjectHasA0PS"));
            Assert.AreEqual(true, OrdrJson.Value(original, "dragonObjectHasA0PS"));
            Assert.IsTrue(SourceModelNativeCharacterRules.KaidoCanRollAutoMorph("h0AD"));
            Assert.IsFalse(SourceModelNativeCharacterRules.KaidoCanRollAutoMorph("h07M"));
            Assert.AreEqual(2, SourceModelNativeCharacterRules.KaidoUserDataIncrease(8));
            Assert.AreEqual(2, SourceModelNativeCharacterRules.KaidoUserDataIncrease(15));
            Assert.AreEqual(1, SourceModelNativeCharacterRules.KaidoUserDataIncrease(0));
            using (var firstBranch = Create("I-11", true, -4900, 5400))
            {
                var all = firstBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 타격"));
                Assert.IsFalse(all.Any(x => x.SkillName == "카이도 변신"));
            }
            using (var normalBranch = Create("I-11", false, -4900, 5400))
            {
                var all = normalBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 기본 콜백"));
            }
            using (var secondBranch = Create("I-11", false, -4900, 5400))
            {
                secondBranch.Rolls.ForceKaidoSecondBranch = true;
                var all = secondBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 두 번째 타격"));
            }
        }

        [Test]
        public void KaidoModelEighthPercentHitAndArmorStacksUseSharedEnemyStatus()
        {
            using (var live = Create("I-11", true, -4900, 5400))
            {
                // h07M cannot morph; suppressing ALL 100-sided rolls also
                // disables Kaido's separate source 8% hit.
                var events = live.Battle.Advance(4);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "인간형 타격" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "약육강식"));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-kaido:armor:") &&
                        effect.Amount >= 70)));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsFalse(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.SourceId.StartsWith("practice-kaido:armor:"))));
            }
        }

        [Test]
        public void ViviCommittedUpgradeSurvivesPostCommitWalletObserverException()
        {
            using (var live = Create("E-06", true, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add("wood", 2)
                    .Add(WispResourceIds.Random, 3));
                wallet.Granted += amount =>
                    throw new InvalidOperationException("POST_COMMIT_OBSERVER");
                bool won; int level;
                Assert.IsTrue(live.Combat.TryUpgradeSourceModelViviAlabasta(
                    live.ActorId, wallet, out won, out level));
                Assert.IsTrue(won);
                Assert.AreEqual(2, level);
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.AreEqual(0, wallet.Balance(WispResourceIds.Random));
                Assert.IsFalse(live.Combat.TryUpgradeSourceModelViviAlabasta(
                    live.ActorId, wallet, out won, out level));
            }
        }

        [Test]
        public void ViviLostRollConsumesCostButAllowsNextPaidTry()
        {
            using (var live = Create("E-06", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add("wood", 4)
                    .Add(WispResourceIds.Random, 6));
                bool won; int level;
                Assert.IsTrue(live.Combat.TryUpgradeSourceModelViviAlabasta(
                    live.ActorId, wallet, out won, out level));
                Assert.IsFalse(won);
                Assert.AreEqual(1, level);
                Assert.AreEqual(2, wallet.Balance("wood"));
                Assert.AreEqual(3, wallet.Balance(WispResourceIds.Random));
                Assert.IsTrue(live.Combat.TryUpgradeSourceModelViviAlabasta(
                    live.ActorId, wallet, out won, out level));
                Assert.IsFalse(won);
                Assert.AreEqual(1, level);
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.AreEqual(0, wallet.Balance(WispResourceIds.Random));
            }
        }

        [Test]
        public void KumaTenPercentAttackSharesTransferDamageAndStunExecutor()
        {
            using (var live = Create("L-32", true, -4900, 5400))
            {
                var outcomes = live.Battle.Advance(6);
                Assert.IsTrue(outcomes.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "발바닥발바닥 열매" &&
                    e.Detail.Contains("BtL: 10%")),
                    "The attack-declaration proc must occur, not only transfer.");
                Assert.IsTrue(outcomes.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "발바닥발바닥 열매"));
                Assert.IsTrue(outcomes.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "발바닥발바닥 열매"));
            }
        }

        [Test]
        public void DetachedHibariBentoCanBeConsumedAfterCreatorSale()
        {
            using (var live = Create("L-39", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                long target = live.Combat.SpawnCharacter("L-01", -4900, 5400);
                var port = new Original323SourceDedicatedSkillPort(live.Combat, wallet);
                var actions = new MobileDedicatedSkillActions();
                actions.Configure(port);
                Assert.IsTrue(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                string item = Original323SourceNativeActionKeys.HibariItem(live.ActorId);
                Assert.AreEqual(1, wallet.Balance(item));
                string action = Original323SourceNativeActionKeys.HibariBento(
                    live.ActorId, 0);
                Assert.IsTrue(actions.AvailableFor(0).Any(x => x.Id == action &&
                    x.IsDetachedItem && x.RequiresAllyTarget));
                Assert.AreEqual(ManualActionState.Succeeded,
                    actions.RequestTargeted(action, live.ActorId, target).State);
                Assert.AreEqual(0, wallet.Balance(item));
                Assert.IsFalse(actions.AvailableFor(0).Any(x => x.Id == action));
                Assert.AreEqual(ManualActionState.Rejected,
                    actions.RequestTargeted(action, live.ActorId, target).State);
            }
        }

        [Test]
        public void DetachedHibariBentoSellDoesNotGrantTwice()
        {
            using (var live = Create("L-39", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                var actions = new MobileDedicatedSkillActions();
                actions.Configure(new Original323SourceDedicatedSkillPort(
                    live.Combat, wallet));
                Assert.IsTrue(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                string action = Original323SourceNativeActionKeys.HibariSell(
                    live.ActorId);
                Assert.IsTrue(actions.AvailableFor(0).Any(x => x.Id == action &&
                    x.IsDetachedItem));
                Assert.AreEqual(ManualActionState.Succeeded,
                    actions.Request(action).State);
                Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
                Assert.AreEqual(ManualActionState.Rejected,
                    actions.Request(action).State);
                Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
            }
        }

        [Test]
        public void OriginalHibariBentoRequiresCreationThenConsumesOrSellsTheSameItem()
        {
            using (var live = Create("L-39", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                long ally = live.Combat.SpawnCharacter("L-01", -4900, 5400);
                Assert.IsFalse(live.Combat.CanUseSourceModelHibariBento(
                    live.ActorId, ally, wallet));
                Assert.IsTrue(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                Assert.IsFalse(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                string key = Original323SourceNativeActionKeys.HibariItem(live.ActorId);
                Assert.AreEqual(1, wallet.Balance(key));
                Assert.IsTrue(live.Combat.TryUseSourceModelHibariBento(
                    live.ActorId, ally, wallet));
                Assert.AreEqual(0, wallet.Balance(key));
                Assert.IsFalse(live.Combat.TrySellSourceModelHibariBento(
                    live.ActorId, wallet));
                Assert.IsTrue(live.Combat.UnitStatusOf(ally).Snapshot().Any(
                    effect => effect.Type == CombatEffectType.AttackSpeedBuff &&
                        effect.SourceId.StartsWith("practice-tooltip:hibari-bento:")));
            }
            using (var live = Create("L-39", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                Assert.IsTrue(live.Combat.TryCreateSourceModelHibariBento(
                    live.ActorId, wallet));
                Assert.IsTrue(live.Combat.TrySellSourceModelHibariBento(
                    live.ActorId, wallet));
                Assert.AreEqual(1, wallet.Balance(WispResourceIds.Choice));
                Assert.IsFalse(live.Combat.TrySellSourceModelHibariBento(
                    live.ActorId, wallet));
            }
        }

        [Test]
        public void MariaFiveSecondPeriodicEffectIsNotSilentlyCutOffAtSixTicks()
        {
            using (var live = Create("D-01", false, -4900, 5400))
            {
                Assert.IsTrue(live.Combat.TryCycleSourceModelMaria(live.ActorId));
                live.Battle.Advance(36);
                Assert.AreEqual("꼼짝말란 마리아",
                    live.Combat.SourceModelMariaCurrentModeName(live.ActorId));
                Assert.AreEqual(0,
                    live.Combat.SourceModelMariaEffectRemaining(live.ActorId));
                Assert.IsFalse(live.Combat.CanCycleSourceModelMaria(live.ActorId) &&
                    live.Combat.SourceModelMariaCooldownRemaining(live.ActorId) > 0);
            }
        }

        [Test]
        public void KumaJassManaUsesAttackDeclaredNotFreeAutoCast()
        {
            using (var live = Create("L-32", false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.AreEqual(115, live.Combat.ResourcesOf(live.ActorId).Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
                live.Battle.Advance(5);
                Assert.Less(live.Combat.ResourcesOf(live.ActorId).Mana, 115);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
            }
        }

        [Test]
        public void I07CurrentHealthBonusUsesOnlyPositiveSourceVerifiedPercent()
        {
            var source = Data("character-skill-profiles.json");
            var valid = CharacterSkillCatalog.FromJson(source)
                .Require("I-07").Skills.Single(x => x.Name == "마법 타격");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(valid));
            // The imported 5% attack proc includes 400000 flat magic and
            // 2% of the target's CURRENT HP. Never authorize zero/negative
            // percent or a malformed model via a generic field whitelist.
            const string recorded = "\"plusTargetCurrentHealthPercent\": 2\n";
            StringAssert.Contains(recorded, source);
            foreach (string invalid in new[] { "0", "-2" })
            {
                var changed = source.Replace(recorded,
                    "\"plusTargetCurrentHealthPercent\": " + invalid + "\n");
                Assert.AreNotEqual(source, changed);
                var attack = CharacterSkillCatalog.FromJson(changed)
                    .Require("I-07").Skills.Single(x => x.Name == "마법 타격");
                Assert.IsFalse(SourceModelNextThirtySkillCatalog
                    .CanModelCompleteAttack(attack), invalid);
            }
        }

        [Test]
        public void SunnyKnowledgeShotNeedsExactSourceCondition()
        {
            var original = Data("character-skill-profiles.json");
            var expected = CharacterSkillCatalog.FromJson(original)
                .Require("H-15").Skills.Single(x => x.Name == "베가펑크 지식 탑재 포격");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(expected));
            const string witness = "\"requires\": \"vegapunkKnowledge\"";
            StringAssert.Contains(witness, original);
            var changed = original.Replace(witness, "\"requires\": \"noVegapunkKnowledge\"");
            Assert.AreNotEqual(original, changed);
            var invalid = CharacterSkillCatalog.FromJson(changed)
                .Require("H-15").Skills.Single(x => x.Name == "베가펑크 지식 탑재 포격");
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(invalid));
        }

        [Test]
        public void NewAttackPacketsAreIndependentAndSourceProfilesStayComplete()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var mapping = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            Assert.GreaterOrEqual(SourceModelUnconnectedSkillCatalog.ApprovedAttackPacketCount, 30);
            var seen = new HashSet<string>();
            var unsupported = new List<string>();
            foreach (var row in SourceModelUnconnectedSkillCatalog.All)
            {
                Assert.IsTrue(seen.Add(row.Rawcode));
                Assert.AreEqual(row.Rawcode, mapping.Find(row.CatalogId).OriginalRawcode);
                Assert.IsNotNull(skills.Find(row.CatalogId));
                SourceModelNextThirtyUnit previouslyIncluded;
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.TryGet(
                    row.Rawcode, out previouslyIncluded), row.CatalogId);
                foreach (string name in row.PracticeAttackSkills)
                {
                    var skill = skills.Find(row.CatalogId).Skills.Single(x => x.Name == name);
                    if (!SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill))
                        unsupported.Add(row.CatalogId + " attack " + name);
                }
                // I-03 primary-target bonus is now modeled. Clone proc parity
                // remains unverified and is not asserted by this test.
                if (row.CatalogId == "I-03")
                {
                    var magic = skills.Find("I-03").Skills.Single(x => x.Name == "범위 마법");
                    Assert.AreEqual(200000d, magic.Components[0].Number("singleExtraAmount"));
                    Assert.AreEqual(true, magic.Get("alsoUsedByClones"));
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(magic));
                }
                foreach (string name in row.PracticeActiveSkills)
                {
                    var skill = skills.Find(row.CatalogId).Skills.Single(x => x.Name == name);
                    if (!SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill))
                        unsupported.Add(row.CatalogId + " active " + name);
                }
            }
            Assert.IsEmpty(unsupported,
                "Every approved source-profile packet must pass the complete-model gate: " +
                string.Join("; ", unsupported));
        }

        [Test]
        public void EnelLightningPreservesPinnedThreeSecondElevenHitSchedule()
        {
            using (var live = Create("I-02", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                live.Battle.Advance(2);
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(pool);
                Assert.AreEqual(160, pool.Mana);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives());
                Assert.AreEqual(0, pool.Mana);
                Assert.IsFalse(live.Combat.LastEvents.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "마나 낙뢰"),
                    "The 3-second packet cannot apply eleven instant hits.");
                var results = live.Battle.Advance(3.1);
                Assert.IsTrue(results.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "마나 낙뢰" && e.Amount > 0));
            }
        }

        [TestCase("H-09", "뇌영", true)]
        [TestCase("H-07", "갑판 포격", true)]
        [TestCase("E-04", "마나 고정 피해", true)]
        [TestCase("I-06", "대자재천신", true)]
        [TestCase("RN-05", "데스노트", false)]
        public void TwentyThirdSourcePacketIsCompleteAndOwnerScoped(
            string id, string name, bool active)
        {
            var profiles = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var skill = profiles.Find(id).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelTwentyThirdSkillCatalog.IsSpecial(skill));
            Assert.IsTrue(active ?
                SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill) :
                SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            Assert.IsFalse(active ?
                SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill) :
                SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            // No approval by a shared Korean skill name on another unit.
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "wrong-owner", new[] { name }, skill));
        }

        [TestCase("TR-05", "H096")]
        [TestCase("TR-25", "H0B4")]
        public void TwentyFifthFullKitsMustMatchSourceOwnerAndAllFields(
            string owner, string rawcode)
        {
            var profiles = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var profile = profiles.Require(owner);
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var original = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            Assert.AreEqual(rawcode, original.Find(owner).OriginalRawcode);
            Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.HasExactOwner(profile, owner));
            foreach (var skill in profile.Skills)
            {
                Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.CanModelExact(skill), skill.Id);
                Assert.IsTrue(skill.Trigger.StartsWith("active") ?
                    SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill) :
                    SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                    "wrong-owner", new[] { skill.Name }, skill));
            }
        }

        [TestCase("R-02", "h01Q", 1)]
        [TestCase("LI-09", "h05F", 3)]
        public void TwentyEighthWholeProfilesGateAllFourSourceSkills(
            string owner, string rawcode, int count)
        {
            var profiles = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var profile = profiles.Require(owner);
            Assert.AreEqual(rawcode, map.Find(owner).OriginalRawcode);
            Assert.AreEqual(count, profile.Skills.Count);
            Assert.IsTrue(SourceModelTwentyEighthSkillCatalog.HasExactOwner(
                profile, owner));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet(rawcode, out row));
            Assert.AreEqual(owner, row.CatalogId);
            Assert.AreEqual(count, row.PracticeAttackSkills.Count);
            foreach (var skill in profile.Skills)
            {
                Assert.IsTrue(SourceModelTwentyEighthSkillCatalog.CanModelExact(skill));
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            }
            Assert.AreEqual(owner == "R-02" ? false : true,
                SourceModelTwentyEighthSkillCatalog.UsesPrimaryTarget(profile.Skills[0]));
        }

        [Test]
        public void TwentyEighthTamperedFieldBlocksWholeOwnerNotJustOneProc()
        {
            var original = Data("character-skill-profiles.json");
            var profiles = CharacterSkillCatalog.FromJson(original.Replace(
                "\"name\": \"거프 타격\"", "\"name\": \"거프 가짜 타격\""));
            Assert.IsFalse(SourceModelTwentyEighthSkillCatalog.HasExactOwner(
                profiles.Require("R-02"), "R-02"));
            var changed = CharacterSkillCatalog.FromJson(original.Replace(
                "\"name\": \"혼합 스턴\"", "\"name\": \"혼합 가짜 스턴\""));
            Assert.IsFalse(SourceModelTwentyEighthSkillCatalog.HasExactOwner(
                changed.Require("LI-09"), "LI-09"));
        }

        [TestCase("R-02", "거프 타격")]
        [TestCase("LI-09", "단일 참격")]
        [TestCase("LI-09", "광역 물리")]
        [TestCase("LI-09", "혼합 스턴")]
        public void TwentyEighthProcPacketsHitEnemyOnlyWhenRollSucceeds(
            string owner, string name)
        {
            using (var success = Create(owner, true, -4900, 5400))
            {
                var events = success.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillActivated && x.SkillName == name));
                Assert.IsTrue(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillDamage && x.SkillName == name &&
                    x.Amount > 0));
                if (owner == "R-02")
                    Assert.IsTrue(events.Any(x => x.Type ==
                        CharacterCombatEventType.ArmorBreakApplied && x.SkillName == name));
            }
            using (var blocked = Create(owner, false, -4900, 5400))
            {
                var events = blocked.Battle.Advance(6);
                Assert.IsFalse(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillActivated && x.SkillName == name));
            }
        }

        [TestCase("LI-08", "h07I", 4, 3)]
        [TestCase("L-37", "h031", 1, 1)]
        [TestCase("L-34", "h0AH", 1, 1)]
        [TestCase("RN-06", "h06Z", 3, 1)]
        public void TwentyNinthOwnersGateFullProfilesButAuthorizeOnlyFiveProcs(
            string owner, string rawcode, int skillCount, int approved)
        {
            var profiles = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var profile = profiles.Require(owner);
            Assert.AreEqual(rawcode, map.Find(owner).OriginalRawcode);
            Assert.AreEqual(skillCount, profile.Skills.Count);
            Assert.IsTrue(SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(profile));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet(rawcode, out row));
            Assert.AreEqual(owner, row.CatalogId);
            Assert.AreEqual(approved, row.PracticeAttackSkills.Count);
            int runnable = 0;
            foreach (var skill in profile.Skills)
            {
                if (SourceModelTwentyNinthSkillCatalog.CanModelExact(skill))
                {
                    runnable++;
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
                }
                else
                    Assert.IsFalse(SourceModelTwentyNinthSkillCatalog.IsSpecial(skill));
            }
            Assert.AreEqual(approved, runnable);
            if (owner == "LI-08")
            {
                Assert.IsTrue(SourceModelTwentyNinthSkillCatalog.UsesPrimaryTarget(
                    profile.Skills[1]));
                Assert.IsFalse(SourceModelTwentyNinthSkillCatalog.UsesPrimaryTarget(
                    profile.Skills[2]));
            }
        }

        [Test]
        public void TwentyNinthModifyingEvenDeferredSkillsOrPassivesRejectsOwner()
        {
            string original = Data("character-skill-profiles.json");
            var badDeferred = CharacterSkillCatalog.FromJson(original.Replace(
                "\"name\": \"자기 가속\"", "\"name\": \"임의 가속\""));
            Assert.IsFalse(SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(
                badDeferred.Require("LI-08")));
            var badSelected = CharacterSkillCatalog.FromJson(original.Replace(
                "\"name\": \"중력검\"", "\"name\": \"변조 중력검\""));
            Assert.IsFalse(SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(
                badSelected.Require("L-37")));
            var badToken = CharacterSkillCatalog.FromJson(original.Replace(
                "\"name\": \"행운의 토큰(Q)\"", "\"name\": \"임의 토큰\""));
            Assert.IsFalse(SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(
                badToken.Require("RN-06")));
        }

        [TestCase("LI-08", "단일 타격")]
        [TestCase("LI-08", "광역 마법")]
        [TestCase("L-37", "중력검")]
        [TestCase("L-34", "화재")]
        [TestCase("RN-06", "다크니스 에로우")]
        public void TwentyNinthFiveProcsConsumeOnlySuccessfulPracticeRolls(
            string owner, string name)
        {
            using (var activated = Create(owner, true, -4900, 5400))
            {
                var events = activated.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillActivated && x.SkillName == name));
                Assert.IsTrue(events.Any(x => x.Type ==
                    CharacterCombatEventType.SkillDamage && x.SkillName == name &&
                    x.Amount > 0));
                if (owner == "L-34")
                    Assert.IsTrue(events.Any(x => x.Type ==
                        CharacterCombatEventType.StatusApplied && x.SkillName == name &&
                        x.Detail.Contains("방어 감소 20")));
            }
            using (var rejected = Create(owner, false, -4900, 5400))
                Assert.IsFalse(rejected.Battle.Advance(6).Any(x => x.Type ==
                    CharacterCombatEventType.SkillActivated && x.SkillName == name));
        }

        [Test]
        public void ThirtiethKatakuriUsesRealSourceHPAndOnlyThenEnablesMaxSpeed()
        {
            var profile = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json")).Require("LI-08");
            Assert.IsTrue(SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(profile));
            Assert.IsTrue(SourceModelTwentyNinthSkillCatalog.CanModelExact(
                profile.Skills[0]));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet("h07I", out row));
            Assert.IsTrue(row.PracticeAttackSkills.Contains("자기 가속"));
            using (var live = Create("LI-08", true, -4900, 5400,
                withSourceResources: true))
            {
                var changes = live.Battle.Advance(6);
                Assert.IsTrue(changes.Any(e => e.Type ==
                    CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "자기 가속" && e.Detail.Contains("HP 17")));
                Assert.IsTrue(changes.Any(e => e.Type ==
                    CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "자기 가속" && e.Amount > 0));
            }
            using (var noPool = Create("LI-08", true, -4900, 5400))
            {
                Assert.IsFalse(noPool.Battle.Advance(6).Any(e =>
                    e.SkillName == "자기 가속" && e.Type ==
                    CharacterCombatEventType.SkillActivated));
            }
        }

        [Test]
        public void ThirtiethKatakuriApproachRollsOnRangeEntryNotEachAttack()
        {
            var profile = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json")).Require("LI-08");
            Assert.IsTrue(SourceModelTwentyNinthSkillCatalog.CanModelKatakuriApproach(
                profile.Skills[3]));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(
                profile.Skills[3]));
            using (var live = Create("LI-08", true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type ==
                    CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "접근 상대 강타"));
                Assert.IsTrue(events.Any(e => e.Type ==
                    CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "접근 상대 강타" && e.Amount > 0));
                Assert.IsTrue(events.Any(e => e.Type ==
                    CharacterCombatEventType.ArmorBreakApplied &&
                    e.SkillName == "접근 상대 강타"));
            }
            using (var blocked = Create("LI-08", false, -4900, 5400))
                Assert.IsFalse(blocked.Battle.Advance(6).Any(e => e.Type ==
                    CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "접근 상대 강타"));
        }

        [Test]
        public void ThirtiethKingBasicHitAppliesSeparateSlowAndMagicDot()
        {
            using (var live = Create("L-34", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type ==
                    CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "킹 평타 둔화" && e.Detail.Contains("45%")));
                Assert.IsTrue(events.Any(e => e.Type ==
                    CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "킹 평타 지속 피해" && e.Amount > 0));
                Assert.IsFalse(events.Any(e => e.Type ==
                    CharacterCombatEventType.SkillActivated && e.SkillName == "화재"));
            }
        }

        [Test]
        public void TwentyFifthConflictingTooltipOwnersNeverReceiveForeignSkills()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var original = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var skills = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            Assert.IsTrue(original.Find("L-30").MapName.Contains("카르가라"));
            Assert.IsTrue(original.Find("D-06").MapName.Contains("바제스"));
            Assert.AreEqual("h03F", original.Find("L-30").OriginalRawcode);
            Assert.AreEqual("h09V", original.Find("D-06").OriginalRawcode);
            Assert.AreEqual("h024", original.Find("R-29").OriginalRawcode);
            Assert.IsTrue(original.Find("R-29").MapName.Contains("와이퍼"));
            Assert.IsTrue(skills.Require("L-30").Skills.Any(x => x.Name == "샨디아의 창"));
            Assert.IsTrue(skills.Require("D-06").Skills.Any(x => x.Name == "파동 엘보"));
            Assert.IsFalse(SourceModelTwentyFifthSkillCatalog.HasExactOwner(
                skills.Require("L-30"), "L-30"));
            Assert.IsFalse(SourceModelTwentyFifthSkillCatalog.HasExactOwner(
                skills.Require("D-06"), "D-06"));
            SourceModelUnconnectedSkillRow unused;
            Assert.IsFalse(SourceModelUnconnectedSkillCatalog.TryGet("h03F", out unused));
            Assert.IsFalse(SourceModelUnconnectedSkillCatalog.TryGet("h09V", out unused));
            Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.IsKnownForeignProfile(
                skills.Require("L-30")));
            Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.IsKnownForeignProfile(
                skills.Require("D-06")));
            using (var kalgara = Create("L-30", false))
                Assert.AreEqual(AbilityCastResult.UnsupportedEffect,
                    kalgara.Combat.TryCastActiveSkill(kalgara.ActorId,
                        "L-30:skill:0", null));
            using (var burgess = Create("D-06", false))
                Assert.AreEqual(AbilityCastResult.UnsupportedEffect,
                    burgess.Combat.TryCastActiveSkill(burgess.ActorId,
                        "D-06:skill:0", null));
        }

        [Test]
        public void KidTraitPacketMustMatchBothExactOriginalReplacements()
        {
            var source = Data("character-skill-profiles.json");
            var profiles = CharacterSkillCatalog.FromJson(source);
            Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.CanModelExactKidEnhancement(
                profiles.Require("TR-25")));
            Assert.IsFalse(SourceModelTwentyFifthSkillCatalog.CanModelExactKidEnhancement(
                profiles.Require("TR-05")));
            var tampered = CharacterSkillCatalog.FromJson(source.Replace(
                "\"stunPerHitSeconds\": 2.85", "\"stunPerHitSeconds\": 2.86"));
            Assert.IsFalse(SourceModelTwentyFifthSkillCatalog.CanModelExactKidEnhancement(
                tampered.Require("TR-25")), "One changed upgrade field must invalidate both swaps");
        }

        [Test]
        public void KidEnhancedDamnedPunkRequiresTraitWitnessAndReplacesRepel()
        {
            using (var ordinary = Create("TR-25", true, -4900, 5400))
            {
                Assert.IsFalse(ordinary.Combat.PracticeKidEnhancedOf(ordinary.ActorId));
                var previous = ordinary.Battle.Advance(6);
                Assert.IsTrue(previous.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "리펠"));
                Assert.IsFalse(previous.Any(x => x.SkillName == "댐드 펑크"));
            }
            using (var enhanced = Create("TR-25", true, -4900, 5400))
            {
                enhanced.Combat.ConfigureSourceModelKidEnhancedEligibility(
                    id => id == enhanced.ActorId);
                Assert.IsTrue(enhanced.Combat.PracticeKidEnhancedOf(enhanced.ActorId));
                var events = enhanced.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "댐드 펑크"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "댐드 펑크" && x.Amount > 0));
                Assert.IsFalse(events.Any(x => x.SkillName == "리펠"));
            }
        }

        [Test]
        public void KidTraitActiveCostsFiftyHealthAndResolvesThreeHitsAfterWindup()
        {
            using (var live = Create("TR-25", false, -4900, 5400))
            {
                live.Combat.ConfigureCharacterResources(live.ActorId,
                    new CharacterResources(100, 100, 0, 0));
                live.Combat.ConfigureSourceModelKidEnhancedEligibility(
                    id => id == live.ActorId);
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId,
                        "TR-25:skill:0", target.Id));
                Assert.AreEqual(50, live.Combat.ResourcesOf(live.ActorId).Health);
                var events = live.Battle.Advance(.6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "펑크 크래시"));
                Assert.IsTrue(events.Count(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "펑크 크래시") >= 3,
                    "The enhanced packet is three hits, not one six-million lump");
                Assert.IsFalse(events.Any(x => x.SkillName == "펑크 깁슨"));
            }
        }

        [Test]
        public void KidThirtyFivePercentAttackReplacesOnlyTheBasicMagicSplash()
        {
            // Fully source-approved packet: native basic hit stays, the
            // separate 40k area-magic passive is mutually exclusive with
            // the 360k physical + 1.5s single-target stun packet.
            var catalog = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var profile = catalog.Require("TR-25");
            Assert.IsTrue(SourceModelTwentyFifthSkillCatalog.HasExactOwner(
                profile, "TR-25"));
            Assert.AreEqual("단일 대체 공격", profile.Skills[3].Name);
            Assert.AreEqual(true, profile.Skills[3].Get("overridesBasicMagicSplash"));
            using (var live = Create("TR-25", true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack),
                    "The normal attack must still resolve before the replacement");
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "단일 대체 공격"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "단일 대체 공격" && x.Amount > 0));
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "키드 · 기본 공격 부가 마법"),
                    "A replacement must never stack with the basic magic splash");
            }
            using (var live = Create("TR-25", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsFalse(events.Any(x => x.SkillName == "단일 대체 공격"));
            }
        }

        [TestCase("H-10", "맥시멈 스윙")]
        [TestCase("I-03", "패기발산")]
        [TestCase("L-07", "패기발산")]
        [TestCase("L-20", "화이트 아웃")]
        [TestCase("L-01", "어둠어둠 열매")]
        [TestCase("TR-28", "라디칼 빔")]
        public void TwentyFourthSourcePacketIsWholeAndOwnerScoped(
            string id, string name)
        {
            var profiles = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var skill = profiles.Require(id).Skills.Single(x => x.Name == name);
            Assert.IsTrue(SourceModelTwentyFourthSkillCatalog.IsSpecial(skill));
            Assert.IsTrue(SourceModelTwentyFourthSkillCatalog.CanModelExactActive(skill),
                id + " source packet changed");
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "wrong-owner", new[] { name }, skill));
        }

        [TestCase("RN-02", "대옥 나선환")]
        [TestCase("RN-13", "비상식량")]
        [TestCase("LI-01", "광역 마법 공격")]
        [TestCase("L-24", "산화 부지화")]
        [TestCase("I-02", "범위 마법 스턴")]
        public void MissingAttackPacketExecutesInSharedPracticeCombat(
            string unitId, string skillName)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == skillName && e.Detail.Contains("TOOLTIP_MODEL_ONLY")),
                    unitId + " " + skillName);
            }
        }

        [Test]
        public void KumaOriginalObjectManaPoolEnablesNewApprovedActiveOnce()
        {
            using (var live = Create("L-32", false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.AreEqual(115, live.Combat.ResourcesOf(live.ActorId).Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "BtL is a SOURCE attack callback, not a button/autocast.");
                var result = live.Battle.Advance(5);
                Assert.IsTrue(result.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "우르스스 쇼크"));
                Assert.Less(live.Combat.ResourcesOf(live.ActorId).Mana, 115);
            }
        }

        [Test]
        public void RareLawAppliesOneTimedAttackSpeedBuffToAnAllyOnly()
        {
            using (var live = Create("R-05", false, -4900, 5400))
            {
                long ally = live.Combat.SpawnCharacter("C-01", -4900, 5400);
                live.Battle.Advance(1);
                Assert.AreEqual(1, live.Combat.AutoCastSourceRareLawAcceleration());
                Assert.IsTrue(live.Combat.UnitStatusOf(ally).Snapshot().Any(
                    x => x.Type == CombatEffectType.AttackSpeedBuff &&
                        x.Amount == 120 && x.DurationSeconds == 8));
                Assert.AreEqual(0, live.Combat.AutoCastSourceRareLawAcceleration());
                live.Combat.RemoveCharacter(ally);
                Assert.AreEqual(0, live.Combat.AutoCastSourceRareLawAcceleration());
            }
        }

        [Test]
        public void SecondThirtyUnverifiedFormAndBossSkillsRemainDeferred()
        {
            foreach (var candidate in new[] {
                new[] { "TR-23", "쵸파 럼블볼 폭주" },
                new[] { "TR-30", "베가펑크 마나 공격" },
                new[] { "I-06", "대자재천신" },
                new[] { "I-11", "체력 소모 광역 공격" }
            })
            {
                using (var live = Create(candidate[0], true, -4900, 5400))
                {
                    var events = live.Battle.Advance(6);
                    Assert.IsNull(live.Combat.ResourcesOf(live.ActorId));
                    Assert.IsFalse(events.Any(e =>
                        e.Type == CharacterCombatEventType.SkillActivated &&
                        e.SkillName == candidate[1]),
                        candidate[0] + ": no unprovisioned active may debit resources");
                }
            }
        }

        [Test]
        public void SecondThirtyIncompleteActivePacketsCannotSpendResources()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            foreach (var entry in new[] {
                new[] { "TR-23", "럼블볼 폭주" },
                new[] { "E-01", "거대화" },
                new[] { "TR-30", "베가펑크 마나 공격" }
            })
            {
                var skill = profiles.Require(entry[0]).Skills.Single(x => x.Name == entry[1]);
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                    entry[0] + ": " + entry[1]);
                var row = SourceModelNextThirtySkillCatalog.All.Single(x => x.CatalogId == entry[0]);
                Assert.IsFalse(row.AllowsActive(skill.Name));
            }
        }

        [Test]
        public void SecondThirtyModelablePacketDoesNotGrantCastPermission()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var skill = profiles.Require("I-11").Skills.Single(
                x => x.Name == "체력 소모 광역 공격");
            // The I-11 tooltip has a parsable damage packet, but no reviewed
            // authorization in the unit's practice active-skill allow-list.
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "I-11").AllowsActive(skill.Name));
        }

        [Test]
        public void SameNamedActivePacketsCannotBorrowAnotherUnitsPermission()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var robin = profiles.Require("TR-04").Skills.Single(
                x => x.Name == "체력 소모 광역 공격");
            var kaido = profiles.Require("I-11").Skills.Single(x => x.Name == robin.Name);
            var approved = SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "TR-04").PracticeActiveSkills;
            var denied = SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "I-11").PracticeActiveSkills;
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(kaido));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-04", approved, robin));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-04", approved, kaido));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "I-11", denied, kaido));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-0", approved, robin));
        }

        [TestCase("TR-23", "TR-23:skill:0", "럼블볼 폭주")]
        [TestCase("E-01", "E-01:skill:0", "거대화")]
        [TestCase("TR-30", "TR-30:skill:0", "베가펑크 마나 공격")]
        [TestCase("I-11", "I-11:skill:0", "체력 소모 광역 공격")]
        // TR-09's health-cost active has source approval and is covered
        // by the real resource/second-cast test above.
        // I-04/L-07/I-06 have gained approved source-model actives.
        // Keep the negative test limited to genuinely deferred packets.
        public void SecondThirtyUnapprovedActiveSkillCannotSpendThroughGenericFallback(
            string unitId, string skillId, string skillName)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                var resources = new CharacterResources(1000, 1000, 1000, 1000);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                int eventCount = live.Combat.LastEvents.Count;
                for (int attempt = 0; attempt < 3; attempt++)
                    Assert.AreEqual(unitId == "E-01" ?
                            AbilityCastResult.NoResourceState :
                            AbilityCastResult.UnsupportedEffect,
                        live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id), unitId);
                Assert.AreEqual(1000, resources.Health, unitId);
                Assert.AreEqual(1000, resources.Mana, unitId);
                Assert.AreEqual(eventCount, live.Combat.LastEvents.Count, unitId);
                Assert.IsFalse(live.Combat.LastEvents.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == skillName), unitId);
            }
        }

        [Test]
        public void SecondThirtyFailedProcNeverAppliesTooltipDamage()
        {
            using (var live = Create("L-13", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "배리어 볼"));
            }
        }

        [Test]
        public void TemporaryFormCatalogAcceptsOnlyCompleteStandalonePackets()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            SourceModelFormSpec form;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-14").Skills.Single(x => x.Name == "블랙 맘바 각성"), out form));
            Assert.AreEqual(8.5, form.Duration);
            Assert.AreEqual(12500, form.FlatAttackPower);
            Assert.AreEqual(.03, form.AttackIntervalReduction);
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-18").Skills.Single(x => x.Name == "인수화"), out form));
            Assert.AreEqual(75000, form.FlatAttackPower);
            Assert.AreEqual(.07, form.AttackIntervalReduction);
            Assert.IsFalse(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-23").Skills.Single(x => x.Name == "럼블볼 폭주"), out form));
            Assert.IsFalse(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("E-01").Skills.Single(x => x.Name == "거대화"), out form));
            double damage, radius;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadClonePulse(
                profiles.Require("I-03").Skills.Single(x => x.Name == "분신"),
                out damage, out radius));
            Assert.AreEqual(100000, damage);
            Assert.AreEqual(400, radius);
            string name;
            double duration, stun;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadTimedClone(
                profiles.Require("L-21").Skills.Single(x => x.Name == "인술 분신"),
                out name, out duration, out stun));
            Assert.AreEqual("분신", name);
            Assert.AreEqual(2, duration);
            Assert.AreEqual(1.5, stun);
        }

        [Test]
        public void SnakeFormUsesSameOwnedUnitAndExpiresWithoutRecreatingIt()
        {
            using (var live = Create("TR-14", true, -4900, 5400))
            {
                int owned = live.Combat.CharacterCount;
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeFormOf(live.ActorId) == null; tick++)
                    events.AddRange(live.Battle.Advance(.2));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.StatusApplied &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.AreEqual("blackMamba", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
                var status = live.Combat.UnitStatusOf(live.ActorId).Snapshot();
                Assert.AreEqual(12500, SourceModelTemporarySkillCatalog.FlatFormPower(status));
                Assert.AreEqual(.03, SourceModelTemporarySkillCatalog.FormIntervalReduction(status));
                live.Rolls.Trigger = false;
                live.Battle.Advance(15);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void FailedSnakeFormRollDoesNotApplyAnyBuff()
        {
            using (var live = Create("TR-14", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(x => x.SkillName == "블랙 맘바 각성" &&
                    x.Type == CharacterCombatEventType.StatusApplied));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void PendingSnakeFormDoesNotReturnAcrossStoryDispatchAndRecall()
        {
            using (var live = Create("TR-14", true, -4900, 5400))
            {
                var lane = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lane);
                bool castStarted = false;
                for (int tick = 0; tick < 100; tick++)
                {
                    var events = live.Battle.Advance(.05);
                    if (!events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                        x.SkillName == "블랙 맘바 각성")) continue;
                    castStarted = true;
                    break;
                }
                Assert.IsTrue(castStarted);
                live.Rolls.Trigger = false;
                lane.Deployed = true;
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                var castField = typeof(CharacterCombatRuntime).GetField(
                    "sourceFormCastFinishes", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(castField);
                Assert.IsFalse(((Dictionary<long, double>)castField.GetValue(
                    live.Combat)).ContainsKey(live.ActorId),
                    "Canceled Snake Man windup cannot block later casting.");
                lane.Deployed = false;
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                var after = live.Battle.Advance(3);
                Assert.IsFalse(after.Any(x => x.Type == CharacterCombatEventType.StatusApplied &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void YamatoFormSpendsConfiguredHealthOnceAndReverts()
        {
            using (var live = Create("TR-18", false, -4900, 5400))
            {
                var resources = new CharacterResources(1000, 1000, 0, 0);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-18:skill:0", null));
                Assert.AreEqual(905, resources.Health);
                Assert.AreEqual("beast", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(75000, SourceModelTemporarySkillCatalog.FlatFormPower(
                    live.Combat.UnitStatusOf(live.ActorId).Snapshot()));
                live.Battle.Advance(4);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(905, resources.Health);
            }
        }

        [Test]
        public void YamatoTemporaryFormEndsImmediatelyOnDecodeWithoutRefillingHealth()
        {
            using (var live = Create("TR-18", false, -4900, 5400))
            {
                var pool = new CharacterResources(1000, 1000, 0, 0);
                live.Combat.ConfigureCharacterResources(live.ActorId, pool);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(
                        live.ActorId, "TR-18:skill:0", null));
                Assert.AreEqual("beast", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(905, pool.Health);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(0, SourceModelTemporarySkillCatalog.FlatFormPower(
                    live.Combat.UnitStatusOf(live.ActorId).Snapshot()));
                Assert.AreEqual(905, pool.Health, "Changing lane is not healing.");
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(1, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void DoflamingoEarlyReversionDoesNotRestoreManaOrCreateFreeRecast()
        {
            using (var live = Create("TR-03", false, -4900, 5400))
            {
                var pool = new CharacterResources(10, 10, 150, 150);
                live.Combat.ConfigureCharacterResources(live.ActorId, pool);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(
                        live.ActorId, "TR-03:skill:0", null));
                Assert.AreEqual(0, pool.Mana);
                Assert.AreEqual("awakening", live.Combat.PracticeFormOf(live.ActorId));
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(0, SourceModelTemporarySkillCatalog.FormIntervalReduction(
                    live.Combat.UnitStatusOf(live.ActorId).Snapshot()), .000001);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
                // Original TR-03 profile does not specify a positive
                // cooldown. Do not invent one: the actual native 150-MP
                // cost alone prevents a free recast after dispatch.
                Assert.AreEqual(AbilityCastResult.InsufficientResources,
                    live.Combat.TryCastActiveSkill(
                        live.ActorId, "TR-03:skill:0", null));
                Assert.AreEqual(0, pool.Mana);
            }
        }

        [Test]
        public void BonneyNikaManaMaximumAndObjectRevertAtLaneCommitNotNextTick()
        {
            using (var live = Create("TR-21", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(
                        live.ActorId, "TR-21:skill:0", null));
                Assert.AreEqual(100, pool.MaximumMana);
                Assert.AreEqual("nika", live.Combat.PracticeFormOf(live.ActorId));
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.AreEqual(150, pool.MaximumMana,
                    "H0BY form pool must revert in the transaction itself.");
                Assert.AreEqual(100, pool.Mana,
                    "The form transition must not fabricate an MP refill.");
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
                Assert.AreEqual(150, pool.MaximumMana);
                Assert.AreEqual(1, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void UtaNativeFormIsVisibleAndOwnerRemovalClearsNativeCycleStates()
        {
            using (var live = Create("E-09", false, -4900, 5400,
                withSourceResources: true))
            {
                live.Combat.ConfigureSourceModelFusionNativeReceipts(id =>
                    id == Original323FusionNativeFollowupLedger.UtaSongId ? 1 : 0);
                Assert.IsTrue(live.Combat.TryStartSourceModelUtaIntro(live.ActorId));
                var ruleField = typeof(CharacterCombatRuntime).GetField(
                    "sourceModelRules", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(ruleField);
                var rules = (SourceModelBasicCombatRules)ruleField.GetValue(live.Combat);
                rules.SetSourceForm(live.ActorId.ToString(), "h0AF");
                Assert.AreEqual("신세계", live.Combat.PracticeFormOf(live.ActorId));

                live.Combat.RemoveCharacter(live.ActorId);

                var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic;
                var introField = typeof(CharacterCombatRuntime).GetField(
                    "sourceModelUtaIntroStarted", flags);
                var cycleField = typeof(CharacterCombatRuntime).GetField(
                    "sourceModelUtaCycleReadyAt", flags);
                Assert.IsNotNull(introField);
                Assert.IsNotNull(cycleField);
                Assert.IsFalse(((HashSet<long>)introField.GetValue(live.Combat))
                    .Contains(live.ActorId));
                Assert.IsFalse(((Dictionary<long, double>)cycleField.GetValue(live.Combat))
                    .ContainsKey(live.ActorId));
                Assert.IsNull(rules.SourceFormRawcodeOf(live.ActorId.ToString()));
            }
        }

        [Test]
        public void UtaNativeHealthTransformDoesNotFireDuringPoneglyphDecoding()
        {
            using (var live = Create("E-09", false, -4900, 5400,
                withSourceResources: true))
            {
                live.Combat.ConfigureSourceModelFusionNativeReceipts(id =>
                    id == Original323FusionNativeFollowupLedger.UtaSongId ? 1 : 0);
                Assert.IsTrue(live.Combat.TryStartSourceModelUtaIntro(live.ActorId));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                pool.SetHealth(1);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                var events = live.Battle.Advance(.7);
                Assert.Less(pool.Health, 100,
                    "B61 HP150 reset must not occur while the unit decodes.");
                Assert.IsFalse(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "우타 · 신세계 변신"));
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
            }
        }

        [Test]
        public void DoflamingoAwakeningSpendsSourceManaPreservesIdentityAndExpires()
        {
            using (var live = Create("TR-03", false, -4900, 5400))
            {
                var resources = new CharacterResources(10, 10, 150, 150);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-03:skill:0", null));
                Assert.AreEqual(0, resources.Mana);
                Assert.AreEqual("awakening", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(.03, SourceModelTemporarySkillCatalog.FormIntervalReduction(
                    live.Combat.UnitStatusOf(live.ActorId).Snapshot()), .000001);
                Assert.AreEqual(AbilityCastResult.Cooldown,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-03:skill:0", null));
                live.Battle.Advance(4.6);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(0, resources.Mana);
            }
        }

        [Test]
        public void RyokugyuForestFormAddsExactlyOneNutrientAndExpires()
        {
            using (var live = Create("TR-06", false, -4900, 5400))
            {
                var resources = new CharacterResources(10, 10, 130, 130);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-06:skill:0", null));
                Assert.AreEqual("forest", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(1, live.Combat.PracticeForestNutrientsOf(live.ActorId));
                Assert.AreEqual(0, resources.Mana);
                Assert.AreEqual(AbilityCastResult.Cooldown,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-06:skill:0", null));
                live.Battle.Advance(7.1);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(1, live.Combat.PracticeForestNutrientsOf(live.ActorId));
            }
        }

        [Test]
        public void OrdinaryFrankyCannotUnlockGeneralOrSpendManaForFree()
        {
            using (var live = Create("TR-28", true, -4900, 5400))
            {
                var resources = new CharacterResources(10, 10, 150, 150);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.MissingOrUnsupportedRequirement,
                    live.Combat.TryCastActiveSkill(live.ActorId,
                        "TR-28:enhanced:frankyGeneral", null));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(150, resources.Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
            }
        }

        [Test]
        public void VerifiedFrankyEnhancementUnlocksGeneralPressAndReverts()
        {
            using (var live = Create("TR-28", false, -4900, 5400))
            {
                var resources = new CharacterResources(10, 10, 150, 150);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                live.Combat.ConfigureSourceModelFrankyEnhancedEligibility(
                    id => id == live.ActorId);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives());
                Assert.AreEqual(0, resources.Mana);
                Assert.AreEqual("frankyGeneral", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
                var events = live.Battle.Advance(.1);
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "제너럴 프레스"),
                    "Only original-upgraded Franky can debit mana and apply the press packet");
                live.Battle.Advance(6.1);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void FormWithoutResourceStateCannotSpendHealthOrActivate()
        {
            using (var live = Create("TR-18", true, -4900, 5400))
            {
                Assert.AreEqual(AbilityCastResult.NoResourceState,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-18:skill:0", null));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void NikaGiantSpendsSourceHealthPreservesOwnerAndRevertsAfterFourPointTwoFiveSeconds()
        {
            using (var live = Create("E-01", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.AreEqual(115, pool.Health);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "E-01:skill:0", null));
                Assert.AreEqual(0, pool.Health);
                Assert.AreEqual("giant", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(AbilityCastResult.Cooldown,
                    live.Combat.TryCastActiveSkill(live.ActorId, "E-01:skill:0", null));
                live.Battle.Advance(4.3);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.Greater(pool.Health, 0,
                    "The original H0BK object regenerates 0.25 HP per second");
                Assert.Less(pool.Health, 115);
            }
        }

        [Test]
        public void BonneyNikaUsesSeparateOriginalAttackObjectButTheSameCharacterInstance()
        {
            using (var live = Create("TR-21", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.AreEqual(150, pool.MaximumMana);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-21:skill:0", null));
                Assert.AreEqual("nika", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(100, pool.MaximumMana);
                Assert.AreEqual(100, pool.Mana);
                pool.SetMana(0);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(150, pool.MaximumMana);
                Assert.AreEqual(0, pool.Mana);
                Assert.AreEqual(AbilityCastResult.InsufficientResources,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-21:skill:0", null));
            }
        }

        [Test]
        public void KingRequiresRealFiftyBreaksBeforeSpendingAnyMana()
        {
            using (var live = Create("LI-10", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var pool = live.Combat.ResourcesOf(live.ActorId);
                double originalMana = pool.Mana;
                live.Battle.Advance(2);
                Assert.AreEqual(AbilityCastResult.MissingOrUnsupportedRequirement,
                    live.Combat.TryCastActiveSkill(live.ActorId, "LI-10:skill:0", null));
                Assert.AreEqual(originalMana, pool.Mana);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void MarcoManualBaseFormOrExplicitWitnessAndSnakeNikaCannotBorrowLuffyObject()
        {
            using (var unknown = Create("E-02", true))
            {
                Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource,
                    unknown.Combat.InspectBasicAttack(unknown.ActorId).Kind,
                    "An unmatched Nika identity cannot borrow another Luffy's attack");
                var samples = unknown.Battle.Advance(2);
                Assert.IsFalse(samples.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == unknown.ActorId.ToString()),
                    "Source-unverified shells must not fabricate basic attacks");
            }
            using (var live = Create("LI-03", true, -4900, 5400))
            {
                Assert.AreEqual("human", live.Combat.PracticeFormOf(live.ActorId),
                    "The source documented F command begins on the base human object.");
                live.Combat.ConfigureSourceModelMarcoFormEvidence(id =>
                    id == live.ActorId ? "phoenix" : null);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId),
                    "An explicit original witness overrides the mobile fallback.");
                Assert.IsFalse(live.Combat.CanToggleSourceModelMarcoForm(live.ActorId),
                    "A native witness and a mobile F clock cannot be mixed.");
            }
        }

        // The original LI-03 tooltip describes an attack proc conditional
        // on a witnessed form; it does NOT give a mobile-only transform key.
        // Invoke the already existing callback with an explicit enemy so the
        // HP threshold and fixed bonus are not polluted by unrelated ticks.
        private static void TriggerWitnessedMarcoModelAttack(
            CharacterCombatRuntime combat, long ownerId, string enemyId)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var characters = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("characters", flags).GetValue(combat);
            var enemies = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("enemies", flags).GetValue(combat);
            var callback = typeof(CharacterCombatRuntime).GetMethod(
                "SourceModelEleventhMarcoAttack", flags);
            Assert.IsNotNull(callback);
            callback.Invoke(combat, new[] { characters[ownerId], enemies[enemyId] });
        }

        private static void SpawnMarcoModelTarget(
            CharacterCombatRuntime combat, string enemyId)
        {
            combat.SpawnEnemyWithDefenses(enemyId, 1000000000,
                CharacterEnemyKind.Line, -4900, 5400, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, 0,
                    "medium", "MODEL_TEST_VERIFIED_DEFENSE"));
        }

        [Test]
        public void MarcoFormEvidenceIsRequiredAndFifteenHpIsCheckedBeforeFormCost()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                SpawnMarcoModelTarget(live.Combat, "marco-form-line");
                var pool = live.Combat.ResourcesOf(live.ActorId);
                Assert.AreEqual(85, pool.MaximumHealth,
                    "Source h08O UnitBalance HP=85, not an invented ability meter.");
                string evidence = null;
                live.Combat.ConfigureSourceModelMarcoFormEvidence(id =>
                    id == live.ActorId ? evidence : null);
                pool.SetHealth(15);
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-form-line");
                Assert.AreEqual(15, pool.Health, "No witness means no proc or HP cost.");

                evidence = "unknown";
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-form-line");
                Assert.AreEqual(15, pool.Health);

                evidence = "phoenix";
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
                pool.SetHealth(14);
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-form-line");
                Assert.AreEqual(14, pool.Health,
                    "Both form skills require HP >= 15 before the roll.");
                pool.SetHealth(15);
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-form-line");
                Assert.AreEqual(5, pool.Health,
                    "Phoenix consumes 10 at exactly 15 HP, not only at 25.");

                evidence = "human";
                Assert.AreEqual("human", live.Combat.PracticeFormOf(live.ActorId));
                pool.SetHealth(15);
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-form-line");
                Assert.AreEqual(8, pool.Health,
                    "Human consumes 7 at exactly 15 HP, not only at 22.");
                Assert.AreEqual(1, live.Combat.CharacterCount,
                    "A form is never a second material/roster instance.");
            }
        }

        [Test]
        public void MarcoPhoenixPrimaryGetsOnlyOneFortyPercentFixedBonus()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                SpawnMarcoModelTarget(live.Combat, "marco-primary");
                SpawnMarcoModelTarget(live.Combat, "marco-secondary");
                live.Combat.ConfigureSourceModelMarcoFormEvidence(id =>
                    id == live.ActorId ? "phoenix" : null);
                var pool = live.Combat.ResourcesOf(live.ActorId);
                var eventField = typeof(CharacterCombatRuntime).GetField(
                    "events", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(eventField);
                var all = (List<CharacterCombatEvent>)eventField.GetValue(live.Combat);
                int before = all.Count;
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-primary");
                var effects = all.Skip(before).ToList();
                Assert.AreEqual(75, pool.Health);
                Assert.AreEqual(1, effects.Count(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "불사조 상태 폭발 · 단일 추가 고정 피해" &&
                    x.EnemyId == "marco-primary" && x.Amount > 0),
                    "The 40% packet is primary-only, never a second area volley.");
                Assert.IsFalse(effects.Any(x =>
                    x.SkillName == "불사조 상태 폭발 · 단일 추가 고정 피해" &&
                    x.EnemyId == "marco-secondary"));
            }
        }

        [Test]
        public void MarcoPhoenixAdditionalMagicHitsEightDistinctEnemiesOnly()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                SpawnMarcoModelTarget(live.Combat, "marco-focus");
                for (int i = 0; i < 11; i++)
                    SpawnMarcoModelTarget(live.Combat,
                        "marco-extra-" + i.ToString("00"));
                live.Combat.ConfigureSourceModelMarcoFormEvidence(id =>
                    id == live.ActorId ? "phoenix" : null);
                var eventsField = typeof(CharacterCombatRuntime).GetField(
                    "events", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                var events = (List<CharacterCombatEvent>)eventsField.GetValue(live.Combat);
                int before = events.Count;
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-focus");
                var extra = events.Skip(before).Where(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "불사조 상태 폭발 · 추가 8명 마법" &&
                    e.Amount > 0).ToList();
                Assert.AreEqual(8, extra.Count);
                Assert.AreEqual(8, extra.Select(e => e.EnemyId).Distinct().Count());
                Assert.IsFalse(extra.Any(e => e.EnemyId == "marco-focus"));
                Assert.AreEqual(75, live.Combat.ResourcesOf(live.ActorId).Health);
            }
        }

        [Test]
        public void MarcoHumanTenPercentExplosionUsesOnePrimaryTarget()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                SpawnMarcoModelTarget(live.Combat, "marco-human");
                SpawnMarcoModelTarget(live.Combat, "marco-bystander");
                var eventField = typeof(CharacterCombatRuntime).GetField(
                    "events", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                var events = (List<CharacterCombatEvent>)eventField.GetValue(live.Combat);
                int before = events.Count;
                var chars = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                    .GetField("characters", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).GetValue(live.Combat);
                var targets = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                    .GetField("enemies", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).GetValue(live.Combat);
                var method = typeof(CharacterCombatRuntime).GetMethod(
                    "SourceModelMarcoHumanAttackEffect",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(method);
                method.Invoke(live.Combat,
                    new[] { chars[live.ActorId], targets["marco-human"] });
                var extra = events.Skip(before).Where(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "새새열매 · 인간형 추가 폭발").ToList();
                Assert.AreEqual(1, extra.Count);
                Assert.AreEqual("marco-human", extra[0].EnemyId);
                Assert.Greater(extra[0].Amount, 0);
            }
        }

        [Test]
        public void MarcoFeatherRegenIsPerRecipientNotPerNumberOfCasters()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                long second = live.Combat.SpawnCharacter("LI-03", -4898, 5400);
                var owner = live.Combat.ResourcesOf(live.ActorId);
                var another = live.Combat.ResourcesOf(second);
                owner.SetHealth(40);
                another.SetHealth(50);
                var method = typeof(CharacterCombatRuntime).GetMethod(
                    "AdvanceSourceModelMarcoAllyRegeneration",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(method);
                method.Invoke(live.Combat, new object[] { 2.0 });
                Assert.AreEqual(46, owner.Health);
                Assert.AreEqual(56, another.Health);
                live.Combat.SetOriginalPoneglyphChanneling(second, true);
                method.Invoke(live.Combat, new object[] { 1.0 });
                Assert.AreEqual(49, owner.Health);
                Assert.AreEqual(56, another.Health,
                    "The decoding recipient is not eligible for the aura.");
            }
        }

        [Test]
        public void MarcoWitnessedFormDoesNotProcDuringDecodeOrAfterRemoval()
        {
            using (var live = Create("LI-03", true, -4900, 5400,
                withSourceResources: true))
            {
                SpawnMarcoModelTarget(live.Combat, "marco-decode-line");
                var pool = live.Combat.ResourcesOf(live.ActorId);
                live.Combat.ConfigureSourceModelMarcoFormEvidence(id =>
                    id == live.ActorId ? "phoenix" : null);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                double before = pool.Health;
                TriggerWitnessedMarcoModelAttack(
                    live.Combat, live.ActorId, "marco-decode-line");
                Assert.AreEqual(before, pool.Health);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(0, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void MarcoFActionAppearsInExistingDedicatedSkillRibbonAndUsesOneOwner()
        {
            using (var live = Create("LI-03", true, -4900, 5400))
            {
                var port = new Original323SourceDedicatedSkillPort(
                    live.Combat, new EconomyWallet());
                var ribbon = new MobileDedicatedSkillActions();
                ribbon.Configure(port);
                string action = Original323SourceNativeActionKeys.MarcoTransformAction(
                    live.ActorId);
                var first = ribbon.Available().Single(x => x.Id == action);
                Assert.IsTrue(first.IsUnlocked && first.CanExecute);
                StringAssert.Contains("불사조", first.Title);
                StringAssert.Contains("10.75초", first.OriginalCostAndRules);
                int initialUnits = live.Combat.CharacterCount;
                Assert.AreEqual(ManualActionState.Succeeded, ribbon.Request(action).State);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(initialUnits, live.Combat.CharacterCount);
                Assert.AreEqual(10.75,
                    live.Combat.SourceModelMarcoTransformCooldownRemaining(
                        live.ActorId), .00001);
                var cooling = ribbon.Available().Single(x => x.Id == action);
                Assert.IsFalse(cooling.CanExecute);
                StringAssert.Contains("인간형", cooling.Title);
                StringAssert.Contains("대기", cooling.OriginalCostAndRules);
                Assert.AreEqual(ManualActionState.Rejected, ribbon.Request(action).State);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
                live.Battle.Advance(10.8);
                Assert.IsTrue(ribbon.Available().Single(x => x.Id == action).CanExecute);
                Assert.AreEqual(ManualActionState.Succeeded, ribbon.Request(action).State);
                Assert.AreEqual("human", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(initialUnits, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void MarcoManualFBlocksDecodingAndKeepsCooldownAcrossStoryDispatch()
        {
            using (var live = Create("LI-03", false, -4900, 5400))
            {
                Assert.IsTrue(live.Combat.TryToggleSourceModelMarcoForm(live.ActorId));
                var lane = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lane);
                lane.DeployedIds.Add(live.ActorId);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId),
                    "F is not a timed buff and should not be discarded on dispatch.");
                Assert.IsFalse(live.Combat.CanToggleSourceModelMarcoForm(live.ActorId),
                    "Dispatch must not reset the owner's 10.75s cooldown.");
                lane.DeployedIds.Remove(live.ActorId);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, true);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.IsFalse(live.Combat.TryToggleSourceModelMarcoForm(live.ActorId));
                live.Combat.SetOriginalPoneglyphChanneling(live.ActorId, false);
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
                Assert.Greater(live.Combat.SourceModelMarcoTransformCooldownRemaining(
                    live.ActorId), 0);
            }
        }

        [Test]
        public void MarcoFActionRejectsWrongOwnerAndRetiresFormOnSale()
        {
            using (var legend = Create("L-11", false, -4900, 5400))
            {
                var port = new Original323SourceDedicatedSkillPort(
                    legend.Combat, new EconomyWallet());
                Assert.IsFalse(port.AvailableSkills().Any(x =>
                    x.Id.StartsWith(Original323SourceNativeActionKeys.
                        MarcoTransformActionPrefix, StringComparison.Ordinal)));
                Assert.IsFalse(legend.Combat.TryToggleSourceModelMarcoForm(
                    legend.ActorId));
            }
            using (var live = Create("LI-03", false, -4900, 5400))
            {
                var port = new Original323SourceDedicatedSkillPort(
                    live.Combat, new EconomyWallet());
                string skill = Original323SourceNativeActionKeys.MarcoTransformAction(
                    live.ActorId);
                Assert.AreEqual(ManualActionState.Succeeded,
                    port.Execute(skill).State);
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsFalse(port.AvailableSkills().Any(x => x.Id == skill));
                Assert.AreEqual(ManualActionState.Rejected, port.Execute(skill).State);
                Assert.AreEqual(0, live.Combat.SourceModelMarcoTransformCooldownRemaining(
                    live.ActorId));
            }
        }

        [Test]
        public void MarcoHumanSpeedIsExplicitOnlyAndDoesNotInventPhoenixAttackStats()
        {
            using (var live = Create("LI-03", false, -4900, 5400))
            {
                var method = typeof(CharacterCombatRuntime).GetMethod(
                    "SourceModelMarcoHumanAttackSpeedBonus",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(method);
                Assert.AreEqual(125d, (double)method.Invoke(
                    live.Combat, new object[] { live.ActorId }));
                Assert.IsTrue(live.Combat.TryToggleSourceModelMarcoForm(live.ActorId));
                Assert.AreEqual(0d, (double)method.Invoke(
                    live.Combat, new object[] { live.ActorId }));
                Assert.AreEqual("phoenix", live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void BonneyFortyAgeStacksTriggerBothOriginalFollowupsOncePerLiveTarget()
        {
            using (var live = Create("TR-21", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 240 && !events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "세월관통"); tick++)
                    events.AddRange(live.Battle.Advance(.1));
                var unlocks = events.Where(x => x.Type ==
                    CharacterCombatEventType.SkillActivated && x.SkillName == "세월관통")
                    .ToList();
                Assert.IsTrue(unlocks.Count > 0,
                    "Two 20-stack near-death procs must unlock the 40-stack effect");
                foreach (var unlock in unlocks)
                {
                    Assert.GreaterOrEqual(live.Combat.PracticeBonneyAgeStacksOf(
                        live.ActorId, unlock.EnemyId), 40);
                    Assert.AreEqual(1, unlocks.Count(x => x.EnemyId == unlock.EnemyId),
                        "A threshold should fire once for a particular target");
                }
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "세월관통" && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "니카펀치" && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.SkillName == "스턴 광역 마법" &&
                    x.Type == CharacterCombatEventType.SkillDamage));
            }
        }

        [Test]
        public void KingFireAndSlowUseSeparateOriginalSchedulesAndTimedEffects()
        {
            using (var live = Create("LI-10", true, -4900, 5400))
            {
                var events = live.Battle.Advance(4);
                Assert.IsTrue(events.Any(x => x.SkillName == "단일 둔화" &&
                    x.Type == CharacterCombatEventType.SkillDamage && x.Amount > 0),
                    "The refreshed 300000-per-second three-second single target DoT must tick");
                Assert.IsTrue(events.Any(x => x.SkillName == "화재" &&
                    x.Type == CharacterCombatEventType.SkillDamage && x.Amount > 0),
                    "The source 42500/0.27 second fire must reach the shared damage ledger");
                Assert.IsTrue(events.Any(x => x.SkillName == "화재" &&
                    x.Type == CharacterCombatEventType.EffectDeferred &&
                    x.Detail.Contains("추가 방어 감소10")),
                    "Unknown secondary -10 stacking must be reported, not silently added");
            }
        }

        [Test]
        public void BonneyAgeRootAndStacksDoNotRemainInPreviousLaneAfterDispatch()
        {
            using (var live = Create("TR-21", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 240 && !events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "세월관통"); tick++)
                    events.AddRange(live.Battle.Advance(.1));
                var root = events.FirstOrDefault(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "세월관통");
                Assert.IsNotNull(root);
                Assert.GreaterOrEqual(live.Combat.PracticeBonneyAgeStacksOf(
                    live.ActorId, root.EnemyId), 40);
                Assert.IsTrue(live.Combat.IsSourceModelPullHeld(root.EnemyId));
                var lanes = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lanes);
                lanes.DeployedIds.Add(live.ActorId);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeBonneyAgeStacksOf(
                    live.ActorId, root.EnemyId));
                Assert.IsFalse(live.Combat.IsSourceModelPullHeld(root.EnemyId));
                Assert.IsFalse(live.Combat.PracticeBonneyScaleReduced(root.EnemyId));
            }
        }

        [Test]
        public void RayleighClonePulseUsesActualCombatDamageAndProcRoll()
        {
            using (var live = Create("I-03", true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "분신"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.Detail.Contains("clone-proxy") && x.Amount > 0));
            }
            using (var live = Create("I-03", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.Detail.Contains("clone-proxy")));
            }
        }

        [Test]
        public void ShinobuCloneUsesPinnedIndependentObjectDiceAndLifetime()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var first = map.FindMapObjectAttack("h085");
            var second = map.FindMapObjectAttack("h0A9");
            Assert.AreEqual(.73, first.AttackCooldownSeconds.Value);
            Assert.AreEqual(first.AttackCooldownSeconds, second.AttackCooldownSeconds);
            Assert.AreEqual(first.MinimumBaseDamage, second.MinimumBaseDamage);
            Assert.AreEqual(first.MaximumBaseDamage, second.MaximumBaseDamage);
            using (var live = Create("L-21", true, -4900, 5400))
            {
                int owned = live.Combat.CharacterCount;
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeSummonsOf(live.ActorId).Count == 0; tick++)
                    events.AddRange(live.Battle.Advance(.2));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "인술 분신"));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
                var summons = live.Combat.PracticeSummonsOf(live.ActorId);
                Assert.IsTrue(summons.Count > 0);
                Assert.IsTrue(summons.All(x => x.OwnerId == live.ActorId &&
                    x.Name == "분신" && x.SourceUnitRawcode == "h085" &&
                    x.HasIndependentCombat && x.SpawnX != -4900 &&
                    x.SpawnY != 5400));
                Assert.IsTrue(events.Any(x => x.SkillName == "인술 분신" &&
                    x.Detail.Contains("반경119")));
                events.AddRange(live.Battle.Advance(1.1));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:") && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인술 분신 범위 마법" && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "분신 급습" && x.Amount > 0));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }

        [TestCase("L-08", "외과수술(W)", CombatEffectType.AttackSpeedBuff, 150d, 12d)]
        [TestCase("H-08", "선택 아군 가속", CombatEffectType.AttackSpeedBuff, 150d, 12d)]
        [TestCase("T-03", "과열", CombatEffectType.AttackSpeedBuff, 150d, 12d)]
        [TestCase("R-31", "호르몬 버프", CombatEffectType.AttackPowerBuff, 35d, 25d)]
        [TestCase("R-38", "로어", CombatEffectType.AttackPowerBuff, 20d, 15d)]
        [TestCase("RN-11", "판단력(Q)", CombatEffectType.AttackPowerBuff, 40d, 50d)]
        [TestCase("H-18", "체력회복 공격력 버프", CombatEffectType.AttackPowerBuff, 45d, 50d)]
        public void BatchManualSupportUsesPinnedTooltipAndOneCooldown(
            string unitId, string name, CombatEffectType kind,
            double amount, double duration)
        {
            using (var live = Create(unitId, false, -4900, 5400,
                withSourceResources: true))
            {
                long ally = live.Combat.SpawnCharacter("C-01", -4900, 5400);
                live.Battle.Advance(1);
                Assert.AreEqual(1, live.Combat.AutoCastSourceManualSupportAndBurst(),
                    unitId + " automatic mobile manual cast");
                long target = unitId == "H-18" ? live.ActorId : ally;
                Assert.IsTrue(live.Combat.UnitStatusOf(target).Snapshot().Any(x =>
                    x.Type == kind && x.Amount == amount &&
                    x.DurationSeconds == duration &&
                    x.SourceId.StartsWith("practice-source-manual:")), name);
                Assert.AreEqual(0, live.Combat.AutoCastSourceManualSupportAndBurst(),
                    "manual buff must not cast twice before cooldown");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsFalse(live.Combat.UnitStatusOf(ally).Snapshot().Any(x =>
                    x.SourceId.StartsWith("practice-source-manual:")),
                    "caster removal releases recipient buffs");
            }
        }

        [TestCase("RN-03", "폭렬 익스플로전")]
        [TestCase("RN-04", "저격(Q)")]
        public void BatchManualAreaAndSniperProduceOriginalTooltipDamageEvents(
            string unitId, string skillName)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                int casts = 0;
                for (int tick = 0; tick < 20 && casts == 0; tick++)
                {
                    live.Battle.Advance(1);
                    casts += live.Combat.AutoCastSourceManualSupportAndBurst();
                }
                Assert.AreEqual(1, casts, skillName);
                Assert.AreEqual(0, live.Combat.AutoCastSourceManualSupportAndBurst(),
                    "Q cooldown is applied before subsequent frames");
            }
        }

        [Test]
        public void BuggyIndependentAllyBuffProcsWithoutDuplicateNativeDamageRoll()
        {
            using (var live = Create("E-05", true, -4900, 5400))
            {
                long ally = live.Combat.SpawnCharacter("C-01", -4900, 5400);
                var seen = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 50 && !seen.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "전투 아군 공격력 버프"); tick++)
                    seen.AddRange(live.Battle.Advance(.25));
                Assert.IsTrue(seen.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "전투 아군 공격력 버프"));
                Assert.IsTrue(live.Combat.UnitStatusOf(ally).Snapshot().Any(e =>
                    e.SourceId.StartsWith("practice-source-buggy:") &&
                    e.Type == CombatEffectType.AttackPowerBuff &&
                    e.Amount == 33 && e.DurationSeconds == 4.5));
            }
        }
    }
}
