using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323UtaSharkBatch53Tests
    {
        private static CombatEffect Effect(string source, CombatEffectType kind) =>
            new CombatEffect(source, kind, CombatEffectOrigin.Aura, 55, 0);

        [Test]
        public void NativeSharkAndBulletPowerAreAcceptedByBasicAttackResolver()
        {
            var sources = new[]
            {
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.AttackPowerBuff),
                Effect("practice-tooltip:armada:A0WQ:7", CombatEffectType.AttackPowerBuff)
            };
            foreach (var effect in sources)
                Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(effect));
        }

        [Test]
        public void ExistingNativeDefensiveStatesDoNotDisableOwnBasicAttack()
        {
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.ArmorBuff)));
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:ET12:A11L:22", CombatEffectType.ArmorBuff)));
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:R00R:A11B:9", CombatEffectType.MovementSpeedBuff)));
        }

        [Test]
        public void UnrelatedStatusesCannotSlipThroughTheAttackAllowlist()
        {
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.AttackSpeedBuff)));
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:unknown:13", CombatEffectType.AttackPowerBuff)));
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:R00R:A11B:9", CombatEffectType.AttackPowerBuff)));
        }

        [Test]
        public void UtaTraitUsesDistinctDurablePerActorReceipt()
        {
            Assert.AreEqual("source-native:uta:unlock:15",
                Original323SourceNativeActionKeys.UtaUnlockAction(15));
            Assert.AreEqual("source:native-trait:A0D2:15",
                Original323SourceNativeActionKeys.UtaUnlockResource(15));
            Assert.AreNotEqual(Original323SourceNativeActionKeys.UtaUnlockResource(15),
                Original323SourceNativeActionKeys.UtaUnlockResource(16));
        }
    }
}
