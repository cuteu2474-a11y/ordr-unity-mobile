using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Mobile
{
    // A verified original source must supply eligibility and the actual movement
    // transaction. This adapter never assumes that any owned unit may dispatch.
    public interface IOriginal323StoryUnitMovement
    {
        bool CanDispatch(long unitInstanceId);
        bool CanRecall(long unitInstanceId);
        // Return true only after the real unit has changed lanes successfully.
        bool TryDispatch(long unitInstanceId);
        bool TryRecall(long unitInstanceId);
    }

    // Canonical manual state is committed only AFTER the source movement succeeds.
    // No automatic dispatch, invented reward, timer or stage unlock.
    public sealed class Original323StoryManualDispatch : IOriginalStoryDispatchPort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly IOriginal323StoryUnitMovement movement;
        private readonly HashSet<long> deployed = new HashSet<long>();
        public int DeployedCount => deployed.Count;

        public Original323StoryManualDispatch(CharacterCombatRuntime combat,
            IOriginal323StoryUnitMovement verifiedMovement)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            movement = verifiedMovement ?? throw new ArgumentNullException(nameof(verifiedMovement));
        }

        private bool Owned(long id)
        {
            if (id <= 0) return false;
            foreach (var unit in combat.MobileUnitSnapshot())
                if (unit.InstanceId == id) return true;
            return false;
        }

        public bool IsDeployed(long id) => deployed.Contains(id);

        public bool CanDispatch(long id) =>
            Owned(id) && !deployed.Contains(id) && movement.CanDispatch(id);

        public ManualActionResult Dispatch(long id)
        {
            if (!CanDispatch(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 파견 조건을 충족하지 못했습니다.");
            if (!movement.TryDispatch(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 전장 이동이 완료되지 않았습니다.");
            deployed.Add(id);
            return new ManualActionResult(ManualActionState.Succeeded,
                "선택한 유닛을 스토리 전장에 파견했습니다.");
        }

        public bool CanRecall(long id) =>
            deployed.Contains(id) && Owned(id) && movement.CanRecall(id);

        public ManualActionResult Recall(long id)
        {
            if (!CanRecall(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 복귀 조건을 충족하지 못했습니다.");
            if (!movement.TryRecall(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 복귀 이동이 완료되지 않았습니다.");
            deployed.Remove(id);
            return new ManualActionResult(ManualActionState.Succeeded,
                "유닛이 메인 전장으로 복귀했습니다.");
        }

        // Called only after the canonical unit removal has been confirmed.
        public void ForgetRemovedUnit(long id)
        {
            if (Owned(id))
                throw new InvalidOperationException("보유 중인 유닛의 파견 상태를 제거할 수 없습니다.");
            deployed.Remove(id);
        }
    }
}
