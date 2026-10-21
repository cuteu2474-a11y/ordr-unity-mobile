using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 원작 스킬의 일반 타깃·보스 우선·최전방 우선 등의 정책을 주입한다.
    public sealed class VerifiedTargetContact
    {
        public TargetCandidate Candidate { get; private set; }
        public CharacterEnemyKind EnemyKind { get; private set; }

        public VerifiedTargetContact(TargetCandidate candidate,
            CharacterEnemyKind kind)
        {
            Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
            if (!Enum.IsDefined(typeof(CharacterEnemyKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            EnemyKind = kind;
        }
    }

    public interface IOriginalCombatTargetSelectionRules
    {
        // true면 preferredTargetId는 반드시 현재 전장의 적 후보여야 한다.
        // 모바일 모드에서는 verifiedRange에 double.MaxValue가 전달되어
        // 캐릭터별 기본 사거리 제한을 적용하지 않는다.
        // false면 가까운 적/기존 보스 우선 정책을 사용한다.
        bool TryChoose(CatalogUnit unit, IReadOnlyList<VerifiedTargetContact> candidates,
            double verifiedRange, out string preferredTargetId);
    }
}
