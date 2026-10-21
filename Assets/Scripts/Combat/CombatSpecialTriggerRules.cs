using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 조건의 실제 판단(카운터/변신/아이템/특성/캐릭터 연계)은
    // 2.323[C]에서 확인한 룰 모듈이 담당한다.
    public interface IOriginalSpecialSkillEventRules
    {
        bool CanActivate(CatalogUnit caster, ImportedCombatSkill skill,
            CharacterEnemyKind targetKind, double combatTime);
    }
}
