using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    public enum MobileOperation
    {
        BasicAttack, RandomProcSkill, UnitPlacement, BossTargeting, ActiveQwr,
        StoryDispatch, Fusion, Shop, Gambling, Wisp
    }

    public enum MobileInteractionMode { Automatic, Assisted, Manual }

    // 사용자 선택을 고정 정책으로 명시. 원작의 전투 수치나 소모 자원은 변경하지 않는다.
    public static class MobileInteractionPolicy
    {
        public static MobileInteractionMode Mode(MobileOperation operation)
        {
            switch (operation)
            {
                case MobileOperation.BasicAttack:
                case MobileOperation.RandomProcSkill:
                case MobileOperation.UnitPlacement:
                case MobileOperation.BossTargeting:
                case MobileOperation.ActiveQwr:
                    return MobileInteractionMode.Automatic;
                case MobileOperation.Fusion:
                    return MobileInteractionMode.Assisted;
                case MobileOperation.StoryDispatch:
                case MobileOperation.Shop:
                case MobileOperation.Gambling:
                case MobileOperation.Wisp:
                    return MobileInteractionMode.Manual;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }

    // 자동 배치는 원작의 설치 가능 좌표·지역·파견 제약을 구현한 서비스에서만 허용.
    // 반환 좌표가 실제로 합법적인지 두 번째 함수로 확인해 우회하지 않는다.
    public interface IOriginalPlacementRules
    {
        bool TryChoosePosition(CatalogUnit unit, UnitState original, out double x, out double y);
        bool IsLegalPosition(CatalogUnit unit, UnitState original, double x, double y);
    }

    // 보스를 선호하는 것이 원작에서도 허용되는 유닛/상황일 때만 true.
    // 모바일 무제한 평타 모드에서는 마지막 사거리 인자가 참고값일 뿐,
    // 실제 사거리 제한은 적용되지 않는다. 전장 구분은 Runtime에서 반드시 유지한다.
    public interface IOriginalBossFocusRules
    {
        bool CanPreferBoss(CatalogUnit unit, CombatEnemy boss, double distanceSquared,
            double verifiedAttackRange);
    }

    public sealed class MobileUnitSnapshot
    {
        public long InstanceId { get; private set; }
        public CatalogUnit Unit { get; private set; }
        public CharacterSkillProfile Profile { get; private set; }
        public UnitState Position { get; private set; }

        public MobileUnitSnapshot(long instanceId, CatalogUnit unit,
            CharacterSkillProfile profile, UnitState position)
        {
            InstanceId = instanceId;
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            Profile = profile;
            Position = position;
        }
    }

    public sealed class MobileEnemySnapshot
    {
        public string Id { get; private set; }
        public CharacterEnemyKind Kind { get; private set; }
        public long Health { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }

        public MobileEnemySnapshot(string id, CharacterEnemyKind kind, long health, double x, double y)
        {
            Id = id; Kind = kind; Health = health; X = x; Y = y;
        }
    }

    // 원작 조건/사거리/수동스킬 발동 가능 여부까지 검증할 수 있는 서비스가
    // 승인한 대상만 사용한다. 단순히 '보스가 보인다'는 이유로 자동 사용하지 않는다.
    public interface IOriginalAutoSkillRules
    {
        bool TryApproveCast(CatalogUnit unit, ImportedCombatSkill skill,
            UnitState casterPosition, IReadOnlyList<MobileEnemySnapshot> enemies,
            double gameSeconds, out string targetEnemyId);
    }

    public sealed class MobileAutomationStatus
    {
        public bool PlacementConnected { get; private set; }
        public bool BossFocusConnected { get; private set; }
        public bool AutoSkillsConnected { get; private set; }

        public MobileAutomationStatus(bool placement, bool bossFocus, bool skills)
        {
            PlacementConnected = placement;
            BossFocusConnected = bossFocus;
            AutoSkillsConnected = skills;
        }
    }

    // 자동 조작 계층은 게임 규칙을 만들지 않고 원본 검증 서비스의 명령만 실행한다.
    // 원본 서비스가 연결되지 않으면 같은 전장 안의 가까운 적 우선 정책을 유지한다.
    public sealed class MobileCombatAutomation
    {
        private IOriginalPlacementRules placement;
        private IOriginalBossFocusRules bossFocus;
        private IOriginalAutoSkillRules autoSkills;

        public MobileAutomationStatus Status
        {
            get { return new MobileAutomationStatus(placement != null, bossFocus != null, autoSkills != null); }
        }

        public IOriginalBossFocusRules BossRules { get { return bossFocus; } }

        public void Configure(IOriginalPlacementRules verifiedPlacement,
            IOriginalBossFocusRules verifiedBossFocus, IOriginalAutoSkillRules verifiedAutoSkills)
        {
            placement = verifiedPlacement;
            bossFocus = verifiedBossFocus;
            autoSkills = verifiedAutoSkills;
        }

        public bool TryPlace(CatalogUnit unit, UnitRoster roster, long instanceId)
        {
            if (placement == null) return false;
            UnitState state = roster.Get(instanceId);
            double x, y;
            if (!placement.TryChoosePosition(unit, state, out x, out y)) return false;
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y)) return false;
            if (!placement.IsLegalPosition(unit, state, x, y)) return false;
            if (state.X == x && state.Y == y) return false;
            roster.Move(instanceId, x, y);
            return true;
        }

        // 이 결정은 정해진 게임 Tick 당 최대 한 번만 호출되며,
        // 원작 공식 미등록 스킬은 자원도 소비하지 않는다.
        public void RunAutomaticAbilities(CharacterCombatRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (autoSkills == null) return;
            var enemies = runtime.MobileEnemySnapshot();
            foreach (var unit in runtime.MobileUnitSnapshot())
            {
                if (runtime.IsStoryDeployed(unit.InstanceId)) continue;
                if (unit.Profile == null) continue;
                if (runtime.ResourcesOf(unit.InstanceId) == null) continue;
                foreach (var skill in unit.Profile.Skills)
                {
                    if (skill.Trigger != "manual" && skill.Trigger != "activeMana" &&
                        skill.Trigger != "activeHealth") continue;
                    // 원본에서 확실한 쿨다운이 없으면 매 프레임 자동 연타할 수 있다.
                    // 그런 스킬은 정확한 이벤트형 스케줄러 구현 전 보류한다.
                    double? cooldown = skill.Number("absoluteCooldownSeconds") ?? skill.CooldownSeconds;
                    if (!cooldown.HasValue || cooldown.Value <= 0) continue;
                    // 캐스팅 중 공격/이동 잠금 미구현: 시전시간이 있는 스킬을
                    // 즉시 시전하면 원작 DPS가 달라지므로 이 경로에선 보류.
                    double? castTime = skill.Number("castTimeSeconds");
                    if (castTime.HasValue && castTime.Value > 0) continue;
                    if (skill.Get("requiresOriginalVerification") is bool &&
                        (bool)skill.Get("requiresOriginalVerification")) continue;
                    string targetId;
                    if (!autoSkills.TryApproveCast(unit.Unit, skill, unit.Position, enemies,
                        runtime.Time, out targetId)) continue;
                    if (string.IsNullOrWhiteSpace(targetId)) continue;
                    bool liveTarget = false;
                    foreach (var enemy in enemies)
                        if (enemy.Id == targetId && enemy.Health > 0)
                        { liveTarget = true; break; }
                    if (!liveTarget) continue;
                    // 시전 성공 시 해당 유닛의 나머지 스킬은 같은 Tick에 연달아 쓰지 않는다.
                    // 추가 콤보 규칙은 원작 실행기로 연결하기 전 자동으로 만들어내지 않는다.
                    if (runtime.TryCastActiveSkill(unit.InstanceId, skill.Id, targetId)
                        == AbilityCastResult.Cast) break;
                }
            }
        }
    }
}
