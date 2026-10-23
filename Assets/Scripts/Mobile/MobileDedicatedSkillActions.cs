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

        public MobileDedicatedSkillOffer(string id, string title,
            string originalCostAndRules, bool isUnlocked, bool canExecute)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(originalCostAndRules) ||
                (!isUnlocked && canExecute))
                throw new ArgumentException("원작 전용 스킬의 식별자·조건이 불완전합니다.");
            Id = id;
            Title = title;
            OriginalCostAndRules = originalCostAndRules;
            IsUnlocked = isUnlocked;
            CanExecute = canExecute;
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
                return original.Execute(skillId) ??
                    new ManualActionResult(ManualActionState.Rejected,
                        "원작 전용 스킬의 실행 결과를 확인하지 못했습니다.");
            }
            return new ManualActionResult(ManualActionState.Rejected,
                "현재 원작 전용 스킬 목록에 없는 기능입니다.");
        }
    }
}
