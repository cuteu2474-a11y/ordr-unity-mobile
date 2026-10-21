using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 피해 종류별 원작 배율/방어/저항/특성 강화/반올림 방식을 확인한 구현체를
    // 외부에서 주입한다. 이 파일은 피해량을 임의로 결정하는 기본값을 갖지 않는다.
    public interface IOriginalCombatDamageRules
    {
        long ResolveBasic(BasicDamageRequest request, CombatEnemy target,
            IReadOnlyList<CombatEffect> targetEffects, int armorBreakStacks);

        long ResolveSkill(CharacterSkillDamageRequest request,
            IReadOnlyList<CombatEffect> targetEffects);
    }

    // 자원/스킬 쿨타임을 소비하기 *전* 사용할 수 없는 피해식을 차단.
    // 실제 피해 타깃·상태를 포함한 최종 실행 승인과는 별도로 관리한다.
    public interface IOriginalSkillDamagePreflight
    {
        bool CanResolveSkillDamage(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect);
    }

    // 원본 피해 공식을 연결하는 공통 입구. 모든 스킬/평타가 동일한 실시간
    // 대상 상태를 참조하게 하며, 미연결 수식은 예외로 정직하게 보류한다.
    public sealed class CombatDamageRulesAdapter :
        IOriginalDamageResolver, IVerifiedCharacterSkillDamageResolver,
        IOriginalSkillDamagePreflight
    {
        private readonly IOriginalCombatDamageRules originalRules;
        private readonly Func<string, CombatStatusEngine> getStatus;
        private readonly Func<string, int> getArmorBreakStacks;

        public CombatDamageRulesAdapter(IOriginalCombatDamageRules originalRules,
            Func<string, CombatStatusEngine> statusLookup,
            Func<string, int> armorBreakLookup)
        {
            this.originalRules = originalRules ??
                throw new ArgumentNullException(nameof(originalRules));
            getStatus = statusLookup ??
                throw new ArgumentNullException(nameof(statusLookup));
            getArmorBreakStacks = armorBreakLookup ??
                throw new ArgumentNullException(nameof(armorBreakLookup));
        }

        public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target)
        {
            if (request == null || target == null)
                throw new ArgumentNullException(request == null ? nameof(request) : nameof(target));
            var status = getStatus(target.Id);
            int stacks = getArmorBreakStacks(target.Id);
            long damage = originalRules.ResolveBasic(request, target,
                status == null ? EmptyEffects() : status.Snapshot(), stacks);
            if (damage < 0)
                throw new InvalidOperationException("원작 평타 피해 공식이 음수를 반환했습니다.");
            return damage;
        }

        public long ResolveDamage(CharacterSkillDamageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var status = getStatus(request.Target.Id);
            long damage = originalRules.ResolveSkill(request,
                status == null ? EmptyEffects() : status.Snapshot());
            if (damage < 0)
                throw new InvalidOperationException("원작 스킬 피해 공식이 음수를 반환했습니다.");
            return damage;
        }

        public bool CanResolveSkillDamage(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect)
        {
            var verified = originalRules as IOriginalSkillDamagePreflight;
            // 보수적 차단: 이 어댑터에 피해 검증기가 없으면
            // 액티브 스킬의 자원소모를 허용하지 않는다.
            return verified != null && verified.CanResolveSkillDamage(
                profile, skill, effect);
        }

        private static IReadOnlyList<CombatEffect> EmptyEffects()
        {
            return new List<CombatEffect>().AsReadOnly();
        }
    }
}
