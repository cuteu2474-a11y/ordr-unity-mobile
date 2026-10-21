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
        // true면 preferredTargetId가 반드시 후보 목록에 있고 사거리 안이어야 함.
        // false면 기존 기본 타깃 정책(가까운 적 및 별도 보스 규칙)을 사용.
        bool TryChoose(CatalogUnit unit, IReadOnlyList<VerifiedTargetContact> candidates,
            double verifiedRange, out string preferredTargetId);
    }
}
