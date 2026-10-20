using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public class CombatStatusEngineTests
    {
        // 테스트 값은 원작 유닛 수치가 아니라 엔진 동작 검증용이다.
        [Test]
        public void TriggeredStunExpiresWhileAuraPersists()
        {
            var engine = new CombatStatusEngine();
            engine.Apply(new CombatEffect("test-aura", CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura, 10, 0));
            engine.Apply(new CombatEffect("test-stun", CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, 2));
            Assert.IsTrue(engine.IsStunned());
            engine.Advance(2);
            Assert.IsFalse(engine.IsStunned());
            Assert.IsTrue(engine.HasActiveEffect(CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura));
        }

        [Test]
        public void RemovingSourceRemovesOnlyItsEffects()
        {
            var engine = new CombatStatusEngine();
            engine.Apply(new CombatEffect("test-a", CombatEffectType.MovementSlowPercent,
                CombatEffectOrigin.Aura, 10, 0));
            engine.Apply(new CombatEffect("test-b", CombatEffectType.MovementSlowPercent,
                CombatEffectOrigin.TriggeredSkill, 15, 3));
            engine.RemoveSource("test-a");
            Assert.IsFalse(engine.HasActiveEffect(CombatEffectType.MovementSlowPercent, CombatEffectOrigin.Aura));
            Assert.IsTrue(engine.HasActiveEffect(CombatEffectType.MovementSlowPercent, CombatEffectOrigin.TriggeredSkill));
        }

        [Test]
        public void RejectsInvalidDurationAndTime()
        {
            Assert.Throws<ArgumentException>(() => new CombatEffect("test", CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatusEngine().Advance(-1));
        }
    }
}
