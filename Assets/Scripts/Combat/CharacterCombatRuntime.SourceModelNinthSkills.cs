using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Strict, source-ID-specific approval: a name or a generic component is
    // never enough to grant a new on-hit/on-declaration skill.
    public static class SourceModelNinthSkillCatalog
    {
        private static readonly Dictionary<string, string> Owners =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                {"h0CM", "L-39"}, {"h03G", "L-28"},
                {"h04C", "I-01"}, {"h04Q", "I-06"},
                {"h0BF", "MY-02"}, {"h08Q", "LI-06"},
                {"h09Y", "SR-01"}
            };

        public static bool HasOwner(string rawcode) =>
            rawcode != null && Owners.ContainsKey(rawcode);

        internal static bool OwnerMatches(string rawcode, string catalogId)
        {
            string expected;
            return rawcode != null && Owners.TryGetValue(rawcode, out expected) &&
                expected == catalogId;
        }

        public static bool Approved(ImportedCombatSkill skill)
        {
            if (skill == null) return false;
            switch (skill.Id)
            {
                case "L-39:skill:0": return Header(skill, "백발백중", "onNthSuccessfulBasicHit", "area", 800, null,
                        "requiredSuccessfulHits") && skill.Number("requiredSuccessfulHits") == 4 &&
                    Components(skill, "magical|amount", "physical|basis,percent,onlyTargetKinds,scope",
                        "magical|basis,percent,onlyTargetKinds,scope", "magical|amount,onlyTargetKinds,scope",
                        "stun|durationSeconds,ignoresResistance,scope") &&
                    skill.Components[0].Number("amount") == 500000 &&
                    skill.Components[1].Number("percent") == 22.5 &&
                    skill.Components[2].Number("percent") == 7.5 &&
                    skill.Components[3].Number("amount") == 2000000 &&
                    skill.Components[4].Number("durationSeconds") == 3;
                case "L-28:skill:1": return Header(skill, "해류 한판 업어치기", "onBasicAttackWithAbsoluteCooldown",
                        "area", null, null, "absoluteCooldownSeconds") &&
                    skill.Number("absoluteCooldownSeconds") == 7 &&
                    Components(skill, "summonProjectiles|count,durationSeconds,magicalDamageEach") &&
                    skill.Components[0].Number("count") == 10 &&
                    skill.Components[0].Number("durationSeconds") == 2 &&
                    skill.Components[0].Number("magicalDamageEach") == 150000;
                case "I-01:skill:3": return Header(skill, "기본 공격 범위 추가", "onEveryAttack", "area", 325, null) &&
                    Components(skill, "physical|basicAttackPercent") &&
                    skill.Components[0].Number("basicAttackPercent") == 95;
                case "I-06:skill:3": return Header(skill, "기본 공격 추가", "onEveryAttack", "area", 450, null) &&
                    Components(skill, "magical|basicAttackPercent,flatAmount") &&
                    skill.Components[0].Number("basicAttackPercent") == 66 &&
                    skill.Components[0].Number("flatAmount") == 333333;
                case "MY-02:skill:0": return Header(skill, "오니", "onEveryAttack", "single", null, null) &&
                    Components(skill, "stun|durationSeconds,ignoresResistance") &&
                    skill.Components[0].Number("durationSeconds") == 1;
                case "MY-02:skill:1": return Header(skill, "할퀴기", "onEveryAttack", "single", null, null) &&
                    Components(skill, "magical|amount", "explosive|targetCurrentHealthPercent,onlyTargetKinds") &&
                    skill.Components[0].Number("amount") == 50000 &&
                    skill.Components[1].Number("targetCurrentHealthPercent") == 8;
                case "MY-02:skill:2": return Header(skill, "휩쓸기", "onAttack", "area", 750, 9) &&
                    Components(skill, "magical|amount,multiplierRange",
                        "explosive|targetCurrentHealthPercent,onlyTargetKinds") &&
                    skill.Components[0].Number("amount") == 800000 &&
                    skill.Components[1].Number("targetCurrentHealthPercent") == 10;
                case "LI-06:skill:1": return Header(skill, "확정 단일 스턴", "onEveryAttack", null, null, 100) &&
                    Components(skill, "stun|durationSeconds,overrideBossSeconds") &&
                    skill.Components[0].Number("durationSeconds") == 3 &&
                    skill.Components[0].Number("overrideBossSeconds") == 1.5;
                case "SR-01:skill:2": return Header(skill, "피스톨 키스", "onAttack", "self", null, 8) &&
                    Components(skill, "selfAttackSpeedBuff|percent",
                        "magical|amount,multiplierRange,onEveryAttackWhileActive,radius",
                        "explosive|basis,percent,onlyTargetKinds",
                        "magical|amount,multiplierRange,onlyTargetKinds") &&
                    skill.Components[0].Number("percent") == 45 &&
                    skill.Components[1].Number("amount") == 150000 &&
                    skill.Components[1].Number("radius") == 425 &&
                    Equals(skill.Components[1].Get("onEveryAttackWhileActive"), true) &&
                    skill.Components[2].Number("percent") == 7 &&
                    skill.Components[3].Number("amount") == 300000;
                case "SR-01:skill:3": return Header(skill, "피스톨 키스 6회 발동 보너스",
                        "afterNthSkillActivation", "area", 525, null,
                        "afterSkill", "requiredOccurrences") &&
                    Equals(skill.Get("afterSkill"), "피스톨 키스") &&
                    skill.Number("requiredOccurrences") == 6 &&
                    Components(skill, "explosive|basis,percent,targetCount,onlyTargetKinds",
                        "explosiveDamageAmplification|percent,maxPercent") &&
                    skill.Components[0].Number("percent") == 6 &&
                    skill.Components[0].Number("targetCount") == 2 &&
                    skill.Components[1].Number("percent") == 3 &&
                    skill.Components[1].Number("maxPercent") == 25;
                default: return false;
            }
        }

        private static bool Header(ImportedCombatSkill skill, string name,
            string trigger, string target, double? radius, double? chance,
            params string[] extra)
        {
            if (skill.Name != name || skill.Trigger != trigger ||
                skill.Target != target || skill.Radius != radius ||
                skill.ChancePercent != chance) return false;
            var allowed = new HashSet<string>(StringComparer.Ordinal) {
                "name", "trigger", "target", "radius", "chancePercent", "components"
            };
            foreach (string key in extra) allowed.Add(key);
            foreach (string key in skill.OriginalFields.Keys)
                if (!allowed.Contains(key)) return false;
            return true;
        }

        private static bool Components(ImportedCombatSkill skill,
            params string[] schemas)
        {
            if (skill.Components.Count != schemas.Length) return false;
            for (int i = 0; i < schemas.Length; i++)
            {
                var parts = schemas[i].Split('|');
                if (skill.Components[i].Type != parts[0]) return false;
                var names = parts[1].Split(',');
                if (skill.Components[i].Fields.Count != names.Length + 1) return false;
                foreach (string key in names)
                    if (!skill.Components[i].Fields.ContainsKey(key)) return false;
            }
            return true;
        }
    }

    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<string, int> sourceModelNinthCounters =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> sourceModelNinthDue =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<long, int> sourceModelNinthPistolStacks =
            new Dictionary<long, int>();
        private readonly HashSet<long> sourceModelNinthPistolActive =
            new HashSet<long>();

        private sealed class SourceModelNinthProjectile
        {
            public long CasterId;
            public SpatialCombatEnemy Target;
            public string Name;
            public double NextAt;
            public double Interval;
            public double Damage;
            public int Remaining;
        }

        private readonly List<SourceModelNinthProjectile> sourceModelNinthProjectiles =
            new List<SourceModelNinthProjectile>();

        private ImportedCombatSkill SourceModelNinthFind(CharacterInstance actor,
            string name)
        {
            if (actor.Profile == null || actor.Profile.UnitId != actor.Catalog.Id)
                return null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Name == name &&
                    SourceModelNinthSkillCatalog.Approved(skill)) return skill;
            return null;
        }

        private int SourceModelNinthIncrement(long actorId, string key, int limit)
        {
            string id = actorId.ToString(CultureInfo.InvariantCulture) + ":" + key;
            int previous;
            sourceModelNinthCounters.TryGetValue(id, out previous);
            int next = previous >= limit ? 1 : previous + 1;
            sourceModelNinthCounters[id] = next;
            return next;
        }

        private void ExecuteSourceModelNinthDeclaredAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, string rawcode)
        {
            if (!SourceModelNinthSkillCatalog.OwnerMatches(rawcode, actor.Catalog.Id))
                return;
            switch (actor.Catalog.Id)
            {
                case "L-28": SourceModelNinthStartProjectiles(actor, primary); break;
                case "I-01": SourceModelNinthTryHit(actor, primary, "기본 공격 범위 추가"); break;
                case "I-06": SourceModelNinthTryHit(actor, primary, "기본 공격 추가"); break;
                case "MY-02":
                    SourceModelNinthTryHit(actor, primary, "오니");
                    SourceModelNinthTryHit(actor, primary, "할퀴기");
                    SourceModelNinthTryHit(actor, primary, "휩쓸기");
                    break;
                case "LI-06": SourceModelNinthTryHit(actor, primary, "확정 단일 스턴"); break;
                case "SR-01": SourceModelNinthPistolAttack(actor, primary); break;
            }
        }

        private void SourceModelNinthTryHit(CharacterInstance actor,
            SpatialCombatEnemy primary, string name)
        {
            var skill = SourceModelNinthFind(actor, name);
            if (skill == null) return;
            if (skill.ChancePercent.HasValue &&
                sourceModelRules.NextScriptRoll(1000000) >=
                (int)Math.Round(skill.ChancePercent.Value * 10000)) return;
            var shape = skill.Target == "area" ?
                SourceScriptHitShape.Circle : SourceScriptHitShape.Direct;
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                shape, skill.Radius ?? 0, 0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 9차 공격 선언", 0));
            if (skill.Id == "LI-06:skill:1")
            {
                SourceModelNinthAlvidaStun(actor, primary, skill);
                return;
            }
            ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
        }

        private void SourceModelNinthAlvidaStun(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            if (!primary.Enemy.IsAlive) return;
            double duration = primary.Kind == CharacterEnemyKind.Boss ?
                skill.Components[0].Number("overrideBossSeconds").Value :
                skill.Components[0].Number("durationSeconds").Value;
            string source = "practice-tooltip-stun:" + actor.Id + ":" + skill.Id;
            primary.Enemy.Status.RemoveSource(source);
            primary.Enemy.Status.Apply(new CombatEffect(source, CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, duration));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 보스 1.5초, 기타 3초 · 실제 면역 미검증", 0));
        }

        private void SourceModelNinthPistolAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var pistol = SourceModelNinthFind(actor, "피스톨 키스");
            var bonus = SourceModelNinthFind(actor, "피스톨 키스 6회 발동 보너스");
            if (pistol == null || bonus == null) return;
            // Existing speed is applied to future attacks. No tooltip duration
            // is supplied, so this practice buff is retained until despawn.
            if (sourceModelNinthPistolActive.Contains(actor.Id))
            {
                var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, pistol.Components[1].Number("radius").Value,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
                foreach (var target in targets)
                {
                    if (!target.Enemy.IsAlive) continue;
                    SourceModelNinthPistolDamage(actor, target, pistol.Components[1]);
                    if (target.Kind == CharacterEnemyKind.Line)
                        ApplySourceModelSeventhTimedHit(actor, target, pistol.Name,
                            target.Enemy.MaxHealth * pistol.Components[2].Number("percent").Value / 100 *
                            SourceModelNinthPistolExplosiveFactor(actor.Id),
                            "explosive");
                    if (target.Kind == CharacterEnemyKind.Boss ||
                        target.Kind == CharacterEnemyKind.Enraged ||
                        target.Kind == CharacterEnemyKind.Story)
                        SourceModelNinthPistolDamage(actor, target, pistol.Components[3]);
                }
            }
            if (sourceModelRules.NextScriptRoll(1000000) >=
                (int)(pistol.ChancePercent.Value * 10000)) return;
            sourceModelNinthPistolActive.Add(actor.Id);
            string actorKey = InstanceKey(actor.Id);
            string source = "practice-tooltip:" + actorKey + ":" + pistol.Id;
            var status = unitStatuses[actorKey];
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.Aura, pistol.Components[0].Number("percent").Value, 0));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                actorKey, primary.Id, pistol.Name,
                "TOOLTIP_MODEL_ONLY / 공속45% · 원본 유지시간 미기재", 0));
            if (SourceModelNinthIncrement(actor.Id, pistol.Id, 6) != 6) return;
            var targetsBonus = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, bonus.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            int affected = 0;
            foreach (var target in targetsBonus)
            {
                if (affected >= bonus.Components[0].Number("targetCount").Value) break;
                if (!target.Enemy.IsAlive || target.Kind != CharacterEnemyKind.Line) continue;
                ApplySourceModelSeventhTimedHit(actor, target, bonus.Name,
                    target.Enemy.MaxHealth * bonus.Components[0].Number("percent").Value / 100 *
                    SourceModelNinthPistolExplosiveFactor(actor.Id),
                    "explosive");
                affected++;
            }
            int previous;
            sourceModelNinthPistolStacks.TryGetValue(actor.Id, out previous);
            int next = Math.Min((int)bonus.Components[1].Number("maxPercent").Value,
                previous + (int)bonus.Components[1].Number("percent").Value);
            sourceModelNinthPistolStacks[actor.Id] = next;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                actorKey, primary.Id, bonus.Name,
                "TOOLTIP_MODEL_ONLY / 6회 발동 · 폭발 증폭 " + next + "%", 0));
        }

        private void SourceModelNinthPistolDamage(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatEffect component)
        {
            var range = component.Get("multiplierRange") as List<object>;
            if (range == null || range.Count != 2) return;
            double factor = Ordr323MapDamageMath.ScriptDamageBeforeDefense(1,
                Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
            ApplySourceModelSeventhTimedHit(actor, target, "피스톨 키스",
                component.Number("amount").Value * factor, "magical");
        }

        private double SourceModelNinthPistolExplosiveFactor(long actorId)
        {
            int amount;
            sourceModelNinthPistolStacks.TryGetValue(actorId, out amount);
            return 1 + amount / 100.0;
        }

        private void OnSourceModelNinthSuccessfulBasicHit(string actorKey,
            string rawcode, string targetId, long actual)
        {
            SourceModelTwentiethAfterBasicHit(actorKey, rawcode, targetId, actual);
            OnSourceModelThirtiethSuccessfulBasicHit(actorKey, rawcode, targetId, actual);
            if (!UsesSourceModelBasicAttacks || !processingStep || actual <= 0 ||
                rawcode != "h0CM") return;
            long id;
            CharacterInstance actor;
            if (!long.TryParse(actorKey, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) || !actor.AttackReady ||
                !SourceModelNinthSkillCatalog.OwnerMatches(rawcode, actor.Catalog.Id))
                return;
            var skill = SourceModelNinthFind(actor, "백발백중");
            if (skill == null || SourceModelNinthIncrement(id, skill.Id, 4) != 4)
                return;
            SpatialCombatEnemy primary;
            if (!enemies.TryGetValue(targetId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
            {
                var choices = BuildTargetCandidates(actor);
                string candidate = choices.Count == 0 ? null :
                    ChoosePreviewTarget(actor, choices);
                if (candidate == null || !enemies.TryGetValue(candidate, out primary) ||
                    !primary.Enemy.IsAlive) return;
            }
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                actorKey, primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 실명중 4회 누적", 0));
            ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
        }

        private void SourceModelNinthStartProjectiles(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = SourceModelNinthFind(actor, "해류 한판 업어치기");
            if (skill == null) return;
            string key = actor.Id + ":" + skill.Id;
            double due;
            if (sourceModelNinthDue.TryGetValue(key, out due) && due > Time) return;
            sourceModelNinthDue[key] = Time + skill.Number("absoluteCooldownSeconds").Value;
            var component = skill.Components[0];
            sourceModelNinthProjectiles.Add(new SourceModelNinthProjectile {
                CasterId = actor.Id, Target = primary, Name = skill.Name,
                NextAt = Time + component.Number("durationSeconds").Value /
                    component.Number("count").Value,
                Interval = component.Number("durationSeconds").Value /
                    component.Number("count").Value,
                Damage = component.Number("magicalDamageEach").Value,
                Remaining = (int)component.Number("count").Value
            });
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 2초 10발 · 투사체 이동 연출 미연결", 0));
        }

        private void AdvanceSourceModelNinthProjectiles(double elapsed)
        {
            if (!UsesSourceModelBasicAttacks || elapsed <= 0) return;
            for (int i = sourceModelNinthProjectiles.Count - 1; i >= 0; i--)
            {
                var entry = sourceModelNinthProjectiles[i];
                CharacterInstance actor;
                SpatialCombatEnemy current;
                if (!characters.TryGetValue(entry.CasterId, out actor) ||
                    !actor.AttackReady ||
                    !enemies.TryGetValue(entry.Target.Id, out current) ||
                    !ReferenceEquals(entry.Target, current) ||
                    !current.Enemy.IsAlive || !CanAttackInThisLane(actor, current))
                {
                    sourceModelNinthProjectiles.RemoveAt(i);
                    continue;
                }
                while (entry.Remaining > 0 && entry.NextAt <= Time + .000001)
                {
                    ApplySourceModelSeventhTimedHit(actor, current, entry.Name,
                        entry.Damage, "magical");
                    entry.Remaining--;
                    entry.NextAt += entry.Interval;
                    if (!current.Enemy.IsAlive) break;
                }
                if (entry.Remaining == 0 || !current.Enemy.IsAlive)
                    sourceModelNinthProjectiles.RemoveAt(i);
            }
        }

        private double SourceModelNinthDamageAdjustment(CharacterInstance actor,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelNinthSkillCatalog.Approved(skill)) return raw;
            if (component.Number("basicAttackPercent").HasValue)
                raw += actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower *
                    component.Number("basicAttackPercent").Value / 100;
            if (component.Number("flatAmount").HasValue)
                raw += component.Number("flatAmount").Value;
            if ((skill.Id == "MY-02:skill:1" || skill.Id == "MY-02:skill:2") &&
                component.Type == "explosive" &&
                target.Kind == CharacterEnemyKind.Line)
                raw += target.Enemy.Health *
                    component.Number("targetCurrentHealthPercent").Value / 100;
            return raw;
        }

        private void ForgetSourceModelNinthActor(long id)
        {
            string prefix = id.ToString(CultureInfo.InvariantCulture) + ":";
            var stale = new List<string>();
            foreach (string key in sourceModelNinthCounters.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(key);
            foreach (string key in stale) sourceModelNinthCounters.Remove(key);
            stale.Clear();
            foreach (string key in sourceModelNinthDue.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(key);
            foreach (string key in stale) sourceModelNinthDue.Remove(key);
            sourceModelNinthProjectiles.RemoveAll(p => p.CasterId == id);
            sourceModelNinthPistolStacks.Remove(id);
            sourceModelNinthPistolActive.Remove(id);
        }

        private void ForgetSourceModelNinthEnemy(string id)
        {
            sourceModelNinthProjectiles.RemoveAll(p => p.Target.Id == id);
        }
    }
}
