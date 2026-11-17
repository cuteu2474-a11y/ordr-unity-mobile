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

    // Story members removed by the canonical roster have no remaining lane.
    public interface IOriginal323StoryDispatchUnitLifecycle
    {
        void OnUnitRemoved(long instanceId);
    }

    // A restore witness must prove that an already-committed story position
    // is valid, without calling the source movement transaction again.
    public interface IOriginal323StoryRestorePositionWitness
    {
        bool IsAttachedTo(CharacterCombatRuntime owner);
        bool IsRestorableStoryPosition(long instanceId, double x, double y);
    }

    public interface IOriginal323StoryDispatchRestorePort : IOriginalStoryDispatchPort
    {
        bool IsAttachedTo(CharacterCombatRuntime owner);
        void RestoreCommittedDeployments(IReadOnlyList<long> ids);
    }

    // Canonical manual state is committed only AFTER the source movement succeeds.
    // No automatic dispatch, invented reward, timer or stage unlock.
    public sealed class Original323StoryManualDispatch :
        IOriginal323StoryDispatchRestorePort, IOriginal323StoryDispatchUnitLifecycle
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

        public bool IsAttachedTo(CharacterCombatRuntime owner) =>
            ReferenceEquals(combat, owner);

        private bool Owned(long id)
        {
            if (id <= 0) return false;
            foreach (var unit in combat.MobileUnitSnapshot())
                if (unit.InstanceId == id) return true;
            return false;
        }

        public bool IsDeployed(long id) => deployed.Contains(id);

        public bool CanDispatch(long id) =>
            Owned(id) && !deployed.Contains(id) &&
            !combat.IsLockedFromRosterTransactions(id) && movement.CanDispatch(id);

        public ManualActionResult Dispatch(long id)
        {
            if (!CanDispatch(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 파견 조건을 충족하지 못했습니다.");
            if (!movement.TryDispatch(id))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 스토리 전장 이동이 완료되지 않았습니다.");
            deployed.Add(id);
            combat.RefreshAfterCommittedStoryLaneChange(id);
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
            combat.RefreshAfterCommittedStoryLaneChange(id);
            return new ManualActionResult(ManualActionState.Succeeded,
                "유닛이 메인 전장으로 복귀했습니다.");
        }

        // Rebind committed story owners on an UNPUBLISHED runtime. Every
        // actor already exists at the saved story location; no dispatch,
        // recall, movement, cooldown or compensation may execute here.
        public void RestoreCommittedDeployments(IReadOnlyList<long> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (deployed.Count != 0)
                throw new InvalidOperationException(
                    "복원 대상 스토리 파견 상태가 이미 사용 중입니다.");
            var witness = movement as IOriginal323StoryRestorePositionWitness;
            if (ids.Count > 0 &&
                (witness == null || !witness.IsAttachedTo(combat)))
                throw new NotSupportedException(
                    "원본 스토리 이동 위치를 증명할 복원 공급자가 없습니다.");
            var seen = new HashSet<long>();
            foreach (long id in ids)
            {
                if (!seen.Add(id) || !Owned(id))
                    throw new FormatException(
                        "스토리 파견 유닛이 중복되거나 소유되어 있지 않습니다.");
                var unit = combat.Roster.Get(id);
                if (!witness.IsRestorableStoryPosition(id, unit.X, unit.Y))
                    throw new FormatException(
                        "복원하려는 스토리 파견 위치의 원본 근거가 없습니다.");
            }
            // All owners were checked before the adapter is mutated.
            foreach (long id in ids) deployed.Add(id);
        }

        // Called only after the canonical unit removal has been confirmed.
        public void OnUnitRemoved(long id) { ForgetRemovedUnit(id); }
        public void ForgetRemovedUnit(long id)
        {
            if (Owned(id))
                throw new InvalidOperationException("보유 중인 유닛의 파견 상태를 제거할 수 없습니다.");
            deployed.Remove(id);
        }
    }
}
