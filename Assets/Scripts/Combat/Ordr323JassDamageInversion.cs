using System;

namespace Ordr.Mobile.Combat
{
    // 추출한 war3map.j의 BdV(8434~8457행) 정확한 *이벤트 피해 역산* 경로.
    // 공격자의 최종 피해 계산과 다르며, 일반 증폭 효과 합산에는 사용 금지.
    public static class Ordr323JassDamageInversion
    {
        public static double ReverseEventDamage(double observedDamage,
            double defenderArmor, bool hasB06J, bool hasB04W, bool hasB07S)
        {
            if (double.IsNaN(observedDamage) || double.IsInfinity(observedDamage))
                throw new ArgumentOutOfRangeException(nameof(observedDamage));
            double result = observedDamage;
            // 원본 JASS if -> elseif -> elseif: 동시에 있으면 첫 버프 하나만 적용.
            if (hasB06J) result /= 1.1;
            else if (hasB04W) result /= 1.25;
            else if (hasB07S) result /= 1.2;

            double armorMultiplier = Ordr323MapDamageMath.ArmorDamageMultiplier(defenderArmor);
            if (armorMultiplier > 0) result /= armorMultiplier;
            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new OverflowException("JASS 역산 결과가 유효하지 않습니다.");
            return result;
        }
    }
}
