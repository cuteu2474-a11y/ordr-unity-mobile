using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 원본 war3map.j의 w5 등록→함수→Bdq(A04M)/Bd5(A04Q) 경로로 확인한 스킬만 연결.
    // 툴팁만 같은 다른 캐릭터는 이 목록에 자동 편입하지 않는다.
    public sealed class Ordr323VerifiedDefenseSkillLink
    {
        public string UnitId { get; private set; }
        public string UnitRawcode { get; private set; }
        public string SkillId { get; private set; }
        public string SkillName { get; private set; }
        public string Trigger { get; private set; }
        public string ComponentType { get; private set; }
        public int Value { get; private set; }
        public int PrimaryBonus { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string JassEvidence { get; private set; }

        public Ordr323VerifiedDefenseSkillLink(string unitId, string unitRawcode,
            int ordinal, string skillName, string trigger, string componentType,
            int value, int primaryBonus, string abilityRawcode, string evidence)
        {
            if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(unitRawcode) ||
                string.IsNullOrWhiteSpace(skillName) || string.IsNullOrWhiteSpace(trigger) ||
                string.IsNullOrWhiteSpace(evidence) || ordinal < 0 || value <= 0 ||
                primaryBonus < 0 || (componentType != "armorBreak" &&
                componentType != "magicResistanceReduction") ||
                (abilityRawcode != "A04M" && abilityRawcode != "A04Q"))
                throw new ArgumentException("검증된 JASS 방깎 연결 정보가 부족합니다.");
            UnitId = unitId; UnitRawcode = unitRawcode;
            SkillId = unitId + ":skill:" + ordinal;
            SkillName = skillName; Trigger = trigger;
            ComponentType = componentType; Value = value;
            PrimaryBonus = primaryBonus; AbilityRawcode = abilityRawcode;
            JassEvidence = evidence;
        }

        public bool Matches(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            if (profile == null || skill == null || component == null ||
                profile.UnitId != UnitId || skill.Id != SkillId ||
                skill.Name != SkillName || skill.Trigger != Trigger ||
                component.Type != ComponentType) return false;
            double? stated = component.Number(
                ComponentType == "armorBreak" ? "stacks" : "percent");
            return stated.HasValue && stated.Value == Value;
        }
    }

    public static class Ordr323VerifiedDefenseSkillLinks
    {
        // 실제 JASS line number (war3map.j SHA 96201e80e...)를 근거로 함.
        // 아래 번호는 Unity JSON ordinal이며 원본 Warcraft ability rawcode가 아님.
        private static readonly Ordr323VerifiedDefenseSkillLink[] Links =
        {
            new Ordr323VerifiedDefenseSkillLink(
                "H-10","h03W",1,"기본 공격 발동형 타격","onAttack",
                "armorBreak",1,1,"A04M","BrQ:w5(h03W,BrP) → BrP:17108 & BrN:17091"),
            new Ordr323VerifiedDefenseSkillLink(
                "H-10","h03W",0,"맥시멈 스윙","activeMana",
                "armorBreak",4,0,"A04M","BrQ:w5(h03W,BrP) → BrP mana100 → BrO:17095"),
            new Ordr323VerifiedDefenseSkillLink(
                "H-09","h03X",0,"뇌영","activeMana",
                "magicResistanceReduction",10,0,"A04Q",
                "Br9:17645 w5(h03X,Br8) → Br8 mana150 → Br6:17604 Bd5"),
            new Ordr323VerifiedDefenseSkillLink(
                "E-10","h05B",1,"미검","onAttack",
                "armorBreak",5,0,"A04M","B5a:w5(h05B,B5Z) → B5Z:33474"),
            new Ordr323VerifiedDefenseSkillLink(
                "I-03","h049",0,"패기발산","activeMana",
                "armorBreak",5,0,"A04M","B8x:37330 w5(h049,B8r) → B8q:37215"),
            new Ordr323VerifiedDefenseSkillLink(
                "I-09","h04B",3,"범위 마법 스턴","onAttack",
                "armorBreak",1,0,"A04M","B97:38668 w5(h04B,B93) → B92:38555")
        };

        public static IReadOnlyList<Ordr323VerifiedDefenseSkillLink> All
        {
            get { return Array.AsReadOnly(Links); }
        }

        public static bool TryMatch(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            out Ordr323VerifiedDefenseSkillLink match)
        {
            foreach (var link in Links)
                if (link.Matches(profile, skill, component))
                {
                    match = link;
                    return true;
                }
            match = null;
            return false;
        }
    }

    // 원작 능력 레벨의 영향을 받을 때만 적용. 다른 스킬의 미검증 상태는 거부.
    public enum Ordr323DebuffTiming
    {
        BeforeDamage,
        AfterDamage
    }

    public sealed class Ordr323VerifiedDefenseEffectRouter
    {
        // BrP: 주 타깃 Bdq(1) → 직접 Bcb → BrN의 광역 Bcb → 광역 Bdq(1).
        // Br6: Bd5(10) → BeC(10) → Bcb. 다른 JASS 콜백은 뒤에 적힌 순서.
        public bool HasPreDamagePart(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            Ordr323VerifiedDefenseSkillLink link;
            if (!Ordr323VerifiedDefenseSkillLinks.TryMatch(
                profile, skill, component, out link)) return false;
            return link.UnitId == "H-10" && link.SkillId == "H-10:skill:1" ||
                   link.UnitId == "H-09" && link.SkillId == "H-09:skill:0";
        }

        // 베르고의 직접 Bdq와 에넬의 영역 Bd5가 본 스킬 피해 전에 발생한다.
        public bool TryApplyAtPhase(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect,
            SpatialCombatEnemy target, bool primary,
            Ordr323DebuffTiming phase, out string reason)
        {
            Ordr323VerifiedDefenseSkillLink link;
            if (!Ordr323VerifiedDefenseSkillLinks.TryMatch(
                profile, skill, effect, out link))
            {
                reason = "원본 JASS 스킬 미연결";
                return false;
            }
            bool vergo = link.UnitId == "H-10" &&
                         link.SkillId == "H-10:skill:1";
            bool enel = link.UnitId == "H-09" &&
                        link.SkillId == "H-09:skill:0";
            if (phase == Ordr323DebuffTiming.BeforeDamage)
            {
                if (vergo && primary)
                    return ApplyDelta(casterId, link, target, 1, out reason);
                if (enel)
                    return ApplyDelta(casterId, link, target, link.Value, out reason);
                reason = "skipped"; // 이 타깃에는 선행 효과가 없음.
                return true;
            }
            if (enel)
            {
                reason = "skipped"; // 앞 단계에서 이미 적용했으므로 재적용 금지.
                return true;
            }
            // 베르고의 BrN 광역은 모든 타깃 +1. 주 대상의 +1은 이미 선행.
            return ApplyDelta(casterId, link, target, link.Value, out reason);
        }

        private readonly Dictionary<string,int> armorBreakStacks;
        private readonly Dictionary<string,int> provenA04MStacks;
        private readonly Dictionary<string,CombatStatusEngine> statuses;
        private long applicationId;

        public Ordr323VerifiedDefenseEffectRouter(Dictionary<string,int> stacks,
            Dictionary<string,int> provenStacks,
            Dictionary<string,CombatStatusEngine> enemyStatuses)
        {
            armorBreakStacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
            provenA04MStacks = provenStacks ??
                throw new ArgumentNullException(nameof(provenStacks));
            statuses = enemyStatuses ?? throw new ArgumentNullException(nameof(enemyStatuses));
        }

        public bool CanRoute(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect)
        {
            Ordr323VerifiedDefenseSkillLink link;
            return Ordr323VerifiedDefenseSkillLinks.TryMatch(
                profile, skill, effect, out link);
        }

        // W3X 원본이 확인된 능력 변동만 실제 상태에 등록.
        // 임의 보정, 범위 외 레벨, 비확정 출처의 상태와 섞지 않는다.
        public bool TryApply(string casterId, CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect,
            SpatialCombatEnemy target, bool isPrimary, out string reason)
        {
            reason = null;
            Ordr323VerifiedDefenseSkillLink link;
            if (!Ordr323VerifiedDefenseSkillLinks.TryMatch(profile,skill,effect,out link))
            {
                reason = "원본 Bdq/Bd5 효과 함수와 도감 스킬 연결 미확인";
                return false;
            }
            // 단독 실행 API는 베르고 2회 발생 결과까지 합산한다.
            // 실제 엔진은 시간 순서 보존을 위해 TryApplyAtPhase를 사용.
            return ApplyDelta(casterId,link,target,
                checked(link.Value + (isPrimary ? link.PrimaryBonus : 0)),out reason);
        }

        private bool ApplyDelta(string casterId,
            Ordr323VerifiedDefenseSkillLink link,
            SpatialCombatEnemy target, int delta, out string reason)
        {
            reason = null;
            if (target == null || !target.Enemy.IsAlive ||
                string.IsNullOrWhiteSpace(casterId))
            {
                reason = "공격자나 살아 있는 적 정보가 없습니다";
                return false;
            }
            if (delta == 0) return true;
            if (link.AbilityRawcode == "A04M")
            {
                int current;
                if (!armorBreakStacks.TryGetValue(target.Id, out current))
                {
                    reason = "아머브레이크 몬스터 등록 정보 없음"; return false;
                }
                int next = checked(current + delta);
                double reduction;
                if (!Ordr323VerifiedDefenseAbilities.TryGetArmorBreakFromStacks(
                    next, out reduction))
                {
                    // 1~3스택과 76+스택은 현 W3A 수정값에 없음.
                    // 스택 기록은 실제 존재하지만 피해식 적용은 추가 자료를 필요로 한다.
                    if (next > 75)
                    {
                        reason = "A04M 원본 능력 레벨 한도 초과/원작 처리 미확인";
                        return false;
                    }
                }
                armorBreakStacks[target.Id] = next;
                provenA04MStacks[target.Id] = next;
                return true;
            }
            if (link.AbilityRawcode == "A04Q")
            {
                CombatStatusEngine status;
                if (!statuses.TryGetValue(target.Id, out status))
                {
                    reason = "원본 마방 능력 대상 상태 없음"; return false;
                }
                int? baseLevel = target.Enemy.Defenses.MagicAbilityLevel;
                if (!baseLevel.HasValue)
                {
                    reason = "몬스터 기본 A04Q 레벨 원본 근거 없음"; return false;
                }
                int total = baseLevel.Value;
                foreach (var buff in status.Snapshot())
                    if (buff.Type == CombatEffectType.OriginalA04QLevelIncrease)
                    {
                        if (buff.Amount != Math.Floor(buff.Amount) ||
                            buff.Amount > int.MaxValue ||
                            !buff.SourceId.StartsWith("jass:A04Q:",StringComparison.Ordinal))
                        {
                            reason = "원본이 아닌 A04Q 레벨 상태 발견";
                            return false;
                        }
                        total = checked(total + (int)buff.Amount);
                    }
                int next = checked(total + delta);
                double factor;
                if (!Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(
                    next, out factor))
                {
                    reason = "A04Q 원본 확인 범위(5..41) 밖의 레벨";
                    return false;
                }
                if (applicationId == long.MaxValue)
                    throw new OverflowException("JASS 능력 사용 순번 초과");
                status.Apply(new CombatEffect(
                    "jass:A04Q:" + (++applicationId).ToString(
                        System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + casterId + ":" + link.SkillId,
                    CombatEffectType.OriginalA04QLevelIncrease,
                    CombatEffectOrigin.Aura, delta, 0));
                return true;
            }
            reason = "미지원 원본 방어 능력";
            return false;
        }
    }
}
