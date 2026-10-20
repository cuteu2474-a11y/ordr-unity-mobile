using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 원작 RNG가 확정되기 전 초기 게임 상태/월드 UI 확인용으로만 사용하는 PRNG.
    // 일반 공격·스킬 수치의 원작 검증을 대신하지 않는다.
    public sealed class SeededNonOriginalProcRandom : ICombatProcRandom
    {
        private readonly DeterministicRandom rng;

        public SeededNonOriginalProcRandom(uint seed)
        {
            rng = new DeterministicRandom(seed);
        }

        public int Next(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            // 32-bit 난수를 범위로 사상. 원본 워크래프트 난수 분포와 동일하지 않다.
            return (int)(((ulong)rng.NextUInt() * (uint)exclusiveUpperBound) >> 32);
        }
    }

    // 원작 피해 공식을 아직 갖추지 않았을 때 명시적인 보류를 구현한 정책.
    // 0 피해나 임의 피해를 반환하지 않는다.
    public sealed class RejectUnverifiedDamageFormula :
        IOriginalDamageResolver, IVerifiedCharacterSkillDamageResolver
    {
        public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target)
        {
            throw new NotSupportedException("ORDR 2.323 원본 평타 피해/방어 계산식 미등록.");
        }

        public long ResolveDamage(CharacterSkillDamageRequest request)
        {
            throw new NotSupportedException("ORDR 2.323 원본 스킬 피해/방어 계산식 미등록: " +
                request.Skill.Name + "/" + request.Effect.Type);
        }
    }
}
