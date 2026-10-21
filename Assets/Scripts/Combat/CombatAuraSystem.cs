using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // 오라 종류·반경·대상 범위는 출처 확인 후 주입해야 한다.
    // 중첩으로 얻는 최종 피해/이속 계산은 IOriginalEffectStackingRule 담당.
    public sealed class CombatAuraEmitter
    {
        public long UnitInstanceId { get; private set; }
        public string AuraId { get; private set; }
        public CombatEffectType Type { get; private set; }
        public double Amount { get; private set; }
        public double Radius { get; private set; }
        public string Evidence { get; private set; }
        public IReadOnlyList<CharacterEnemyKind> TargetKinds { get; private set; }
        public string SourceKey
        {
            get { return "aura:" + UnitInstanceId + ":" + AuraId; }
        }

        public CombatAuraEmitter(long casterId, string auraId,
            CombatEffectType type, double amount, double radius,
            IEnumerable<CharacterEnemyKind> eligibleTargets, string evidence)
        {
            if (casterId <= 0) throw new ArgumentOutOfRangeException(nameof(casterId));
            if (string.IsNullOrWhiteSpace(auraId) || string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("오라 ID와 검증 출처를 명시해야 합니다.");
            if (!Enum.IsDefined(typeof(CombatEffectType), type) ||
                double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0 ||
                double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (eligibleTargets == null)
                throw new ArgumentNullException(nameof(eligibleTargets));
            var kinds = new List<CharacterEnemyKind>();
            foreach (var kind in eligibleTargets)
            {
                if (!Enum.IsDefined(typeof(CharacterEnemyKind), kind))
                    throw new ArgumentOutOfRangeException(nameof(eligibleTargets));
                if (!kinds.Contains(kind)) kinds.Add(kind);
            }
            if (kinds.Count == 0)
                throw new ArgumentException("적용 대상 범위를 명시해야 합니다.");
            UnitInstanceId = casterId; AuraId = auraId;
            Type = type; Amount = amount; Radius = radius;
            Evidence = evidence; TargetKinds = kinds.AsReadOnly();
        }
    }

    // 이 시스템은 적에게 영향을 주는 오라만 다룬다.
    // 아군 공속/공격력 오라는 별도의 아군 스탯 계산 정책을 필요로 한다.
    public sealed class CombatAuraSystem
    {
        private readonly Dictionary<string, CombatAuraEmitter> emitters =
            new Dictionary<string, CombatAuraEmitter>(StringComparer.Ordinal);
        private readonly HashSet<string> previousKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public int Count { get { return emitters.Count; } }

        public void Add(CombatAuraEmitter emitter)
        {
            if (emitter == null) throw new ArgumentNullException(nameof(emitter));
            if (emitters.ContainsKey(emitter.SourceKey))
                throw new InvalidOperationException("동일 오라가 이미 등록됐습니다.");
            emitters.Add(emitter.SourceKey, emitter);
        }

        public void Remove(long instanceId, string auraId)
        {
            if (string.IsNullOrWhiteSpace(auraId))
                throw new ArgumentException("오라 ID가 필요합니다.");
            emitters.Remove("aura:" + instanceId + ":" + auraId);
        }

        public void RemoveAll(long instanceId)
        {
            var keys = new List<string>();
            foreach (var pair in emitters)
                if (pair.Value.UnitInstanceId == instanceId) keys.Add(pair.Key);
            foreach (string key in keys) emitters.Remove(key);
        }

        public void Refresh(Dictionary<string, CombatStatusEngine> statuses,
            IEnumerable<SpatialCombatEnemy> enemies,
            Func<long, UnitState?> findCasterPosition)
        {
            if (statuses == null || enemies == null || findCasterPosition == null)
                throw new ArgumentNullException("오라 갱신에 필요한 세계 상태가 없습니다.");
            var current = new List<SpatialCombatEnemy>(enemies);
            foreach (var effect in statuses.Values)
                foreach (string key in previousKeys)
                    effect.RemoveSource(key);
            previousKeys.Clear();

            var ordered = new List<string>(emitters.Keys);
            ordered.Sort(StringComparer.Ordinal);
            foreach (string key in ordered)
            {
                CombatAuraEmitter emitter = emitters[key];
                UnitState? position = findCasterPosition(emitter.UnitInstanceId);
                if (position == null) continue;
                previousKeys.Add(key);
                double r2 = emitter.Radius * emitter.Radius;
                foreach (var enemy in current)
                {
                    if (!enemy.Enemy.IsAlive) continue;
                    bool eligible = false;
                    foreach (var kind in emitter.TargetKinds)
                        if (kind == enemy.Kind) { eligible = true; break; }
                    if (!eligible) continue;
                    double dx = enemy.X - position.Value.X;
                    double dy = enemy.Y - position.Value.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    CombatStatusEngine status;
                    if (!statuses.TryGetValue(enemy.Id, out status)) continue;
                    status.Apply(new CombatEffect(emitter.SourceKey, emitter.Type,
                        CombatEffectOrigin.Aura, emitter.Amount, 0));
                }
            }
        }
    }
}
