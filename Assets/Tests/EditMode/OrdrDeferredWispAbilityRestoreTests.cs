using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrDeferredWispAbilityRestoreTests
    {
        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"테스트\",\"roleTags\":[]}]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{\"unitId\":\"TEST-01\",\"sourceUrl\":\"TEST_FIXTURE\",\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{},\"passives\":[],\"skills\":[]}]}");
            var missing = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills, missing,
                missing, new SeededNonOriginalProcRandom(230));
            world.Start();
            return world;
        }

        [Test]
        public void RestoresOnlyExistingUnitOwnerWithoutApplyingNativeAbility()
        {
            var combat = World();
            long first = combat.SpawnCharacter("TEST-01", 0, 0);
            long second = combat.SpawnCharacter("TEST-01", 10, 10);
            var ledger = new Original323WispDeferredAbilityRegistry(combat);
            Assert.IsTrue(ledger.IsAttachedTo(combat));
            Assert.IsFalse(ledger.IsAttachedTo(World()),
                "새 게임에서 이전 로스터와 능력 장부를 재사용하면 안 됩니다.");
            Assert.IsTrue(ledger.TryDeferOriginalAbility(first, "A0YE"));
            Assert.IsTrue(ledger.TryDeferOriginalAbility(second, "A0YZ"));
            var state = ledger.CaptureState();
            Assert.AreEqual(2, state.Pending.Count);
            Assert.AreEqual(first, state.Pending[0].InstanceId);
            var restored = new Original323WispDeferredAbilityRegistry(combat);
            restored.RestoreState(state);
            Assert.IsTrue(restored.HasAbility(first, "A0YE"));
            Assert.IsTrue(restored.HasAbility(second, "A0YZ"));
            Assert.AreEqual(2, restored.PendingCount);
            Assert.Throws<InvalidOperationException>(() =>
                restored.RestoreState(state));
            Assert.AreEqual(2, restored.PendingCount);
        }

        [Test]
        public void RejectsForgeryDuplicateAndMissingUnitWithoutMutatingNewLedger()
        {
            var combat = World();
            long owner = combat.SpawnCharacter("TEST-01", 0, 0);
            var state = new Original323WispDeferredAbilityRegistryState();
            state.Pending.Add(new Original323DeferredAbilityCheckpoint {
                InstanceId = owner, AbilityRawcode = "A0YE"
            });
            Original323WispDeferredAbilityRegistry.ValidateState(state,
                combat.Roster.CaptureState());
            var restored = new Original323WispDeferredAbilityRegistry(combat);
            state.Pending.Add(new Original323DeferredAbilityCheckpoint {
                InstanceId = owner, AbilityRawcode = "A0YZ"
            });
            Assert.Throws<FormatException>(() => restored.RestoreState(state));
            Assert.AreEqual(0, restored.PendingCount);
            state.Pending[1].InstanceId = owner + 1;
            Assert.Throws<FormatException>(() => restored.RestoreState(state));
            state.Pending.RemoveAt(1);
            state.Pending[0].AbilityRawcode = "A0XX";
            Assert.Throws<FormatException>(() => restored.RestoreState(state));
            Assert.AreEqual(0, restored.PendingCount);
        }
    }
}
