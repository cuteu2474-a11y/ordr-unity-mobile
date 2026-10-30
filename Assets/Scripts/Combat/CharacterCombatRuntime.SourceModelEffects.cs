using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        internal event Action<double> SourceModelTimeAdvancing;
        private SourceModelUnitEffects sourceModelUnitEffects;
        private SourceModelBasicCombatRules sourceModelRules;
        private readonly HashSet<string> modelAuraKeys = new HashSet<string>(StringComparer.Ordinal);
        public SourceModelUnitEffects PracticeUnitEffects => sourceModelUnitEffects;
        internal void ConfigureSourceModelUnitEffects(SourceModelBasicCombatRules rules)
        {
            sourceModelRules = rules;
            sourceModelUnitEffects = rules.UnitEffects;
        }
        internal void MoveSourceModelEnemy(string id, double x, double y)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (!UsesSourceModelBasicAttacks || !processingStep) throw new InvalidOperationException("연습 이동 단계가 아닙니다.");
            SpatialCombatEnemy enemy;
            if (enemies.TryGetValue(id, out enemy) && enemy.Enemy.IsAlive) enemy.Move(x, y, 0);
        }
        private void RefreshSourceModelAuras()
        {
            if (!UsesSourceModelBasicAttacks || sourceModelUnitEffects == null) return;
            foreach (var status in statuses.Values)
                foreach (string key in modelAuraKeys) status.RemoveSource(key);
            foreach (var status in unitStatuses.Values)
                foreach (string key in modelAuraKeys) status.RemoveSource(key);
            modelAuraKeys.Clear();
            sourceModelUnitEffects.DeferredAuraGroups = 0;
            var ordered = new List<long>(characters.Keys);
            ordered.Sort();
            foreach (long target in ordered)
            {
                if (IsStoryDeployed(target) || (activeOriginalFusionSwap != null && activeOriginalFusionSwap.Hides(target))) continue;
                ApplySourceModelAuraGroup(ordered, target, null, CombatEffectType.AttackPowerBuff);
            }
            foreach (var enemy in enemies.Values)
            {
                if (!sourceModelUnitEffects.IsPracticeTarget(enemy.Id) || !enemy.Enemy.IsAlive || (enemy.Kind != CharacterEnemyKind.Line && enemy.Kind != CharacterEnemyKind.Boss)) continue;
                ApplySourceModelAuraGroup(ordered, null, enemy, CombatEffectType.ArmorReduction);
                ApplySourceModelAuraGroup(ordered, null, enemy, CombatEffectType.MovementSlowPercent);
            }
        }
        private void ApplySourceModelAuraGroup(List<long> casters, long? ally, SpatialCombatEnemy enemy, CombatEffectType type)
        {
            var contributions = new List<Original323AuraContribution>();
            Original323SpecialAura first = null;
            foreach (long id in casters)
            {
                if (IsStoryDeployed(id) || (activeOriginalFusionSwap != null && activeOriginalFusionSwap.Hides(id))) continue;
                foreach (var aura in sourceModelUnitEffects.AurasFor(characters[id].Catalog.Id))
                {
                    if (aura.Type != type || !sourceModelUnitEffects.IsSupportedAura(aura)) continue;
                    if (ally.HasValue && !aura.AllowsAllyRole(id == ally.Value)) continue;
                    if (!ally.HasValue && !CanAttackInThisLane(characters[id], enemy)) continue;
                    first = first ?? aura;
                    contributions.Add(new Original323AuraContribution(aura.AbilityRawcode,
                        aura.BuffRawcode, aura.Level, aura.Amount, aura.Type));
                }
            }
            if (contributions.Count == 0) return;
            double amount;
            if (!Original323AuraCommonRules.TryResolveIdenticalAura(contributions, out amount))
            { sourceModelUnitEffects.DeferredAuraGroups++; return; }
            string key = "practice-aura:" + (int)type + ":" + first.AbilityRawcode;
            modelAuraKeys.Add(key);
            var status = ally.HasValue ? unitStatuses[InstanceKey(ally.Value)] : enemy.Enemy.Status;
            status.Apply(new CombatEffect(key, type, CombatEffectOrigin.Aura, amount, 0));
        }
        private void CompleteSourceModelHit(CharacterInstance actor, BasicAttackResult hit, SpatialCombatEnemy primary)
        {
            if (sourceModelUnitEffects == null) return;
            var passive = sourceModelUnitEffects.Take(InstanceKey(actor.Id), hit.TargetId);
            if (passive != null)
            {
                if (passive.Kind != OriginalNativePassiveKind.Stun)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, passive.Name,
                        "연습 객체 크리티컬 · " + passive.CriticalMultiplier + "배 타격에 반영", 0));
                if (passive.Kind != OriginalNativePassiveKind.Critical && primary.Enemy.IsAlive &&
                    hit.ActualDamage > 0 && primary.Kind == CharacterEnemyKind.Line)
                {
                    var status = primary.Enemy.Status;
                    string key = "practice-passive:" + passive.AbilityRawcode;
                    status.RemoveSource(key);
                    status.Apply(new CombatEffect(key, CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1, passive.NormalStunSeconds));
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, passive.Name,
                        "연습 객체 기절 · " + passive.NormalStunSeconds + "초 / 보스 저항 별도", 0));
                }
            }
            if (actor.Catalog.Id != "U-01") return;
            // Source A07M has THREE total targets. The practice selects the
            // nearest two additional living targets, tied by immutable ID;
            // native missile flight and target ordering remain unverified.
            var candidates = new List<SpatialCombatEnemy>();
            foreach (var other in enemies.Values)
                if (other.Id != hit.TargetId && other.Enemy.IsAlive && CanAttackInThisLane(actor, other))
                    candidates.Add(other);
            var position = roster.Get(actor.Id);
            candidates.Sort((a,b) => {
                double da = (a.X-position.X)*(a.X-position.X)+(a.Y-position.Y)*(a.Y-position.Y);
                double db = (b.X-position.X)*(b.X-position.X)+(b.Y-position.Y)*(b.Y-position.Y);
                int cmp = da.CompareTo(db); return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Id,b.Id);
            });
            int count = Math.Min(2, candidates.Count);
            if (count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), hit.TargetId, "꽃꽃 열매", "연습 · 3명 동시 공격", count + 1));
            for (int i=0;i<count;i++)
            {
                var target = candidates[i];
                var request = new BasicDamageRequest(InstanceKey(actor.Id), actor.Mapping.AttackSpec.UnitRawCode,
                    actor.Mapping.AttackSpec.BasicAttack, target.Id, unitStatuses[InstanceKey(actor.Id)].Snapshot());
                long damage = sourceModelRules.ResolveBasicDamage(request, target.Enemy), actual;
                if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id), target.Id, damage, out actual)) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                    InstanceKey(actor.Id), target.Id, "꽃꽃 열매", "연습 추가 기본 타격", actual));
                if (!target.Enemy.IsAlive) events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    InstanceKey(actor.Id), target.Id, "꽃꽃 열매", "연습 추가 타격으로 처치", 0));
            }
        }
    }
}
