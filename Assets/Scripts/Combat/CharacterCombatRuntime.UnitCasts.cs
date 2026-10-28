using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, OriginalUnitCast> originalUnitCasts =
            new Dictionary<long, OriginalUnitCast>();

        // A player-requested native cast. It does not freeze the world or other units.
        // The decomposition service owns its duration; movement/removal interrupts it.
        public sealed class OriginalUnitCast : IDisposable
        {
            private readonly CharacterCombatRuntime world;
            private readonly UnitState source;
            private bool disposed;
            internal OriginalUnitCast(CharacterCombatRuntime owner, UnitState state)
            { world = owner; source = state; }
            public bool IsCurrent
            {
                get
                {
                    OriginalUnitCast active;
                    if (disposed || world.Game.Phase != SessionPhase.Running ||
                        !world.originalUnitCasts.TryGetValue(source.InstanceId, out active) ||
                        !Object.ReferenceEquals(this, active) ||
                        !world.characters.ContainsKey(source.InstanceId) ||
                        world.IsStoryDeployed(source.InstanceId)) return false;
                    var now = world.roster.Get(source.InstanceId);
                    return now.UnitId == source.UnitId && now.X == source.X && now.Y == source.Y;
                }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                OriginalUnitCast active;
                if (world.originalUnitCasts.TryGetValue(source.InstanceId, out active) &&
                    Object.ReferenceEquals(this, active)) world.originalUnitCasts.Remove(source.InstanceId);
            }
        }

        internal OriginalUnitCast BeginOriginalUnitCast(long id)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (processingStep || pendingStepSeconds != 0 || Game.Phase != SessionPhase.Running ||
                !characters.ContainsKey(id) || IsStoryDeployed(id) || originalUnitCasts.ContainsKey(id))
                throw new InvalidOperationException("현재 이 유닛은 시전할 수 없습니다.");
            var cast = new OriginalUnitCast(this, roster.Get(id));
            originalUnitCasts.Add(id, cast);
            return cast;
        }
        public bool IsOriginalUnitCasting(long id) => originalUnitCasts.ContainsKey(id);
        private void CancelOriginalUnitCast(long id)
        {
            OriginalUnitCast cast;
            if (originalUnitCasts.TryGetValue(id, out cast)) cast.Dispose();
        }
    }
}
