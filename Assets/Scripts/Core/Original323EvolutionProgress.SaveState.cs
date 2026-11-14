using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    [Serializable]
    public sealed class Original323EvolutionProgressState
    {
        public int Version = 1;
        public long ActorId;
        public bool HasInitialUncommon;
        public long InitialUncommonId;
        public string DormantSpecialCatalogId;
        public bool IsUnsealing;
        public double UnsealStartTime;
        public bool HasReleasedUnit;
        public long ReleasedUnitId;
        public List<string> DefeatedEnemyKeys = new List<string>();
    }

    public sealed partial class Original323EvolutionProgress
    {
        // The source's 152 distinct wave/id receipts, never just a kill count.
        public Original323EvolutionProgressState CaptureState()
        {
            var state = new Original323EvolutionProgressState {
                ActorId = ActorId,
                HasInitialUncommon = InitialUncommonId.HasValue,
                InitialUncommonId = InitialUncommonId.GetValueOrDefault(),
                DormantSpecialCatalogId = DormantSpecialCatalogId,
                IsUnsealing = IsUnsealing, UnsealStartTime = UnsealStartTime,
                HasReleasedUnit = ReleasedUnitId.HasValue,
                ReleasedUnitId = ReleasedUnitId.GetValueOrDefault()
            };
            state.DefeatedEnemyKeys.AddRange(defeated);
            state.DefeatedEnemyKeys.Sort(StringComparer.Ordinal);
            ValidateState(state, double.MaxValue);
            return state;
        }

        public static void ValidateState(Original323EvolutionProgressState state,
            double logicalTime)
        {
            int required = Original323EvolutionSourceRules.RequiredLineKills;
            if (state == null || state.Version != 1 || state.ActorId <= 0 ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                logicalTime < 0 || state.DefeatedEnemyKeys == null ||
                state.DefeatedEnemyKeys.Count > required ||
                state.HasInitialUncommon != (state.InitialUncommonId > 0) ||
                state.HasInitialUncommon !=
                    !string.IsNullOrWhiteSpace(state.DormantSpecialCatalogId) ||
                (!state.HasInitialUncommon &&
                    (state.DefeatedEnemyKeys.Count != 0 || state.IsUnsealing ||
                     state.HasReleasedUnit)) ||
                state.IsUnsealing != (state.DefeatedEnemyKeys.Count == required) ||
                state.HasReleasedUnit != (state.ReleasedUnitId > 0) ||
                (state.HasReleasedUnit && !state.IsUnsealing) ||
                double.IsNaN(state.UnsealStartTime) ||
                double.IsInfinity(state.UnsealStartTime) ||
                state.UnsealStartTime < 0 ||
                (!state.IsUnsealing && state.UnsealStartTime != 0) ||
                (state.IsUnsealing && state.UnsealStartTime > logicalTime) ||
                (state.HasReleasedUnit && logicalTime + 1e-8 <
                    state.UnsealStartTime + Original323EvolutionSourceRules.UnsealSeconds))
                throw new FormatException("진화체 영수증·원본 봉인 시계가 잘못됐습니다.");
            if (state.HasInitialUncommon)
            {
                bool found = false;
                foreach (var unit in Original323EvolutionSourceRules.SpecialPool)
                    if (unit.CatalogId == state.DormantSpecialCatalogId) found = true;
                if (!found) throw new FormatException("대기 진화체가 원본 32종 풀에 없습니다.");
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in state.DefeatedEnemyKeys)
            {
                int separator = key == null ? -1 : key.IndexOf(':');
                int wave;
                if (separator <= 0 || separator == key.Length - 1 ||
                    !int.TryParse(key.Substring(0, separator), out wave) ||
                    wave < 1 || wave > 65 || !unique.Add(key))
                    throw new FormatException("중복·유효하지 않은 진화체 처치 ID가 저장됐습니다.");
            }
        }

        public static Original323EvolutionProgress Restore(
            Original323EvolutionProgressState state, double logicalTime)
        {
            ValidateState(state, logicalTime);
            var restored = new Original323EvolutionProgress(state.ActorId) {
                InitialUncommonId = state.HasInitialUncommon ?
                    (long?)state.InitialUncommonId : null,
                DormantSpecialCatalogId = state.DormantSpecialCatalogId,
                IsUnsealing = state.IsUnsealing, UnsealStartTime = state.UnsealStartTime,
                ReleasedUnitId = state.HasReleasedUnit ? (long?)state.ReleasedUnitId : null
            };
            foreach (var key in state.DefeatedEnemyKeys) restored.defeated.Add(key);
            return restored;
        }
    }
}
