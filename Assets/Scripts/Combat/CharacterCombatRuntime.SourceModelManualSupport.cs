using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Complete numeric tooltip packets without a connected mobile manual
    // interface. These are explicitly MODEL_ONLY automatic Q/W/support actions,
    // never proof of Warcraft native casting or resistance parity.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceManualSupportSpec
        {
            internal readonly string CatalogId, Rawcode, Name, Trigger, Target;
            internal readonly double Speed, Power, HealthRegen, ManaRegen;
            internal readonly double Duration, Cooldown, ManaCost;
            internal readonly bool Area, Self;
            internal SourceManualSupportSpec(string id, string raw, string name,
                string trigger, string target, double speed, double power,
                double hp, double mp, double duration, double cooldown,
                bool area = false, bool self = false, double manaCost = 0)
            {
                CatalogId = id; Rawcode = raw; Name = name; Trigger = trigger;
                Target = target; Speed = speed; Power = power;
                HealthRegen = hp; ManaRegen = mp;
                Duration = duration; Cooldown = cooldown; ManaCost = manaCost;
                Area = area; Self = self;
            }
        }

        private sealed class SourceManualRegenLease
        {
            internal long Owner, Recipient;
            internal string Source;
            internal double End, Health, Mana;
        }

        // Existing R-05 Law and S-05 A08B accelerations are intentionally
        // excluded. X-06 already has a 40% passive aura: its active
        // tooltip cannot safely be stacked without original parity evidence.
        private static readonly SourceManualSupportSpec[] SourceManualSupports = {
            new SourceManualSupportSpec("L-08", "h03C", "외과수술(W)",
                "manual", "ally", 150, 0, 0, 0, 12, 50),
            new SourceManualSupportSpec("H-08", "h03P", "선택 아군 가속",
                "manual", null, 150, 0, 0, 0, 12, 50),
            new SourceManualSupportSpec("T-03", "h07N", "과열",
                "manual", "alliedSelected", 150, 0, 0, 0, 12, 50),
            new SourceManualSupportSpec("R-31", "h02A", "호르몬 버프",
                "manual", "selectedAlly", 0, 35, 0, 0, 25, 50),
            new SourceManualSupportSpec("R-38", "h02H", "로어",
                "manual", "allyArea", 0, 20, 0, 0, 15, 35, area: true),
            new SourceManualSupportSpec("RN-11", "h06Y", "판단력(Q)",
                "manual", "alliedArea", 0, 40, .3, .3, 50, 30, area: true),
            // No target selector is documented for H-18. Apply the
            // specified buff and regeneration to the casting unit only.
            new SourceManualSupportSpec("H-18", "h03Y", "체력회복 공격력 버프",
                "manual", null, 0, 45, 1, 0, 50, 30, self: true),
            // Koby's separate 115-MP support skill is not his ordinary
            // on-attack proc, and must not be re-rolled when casting.
            new SourceManualSupportSpec("L-31", "h03E", "용기의 외침",
                "activeMana", "alliedArea", 0, 20, 1, 1, 5, 0,
                area: true, manaCost: 115),
            // The imported Cracker biscuit has no duration. A removable,
            // owner-bound aura lease avoids inventing a timed expiration.
            new SourceManualSupportSpec("L-33", "h03H", "크래커 비스킷",
                "manual", "ownedAlly", 9, 19, 0, 0, 0, 240)
        };

        private readonly Dictionary<long, double> sourceManualReadyAt =
            new Dictionary<long, double>();
        private readonly Dictionary<string, long> sourceManualRecipients =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, SourceManualRegenLease> sourceManualRegens =
            new Dictionary<string, SourceManualRegenLease>(StringComparer.Ordinal);

        // Called both on roster removal and on a committed lane transfer.
        // Transfers keep the original cooldown; deletion releases it.
        private void ForgetSourceManualSupport(long instanceId, bool removeCooldown)
        {
            if (removeCooldown) sourceManualReadyAt.Remove(instanceId);
            var removed = new List<string>();
            string owned = "practice-source-manual:" + instanceId + ":";
            foreach (var record in sourceManualRecipients)
                if (record.Key.StartsWith(owned, StringComparison.Ordinal) ||
                    record.Value == instanceId)
                    removed.Add(record.Key);
            foreach (var source in removed)
            {
                long recipient;
                if (sourceManualRecipients.TryGetValue(source, out recipient))
                {
                    CombatStatusEngine status;
                    if (unitStatuses.TryGetValue(InstanceKey(recipient), out status))
                        status.RemoveSource(source);
                }
                sourceManualRecipients.Remove(source);
                sourceManualRegens.Remove(source);
            }
        }

        // Resource tick happens before the shared CombatStatusEngine advances.
        // Clip the regenerated interval to the exact buff expiry, and stop
        // immediately when dispelled, transferred or deleted.
        private void AdvanceSourceManualRegeneration(double seconds)
        {
            if (seconds <= 0 || sourceManualRegens.Count == 0) return;
            var stale = new List<string>();
            foreach (var item in sourceManualRegens)
            {
                var lease = item.Value;
                CharacterInstance owner, ally;
                CombatStatusEngine status;
                if (!characters.TryGetValue(lease.Owner, out owner) ||
                    !characters.TryGetValue(lease.Recipient, out ally) ||
                    IsStoryDeployed(lease.Owner) != IsStoryDeployed(lease.Recipient) ||
                    !unitStatuses.TryGetValue(InstanceKey(lease.Recipient), out status))
                {
                    stale.Add(item.Key);
                    continue;
                }
                bool active = false;
                foreach (var effect in status.Snapshot())
                    if (effect.SourceId == lease.Source &&
                        effect.Type == CombatEffectType.AttackPowerBuff)
                    {
                        active = true;
                        break;
                    }
                if (!active || lease.End <= status.CurrentTime)
                {
                    stale.Add(item.Key);
                    continue;
                }
                double elapsed = Math.Min(seconds, lease.End - status.CurrentTime);
                ProvisionSourceModelAuraRecipientResources(ally);
                var pool = ally.Abilities.Resources;
                if (pool != null && elapsed > 0)
                {
                    pool.SetHealth(Math.Min(pool.MaximumHealth,
                        pool.Health + lease.Health * elapsed));
                    pool.SetMana(Math.Min(pool.MaximumMana,
                        pool.Mana + lease.Mana * elapsed));
                }
                if (lease.End <= status.CurrentTime + seconds)
                    stale.Add(item.Key);
            }
            foreach (string key in stale) sourceManualRegens.Remove(key);
        }

        private void PruneExpiredSourceManualRecipients()
        {
            if (sourceManualRecipients.Count == 0) return;
            var gone = new List<string>();
            foreach (var record in sourceManualRecipients)
            {
                CombatStatusEngine status;
                if (!unitStatuses.TryGetValue(InstanceKey(record.Value), out status))
                {
                    gone.Add(record.Key);
                    continue;
                }
                bool exists = false;
                foreach (var active in status.Snapshot())
                    if (active.SourceId == record.Key) { exists = true; break; }
                if (!exists) gone.Add(record.Key);
            }
            foreach (string key in gone)
            {
                sourceManualRecipients.Remove(key);
                sourceManualRegens.Remove(key);
            }
        }

        private static ImportedCombatSkill SourceManualSkill(CharacterInstance actor,
            string name)
        {
            if (actor.Profile == null || actor.Profile.UnitId != actor.Catalog.Id)
                return null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Name == name) return skill;
            return null;
        }

        private static bool SourceManualSupportMatches(SourceManualSupportSpec spec,
            ImportedCombatSkill skill)
        {
            if (skill == null || skill.Trigger != spec.Trigger ||
                skill.Target != spec.Target ||
                skill.Number("cooldownSeconds") !=
                    (spec.Cooldown > 0 ? (double?)spec.Cooldown : null))
                return false;
            if (spec.CatalogId == "L-31")
                return skill.Number("manaCost") == 115 && skill.Radius == 900 &&
                    skill.Components.Count == 3 &&
                    skill.Components[0].Type == "allyAttackPowerBuff" &&
                    skill.Components[0].Number("percent") == 20 &&
                    skill.Components[0].Number("durationSeconds") == 5 &&
                    skill.Components[1].Type == "allyHealthRegeneration" &&
                    skill.Components[1].Number("perSecond") == 1 &&
                    skill.Components[1].Number("durationSeconds") == 5 &&
                    skill.Components[2].Type == "allyManaRegeneration" &&
                    skill.Components[2].Number("perSecond") == 1 &&
                    skill.Components[2].Number("durationSeconds") == 5;
            if (spec.CatalogId == "RN-11")
                return skill.Components.Count == 3 &&
                    skill.Components[0].Type == "allyHealthRegeneration" &&
                    skill.Components[0].Number("perSecond") == spec.HealthRegen &&
                    skill.Components[0].Number("durationSeconds") == spec.Duration &&
                    skill.Components[1].Type == "allyManaRegeneration" &&
                    skill.Components[1].Number("perSecond") == spec.ManaRegen &&
                    skill.Components[1].Number("durationSeconds") == spec.Duration &&
                    skill.Components[2].Type == "allyAttackPowerBuff" &&
                    skill.Components[2].Number("percent") == spec.Power &&
                    skill.Components[2].Number("durationSeconds") == spec.Duration;
            if (spec.CatalogId == "H-18")
                return skill.Components.Count == 2 &&
                    skill.Components[0].Type == "allyAttackPowerBuff" &&
                    skill.Components[0].Number("percent") == spec.Power &&
                    skill.Components[0].Number("durationSeconds") == spec.Duration &&
                    skill.Components[1].Type == "healthRegeneration" &&
                    skill.Components[1].Number("perSecond") == spec.HealthRegen &&
                    skill.Components[1].Number("durationSeconds") == spec.Duration;
            if (spec.CatalogId == "L-33")
                return skill.Id == "L-33:skill:1" &&
                    skill.OriginalFields.Count == 6 &&
                    Equals(skill.Get("cannotStackOnSameUnit"), true) &&
                    skill.Components.Count == 2 &&
                    skill.Components[0].Fields.Count == 2 &&
                    skill.Components[0].Type == "allyAttackPowerBuff" &&
                    skill.Components[0].Number("percent") == 19 &&
                    skill.Components[1].Fields.Count == 2 &&
                    skill.Components[1].Type == "allyAttackSpeedBuff" &&
                    skill.Components[1].Number("percent") == 9;
            if (skill.Components.Count != 1) return false;
            var effect = skill.Components[0];
            return (spec.Speed > 0 && effect.Type == "allyAttackSpeedBuff" &&
                    effect.Number("percent") == spec.Speed ||
                    spec.Power > 0 && effect.Type == "allyAttackPowerBuff" &&
                    effect.Number("percent") == spec.Power) &&
                effect.Number("durationSeconds") == spec.Duration;
        }

        private List<long> SourceManualRecipientsFor(CharacterInstance actor,
            SourceManualSupportSpec spec, IReadOnlyList<long> sortedIds)
        {
            var result = new List<long>();
            if (spec.Self)
            {
                result.Add(actor.Id);
                return result;
            }
            bool story = IsStoryDeployed(actor.Id);
            if (spec.Area)
            {
                foreach (long candidate in sortedIds)
                {
                    CharacterInstance ally;
                    if (characters.TryGetValue(candidate, out ally) &&
                        IsStoryDeployed(candidate) == story)
                        result.Add(candidate);
                }
                return result;
            }
            var candidates = new List<LawDpsCandidate>();
            foreach (long candidate in sortedIds)
            {
                CharacterInstance ally;
                if (candidate == actor.Id ||
                    !characters.TryGetValue(candidate, out ally) ||
                    !ally.AttackReady || ally.Mapping == null ||
                    IsOriginalUnitCasting(candidate) ||
                    IsStoryDeployed(candidate) != story) continue;
                var attack = ally.Mapping.AttackSpec.BasicAttack;
                if (attack == null || attack.IntervalSeconds <= 0) continue;
                if (spec.Speed > 0 && spec.CatalogId != "L-33" &&
                    unitStatuses[InstanceKey(candidate)].HasActiveEffect(
                        CombatEffectType.AttackSpeedBuff,
                        CombatEffectOrigin.TriggeredSkill)) continue;
                // Cracker cannot cast a second biscuit onto the same ally.
                // Other speed buffs do not block this distinct named effect.
                if (spec.CatalogId == "L-33")
                {
                    bool alreadyBiscuit = false;
                    foreach (var active in unitStatuses[InstanceKey(candidate)].Snapshot())
                        if (active.SourceId.Contains(":L-33:"))
                        { alreadyBiscuit = true; break; }
                    if (alreadyBiscuit) continue;
                }
                double dps = attack.NominalAttackPower / attack.IntervalSeconds;
                candidates.Add(new LawDpsCandidate(candidate, story, true, dps));
            }
            long? chosen = SpecialGradeMobilePolicy.ChooseLawTarget(story, candidates);
            if (chosen.HasValue) result.Add(chosen.Value);
            return result;
        }

        private void ApplySourceManualSupport(CharacterInstance actor,
            SourceManualSupportSpec spec, IReadOnlyList<long> recipients)
        {
            // Charge once for the entire effect packet, never per recipient.
            if (spec.ManaCost > 0)
                actor.Abilities.Resources.Spend(0, spec.ManaCost);
            foreach (long recipientId in recipients)
            {
                string source = "practice-source-manual:" + actor.Id + ":" +
                    spec.CatalogId + ":" + recipientId;
                var status = unitStatuses[InstanceKey(recipientId)];
                status.RemoveSource(source);
                var origin = spec.CatalogId == "L-33" ?
                    CombatEffectOrigin.Aura : CombatEffectOrigin.TriggeredSkill;
                if (spec.Speed > 0)
                    status.Apply(new CombatEffect(source,
                        CombatEffectType.AttackSpeedBuff,
                        origin, spec.Speed, spec.Duration));
                if (spec.Power > 0)
                    status.Apply(new CombatEffect(source,
                        CombatEffectType.AttackPowerBuff,
                        origin, spec.Power, spec.Duration));
                sourceManualRecipients[source] = recipientId;
                if (spec.HealthRegen > 0 || spec.ManaRegen > 0)
                    sourceManualRegens[source] = new SourceManualRegenLease {
                        Owner = actor.Id, Recipient = recipientId, Source = source,
                        End = status.CurrentTime + spec.Duration,
                        Health = spec.HealthRegen, Mana = spec.ManaRegen
                    };
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), InstanceKey(recipientId),
                    spec.Name, "TOOLTIP_MODEL_ONLY / 원본 지원 시전·중첩 미검증", 0));
            }
            sourceManualReadyAt[actor.Id] = Time +
                (spec.Cooldown > 0 ? spec.Cooldown : spec.Duration);
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                InstanceKey(recipients[0]), spec.Name,
                "TOOLTIP_MODEL_ONLY / 자동 지원 시전·같은 전장 도달 무제한", 0));
        }

        private bool SourceManualBurstMatches(CharacterInstance actor, string id)
        {
            if (actor.Catalog.Id != id || actor.Profile == null ||
                actor.Profile.UnitId != id) return false;
            var skill = SourceManualSkill(actor,
                id == "RN-03" ? "폭렬 익스플로전" : "저격(Q)");
            if (skill == null || skill.Trigger != "manual" ||
                skill.Target != (id == "RN-03" ? "area" : "single") ||
                skill.Number("cooldownSeconds") !=
                    (id == "RN-03" ? 150d : 250d) ||
                skill.Components.Count != (id == "RN-03" ? 1 : 2))
                return false;
            if (id == "RN-03")
                return skill.Radius == 800 &&
                    skill.Components[0].Type == "fixed" &&
                    skill.Components[0].Number("targetMaxHealthPercent") == 12.5;
            return skill.Components[0].Type == "magical" &&
                skill.Components[0].Number("amount") == 80000000 &&
                skill.Components[1].Type == "stun" &&
                skill.Components[1].Number("durationSeconds") == 1;
        }

        private bool SourceManualBurst(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            string id = actor.Catalog.Id;
            string source = InstanceKey(actor.Id);
            int before = events.Count;
            if (id == "RN-03")
            {
                var targets = SourceScriptTargets(actor, primary,
                    new SourceScriptHit(SourceScriptHitShape.Circle,
                        800, 0, 0, 1, 1, Ordr323AttackType.Spells));
                if (targets.Count == 0) return false;
                foreach (var target in targets)
                {
                    double amount = target.Enemy.MaxHealth * .125;
                    if (double.IsNaN(amount) || double.IsInfinity(amount) ||
                        amount >= long.MaxValue)
                        throw new OverflowException("메구밍 범위 고정피해가 범위를 초과했습니다.");
                    long actual;
                    if (!resolution.TryApplySkillDamage(source, target.Id,
                            (long)Math.Floor(amount), out actual)) continue;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage, source, target.Id,
                        "폭렬 익스플로전",
                        "TOOLTIP_MODEL_ONLY / 최대 체력 12.5% 고정피해·원본 면역 미검증",
                        actual));
                    if (!target.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated, source, target.Id,
                            "폭렬 익스플로전", "연습 스킬 처치", actual));
                }
            }
            else
            {
                Ordr323ArmorType armor;
                if (!primary.Enemy.Defenses.BaseArmor.HasValue ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        primary.Enemy.Defenses.WarcraftArmorType, out armor))
                    return false;
                var hit = new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 80000000, 1, 1, Ordr323AttackType.Spells);
                var script = new SourceScriptAttackSpec(id, "h06X",
                    "tooltip-manual-sniper", 1, 1,
                    Array.Empty<SourceScriptHit>(), new[] { hit }, null);
                ApplySourceScriptHits(actor, primary, script, script.OnProc);
                if (primary.Enemy.IsAlive &&
                    primary.Kind == CharacterEnemyKind.Line)
                {
                    string stunSource = "practice-source-manual-stun:" + actor.Id;
                    primary.Enemy.Status.RemoveSource(stunSource);
                    primary.Enemy.Status.Apply(new CombatEffect(stunSource,
                        CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill, 1, 1));
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.StatusApplied, source, primary.Id,
                        "저격(Q)", "TOOLTIP_MODEL_ONLY / 일반 적 1초 기절", 0));
                }
            }
            sourceManualReadyAt[actor.Id] = Time +
                (id == "RN-03" ? 150 : 250);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, source, primary.Id,
                id == "RN-03" ? "폭렬 익스플로전" : "저격(Q)",
                "TOOLTIP_MODEL_ONLY / 사용자 지정 대신 자동 Q 시전", 0));
            RemoveDefeatedEnemies();
            for (int i = before; i < events.Count; i++)
                pendingCommandEvents.Add(events[i]);
            return true;
        }

        // Called once after the integrated combat step, never while iterating
        // a damage callback. At most one automatic support or Q per owner.
        public int AutoCastSourceManualSupportAndBurst(int maximumPerFrame = 16)
        {
            if (maximumPerFrame < 1 || maximumPerFrame > 128)
                throw new ArgumentOutOfRangeException(nameof(maximumPerFrame));
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0) return 0;
            PruneExpiredSourceManualRecipients();
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            int count = 0;
            foreach (long id in ids)
            {
                if (count >= maximumPerFrame) break;
                CharacterInstance actor;
                if (!characters.TryGetValue(id, out actor) ||
                    actor.Profile == null || actor.Mapping == null ||
                    IsOriginalUnitCasting(id) ||
                    SourceFormIsCasting(id)) continue;
                double ready;
                if (sourceManualReadyAt.TryGetValue(id, out ready) &&
                    Time < ready) continue;
                bool handled = false;
                foreach (var spec in SourceManualSupports)
                {
                    if (actor.Catalog.Id != spec.CatalogId ||
                        actor.Mapping.AttackSpec.UnitRawCode != spec.Rawcode ||
                        !SourceManualSupportMatches(spec,
                            SourceManualSkill(actor, spec.Name))) continue;
                    if (spec.ManaCost > 0 &&
                        (actor.Abilities.Resources == null ||
                         !actor.Abilities.Resources.CanSpend(0, spec.ManaCost)))
                        continue;
                    var recipients = SourceManualRecipientsFor(actor, spec, ids);
                    if (recipients.Count == 0) continue;
                    ApplySourceManualSupport(actor, spec, recipients);
                    count++;
                    handled = true;
                    break;
                }
                if (handled) continue;
                if ((actor.Catalog.Id != "RN-03" ||
                     actor.Mapping.AttackSpec.UnitRawCode != "h065") &&
                    (actor.Catalog.Id != "RN-04" ||
                     actor.Mapping.AttackSpec.UnitRawCode != "h06X"))
                    continue;
                if (!SourceManualBurstMatches(actor, actor.Catalog.Id)) continue;
                var targets = BuildTargetCandidates(actor);
                if (targets.Count == 0) continue;
                string selected = ChoosePreviewTarget(actor, targets);
                SpatialCombatEnemy primary;
                if (selected == null || !enemies.TryGetValue(selected, out primary) ||
                    !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                    continue;
                if (SourceManualBurst(actor, primary)) count++;
            }
            return count;
        }

        // The Buggy attack-proc ally buff is separate from his other
        // on-attack damage packet and has independent 4.5% odds.
        private void TrySourceBuggyAllyAttackBuff(CharacterInstance actor)
        {
            if (actor.Catalog.Id != "E-05" || actor.Profile == null ||
                actor.Profile.UnitId != "E-05" ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h05A") return;
            var skill = SourceManualSkill(actor, "전투 아군 공격력 버프");
            if (skill == null || skill.Trigger != "onAttack" ||
                skill.Target != "allyArea" || skill.ChancePercent != 4.5 ||
                skill.Components.Count != 1 ||
                skill.Components[0].Type != "allyAttackPowerBuff" ||
                skill.Components[0].Number("percent") != 33 ||
                skill.Components[0].Number("durationSeconds") != 4.5)
                return;
            if (sourceModelRules.NextScriptRoll(1000000) >= 45000) return;
            foreach (var ally in characters.Values)
            {
                if (!ally.AttackReady ||
                    IsStoryDeployed(ally.Id) != IsStoryDeployed(actor.Id)) continue;
                string key = "practice-source-buggy:" + actor.Id + ":" + ally.Id;
                var status = unitStatuses[InstanceKey(ally.Id)];
                status.RemoveSource(key);
                status.Apply(new CombatEffect(key,
                    CombatEffectType.AttackPowerBuff,
                    CombatEffectOrigin.TriggeredSkill, 33, 4.5));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied, InstanceKey(actor.Id),
                    InstanceKey(ally.Id), "전투 아군 공격력 버프",
                    "TOOLTIP_MODEL_ONLY / 버기 독립 4.5% 아군 강화", 0));
            }
        }
    }
}
