using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalMapCommonAttackPreviewTests
    {
        private const string UnitFixture =
            "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
            "{\"id\":\"C-01\",\"grade\":\"흔함\",\"name\":\"루피\",\"roleTags\":[]}," +
            "{\"id\":\"C-02\",\"grade\":\"흔함\",\"name\":\"조로\",\"roleTags\":[]}]}";

        private const string MapFixture =
            "{\"format\":\"ordr-source-map-base-stats-v1\"," +
            "\"gameVersion\":\"S2 2.323[C]\"," +
            "\"sourceW3xSha256\":\"0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c\"," +
            "\"catalogEntries\":[" +
            "{\"catalogId\":\"C-01\",\"catalogName\":\"루피\",\"grade\":\"흔함\"," +
            "\"mappingStatus\":\"verified-common-map-identity\",\"originalRawcode\":\"h003\"," +
            "\"sourceMapName\":\"몽키.D.루피 - 흔함\",\"combatParityVerified\":false," +
            "\"attack1\":{\"minimumBaseDamage\":29,\"maximumBaseDamage\":31," +
            "\"attackCooldownSeconds\":1.02,\"attackPointSeconds\":0.35}," +
            "\"baseStats\":{}}," +
            "{\"catalogId\":\"C-02\",\"catalogName\":\"조로\",\"grade\":\"흔함\"," +
            "\"mappingStatus\":\"verified-common-map-identity\",\"originalRawcode\":\"h002\"," +
            "\"sourceMapName\":\"롤로노아 조로 - 흔함\",\"combatParityVerified\":false," +
            "\"attack1\":{\"minimumBaseDamage\":31,\"maximumBaseDamage\":33," +
            "\"attackCooldownSeconds\":1.03,\"attackPointSeconds\":0.25}," +
            "\"baseStats\":{}}]}";

        private static OriginalMapBaseStatCatalog Source()
        {
            return OriginalMapBaseStatCatalog.FromJson(MapFixture,
                CharacterUnitCatalog.FromJson(UnitFixture));
        }

        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(UnitFixture);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var missing = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills, missing, missing,
                new SeededNonOriginalProcRandom(1));
            world.ConfigureOriginalMapCommonAttackPreview(Source());
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static int CountPreview(System.Collections.Generic.IReadOnlyList<CharacterCombatEvent> events)
        {
            int count = 0;
            foreach (var e in events)
                if (e.Type == CharacterCombatEventType.AttackPreview) count++;
            return count;
        }

        [Test]
        public void CommonPreviewSourceUsesMapAttackPointAndCooldown()
        {
            var clock = new OriginalMapCommonAttackPreview(Source());
            Assert.IsTrue(clock.TryTrack(1, "C-01", 7.0));
            Assert.IsTrue(clock.TryTrack(2, "C-02", 7.0));
            Assert.IsFalse(clock.TryTrack(3, "U-01", 7.0));
            Assert.AreEqual(7.35, clock.NextAt(1).Value, 0.000001);
            Assert.AreEqual(7.25, clock.NextAt(2).Value, 0.000001);
            OriginalMapCharacterBasics evidence;
            Assert.IsFalse(clock.TryCommit(1, 7.34, out evidence));
            Assert.IsTrue(clock.TryCommit(1, 7.35, out evidence));
            Assert.AreEqual("h003", evidence.OriginalRawcode);
            Assert.AreEqual(8.37, clock.NextAt(1).Value, 0.000001);
        }

        [Test]
        public void AutomaticTargetAndCadenceEmitPreviewWithoutModifyingEnemyHealth()
        {
            var world = World();
            long id = world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("far", 1000, CharacterEnemyKind.Line, 100, 0, 0);
            world.SpawnEnemy("near", 1000, CharacterEnemyKind.Line, 10, 0, 0);
            world.SpawnEnemy("story", 1000, CharacterEnemyKind.Story, 1, 0, 0);
            Assert.AreEqual(1, world.PreviewAttackers);
            Assert.IsFalse(world.HasVerifiedAttack(id));

            Assert.AreEqual(0, CountPreview(world.Step(0.34)));
            var first = world.Step(0.01);
            Assert.AreEqual(1, CountPreview(first));
            bool selectedNear = false;
            foreach (var item in first)
                if (item.Type == CharacterCombatEventType.AttackPreview &&
                    item.ActorId == id.ToString() && item.EnemyId == "near" &&
                    item.Amount == 0 && item.Detail.Contains("29-31"))
                    selectedNear = true;
            Assert.IsTrue(selectedNear);
            Assert.AreEqual(0, CountPreview(world.Step(1.01)));
            Assert.AreEqual(1, CountPreview(world.Step(0.02)));
            Assert.AreEqual(1000L, world.EnemyOf("near").Health);
            Assert.AreEqual(1000L, world.EnemyOf("story").Health);
        }

        [Test]
        public void VerifiedAttackBindingReplacesPreviewAndRemovalCleansUpClock()
        {
            var world = World();
            long luffy = world.SpawnCharacter("C-01", 0, 0);
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            Assert.AreEqual(2, world.PreviewAttackers);

            world.BindVerifiedBasicAttack(luffy,
                new BasicAttackSpec(30, 1.02, DamageSchool.Physical,
                    DamageTraits.None), 0, "h003");
            Assert.AreEqual(1, world.PreviewAttackers);
            Assert.IsTrue(world.HasVerifiedAttack(luffy));
            world.RemoveCharacter(zoro);
            Assert.AreEqual(0, world.PreviewAttackers);
        }
    }
}
