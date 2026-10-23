using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Coordinates must be reconstructed from the current player's original
    // start location (e016/e019/e015) or Lq center (e017).
    public interface IOriginalWispSpawnPositionPort
    {
        bool TryGetPosition(string originalWispId, out double x, out double y);
    }

    // Adds the actual rare-Wisp A0YE behavior to an already created unit.
    // Returning false blocks consumption; cosmetic marking is insufficient.
    public interface IOriginalWispBonusAbilityPort
    {
        bool TryApplyOriginalAbility(long instanceId, string abilityRawcode);
    }

    // A real Combat.SpawnCharacter writes to the very same UnitRoster as the
    // economy session. Rawcode guesses are rejected: only reviewed mappings
    // may instantiate a catalog character.
    public sealed class OriginalWispCombatRewardWorld : IWispRewardWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly IVerifiedUnitRawcodeMap rawcodes;
        private readonly IOriginalWispSpawnPositionPort positions;
        private readonly IOriginalWispBonusAbilityPort abilities;

        public OriginalWispCombatRewardWorld(CharacterCombatRuntime liveCombat,
            IVerifiedUnitRawcodeMap verifiedMap, IOriginalWispSpawnPositionPort originalPositions,
            IOriginalWispBonusAbilityPort originalAbilities)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            rawcodes = verifiedMap ?? throw new ArgumentNullException(nameof(verifiedMap));
            positions = originalPositions ?? throw new ArgumentNullException(nameof(originalPositions));
            abilities = originalAbilities ?? throw new ArgumentNullException(nameof(originalAbilities));
        }

        public IWispRewardTransaction Begin() => new Transaction(this);

        private sealed class Transaction : IWispRewardTransaction
        {
            private readonly OriginalWispCombatRewardWorld owner;
            private long created;
            private bool committed;
            private bool disposed;

            public Transaction(OriginalWispCombatRewardWorld world) { owner = world; }
            public void Apply(WispReward reward)
            {
                if (disposed || created != 0 || reward == null)
                    throw new InvalidOperationException("위습 보상 트랜잭션 재사용 금지");
                string catalogId;
                double x, y;
                if (!owner.rawcodes.TryResolveUnit(reward.UnitRawcode, out catalogId) ||
                    string.IsNullOrWhiteSpace(catalogId) ||
                    !owner.positions.TryGetPosition(reward.SourceWispId, out x, out y) ||
                    double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y))
                    throw new InvalidOperationException("원본 위습 보상 객체/소환 좌표 미검증");
                created = owner.combat.SpawnCharacter(catalogId, x, y);
                if (created <= 0 || owner.combat.Roster.Get(created).UnitId != catalogId)
                    throw new InvalidOperationException("공유 유닛 목록 등록 실패");
                if (reward.BonusAbilityRawcode != null &&
                    (!String.Equals(reward.BonusAbilityRawcode, "A0YE", StringComparison.Ordinal) ||
                     !owner.abilities.TryApplyOriginalAbility(created, reward.BonusAbilityRawcode)))
                    throw new InvalidOperationException("원본 희귀위습 A0YE 적용 실패");
            }

            public void Commit()
            {
                if (disposed || created <= 0 || committed)
                    throw new InvalidOperationException("위습 월드 거래 확정 실패");
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (!committed && created > 0)
                    owner.combat.RollbackUncommittedSpawn(created);
            }
        }
    }
}
