using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Only non-combat, player-triggered original actions are shown here.
    // Q/W/R combat skills remain on the existing automatic casting lane.
    public sealed class MobileDedicatedSkillOffer
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string OriginalCostAndRules { get; private set; }
        public bool IsUnlocked { get; private set; }
        public bool CanExecute { get; private set; }
        // Exact owning roster ID. Zero denotes an existing global action.
        // Selection-focused mobile ribbon never presents another owner's action.
        public long OwnerInstanceId { get; private set; }
        public bool RequiresAllyTarget { get; private set; }
        public bool RequiresConfirmation { get; private set; }
        // Read-only, live evidence calculated from the same authoritative
        // resource/clock/state services used by Execute. Never an extra timer.
        public string LiveStatus { get; private set; }
        public double CooldownRemainingSeconds { get; private set; }

        public MobileDedicatedSkillOffer(string id, string title,
            string originalCostAndRules, bool isUnlocked, bool canExecute,
            long ownerInstanceId = 0, bool requiresAllyTarget = false,
            bool requiresConfirmation = false, string liveStatus = null,
            double cooldownRemainingSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(originalCostAndRules) ||
                (!isUnlocked && canExecute) || ownerInstanceId < 0 ||
                (requiresAllyTarget && requiresConfirmation) ||
                ((requiresAllyTarget || requiresConfirmation) && ownerInstanceId == 0) ||
                double.IsNaN(cooldownRemainingSeconds) ||
                double.IsInfinity(cooldownRemainingSeconds) ||
                cooldownRemainingSeconds < 0)
                throw new ArgumentException("원작 전용 스킬의 식별자·대상·조건이 불완전합니다.");
            Id = id;
            Title = title;
            OriginalCostAndRules = originalCostAndRules;
            IsUnlocked = isUnlocked;
            CanExecute = canExecute;
            OwnerInstanceId = ownerInstanceId;
            RequiresAllyTarget = requiresAllyTarget;
            RequiresConfirmation = requiresConfirmation;
            LiveStatus = liveStatus ?? "";
            CooldownRemainingSeconds = cooldownRemainingSeconds;
        }
    }

    public interface IOriginalDedicatedSkillPort
    {
        // Return verified actions, including their current unlock/resource state.
        IReadOnlyList<MobileDedicatedSkillOffer> AvailableSkills();

        // MUST re-check original ownership, unlocks, costs and effects atomically.
        // UI availability alone never authorizes a transaction.
        ManualActionResult Execute(string skillId);
    }

    // Optional target-cast extension. Older original providers can still
    // serve ordinary untargeted commands without implementing it.
    public interface IOriginalTargetedDedicatedSkillPort
    {
        ManualActionResult ExecuteForAlly(string offeredSkillId,
            long ownerInstanceId, long targetInstanceId);
    }

    public sealed class MobileDedicatedSkillActions
    {
        private IOriginalDedicatedSkillPort original;

        public bool IsConnected { get { return original != null; } }

        public void Configure(IOriginalDedicatedSkillPort verifiedOriginalWorld)
        {
            original = verifiedOriginalWorld ??
                throw new ArgumentNullException(nameof(verifiedOriginalWorld));
        }

        public IReadOnlyList<MobileDedicatedSkillOffer> Available()
        {
            if (original == null) return new MobileDedicatedSkillOffer[0];
            return original.AvailableSkills() ?? new MobileDedicatedSkillOffer[0];
        }

        public IReadOnlyList<MobileDedicatedSkillOffer> AvailableFor(long ownerId)
        {
            var own = new List<MobileDedicatedSkillOffer>();
            if (ownerId <= 0) return own.AsReadOnly();
            foreach (var skill in Available())
                if (skill != null && skill.OwnerInstanceId == ownerId)
                    own.Add(skill);
            return own.AsReadOnly();
        }

        private static ManualActionResult Reject(string reason) =>
            new ManualActionResult(ManualActionState.Rejected, reason);

        // The selected source remains selected. A map/formation tap supplies
        // the target ID directly; it never alters Roster.SelectedId.
        public ManualActionResult RequestTargeted(string offeredSkillId,
            long ownerInstanceId, long targetInstanceId)
        {
            if (original == null) return ManualActionResult.Unavailable("전용 스킬");
            if (ownerInstanceId <= 0 || targetInstanceId <= 0)
                return Reject("시전자와 대상 유닛을 다시 선택해 주세요.");
            foreach (var offer in AvailableFor(ownerInstanceId))
            {
                if (offer.Id != offeredSkillId) continue;
                if (!offer.RequiresAllyTarget || !offer.IsUnlocked || !offer.CanExecute)
                    return Reject("대상 선택 스킬이 현재 실행할 수 없는 상태입니다.");
                var targetPort = original as IOriginalTargetedDedicatedSkillPort;
                if (targetPort == null)
                    return ManualActionResult.Unavailable("대상 지정 전용 스킬");
                return targetPort.ExecuteForAlly(
                    offeredSkillId, ownerInstanceId, targetInstanceId) ??
                    Reject("대상 스킬 실행 결과가 없습니다.");
            }
            return Reject("다른 캐릭터의 스킬 또는 취소된 대상 선택입니다.");
        }

        public ManualActionResult Request(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return new ManualActionResult(ManualActionState.Rejected,
                    "실행할 전용 스킬을 선택해 주세요.");
            if (original == null) return ManualActionResult.Unavailable("전용 스킬");
            // Refresh on the actual tap, not merely when drawing the button.
            foreach (var skill in Available())
            {
                if (skill == null || skill.Id != skillId) continue;
                if (!skill.IsUnlocked)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "원작 전용 스킬 해금 조건이 충족되지 않았습니다.");
                if (!skill.CanExecute)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "현재 원작 자원·대상·쿨타임 조건을 충족하지 못했습니다.");
                if (skill.RequiresAllyTarget)
                    return Reject("전장에서 대상 아군 유닛을 먼저 지정해 주세요.");
                return original.Execute(skillId) ??
                    new ManualActionResult(ManualActionState.Rejected,
                        "원작 전용 스킬의 실행 결과를 확인하지 못했습니다.");
            }
            return new ManualActionResult(ManualActionState.Rejected,
                "현재 원작 전용 스킬 목록에 없는 기능입니다.");
        }
    }
}
