using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // Source-confirmed line deaths are NOT equivalent to unverified XP.
    public sealed class Original323EvolutionProgress
    {
        private readonly HashSet<string> credited = new HashSet<string>(StringComparer.Ordinal);
        public long ActorId { get; private set; }
        public int LineDefeats { get { return credited.Count; } }
        public long? ReleasedUnitId { get; private set; }
        public bool IsReleased { get { return ReleasedUnitId.HasValue; } }

        public Original323EvolutionProgress(long actorId)
        {
            if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));
            ActorId = actorId;
        }

        public bool RecordVerifiedLineDefeat(int spawnWave, string monsterInstanceId)
        {
            if (IsReleased) return false;
            if (spawnWave <= 0 || string.IsNullOrWhiteSpace(monsterInstanceId))
                throw new ArgumentException("실제 라인 몬스터 처치가 필요합니다.");
            return credited.Add(spawnWave + ":" + monsterInstanceId);
        }

        public void RecordSourceConfirmedRelease(long newUnitId)
        {
            if (IsReleased || newUnitId <= 0)
                throw new InvalidOperationException("진화체 해방을 중복 기록할 수 없습니다.");
            ReleasedUnitId = newUnitId;
        }
    }

    // Adapter must supply original XP/threshold/RNG-verified sticky result and coordinates.
    public interface IOriginal323EvolutionReleaseOutcome
    {
        bool TryGetReleasedSpecial(long evolutionActorId, int observedLineDefeats,
            out string catalogUnitId, out double spawnX, out double spawnY);
    }
}
