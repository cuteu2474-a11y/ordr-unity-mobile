using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalHelperCowAutoAreaTests
    {
        private static MobileEnemySnapshot Enemy(string id, double x, double y,
            CharacterEnemyKind kind = CharacterEnemyKind.Line) =>
            new MobileEnemySnapshot(id, kind, 1000L, 1000L, x, y);

        private sealed class World : IOriginalHelperCowAreaSpellWorld
        {
            public OriginalHelperCowAreaCaster Caster =
                new OriginalHelperCowAreaCaster("h08A", 0, 0, true);
            public IReadOnlyList<OriginalHelperCowReadyAreaAbility> Spells =
                new[] { new OriginalHelperCowReadyAreaAbility("A0BZ", 1, 550) };
            public bool Allowed = true;
            public bool CastSucceeds = true;
            public int Casts;
            public double LastX, LastY;
            public bool TryGetLiveCaster(out OriginalHelperCowAreaCaster caster)
            { caster = Caster; return caster != null; }
            public IReadOnlyList<OriginalHelperCowReadyAreaAbility> AvailableReadyAbilities() => Spells;
            public bool IsVerifiedEligibleTarget(string ability, MobileEnemySnapshot enemy) =>
                enemy.Kind == CharacterEnemyKind.Line ||
                enemy.Kind == CharacterEnemyKind.Boss ||
                enemy.Kind == CharacterEnemyKind.Enraged;
            public bool IsVerifiedCastPointAllowed(string ability, double x, double y) => Allowed;
            public bool TryCommitCast(string ability, double x, double y)
            {
                Casts++;
                LastX = x; LastY = y;
                return CastSucceeds;
            }
        }

        [Test]
        public void PinnedJassRadiusDamageAndA04RIncrementsAreNotInvented()
        {
            Assert.AreEqual(450, OriginalHelperCowAreaEvidence.VerifiedRadius("A0BZ"));
            Assert.AreEqual(625, OriginalHelperCowAreaEvidence.VerifiedRadius("A07T"));
            Assert.AreEqual(7000000L, OriginalHelperCowAreaEvidence.JassDamage("A0BZ", 1));
            Assert.AreEqual(10000000L, OriginalHelperCowAreaEvidence.JassDamage("A0BZ", 2));
            Assert.AreEqual(1000000L, OriginalHelperCowAreaEvidence.JassDamage("A07T", 1));
            Assert.AreEqual(2000000L, OriginalHelperCowAreaEvidence.JassDamage("A07T", 2));
            Assert.AreEqual(0, OriginalHelperCowAreaEvidence.JassA04RLevelIncrease("A0BZ", 2));
            Assert.AreEqual(5, OriginalHelperCowAreaEvidence.JassA04RLevelIncrease("A07T", 1));
            Assert.AreEqual(8, OriginalHelperCowAreaEvidence.JassA04RLevelIncrease("A07T", 2));
            Assert.Throws<ArgumentException>(() =>
                OriginalHelperCowAreaEvidence.VerifiedRadius("A05D"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OriginalHelperCowReadyAreaAbility("A07T", 1, 0));
        }

        [Test]
        public void PicksIntersectionRatherThanSingleEnemyCenter()
        {
            // Two mobs are 800 apart: neither center hits both within R=450.
            // A midpoint does. Real original cast range is separately 1000.
            var enemies = new[] { Enemy("b", 800, 0), Enemy("a", 0, 0) };
            var result = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 1000, 450, enemies, e => true, (x,y) => true);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.HitCount);
            Assert.LessOrEqual(Math.Sqrt(result.X*result.X+result.Y*result.Y), 1000.001);
            foreach (var e in enemies)
                Assert.LessOrEqual(Math.Sqrt(
                    (e.X-result.X)*(e.X-result.X)+(e.Y-result.Y)*(e.Y-result.Y)),
                    450.001);
            CollectionAssert.AreEqual(new[]{"a","b"}, result.EnemyIds);
        }

        [Test]
        public void MobileAreaCastCanCenterDirectlyOnEnemyBeyondOriginalCastRange()
        {
            // In the mobile port the cast range is unlimited, but the
            // 100-unit splash around the chosen point is unchanged.
            var one = new[]{Enemy("far", 140, 0)};
            var result = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 50, 100, one, e => true, (x,y) => true);
            Assert.IsNotNull(result);
            Assert.AreEqual(140, result.X, 0.00001);
            Assert.AreEqual(1, result.HitCount);
        }

        [Test]
        public void UnlimitedCastRangeStillRespectsOriginalAreaRadius()
        {
            var enemies = new[] {Enemy("near", 100000, 0), Enemy("far", 101500, 0)};
            var target = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 50, 450, enemies, e => true, (x,y) => true);
            Assert.IsNotNull(target);
            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(100000, target.X, 0.00001);
        }

        [Test]
        public void OriginalLaneEligibilityExcludesStoryAndUnauthorizedUnits()
        {
            var enemies = new[] {
                Enemy("line", 100, 0), Enemy("story", 110, 0, CharacterEnemyKind.Story)
            };
            var result = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 1000, 450, enemies,
                e => e.Kind != CharacterEnemyKind.Story,
                (x,y) => true);
            CollectionAssert.AreEqual(new[]{"line"}, result.EnemyIds);
        }

        [Test]
        public void BossWinsTieButMoreTargetsBeatOneBoss()
        {
            var enemies = new[] {
                Enemy("regular", 100, 0), Enemy("boss", 850, 0, CharacterEnemyKind.Boss)
            };
            var best = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 2000, 200, enemies, e => true, (x,y) => true);
            CollectionAssert.AreEqual(new[]{"boss"}, best.EnemyIds);

            var three = new[]{
                Enemy("r1",100,0),Enemy("r2",120,0),
                Enemy("boss",900,0,CharacterEnemyKind.Boss)
            };
            var larger = OriginalHelperCowAreaTargeting.Choose(
                0, 0, 2000, 100, three, e => true, (x,y) => true);
            Assert.AreEqual(2, larger.HitCount);
            Assert.AreEqual(0, larger.BossCount);
        }

        [Test]
        public void DeterministicIndependentOfEnemyEnumerationOrder()
        {
            var forward = new[] {Enemy("c",400,0),Enemy("a",0,0),Enemy("b",200,200)};
            var backward = new[] {forward[2], forward[1], forward[0]};
            var a = OriginalHelperCowAreaTargeting.Choose(
                0,0,1000,300,forward,e=>true,(x,y)=>true);
            var b = OriginalHelperCowAreaTargeting.Choose(
                0,0,1000,300,backward,e=>true,(x,y)=>true);
            Assert.AreEqual(a.X,b.X,0.00001);
            Assert.AreEqual(a.Y,b.Y,0.00001);
            CollectionAssert.AreEqual(a.EnemyIds,b.EnemyIds);
        }

        [Test]
        public void DeniedLocationsOrZeroTargetsNeverCast()
        {
            Assert.IsNull(OriginalHelperCowAreaTargeting.Choose(
                0,0,200,50,new[]{Enemy("a",0,0)},e=>true,(x,y)=>false));
            Assert.IsNull(OriginalHelperCowAreaTargeting.Choose(
                0,0,200,50,new[]{Enemy("a",0,0)},e=>false,(x,y)=>true));
        }

        [Test]
        public void AutomaticStepCallsRealPortOnlyWithActiveGameAndApprovedTarget()
        {
            var game = new GameSession();
            var world = new World();
            double time = 1;
            IReadOnlyList<MobileEnemySnapshot> enemies =
                new[]{Enemy("a",400,0),Enemy("b",450,0)};
            var auto = new OriginalHelperCowAutoAreaCasting(
                game,()=>time,()=>enemies,world);
            Assert.IsFalse(auto.Tick());
            Assert.AreEqual(0,world.Casts);
            game.Start();
            Assert.IsTrue(auto.Tick());
            Assert.AreEqual(1,world.Casts);
            Assert.IsFalse(auto.Tick()); // no repeated cast in same 0.15s scan
            time = 1.16;
            world.Allowed = false;
            Assert.IsFalse(auto.Tick());
            Assert.AreEqual(1,world.Casts);
            time = 1.35;
            world.Allowed = true;
            world.Caster = new OriginalHelperCowAreaCaster("h003",0,0,true);
            Assert.IsFalse(auto.Tick());
            Assert.AreEqual(1,world.Casts);
            time = 1.60;
            world.Caster = new OriginalHelperCowAreaCaster("h08A",0,0,true);
            world.Spells = new OriginalHelperCowReadyAreaAbility[0]; // insufficient mana/cooldown/lock
            Assert.IsFalse(auto.Tick());
            Assert.AreEqual(1,world.Casts);
        }
    }
}
