using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // These autonomous actors share the *existing* scheduler, dice resolver,
    // HP/death pipeline and same-lane targeting. They never enter the roster.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceIndependentAttacker
        {
            public string Key;
            public string Rawcode;
            public long OwnerInstanceId;
            public bool StoryLane;
            public double X;
            public double Y;
            public double? ExpiresAt;
            public bool Blocked;
            public CharacterSkillProfile SkillProfile;
            public double MaximumMana;
            public double Mana;
            public double SourceManaRegenPerSecond;
            public double SatelliteRegenBeginsAt;
            public double SatelliteRegenEndsAt;
            public double SatelliteRegenPerSecond;
            // Native B6o's attackonce command selects the next basic hit
            // target; it must never create an additional free damage packet.
            public string AttackOnceTargetId;
            // Source zombie rawcode is not pinned; an explicit model shadow
            // inherits the owner's native attack object and common buffs.
            public bool ModelShadowZombie;
            // Moria S-09 transient ANba h00H: rendered only as a
            // projectile; not owned inventory, placement or fusion material.
            public bool ModelBlackArrowEffect;
            // All transient h00H/h05D/h04O attacks share one visual route.
            // Rendering never changes their original dice/cadence/target.
            public bool RenderAsTransientProjectile;
            public readonly List<string> ShadowInheritedSources = new List<string>();
        }

        private readonly Dictionary<string, SourceIndependentAttacker> sourceIndependentAttackers =
            new Dictionary<string, SourceIndependentAttacker>(StringComparer.Ordinal);

        private static string SatelliteAttackKey(long id) => "practice-satellite:" + id;
        private static string SummonAttackKey(long id) => "practice-summon:" + id;

        private void RegisterSourceIndependentAttack(string key, string rawcode,
            bool storyLane, double x, double y, long ownerInstanceId,
            double? expiresAt = null, CharacterSkillProfile skillProfile = null)
        {
            if (ownerInstanceId <= 0 || !characters.ContainsKey(ownerInstanceId))
                throw new InvalidOperationException("실제 소유 캐릭터가 없는 독립 공격자는 등록할 수 없습니다.");
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                string.IsNullOrWhiteSpace(key) ||
                sourceIndependentAttackers.ContainsKey(key) || unitStatuses.ContainsKey(key) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                (expiresAt.HasValue && expiresAt.Value <= scheduler.Time))
                throw new InvalidOperationException("연습 독립 공격자 생성 조건이 맞지 않습니다.");
            var attack = sourceModelRules.SourceObjectAttack(rawcode);
            if (attack == null) throw new InvalidOperationException("원본 독립 공격 주사위 미확인: " + rawcode);
            // The first full cooldown is a documented practice scheduling
            // convention. The native Warcraft initial attack phase is unknown.
            attackPipeline.RegisterUnit(key, new UnitCombatSpec(rawcode, attack,
                Array.Empty<CombatSkillSpec>(), SourceModelBasicCombatRules.Evidence),
                attack.IntervalSeconds);
            unitStatuses.Add(key, new CombatStatusEngine());
            var resources = sourceModelRules.SourceObjectResources(rawcode);
            // Map object SLK is the only starting mana source. Summons without
            // a documented mana action cannot gain an invented active spell.
            double maximumMana = resources == null ? 0 : resources.MaximumMana;
            sourceIndependentAttackers.Add(key, new SourceIndependentAttacker {
                Key = key, Rawcode = rawcode,
                OwnerInstanceId = ownerInstanceId, StoryLane = storyLane,
                X = x, Y = y, ExpiresAt = expiresAt, SkillProfile = skillProfile,
                MaximumMana = maximumMana, Mana = maximumMana,
                SourceManaRegenPerSecond = resources == null ? 0 : resources.ManaRegeneration
            });
        }

        // A source-model summon/satellite/song is not a roster character,
        // but its real hit still contributes to the live creator's story MVP.
        // Never attribute a detached actor, unknown key or removed owner.
        internal bool TryGetSourceIndependentAttackOwner(string attackerKey,
            out long ownerId)
        {
            ownerId = 0;
            SourceIndependentAttacker actor;
            if (!UsesSourceModelBasicAttacks || string.IsNullOrWhiteSpace(attackerKey) ||
                !sourceIndependentAttackers.TryGetValue(attackerKey, out actor) ||
                !characters.ContainsKey(actor.OwnerInstanceId)) return false;
            ownerId = actor.OwnerInstanceId;
            return true;
        }

        // Presentation-only lookup: the same native independent attacker
        // supplies actual damage while the Unity view draws a projectile.
        // Permanent U-14 attacks have numeric roster actor IDs and never
        // qualify as a transient Black Arrow effect.
        public bool TryGetSourceMoriaBlackArrowProjectileOrigin(
            string attackKey, out double x, out double y)
        {
            x = 0; y = 0;
            SourceIndependentAttacker actor;
            if (!UsesSourceModelBasicAttacks || string.IsNullOrWhiteSpace(attackKey) ||
                !sourceIndependentAttackers.TryGetValue(attackKey, out actor) ||
                !actor.ModelBlackArrowEffect ||
                !characters.ContainsKey(actor.OwnerInstanceId))
                return false;
            x = actor.X;
            y = actor.Y;
            return true;
        }

        // Rendering-only source lookup for model shadow, Uta song and Black
        // Arrow effects. It cannot be used to select or acquire a unit.
        public bool TryGetSourceTransientProjectileOrigin(
            string attackKey, out double x, out double y, out string rawcode)
        {
            x = 0; y = 0; rawcode = null;
            SourceIndependentAttacker actor;
            if (!UsesSourceModelBasicAttacks || string.IsNullOrWhiteSpace(attackKey) ||
                !sourceIndependentAttackers.TryGetValue(attackKey, out actor) ||
                !(actor.RenderAsTransientProjectile || actor.ModelBlackArrowEffect) ||
                !characters.ContainsKey(actor.OwnerInstanceId))
                return false;
            x = actor.X;
            y = actor.Y;
            rawcode = actor.Rawcode;
            return true;
        }

        public double? PracticeSatelliteManaOf(long id)
        {
            SourceIndependentAttacker actor;
            return UsesSourceModelBasicAttacks &&
                sourceIndependentAttackers.TryGetValue(SatelliteAttackKey(id), out actor)
                ? (double?)actor.Mana : null;
        }

        public double? PracticeSatelliteMaximumManaOf(long id)
        {
            SourceIndependentAttacker actor;
            return UsesSourceModelBasicAttacks &&
                sourceIndependentAttackers.TryGetValue(SatelliteAttackKey(id), out actor)
                ? (double?)actor.MaximumMana : null;
        }

        private void AdvanceSourceIndependentMana(double elapsed)
        {
            if (elapsed <= 0) return;
            double before = effectExecutor.Time, after = before + elapsed;
            foreach (var actor in sourceIndependentAttackers.Values)
            {
                if (actor.MaximumMana <= 0) continue;
                double added = actor.SourceManaRegenPerSecond * elapsed;
                double bonusTime = Math.Max(0, Math.Min(after, actor.SatelliteRegenEndsAt) -
                    Math.Max(before, actor.SatelliteRegenBeginsAt));
                added += actor.SatelliteRegenPerSecond * bonusTime;
                actor.Mana = Math.Min(actor.MaximumMana, actor.Mana + added);
            }
        }

        private void StartSourceSatelliteManaRegeneration(long creatorId,
            double perSecond, double duration)
        {
            foreach (var satellite in sourceSatellites)
            {
                if (satellite.CreatorId != creatorId) continue;
                SourceIndependentAttacker actor;
                if (!sourceIndependentAttackers.TryGetValue(
                    SatelliteAttackKey(satellite.Id), out actor)) continue;
                actor.SatelliteRegenBeginsAt = effectExecutor.Time;
                actor.SatelliteRegenEndsAt = effectExecutor.Time + duration;
                actor.SatelliteRegenPerSecond = perSecond;
            }
        }

        private void UnregisterSourceIndependentAttack(string key)
        {
            if (!sourceIndependentAttackers.Remove(key)) return;
            attackPipeline.RemoveUnit(key);
            unitStatuses.Remove(key);
        }

        private bool CanSourceIndependentAttack(SourceIndependentAttacker actor,
            SpatialCombatEnemy enemy)
        {
            // The independent actor is fixed to its origin lane. Its owner
            // cannot drag its attacks into ordinary combat while fighting
            // a side objective or decoding the Poneglyph.
            if (!enemy.Enemy.IsAlive ||
                !characters.ContainsKey(actor.OwnerInstanceId) ||
                IsSharedObjectiveDeployed(actor.OwnerInstanceId) ||
                IsOriginalPoneglyphChanneling(actor.OwnerInstanceId) ||
                actor.StoryLane != IsStoryDeployed(actor.OwnerInstanceId))
                return false;
            return actor.StoryLane ? enemy.Kind == CharacterEnemyKind.Story :
                enemy.Kind == CharacterEnemyKind.Line ||
                enemy.Kind == CharacterEnemyKind.Boss ||
                enemy.Kind == CharacterEnemyKind.Enraged;
        }

        private bool OrderSourceIndependentAttackOnce(string key, string targetId)
        {
            SourceIndependentAttacker actor;
            SpatialCombatEnemy target;
            if (!sourceIndependentAttackers.TryGetValue(key, out actor) ||
                actor.Blocked || !enemies.TryGetValue(targetId, out target) ||
                !CanSourceIndependentAttack(actor, target)) return false;
            actor.AttackOnceTargetId = targetId;
            return true;
        }

        private List<TargetCandidate> SourceIndependentTargets(SourceIndependentAttacker actor)
        {
            var candidates = new List<TargetCandidate>();
            if (actor.AttackOnceTargetId != null)
            {
                SpatialCombatEnemy ordered;
                if (enemies.TryGetValue(actor.AttackOnceTargetId, out ordered) &&
                    CanSourceIndependentAttack(actor, ordered))
                {
                    double dx = ordered.X - actor.X, dy = ordered.Y - actor.Y;
                    return new List<TargetCandidate> {
                        new TargetCandidate(ordered.Id, dx * dx + dy * dy,
                            ordered.PathProgress, true)
                    };
                }
                actor.AttackOnceTargetId = null;
            }
            foreach (var enemy in enemies.Values)
            {
                if (!CanSourceIndependentAttack(actor, enemy)) continue;
                double dx = enemy.X - actor.X, dy = enemy.Y - actor.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (double.IsInfinity(distanceSquared)) continue;
                candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                    enemy.PathProgress, true));
            }
            candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return candidates;
        }

        private double? NextSourceIndependentAttackAt()
        {
            double? next = null;
            foreach (var actor in sourceIndependentAttackers.Values)
            {
                if (actor.Blocked || SourceIndependentTargets(actor).Count == 0) continue;
                double? at = attackPipeline.NextAttackAt(actor.Key);
                // A lifetime expiring exactly at the next attack time does
                // not authorize one last free hit. The terminal time is
                // exclusive for ALL independent effects, not only Moria.
                if (!at.HasValue || (actor.ExpiresAt.HasValue &&
                    at.Value >= actor.ExpiresAt.Value)) continue;
                if (!next.HasValue || at.Value < next.Value) next = at;
            }
            return next;
        }

        private int ExecuteReadySourceIndependentAttacks(int budget)
        {
            int fired = 0;
            var keys = new List<string>(sourceIndependentAttackers.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (fired >= budget) break;
                SourceIndependentAttacker actor;
                if (!sourceIndependentAttackers.TryGetValue(key, out actor) || actor.Blocked)
                    continue;
                var due = attackPipeline.NextAttackAt(key);
                if (!due.HasValue || !CombatScheduler.HasReached(scheduler.Time, due.Value) ||
                    (actor.ExpiresAt.HasValue &&
                     scheduler.Time >= actor.ExpiresAt.Value))
                    continue;
                var candidates = SourceIndependentTargets(actor);
                if (candidates.Count == 0) continue;
                BasicAttackResult hit;
                try
                {
                    if (actor.ModelShadowZombie)
                        RefreshModelShadowZombieInheritedStatuses(actor);
                    if (!attackPipeline.TryBasicAttack(key, candidates, out hit)) continue;
                }
                catch (NotSupportedException ex)
                {
                    actor.Blocked = true;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, key, null, null,
                        "MODEL_ONLY 독립 평타 피해 미연결: " + ex.Message, 0));
                    continue;
                }
                fired++;
                // One normal scheduled hit fulfils B6o's order; the h04O
                // continues its existing 0.33s autonomous cadence afterward.
                actor.AttackOnceTargetId = null;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    key, hit.TargetId, null, "MODEL_ONLY " + actor.Rawcode + " 기본 공격", hit.ActualDamage));
                SpatialCombatEnemy victim;
                if (enemies.TryGetValue(hit.TargetId, out victim))
                {
                    CompleteSourceIndependentHit(actor, victim, hit.ActualDamage);
                    if (actor.Rawcode == "h05D" && hit.BasicHitApplied)
                        OnSourceMoriaShadowPositiveHit(actor.OwnerInstanceId,
                            victim, hit.ActualDamage);
                    TryAutoCastSourceSatelliteMana(actor, victim);
                }
                if (enemies.TryGetValue(hit.TargetId, out victim) && !victim.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        key, hit.TargetId, null, "MODEL_ONLY 독립 공격 처치", 0));
            }
            RemoveDefeatedEnemies();
            return fired;
        }

        // Shared tooltip-damage pipeline for autonomous units and approved
        // fallback packets. It preserves lane, armor, status, actual HP loss,
        // kill accounting and sorted target enumeration. Never raw-subtract HP.
        private void ApplySourceModelAreaPacket(string attackerId, bool storyLane,
            SpatialCombatEnemy center, double radius, double rawDamage,
            Ordr323AttackType attackType, string skillName,
            double primaryBossMaxHealthPercent = 0,
            double primaryEnragedMaxHealthPercent = 0)
        {
            ApplySourceModelAreaPacketAt(attackerId, storyLane, center,
                center.X, center.Y, radius, rawDamage, attackType, skillName,
                primaryBossMaxHealthPercent, primaryEnragedMaxHealthPercent);
        }

        // Delayed native effects may capture a location while the target
        // continues to move. This overload preserves the original location
        // without changing the existing packet's armor/HP/kill accounting.
        private void ApplySourceModelAreaPacketAt(string attackerId, bool storyLane,
            SpatialCombatEnemy center, double centerX, double centerY,
            double radius, double rawDamage, Ordr323AttackType attackType,
            string skillName, double primaryBossMaxHealthPercent = 0,
            double primaryEnragedMaxHealthPercent = 0)
        {
            if (radius < 0 || rawDamage < 0 || double.IsNaN(rawDamage) ||
                double.IsInfinity(rawDamage)) throw new ArgumentOutOfRangeException(nameof(rawDamage));
            // Real roster attackers use the same objective-specific lane
            // filter as basic hits. The legacy story boolean alone would
            // discard every shared boss/Sea King area hit, or allow a native
            // summon to keep attacking the main lane while its owner sorties.
            long rosterActorId;
            CharacterInstance rosterActor = null;
            SourceIndependentAttacker independentActor = null;
            if (!string.IsNullOrWhiteSpace(attackerId) &&
                long.TryParse(attackerId, out rosterActorId))
                characters.TryGetValue(rosterActorId, out rosterActor);
            if (rosterActor == null &&
                !sourceIndependentAttackers.TryGetValue(attackerId ?? "",
                    out independentActor))
                return;
            var targets = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive ||
                    (radius == 0 && target.Id != center.Id) ||
                    (rosterActor != null ?
                        !CanAttackInThisLane(rosterActor, target) :
                        !CanSourceIndependentAttack(independentActor, target)))
                    continue;
                double dx = target.X - centerX, dy = target.Y - centerY;
                if (dx * dx + dy * dy <= radius * radius) targets.Add(target);
            }
            targets.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in targets)
            {
                Ordr323ArmorType armor;
                if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        target.Enemy.Defenses.WarcraftArmorType, out armor))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        attackerId, target.Id, skillName,
                        "TOOLTIP_MODEL_ONLY 방어 자료 부족: 범위 피해 보류", 0));
                    continue;
                }
                double amount = rawDamage;
                if (target.Id == center.Id && target.Kind == CharacterEnemyKind.Boss)
                    amount += target.Enemy.MaxHealth * primaryBossMaxHealthPercent / 100.0;
                if (target.Id == center.Id && target.Kind == CharacterEnemyKind.Enraged)
                    amount += target.Enemy.MaxHealth * primaryEnragedMaxHealthPercent / 100.0;
                double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                    amount, attackType, armor, true, true,
                    target.Enemy.Defenses.BaseArmor.Value -
                    SourceModelUnitEffects.ResolvePracticeArmorReduction(
                        target.Enemy.Status.Snapshot()), false, 0, 1);
                if (double.IsNaN(modeled) || double.IsInfinity(modeled) ||
                    modeled >= long.MaxValue)
                    throw new OverflowException("모델 독립 스킬 피해 범위 초과");
                long actual;
                if (!resolution.TryApplySkillDamage(attackerId, target.Id,
                        (long)Math.Floor(modeled), out actual)) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                    attackerId, target.Id, skillName,
                    "TOOLTIP_MODEL_ONLY 원작 최종 면역·타이밍 미검증", actual));
                if (!target.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        attackerId, target.Id, skillName, "모델 추가 피해로 처치", actual));
            }
        }

        private void ApplySourceModelFixedPrimaryPacket(string attackerId,
            SpatialCombatEnemy target, long damage, string skill)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (!target.Enemy.IsAlive) return;
            long actual;
            if (!resolution.TryApplySkillDamage(attackerId, target.Id, damage, out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                attackerId, target.Id, skill,
                "TOOLTIP_MODEL_ONLY 고정 피해 / 원작 면역·방어 무시 조건 미검증", actual));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    attackerId, target.Id, skill, "모델 고정 피해 처치", actual));
        }

        private void ApplySourceModelFixedAreaPacket(SourceIndependentAttacker actor,
            SpatialCombatEnemy center, double radius, long damage, string skill)
        {
            var targets = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!CanSourceIndependentAttack(actor, target)) continue;
                double dx = target.X - center.X, dy = target.Y - center.Y;
                if (dx * dx + dy * dy <= radius * radius) targets.Add(target);
            }
            targets.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in targets)
                ApplySourceModelFixedPrimaryPacket(actor.Key, target, damage, skill);
        }

        // A purchased satellite is not a character and cannot use the roster
        // mana auto-caster. Gate its independent action on pinned SLK mana and
        // complete tooltip fields; do not synthesize hero STR/AGI scaling.
        private void TryAutoCastSourceSatelliteMana(SourceIndependentAttacker actor,
            SpatialCombatEnemy primary)
        {
            if (!primary.Enemy.IsAlive || actor.MaximumMana <= 0) return;
            bool lilith = actor.Rawcode == "H0AB";
            if (!lilith && actor.Rawcode != "H0AE") return; // York has no documented mana spell.
            string name = lilith ? "릴리스 마나 공격" : "아틀라스 마나 공격";
            var skill = FindProfileSkill(actor.SkillProfile, name);
            double cost = lilith ? 90 : 110;
            double radius = lilith ? 500 : 625;
            if (skill == null || skill.Trigger != "satelliteActiveMana" ||
                skill.Number("manaCost") != cost || skill.Radius != radius ||
                skill.Target != "area" || actor.Mana < cost ||
                actor.MaximumMana != cost ||
                (lilith ? skill.Components.Count != 2 : skill.Components.Count != 1)) return;

            if (lilith)
            {
                var physical = skill.Components[0];
                var scaled = skill.Components[1];
                if (physical.Type != "physical" ||
                    physical.Number("amount") != 2200000 ||
                    physical.Number("agilityScalingPerPoint") != 22000 ||
                    scaled.Type != "fixed" ||
                    scaled.Text("basis") != "targetMaxHealth" ||
                    scaled.Number("percentPerAgility") != .2 ||
                    scaled.Text("scope") != "singlePrimaryTarget") return;
            }
            else
            {
                var fixedHit = skill.Components[0];
                if (fixedHit.Type != "fixed" ||
                    fixedHit.Number("amount") != 2000000 ||
                    fixedHit.Number("plusTargetMaxHealthPercentPerStrength") != .1) return;
            }

            actor.Mana -= cost;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                actor.Key, primary.Id, name,
                "TOOLTIP_MODEL_ONLY 원본 위성 MP " + cost + " 소비 / " +
                "원본 능력치 계수는 증거 공급 시만 적용", 0));
            if (lilith)
            {
                double agility;
                bool supplied = TrySourceModelSatelliteAttribute(actor,
                    "agility", out agility);
                double damage = 2200000 + (supplied ? 22000 * agility : 0);
                if (!double.IsNaN(damage) && !double.IsInfinity(damage))
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        radius, damage, Ordr323AttackType.Spells, name);
                long fixedAmount;
                if (supplied && primary.Enemy.IsAlive &&
                    SourceModelSafeFixedAmount(primary.Enemy.MaxHealth *
                        (.002 * agility), out fixedAmount))
                    ApplySourceModelFixedPrimaryPacket(actor.Key, primary,
                        fixedAmount, name + " · 민첩 단일 고정 피해");
                if (!supplied)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        actor.Key, primary.Id, name,
                        "민첩 미공급: 원본 22000/포인트·최대HP 0.2%/포인트 보류", 0));
            }
            else
            {
                double strength;
                bool supplied = TrySourceModelSatelliteAttribute(actor,
                    "strength", out strength);
                ApplySourceModelSatelliteFixedArea(actor, primary,
                    radius, 2000000, supplied ? strength : 0, name);
                if (!supplied)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        actor.Key, primary.Id, name,
                        "힘 미공급: 원본 대상 최대HP 0.1%/포인트 보류", 0));
            }
        }

        private void ApplySourceModelSatelliteFixedArea(
            SourceIndependentAttacker actor, SpatialCombatEnemy center,
            double radius, double flat, double strength, string name)
        {
            var targets = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
            {
                double dx = enemy.X - center.X, dy = enemy.Y - center.Y;
                if (CanSourceIndependentAttack(actor, enemy) &&
                    dx * dx + dy * dy <= radius * radius) targets.Add(enemy);
            }
            targets.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in targets)
            {
                long damage;
                if (SourceModelSafeFixedAmount(flat +
                    target.Enemy.MaxHealth * (.001 * strength), out damage))
                    ApplySourceModelFixedPrimaryPacket(actor.Key, target, damage, name);
            }
        }

        private void RefreshModelShadowZombieInheritedStatuses(
            SourceIndependentAttacker actor)
        {
            CombatStatusEngine shadow;
            if (!unitStatuses.TryGetValue(actor.Key, out shadow)) return;
            foreach (var previous in actor.ShadowInheritedSources)
                shadow.RemoveSource(previous);
            actor.ShadowInheritedSources.Clear();
            CombatStatusEngine owner;
            if (!unitStatuses.TryGetValue(
                InstanceKey(actor.OwnerInstanceId), out owner)) return;
            var effects = owner.Snapshot();
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect.Type != CombatEffectType.AttackSpeedBuff &&
                    effect.Type != CombatEffectType.AttackPowerBuff) continue;
                string source = "practice-shadow:" + actor.Key + ":" + i;
                shadow.Apply(new CombatEffect(source, effect.Type,
                    CombatEffectOrigin.Aura, effect.Amount, 0));
                actor.ShadowInheritedSources.Add(source);
            }
        }

        private void CompleteSourceIndependentHit(SourceIndependentAttacker actor,
            SpatialCombatEnemy primary, long actualDamage)
        {
            if (actor.ModelShadowZombie)
            {
                // Tooltip-only 60%-of-basic splash, using the common
                // armor-aware executor; not native Zombie callback proof.
                if (actualDamage > 0)
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane,
                        primary, 375, actualDamage * .60,
                        Ordr323AttackType.Spells, "장군좀비 폭발 공격");
                return;
            }
            if (actor.Rawcode == "h085")
            {
                var skill = FindProfileSkill(actor.SkillProfile, "인술 분신");
                if (skill == null || skill.Components.Count != 2 ||
                    skill.Components[0].Type != "summon" ||
                    skill.Components[0].Number("basicAttackRadius") != 325 ||
                    skill.Components[0].Number("basicAttackMagicalDamage") != 50000) return;
                ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                    325, 50000, Ordr323AttackType.Spells, "인술 분신 범위 마법");
                var followup = FindProfileSkill(actor.SkillProfile, "급습");
                if (followup != null && followup.Trigger == "onAttack" &&
                    followup.ChancePercent == 15 &&
                    followup.Number("summonedCloneProcChanceMultiplier") == .5 &&
                    followup.Number("summonedCloneEffectMultiplier") == .5 &&
                    followup.Components.Count == 3 &&
                    followup.Components[0].Type == "magical" &&
                    followup.Components[0].Number("amount") == 400000 &&
                    followup.Components[1].Number("percent") == 7 &&
                    followup.Components[2].Number("percent") == 1.25 &&
                    sourceModelRules.NextScriptRoll(1000) < 75)
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, "분신 급습",
                        "TOOLTIP_MODEL_ONLY 분신 7.5% / 피해 50%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        0, 200000, Ordr323AttackType.Spells, "분신 급습");
                    double extra = primary.Kind == CharacterEnemyKind.Line
                        ? primary.Enemy.MaxHealth * .035 :
                        primary.Kind == CharacterEnemyKind.Boss
                            ? primary.Enemy.MaxHealth * .00625 : 0;
                    if (extra > 0 && primary.Enemy.IsAlive)
                        ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                            0, extra, Ordr323AttackType.Spells, "분신 급습 · 비례 피해");
                }
                return;
            }
            if (actor.Rawcode != "H0AB" && actor.Rawcode != "H0AE") return;
            if (actor.Rawcode == "H0AB")
            {
                var stun = FindProfileSkill(actor.SkillProfile, "릴리스 단일 스턴");
                if (stun != null && stun.Trigger == "satelliteOnAttack" &&
                    stun.ChancePercent == 22 && stun.Components.Count == 2 &&
                    stun.Components[0].Type == "physical" &&
                    stun.Components[0].Number("amount") == 220000 &&
                    stun.Components[1].Type == "stun" &&
                    stun.Components[1].Number("durationSeconds") == .75 &&
                    SourceRollSkill(stun))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, stun.Name, "TOOLTIP_MODEL_ONLY 릴리스 22%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        0, 220000, Ordr323AttackType.Spells, stun.Name);
                    ApplySourceModelLineStun(actor.Key, primary, stun.Name, .75);
                }
                var area = FindProfileSkill(actor.SkillProfile, "릴리스 광역 마법");
                if (area != null && area.Trigger == "satelliteOnAttack" &&
                    area.ChancePercent == 11 && area.Radius == 475 &&
                    area.Components.Count == 2 &&
                    area.Components[0].Type == "magical" &&
                    area.Components[0].Number("amount") == 220000 &&
                    area.Components[1].Type == "magical" &&
                    area.Components[1].Number("extraBossMaxHealthPercent") == 2 &&
                    area.Components[1].Number("extraEnragedMaxHealthPercent") == 4.4 &&
                    SourceRollSkill(area))
                {
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, area.Name, "TOOLTIP_MODEL_ONLY 릴리스 11%", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        475, 220000, Ordr323AttackType.Spells, area.Name, 2, 4.4);
                }
            }
            else
            {
                var explosive = FindProfileSkill(actor.SkillProfile, "아틀라스 평타·치명타");
                if (explosive != null && explosive.Trigger == "satelliteOnAttack" &&
                    explosive.ChancePercent == 25 && explosive.Components.Count == 3 &&
                    explosive.Components[0].Type == "explosive" &&
                    explosive.Components[0].Number("attackPowerPercent") == 45 &&
                    explosive.Components[0].Number("radius") == 400 &&
                    explosive.Components[1].Type == "critical" &&
                    explosive.Components[1].Number("multiplier") == 2.5 &&
                    explosive.Components[2].Type == "stun" &&
                    explosive.Components[2].Number("durationSeconds") == 1 &&
                    SourceRollSkill(explosive))
                {
                    // The map object dice are known, but native attack-power
                    // conversion and crit occurrence order are not witnessed.
                    double power = sourceModelRules.RollSourceObjectAttackPower("H0AE");
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        actor.Key, primary.Id, explosive.Name,
                        "TOOLTIP_MODEL_ONLY 25% / 공격력45% × 2.5 모델", 0));
                    ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                        400, power * .45 * 2.5, Ordr323AttackType.Spells,
                        explosive.Name);
                    foreach (var target in enemies.Values)
                    {
                        double dx = target.X - primary.X, dy = target.Y - primary.Y;
                        if (dx * dx + dy * dy <= 400 * 400 &&
                            CanSourceIndependentAttack(actor, target))
                            ApplySourceModelLineStun(actor.Key, target, explosive.Name, 1);
                    }
                }
                var haste = FindProfileSkill(actor.SkillProfile, "아틀라스 날뛰기");
                if (haste != null && haste.Trigger == "satelliteOnAttack" &&
                    haste.ChancePercent == 15 && haste.Components.Count == 2 &&
                    haste.Components[0].Type == "selfAttackSpeedBuff" &&
                    haste.Components[0].Number("percent") == 45 &&
                    haste.Components[0].Number("durationSeconds") == 2.25 &&
                    SourceRollSkill(haste))
                {
                    string key = "practice-aura:atlas-haste:" + actor.Key;
                    var status = unitStatuses[actor.Key];
                    status.RemoveSource(key);
                    status.Apply(new CombatEffect(key, CombatEffectType.AttackSpeedBuff,
                        CombatEffectOrigin.TriggeredSkill, 45, 2.25));
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                        actor.Key, actor.Key, haste.Name,
                        "TOOLTIP_MODEL_ONLY 45% 공격속도 2.25초 / 힘 계수 피해 보류", 0));
                    if (haste.Components[1].Type == "magical" &&
                        haste.Components[1].Number("amount") == 400000 &&
                        haste.Components[1].Number("strengthScalingPerPoint") == 4000)
                    {
                        double strength;
                        bool supplied = TrySourceModelSatelliteAttribute(actor,
                            "strength", out strength);
                        double raw = 400000 + (supplied ? 4000 * strength : 0);
                        if (!double.IsNaN(raw) && !double.IsInfinity(raw))
                            ApplySourceModelAreaPacket(actor.Key, actor.StoryLane, primary,
                                0, raw, Ordr323AttackType.Spells, haste.Name);
                    }
                }
            }
        }

        private static ImportedCombatSkill FindProfileSkill(CharacterSkillProfile profile,
            string name)
        {
            if (profile == null) return null;
            foreach (var skill in profile.Skills)
                if (skill.Name == name) return skill;
            return null;
        }

        private void ApplySourceModelLineStun(string actorKey,
            SpatialCombatEnemy target, string skill, double seconds)
        {
            if (!target.Enemy.IsAlive || target.Kind != CharacterEnemyKind.Line) return;
            string key = "practice-skill-stun:" + actorKey + ":" + skill;
            target.Enemy.Status.RemoveSource(key);
            target.Enemy.Status.Apply(new CombatEffect(key, CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, seconds));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                actorKey, target.Id, skill,
                "TOOLTIP_MODEL_ONLY 라인 기절 / 원본 면역·저항 무시 미검증", 0));
        }
    }
}
