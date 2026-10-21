using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // A transaction scope must isolate the player's state from other game commands.
    // Until Commit succeeds, disposing the scope restores currency, units, inventory,
    // placement and every original-JASS side effect. Implement this at the game-world
    // boundary, not by independently calling wallet.Remove and roster.Remove.
    public interface IOriginalJassFusionWorldTransaction : IDisposable
    {
        IReadOnlyList<MobileUnitSnapshot> OwnedUnits();
        bool TryReadBalances(out int gold, out int lumber);
        bool TryChooseCastTarget(string abilityRawcode,
            IReadOnlyList<long> materialIds, out long targetId, out string reason);
        // Replay ordered CWR/BGI restrictions against the locked player state.
        // Must reject every not-yet-implemented ITEM/IFCT/SKPT/CHCT/PICK/
        // RDUN/SPEC/GREN rule, including its later payment or side effects.
        bool TryVerifyOriginalConditions(OriginalJassFusionCommitOrder lockedOrder,
            out string reason);
        // Apply must include all original CHu/CHx side effects, not just BGH spawn.
        // False or an exception must leave Commit uncalled so Dispose rolls back.
        bool TryApplyOriginalFusion(OriginalJassFusionCommitOrder order,
            out string rejectionReason);
        void Commit();
    }

    public interface IOriginalJassFusionTransactionalWorld
    {
        // An exclusive player-scoped, rollback-capable transaction.
        IOriginalJassFusionWorldTransaction BeginFusionTransaction();
    }

    // Rebuild the *entire* plan inside the exclusive world transaction. The
    // externally supplied preview/order is never authority to spend resources.
    public sealed class OriginalJassFusionAtomicCoordinator : IOriginalJassFusionAtomicCommit
    {
        private readonly OriginalJassFusionPlanner planner;
        private readonly IOriginalJassFusionTransactionalWorld world;

        public OriginalJassFusionAtomicCoordinator(OriginalJassFusionPlanner originalPlanner,
            IOriginalJassFusionTransactionalWorld verifiedWorld)
        {
            planner = originalPlanner ?? throw new ArgumentNullException(nameof(originalPlanner));
            world = verifiedWorld ?? throw new ArgumentNullException(nameof(verifiedWorld));
        }

        public ManualActionResult TryCommit(OriginalJassFusionCommitOrder order)
        {
            if (order == null)
                return Rejected("원본 조합 주문이 없습니다.");
            try
            {
                using (var transaction = world.BeginFusionTransaction())
                {
                    if (transaction == null)
                        return Rejected("원본 월드 거래 잠금을 확보하지 못했습니다.");
                    int gold, lumber;
                    if (!transaction.TryReadBalances(out gold, out lumber))
                        return Rejected("원본 재화 상태를 읽지 못했습니다.");
                    var owned = transaction.OwnedUnits();
                    if (owned == null)
                        return Rejected("원본 보유 유닛 상태를 읽지 못했습니다.");

                    // Special-condition evaluator and verified object map are
                    // required by the planner. They must read this same locked
                    // player state; otherwise the world adapter must decline.
                    JassFusionReadiness fresh;
                    try
                    {
                        fresh = planner.Assess(order.AbilityRawcode, owned, gold, lumber);
                    }
                    catch (KeyNotFoundException)
                    {
                        return Rejected("원본에 등록되지 않은 조합 능력입니다.");
                    }
                    if (!fresh.IsReadyForUserConfirmation)
                        return Rejected("확정 시점의 원작 조건 불충족: " +
                            fresh.BlockingReasons[0]);
                    if (fresh.AbilityRawcode != order.AbilityRawcode ||
                        fresh.ResultUnitId != order.ResultUnitId ||
                        fresh.ResultRawcode != order.ResultRawcode ||
                        fresh.GoldCost != order.GoldCost ||
                        fresh.LumberCost != order.LumberCost ||
                        !SameMaterials(fresh.SuggestedMaterialIds, order.MaterialInstanceIds))
                        return Rejected("조합 확인 후 재료 또는 원본 비용이 바뀌었습니다.");

                    long target;
                    string reason;
                    if (!transaction.TryChooseCastTarget(order.AbilityRawcode,
                            fresh.SuggestedMaterialIds, out target, out reason) ||
                        target <= 0 || target != order.CastTargetInstanceId)
                        return Rejected(string.IsNullOrWhiteSpace(reason)
                            ? "원본 시전 대상이 변경됐습니다." : reason);

                    // The exact BGI sequence, BGH result and rawcode-to-instance
                    // bindings all originate from the freshly locked assessment.
                    var lockedOrder = new OriginalJassFusionCommitOrder(fresh, target);
                    if (!transaction.TryVerifyOriginalConditions(lockedOrder,
                        out reason))
                        return Rejected(string.IsNullOrWhiteSpace(reason)
                            ? "잠금된 원본 상태의 추가 조건 재검증이 완료되지 않았습니다."
                            : reason);

                    if (!transaction.TryApplyOriginalFusion(lockedOrder, out reason))
                        return Rejected(string.IsNullOrWhiteSpace(reason)
                            ? "원본 재료 소비·생성 거래가 거절됐습니다." : reason);
                    transaction.Commit();
                    return new ManualActionResult(ManualActionState.Succeeded,
                        "원본 조합 거래가 완료됐습니다.");
                }
            }
            catch (Exception ex)
            {
                // Rollback occurs on scope disposal; never report a partial success.
                return Rejected("원본 거래가 중단됐습니다: " + ex.Message);
            }
        }

        private static bool SameMaterials(IReadOnlyList<long> a, IReadOnlyList<long> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            var expected = new HashSet<long>(a);
            if (expected.Count != a.Count) return false;
            foreach (long id in b)
                if (id <= 0 || !expected.Remove(id)) return false;
            return expected.Count == 0;
        }

        private static ManualActionResult Rejected(string reason)
        {
            return new ManualActionResult(ManualActionState.Rejected, reason);
        }
    }
}
