using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Non-damaging preview of mapped Warcraft object attack timing. Historical
    // class name retained for compatibility with existing runtime wiring.
    // Object damage dice and cooldown are sourced from the W3X;
    // target armor, upgrades, JASS effects and final damage remain unverified.
    public sealed class OriginalMapCommonAttackPreview
    {
        private sealed class TrackedAttack
        {
            public OriginalMapCharacterBasics Basics;
            public double NextAt;
        }

        private readonly OriginalMapBaseStatCatalog source;
        private readonly Dictionary<long, TrackedAttack> tracked =
            new Dictionary<long, TrackedAttack>();

        public int Count { get { return tracked.Count; } }

        public OriginalMapCommonAttackPreview(OriginalMapBaseStatCatalog source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool TryTrack(long instanceId, string catalogId, double currentTime)
        {
            if (double.IsNaN(currentTime) || double.IsInfinity(currentTime) || currentTime < 0)
                throw new ArgumentOutOfRangeException(nameof(currentTime));
            if (tracked.ContainsKey(instanceId))
                throw new InvalidOperationException("이미 미리보기 공격이 등록된 유닛입니다.");
            var entry = source.Find(catalogId);
            if (entry == null ||
                string.IsNullOrEmpty(entry.OriginalRawcode) || entry.Attack1 == null)
                return false;
            var attack = entry.Attack1;
            if (!attack.MinimumBaseDamage.HasValue || !attack.MaximumBaseDamage.HasValue ||
                !attack.AttackCooldownSeconds.HasValue ||
                !attack.AttackPointSeconds.HasValue ||
                attack.AttackPointSeconds.Value < 0 ||
                attack.AttackPointSeconds.Value > attack.AttackCooldownSeconds.Value)
                return false;
            tracked.Add(instanceId, new TrackedAttack {
                Basics = entry,
                NextAt = currentTime + attack.AttackPointSeconds.Value
            });
            return true;
        }

        public double? NextAt(long instanceId)
        {
            TrackedAttack item;
            return tracked.TryGetValue(instanceId, out item)
                ? (double?)item.NextAt : null;
        }

        // Event is a visual cue ONLY. No damage is rolled or applied here.
        public bool TryCommit(long instanceId, double currentTime,
            out OriginalMapCharacterBasics sourceStats)
        {
            sourceStats = null;
            if (double.IsNaN(currentTime) || double.IsInfinity(currentTime) || currentTime < 0)
                throw new ArgumentOutOfRangeException(nameof(currentTime));
            TrackedAttack item;
            if (!tracked.TryGetValue(instanceId, out item) ||
                !CombatScheduler.HasReached(currentTime, item.NextAt)) return false;
            sourceStats = item.Basics;
            double next = Math.Max(item.NextAt, currentTime) +
                item.Basics.Attack1.AttackCooldownSeconds.Value;
            if (double.IsInfinity(next)) throw new OverflowException("공격 미리보기 시간이 초과되었습니다.");
            item.NextAt = next;
            return true;
        }

        public void Remove(long instanceId)
        {
            tracked.Remove(instanceId);
        }
    }
}
