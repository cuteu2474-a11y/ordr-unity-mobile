using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // e0HP is consumed on activation; the preselected dormant unit is
    // intangible until 152 actual marked kills and 101 source timer ticks.
    public sealed class Original323EvolutionProgress
    {
        private readonly HashSet<string> defeated = new HashSet<string>(StringComparer.Ordinal);
        public long ActorId { get; private set; }
        public long? InitialUncommonId { get; private set; }
        public string DormantSpecialCatalogId { get; private set; }
        public bool IsActivated => InitialUncommonId.HasValue;
        public bool IsUnsealing { get; private set; }
        public double UnsealStartTime { get; private set; }
        public int LineDefeats => defeated.Count;
        public long? ReleasedUnitId { get; private set; }
        public bool IsReleased => ReleasedUnitId.HasValue;

        public Original323EvolutionProgress(long actorId)
        {
            if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));
            ActorId = actorId;
        }

        public void Activate(long uncommonId, string specialId)
        {
            if (IsActivated || IsReleased || LineDefeats != 0 || uncommonId <= 0 ||
                string.IsNullOrWhiteSpace(specialId))
                throw new InvalidOperationException("진화체는 한 번만 사용할 수 있습니다.");
            InitialUncommonId = uncommonId;
            DormantSpecialCatalogId = specialId;
        }

        public bool RecordVerifiedLineDefeat(int spawnWave, string id, double atTime)
        {
            if (!IsActivated || IsUnsealing || IsReleased) return false;
            if (spawnWave <= 0 || string.IsNullOrWhiteSpace(id) ||
                atTime < 0 || double.IsNaN(atTime) || double.IsInfinity(atTime))
                throw new ArgumentException("검증된 몬스터와 게임 시간이 필요합니다.");
            if (!defeated.Add(spawnWave + ":" + id)) return false;
            if (defeated.Count == Original323EvolutionSourceRules.RequiredLineKills)
            {
                IsUnsealing = true;
                UnsealStartTime = atTime;
            }
            return true;
        }

        public bool CanRelease(double atTime) =>
            IsUnsealing && !IsReleased && !double.IsNaN(atTime) &&
            atTime + 1e-8 >=
                UnsealStartTime + Original323EvolutionSourceRules.UnsealSeconds;

        public void RecordSourceConfirmedRelease(long unitId)
        {
            if (!IsUnsealing || IsReleased || unitId <= 0)
                throw new InvalidOperationException("해방은 한 번만 확정합니다.");
            ReleasedUnitId = unitId;
        }
    }
}
