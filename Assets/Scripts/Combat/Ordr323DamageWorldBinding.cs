using System;

namespace Ordr.Mobile.Combat
{
    // 입력 데이터를 통과시키는 명시적 연동 팩토리.
    // 원작 입력값이 검증된 경우에만 CharacterCombatRuntime에 피해 정책을 연결.
    // 이 메서드만 호출해서 유닛 공격 수치/원본 스킬 전부가 준비되지는 않는다.
    public static class Ordr323DamageWorldBinding
    {
        public static CharacterCombatRuntime Create(
            Ordr.Mobile.Data.CharacterUnitCatalog units,
            Ordr.Mobile.Data.CharacterSkillCatalog skills,
            IOrdr323VerifiedHitSource hits,
            IOrdr323VerifiedModifierRules modifiers,
            IOrdr323VerifiedDamageQuantization quantizer,
            ICombatProcRandom random)
        {
            CharacterCombatRuntime world = null;
            var rules = new Ordr323OriginalDamageRules(hits, modifiers, quantizer);
            var adapter = new CombatDamageRulesAdapter(rules,
                enemyId => world == null ? null : world.StatusOf(enemyId),
                enemyId => world == null ? 0 : world.ArmorBreakStacksOf(enemyId));
            world = new CharacterCombatRuntime(units, skills, adapter, adapter, random);
            return world;
        }
    }
}
