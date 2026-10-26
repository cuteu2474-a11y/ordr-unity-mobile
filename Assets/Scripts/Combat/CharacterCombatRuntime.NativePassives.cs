using System;
using System.Globalization;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private Original323NativePassiveCatalog nativeAttackPassives;
        private HashSet<string> externallyHandledNativePassives;

        internal void ConfigureSourceNativePassives(Original323NativePassiveCatalog catalog,
            IEnumerable<string> externallyHandled)
        {
            if (characters.Count != 0 || nativeAttackPassives != null)
                throw new InvalidOperationException("네이티브 스킬은 유닛 소환 전에 연결하세요.");
            nativeAttackPassives = catalog ?? throw new ArgumentNullException(nameof(catalog));
            externallyHandledNativePassives = new HashSet<string>(
                externallyHandled ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public OriginalNativePassive ConnectedNativePassiveOf(long instanceId)
        {
            CharacterInstance actor;
            if (!characters.TryGetValue(instanceId, out actor) || actor.Mapping == null ||
                nativeAttackPassives == null) return null;
            var skill = nativeAttackPassives.ForRawcode(actor.Mapping.AttackSpec.UnitRawCode);
            return skill != null && !externallyHandledNativePassives.Contains(skill.AbilityRawcode) ? skill : null;
        }

        private void CompleteOriginalNativePassives(CharacterInstance actor, BasicAttackResult hit,
            CombatEnemy target)
        {
            foreach (var activation in hit.NativePassiveActivations)
            {
                var skill = activation.Skill;
                if (target == null || !ReferenceEquals(target, activation.Target) ||
                    actor.Mapping == null || actor.Mapping.AttackSpec.UnitRawCode != skill.OwnerRawcode)
                    throw new InvalidOperationException("네이티브 스킬의 실제 타격 신원이 다릅니다.");
                if (skill.Kind == OriginalNativePassiveKind.Stun)
                {
                    // Do not stun a corpse or apply a cached pre-hit immunity
                    // result after an actual target state change.
                    if (!target.IsAlive || hit.ActualDamage <= 0) continue;
                    if (target.NativePassiveTargetRevision != activation.TargetRevision ||
                        !ReferenceEquals(target.NativePassiveTargetProfile, activation.TargetProfile))
                    {
                        events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                            InstanceKey(actor.Id), hit.TargetId, skill.Name,
                            "타격 중 대상 면역·분류 상태가 변경되어 기절 적용 보류", 0));
                        continue;
                    }
                    double seconds = activation.TargetProfile.WarcraftHero ?
                        skill.HeroStunSeconds : skill.NormalStunSeconds;
                    if (seconds <= 0) continue;
                    target.Status.Apply(new CombatEffect("original-native:" +
                        skill.AbilityRawcode + ":" + InstanceKey(actor.Id), CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1, seconds));
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, skill.Name,
                        "원본 기절 " + seconds.ToString(CultureInfo.InvariantCulture) + "초", 0));
                }
                else
                {
                    // Critical damage is ALREADY in the normal attack packet,
                    // before armor/quantization. Never add a second HP packet.
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                        InstanceKey(actor.Id), hit.TargetId, skill.Name,
                        "원본 크리티컬 " + skill.CriticalMultiplier.ToString(CultureInfo.InvariantCulture) +
                        "배 · 기본 타격에 반영", 0));
                }
            }
        }
    }
}
