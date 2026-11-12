using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    // Checks the shared lifecycle rather than reimplementing individual skills.
    public sealed class CharacterCombatLifecycleCleanupTests
    {
        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"테스트\",\"roleTags\":[]}]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{\"unitId\":\"TEST-01\",\"sourceUrl\":\"TEST_FIXTURE\",\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{},\"passives\":[],\"skills\":[]}]}");
            var missing = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills, missing, missing,
                new SeededNonOriginalProcRandom(1));
            world.Start();
            world.BeginWave(1);
            return world;
        }

        // Arrange exact lease/counter boundary state. Verify public unit removal
        // against the live ally status, not only a private dictionary mutation.
        private static T Field<T>(object instance, string name) where T : class
        {
            var field = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected state field " + name);
            return (T)field.GetValue(instance);
        }

        [Test]
        public void RemovingBuffSourceClearsSurvivingAllyAuraCooldownAndFormCounters()
        {
            var world = World();
            long owner = world.SpawnCharacter("TEST-01", 0, 0);
            long ally = world.SpawnCharacter("TEST-01", 10, 0);
            string source = "practice-source-manual:" + owner + ":L-33:" + ally;
            var statuses = Field<Dictionary<string, CombatStatusEngine>>(world, "unitStatuses");
            var leases = Field<Dictionary<string, long>>(world, "sourceManualRecipients");
            var ready = Field<Dictionary<long, double>>(world, "sourceManualReadyAt");
            var king = Field<HashSet<long>>(world, "sourceModelKingExecutionUsed");
            var bonney = Field<Dictionary<string, int>>(world, "sourceModelBonneyAge");
            var allyStatus = statuses[ally.ToString(CultureInfo.InvariantCulture)];
            allyStatus.Apply(new CombatEffect(source, CombatEffectType.AttackPowerBuff,
                CombatEffectOrigin.Aura, 19, 0));
            leases[source] = ally;
            ready[owner] = 200;
            king.Add(owner);
            bonney[owner + ":enemy"] = 1;

            world.RemoveCharacter(owner);

            Assert.AreEqual(0, allyStatus.ActiveCount,
                "A removed caster may not leave an indefinite buff on a living ally.");
            Assert.IsFalse(leases.ContainsKey(source));
            Assert.IsFalse(ready.ContainsKey(owner));
            Assert.IsFalse(king.Contains(owner));
            Assert.IsFalse(bonney.ContainsKey(owner + ":enemy"));
        }

        [Test]
        public void RemovingBuffRecipientReleasesLeaseButPreservesLivingCasterCooldown()
        {
            var world = World();
            long owner = world.SpawnCharacter("TEST-01", 0, 0);
            long ally = world.SpawnCharacter("TEST-01", 10, 0);
            var leases = Field<Dictionary<string, long>>(world, "sourceManualRecipients");
            var ready = Field<Dictionary<long, double>>(world, "sourceManualReadyAt");
            string source = "practice-source-manual:" + owner + ":RN-11:" + ally;
            leases[source] = ally;
            ready[owner] = 25;

            world.RemoveCharacter(ally);

            Assert.IsFalse(leases.ContainsKey(source));
            Assert.AreEqual(25, ready[owner],
                "Removing a recipient does not reset the living owner's cooldown.");
        }

        [Test]
        public void EndingDecodeCancelsPendingHitsBeforeMainLaneResumes()
        {
            var world = World();
            long owner = world.SpawnCharacter("TEST-01", 0, 0);
            world.SpawnEnemy("line", 100000, CharacterEnemyKind.Line, 0, 0, 0);
            var executor = Field<CharacterSkillEffectExecutor>(world, "effectExecutor");
            var queue = Field<CombatTimedEffectQueue>(executor, "timedEffects");

            world.SetOriginalPoneglyphChanneling(owner, true);
            world.SetOriginalPoneglyphChanneling(owner, true); // repeated notice
            Assert.IsTrue(world.IsOriginalPoneglyphChanneling(owner));
            int fired = 0;
            queue.Schedule(owner.ToString(CultureInfo.InvariantCulture), "line",
                .5, () => fired++);

            world.SetOriginalPoneglyphChanneling(owner, false);
            Assert.IsFalse(world.IsOriginalPoneglyphChanneling(owner));
            Assert.AreEqual(0, queue.PendingCount);
            world.Step(1);
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void RemovingActorCancelsBothOutgoingAndIncomingScheduledEffects()
        {
            var world = World();
            long owner = world.SpawnCharacter("TEST-01", 0, 0);
            world.SpawnEnemy("line", 100000, CharacterEnemyKind.Line, 0, 0, 0);
            var queue = Field<CombatTimedEffectQueue>(
                Field<CharacterSkillEffectExecutor>(world, "effectExecutor"),
                "timedEffects");
            string actor = owner.ToString(CultureInfo.InvariantCulture);
            int fired = 0;
            queue.Schedule(actor, "line", .5, () => fired++);
            queue.Schedule("another-caster", actor, .7, () => fired++);
            Assert.AreEqual(2, queue.PendingCount);

            world.RemoveCharacter(owner);

            Assert.AreEqual(0, queue.PendingCount);
            world.Step(1);
            Assert.AreEqual(0, fired);
        }
    }
