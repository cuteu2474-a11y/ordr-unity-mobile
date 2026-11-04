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
        private readonly Dictionary<long, double> sourceLowFrenzyReadyAt =
            new Dictionary<long, double>();
        private readonly Dictionary<string, long> sourceLiquidFireRevision =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<string> modelAuraKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<long, long> sourceModelStoryLaneRevisions =
            new Dictionary<long, long>();
        public SourceModelUnitEffects PracticeUnitEffects => sourceModelUnitEffects;
        internal void ConfigureSourceModelUnitEffects(SourceModelBasicCombatRules rules)
        {
            sourceModelRules = rules;
            sourceModelUnitEffects = rules.UnitEffects;
            rules.ConfigurePracticeFlatAttackPower(SourceModelTwentiethFlatAttackPower);
            SourceModelTimeAdvancing += AdvanceSourceModelEighthTimeline;
            SourceModelTimeAdvancing += AdvanceSourceModelNinthProjectiles;
            SourceModelTimeAdvancing += AdvanceSourceModelTenthForms;
            SourceModelTimeAdvancing += AdvanceSourceModelEleventhBonney;
            SourceModelTimeAdvancing += AdvanceSourceChopperBursts;
            SourceModelTimeAdvancing += AdvanceSourceIndependentMana;
            SourceModelTimeAdvancing += AdvanceSourceModelTwentyFifthStates;
            SourceModelTimeAdvancing += AdvanceSourceModelFusionNativeFollowups;
            attackPipeline.ConfigurePracticeAttackOverride(rules.FormAttackOf);
            // The registered catalog identity does not change on form
            // override. Bonney's H0BY has a new tooltip script packet. Keep
            // the original base callback for Chopper/Kaido native JASS paths;
            // their attack object itself is already overridden independently.
            attackPipeline.ConfigurePracticeScriptAttackDeclared(
                (attacker, rawcode, target) => {
                    string form = rules.SourceFormRawcodeOf(attacker);
                    OnSourceModelScriptAttackDeclared(attacker,
                        form == "H0BY" || form == "h0AF" ? form : rawcode, target);
                });
            attackPipeline.ConfigurePracticeSuccessfulBasicHit((attacker, rawcode, target, actual) => {
                OnSourceModelNinthSuccessfulBasicHit(attacker, rawcode, target, actual);
                OnSourceModelTwentyFifthSuccessfulBasicHit(attacker, rawcode, target, actual);
            });
            attackPipeline.ConfigurePracticeAttackCadence((attack, effects) => {
                // Reuse the shared cadence path for a temporary form. Its flat
                // interval change is distinct from percentage-based auras.
                double speed = SourceModelUnitEffects.ResolvePracticeAttackSpeed(effects);
                double interval = attack.IntervalSeconds /
                    (1 + Math.Min(speed / 100.0, 4));
                return Math.Max(.01, interval -
                    SourceModelTemporarySkillCatalog.FormIntervalReduction(effects));
            });
        }
        private void TrySourceModelLowFrenzy(string attackerId)
        {
            long id;
            CharacterInstance actor;
            if (sourceModelRules == null ||
                !long.TryParse(attackerId, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Catalog.Id != "S-05") return;
            double readyAt;
            if (sourceLowFrenzyReadyAt.TryGetValue(id, out readyAt) &&
                !CombatScheduler.HasReached(effectExecutor.Time, readyAt)) return;
            // A08B/Auhf: +100% attack speed, duration 7s, cooldown 50s,
            // range 750. Source confirms self is a valid target but not
            // autocast prioritization. This offline policy targets self.
            var status = unitStatuses[attackerId];
            string source = "practice-native:A08B:" + attackerId;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source,
                CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.TriggeredSkill, 100, 7));
            sourceLowFrenzyReadyAt[id] = effectExecutor.Time + 50;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attackerId, attackerId,
                "트라팔가 로우 · 언홀리 프렌지",
                "MODEL_ONLY A08B: 공격속도 +100% / 7초 / 50초, 자기 시전 모바일 정책 · 원본 자동 타깃 미검증", 0));
        }
        private long SourceModelLaneRevision(long actorId)
        {
            long revision;
            return sourceModelStoryLaneRevisions.TryGetValue(actorId, out revision)
                ? revision : 0;
        }

        // A delayed location attack belongs to the lane at cast time. A
        // dispatch-and-return must not revive the original attack either.
        private bool CanFinishSourceModelDelayedAttack(long actorId,
            bool originalStoryLane, long originalLaneRevision)
        {
            return UsesSourceModelBasicAttacks && game.Phase == SessionPhase.Running &&
                characters.ContainsKey(actorId) &&
                IsStoryDeployed(actorId) == originalStoryLane &&
                SourceModelLaneRevision(actorId) == originalLaneRevision;
        }

        private const string PracticeBonneyArmorSource = "practice-native:A04P";

        // Pinned JASS: H0BX/B4C calls Bd2(RN,10) on ATTACK DECLARED,
        // before the health/mana-conditional branches. H0BY and those
        // additional procs require their own unimplemented state witnesses.
        private void ApplySourceModelBonneyDeclaredArmor(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            string attackerId = InstanceKey(actor.Id);
            var status = target.Enemy.Status;
            int current = 0;
            bool hasPracticeBonneyState = false;
            foreach (var effect in status.Snapshot())
            {
                if (effect.SourceId != PracticeBonneyArmorSource) continue;
                if (hasPracticeBonneyState ||
                    effect.Type != CombatEffectType.ArmorReduction ||
                    effect.Origin != CombatEffectOrigin.Aura ||
                    effect.DurationSeconds != 0 || effect.Amount < 10 ||
                    effect.Amount > 40 || effect.Amount % 10 != 0)
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        attackerId, target.Id, "보니 · A04P",
                        "MODEL_ONLY A04P 상태의 출처/레벨이 불일치하여 보류", 0));
                    return;
                }
                hasPracticeBonneyState = true;
                current = (int)effect.Amount;
            }
            double newArmorChange;
            if (current > 30 ||
                !Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04P",
                    current + 11, out newArmorChange) || newArmorChange >= 0)
            {
                // Idef is sourced only through level 41. No extrapolation.
                if (sourceBatchPendingReported.Add(attackerId + ":Bonney:A04P-cap:" + target.Id))
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        attackerId, target.Id, "보니 · A04P",
                        "MODEL_ONLY A04P 41레벨 초과의 원본 결과 미확인; 추가 적용 보류", 0));
                return;
            }
            status.RemoveSource(PracticeBonneyArmorSource);
            status.Apply(new CombatEffect(PracticeBonneyArmorSource,
                CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura,
                -newArmorChange, 0));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                attackerId, target.Id, "보니 · A04P",
                "MODEL_ONLY B4C/Bd2 공격 선언 +10, A04P 누적 " + (current + 10) +
                "; +20/+2 확률·변신·복합 방깎 중첩은 미검증", 0));
        }
        internal void MoveSourceModelEnemy(string id, double x, double y)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (!UsesSourceModelBasicAttacks || !processingStep) throw new InvalidOperationException("연습 이동 단계가 아닙니다.");
            SpatialCombatEnemy enemy;
            if (enemies.TryGetValue(id, out enemy) && enemy.Enemy.IsAlive)
            {
                if (enemy.Kind != CharacterEnemyKind.Line &&
                    enemy.Kind != CharacterEnemyKind.Boss &&
                    enemy.Kind != CharacterEnemyKind.Enraged)
                    throw new InvalidOperationException("비웨이브/스토리 건물은 라운드 경로를 이동하지 않습니다.");
                enemy.Move(x, y, 0);
            }
        }
        private void RefreshSourceModelAuras()
        {
            if (!UsesSourceModelBasicAttacks) return;
            foreach (var status in statuses.Values)
                foreach (string key in modelAuraKeys) status.RemoveSource(key);
            foreach (var status in unitStatuses.Values)
                foreach (string key in modelAuraKeys) status.RemoveSource(key);
            modelAuraKeys.Clear();
            if (sourceModelUnitEffects != null)
            {
                sourceModelUnitEffects.DeferredAuraGroups = 0;
                var ordered = new List<long>(characters.Keys);
                ordered.Sort();
                foreach (long target in ordered)
                {
                    if (activeOriginalFusionSwap != null && activeOriginalFusionSwap.Hides(target)) continue;
                    ApplySourceModelAuraGroup(ordered, target, null, CombatEffectType.AttackPowerBuff);
                    ApplySourceModelAuraGroup(ordered, target, null, CombatEffectType.AttackSpeedBuff);
                }
                foreach (var independent in sourceIndependentAttackers.Values)
                {
                    ApplySourceModelAuraGroup(ordered, null, null,
                        CombatEffectType.AttackPowerBuff, independent);
                    ApplySourceModelAuraGroup(ordered, null, null,
                        CombatEffectType.AttackSpeedBuff, independent);
                }
                foreach (var enemy in enemies.Values)
                {
                    if (!sourceModelUnitEffects.IsPracticeTarget(enemy.Id) || !enemy.Enemy.IsAlive) continue;
                    ApplySourceModelAuraGroup(ordered, null, enemy, CombatEffectType.ArmorReduction);
                    ApplySourceModelAuraGroup(ordered, null, enemy, CombatEffectType.MovementSlowPercent);
                }
            }
            RefreshSourceModelBulletArmorAuras();
            RefreshSourceModelBulletArmada();
            RefreshSourceKaidoArmorLanes();
        }

        // Catalog units, transformed forms and purchased/autonomous objects
        // share ONE native-aura conflict gate. The source map stores their
        // object rawcodes separately; no character-name special cases.
        // Our mobile rule deliberately removes the native aura radius within
        // the same lane, but preserves native role and identity restrictions.
        private void ApplySourceModelAuraGroup(List<long> casters, long? ally,
            SpatialCombatEnemy enemy, CombatEffectType type,
            SourceIndependentAttacker independentTarget = null)
        {
            var contributions = new List<Original323AuraContribution>();
            Original323SpecialAura first = null;
            bool recipientIsAlly = ally.HasValue || independentTarget != null;
            bool recipientStoryLane = ally.HasValue ? IsStoryDeployed(ally.Value) :
                independentTarget != null && independentTarget.StoryLane;
            foreach (long id in casters)
            {
                if (activeOriginalFusionSwap != null && activeOriginalFusionSwap.Hides(id)) continue;
                if (recipientIsAlly && IsStoryDeployed(id) != recipientStoryLane) continue;
                if (!recipientIsAlly && !CanAttackInThisLane(characters[id], enemy)) continue;
                string form = sourceModelRules.SourceFormRawcodeOf(InstanceKey(id));
                var definitions = form == null
                    ? sourceModelUnitEffects.AurasFor(characters[id].Catalog.Id)
                    : sourceModelUnitEffects.AurasForObject(form);
                // One original actor may carry two overlapping aura
                // abilities of the SAME effect/strength (Chopper's A0OK
                // and A0OL). They provide one effective buff, not two
                // independent contributors or a false multi-owner conflict.
                var owned = new List<Original323AuraContribution>();
                double? strength = null;
                bool equalStrength = true;
                foreach (var aura in definitions)
                {
                    if (aura.Type != type || !sourceModelUnitEffects.IsSupportedAura(aura)) continue;
                    if (recipientIsAlly && !aura.AllowsAllyRole(ally.HasValue && id == ally.Value)) continue;
                    first = first ?? aura;
                    if (strength.HasValue && Math.Abs(strength.Value - aura.Amount) > .000001)
                        equalStrength = false;
                    strength = strength ?? aura.Amount;
                    owned.Add(new Original323AuraContribution(aura.AbilityRawcode,
                        aura.BuffRawcode, aura.Level, aura.Amount, aura.Type));
                }
                if (equalStrength && owned.Count > 1)
                    contributions.Add(owned[0]);
                else
                    contributions.AddRange(owned);
            }
            foreach (var independent in sourceIndependentAttackers.Values)
            {
                if (recipientIsAlly && independent.StoryLane != recipientStoryLane) continue;
                if (!recipientIsAlly && !CanSourceIndependentAttack(independent, enemy)) continue;
                foreach (var aura in sourceModelUnitEffects.AurasForObject(independent.Rawcode))
                {
                    if (aura.Type != type || !sourceModelUnitEffects.IsSupportedAura(aura)) continue;
                    if (recipientIsAlly && !aura.AllowsAllyRole(
                        independentTarget != null && independentTarget.Key == independent.Key)) continue;
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
            var status = ally.HasValue ? unitStatuses[InstanceKey(ally.Value)] :
                independentTarget != null ? unitStatuses[independentTarget.Key] : enemy.Enemy.Status;
            status.Apply(new CombatEffect(key, type, CombatEffectOrigin.Aura, amount, 0));
        }
        private void CompleteSourceModelHit(CharacterInstance actor, BasicAttackResult hit, SpatialCombatEnemy primary)
        {
            if (hit.BasicHitApplied && actor.Catalog.Id == "I-11")
                ApplySourceKaidoBasicArmor(actor, primary);
            if (actor.Catalog.Id == "TR-23" && sourceNativeForms.ContainsKey(actor.Id) &&
                sourceModelRules.FormAttackOf(InstanceKey(actor.Id)) != null &&
                hit.ActualDamage > 0)
                ApplySourceChopperSplash(actor, primary);
            // These native-object follow-ups have pinned W3A inputs and do
            // not depend on the optional passive/aura catalog being present.
            if (actor.Catalog.Id == "S-20" && hit.ActualDamage > 0 &&
                primary.Enemy.IsAlive)
                ScheduleSourceModelLiquidFire(actor, primary);
            if (actor.Catalog.Id == "S-09" && hit.ActualDamage > 0 &&
                !primary.Enemy.IsAlive && primary.Kind == CharacterEnemyKind.Line)
                SpawnSourceModelMoriaZombie(actor, primary);
            if (sourceModelUnitEffects == null) return;
            var passive = sourceModelUnitEffects.Take(InstanceKey(actor.Id), hit.TargetId);
            if (passive != null)
            {
                if (passive.Kind == OriginalNativePassiveKind.Critical ||
                    passive.Kind == OriginalNativePassiveKind.CriticalStun ||
                    passive.Kind == OriginalNativePassiveKind.CriticalBonus ||
                    passive.Kind == OriginalNativePassiveKind.CriticalBonusStun)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, passive.Name,
                        "연습 객체 크리티컬 · " + passive.CriticalMultiplier + "배 타격에 반영", 0));
                if (passive.Kind == OriginalNativePassiveKind.Bonus ||
                    passive.Kind == OriginalNativePassiveKind.BonusStun ||
                    passive.Kind == OriginalNativePassiveKind.CriticalBonus ||
                    passive.Kind == OriginalNativePassiveKind.CriticalBonusStun)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, passive.Name,
                        "연습 객체 추가 피해 " + passive.BonusDamage + " · 기본 타격에 반영", 0));
                if ((passive.Kind == OriginalNativePassiveKind.Stun ||
                     passive.Kind == OriginalNativePassiveKind.CriticalStun ||
                     passive.Kind == OriginalNativePassiveKind.BonusStun ||
                     passive.Kind == OriginalNativePassiveKind.CriticalBonusStun) && primary.Enemy.IsAlive &&
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

        private void ApplySourceChopperSplash(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = FindSourceSkill(actor, "럼블볼 폭주");
            if (skill == null || skill.Components.Count != 4 ||
                skill.Components[1].Type != "basicAttackSplash" ||
                skill.Components[1].Number("radius") != 400) return;
            var nearby = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
            {
                if (enemy.Id == primary.Id || !enemy.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, enemy)) continue;
                double dx = enemy.X - primary.X, dy = enemy.Y - primary.Y;
                if (dx * dx + dy * dy <= 400 * 400) nearby.Add(enemy);
            }
            nearby.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var enemy in nearby)
            {
                string attacker = InstanceKey(actor.Id);
                var request = new BasicDamageRequest(attacker,
                    actor.Mapping.AttackSpec.UnitRawCode,
                    actor.Mapping.AttackSpec.BasicAttack, enemy.Id,
                    unitStatuses[attacker].Snapshot());
                long planned;
                try { planned = sourceModelRules.ResolveBasicDamage(request, enemy.Enemy); }
                catch (NotSupportedException ex)
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        attacker, enemy.Id, skill.Name, "TOOLTIP_MODEL_ONLY 스플래시: " + ex.Message, 0));
                    continue;
                }
                long actual;
                if (!resolution.TryApplySkillDamage(attacker, enemy.Id, planned, out actual)) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                    attacker, enemy.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY 반경400 / 별도 객체 주사위 / 원작 스플래시 분산·방어 미확인", actual));
                if (!enemy.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        attacker, enemy.Id, skill.Name, "모델 스플래시 처치", actual));
            }
        }
    }
}

