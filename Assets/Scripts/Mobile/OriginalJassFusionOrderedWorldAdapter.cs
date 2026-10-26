using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // The production game world provides an exclusive, rollback-capable session.
    // It must resolve expectedRawcode against the ORIGINAL unit object, never
    // assume that a project catalog ID proves the Warcraft rawcode.
    public interface IOriginalJassFusionOrderedSession : IDisposable
    {
        IReadOnlyList<MobileUnitSnapshot> OwnedUnits();
        bool TryReadBalances(out int gold, out int lumber);
        bool TryChooseCastTarget(string abilityRawcode,
            IReadOnlyList<long> materialInstanceIds, out long targetId, out string reason);

        // Must read the actual original object rawcode for each selected unit
        // while holding the transaction lock. A catalog name/id is not proof.
        bool TryReadOriginalUnitRawcode(long instanceId, out string rawcode);

        // Called under the same lock, for every BGI in its original CWR order.
        // Unimplemented ITEM/IFCT/SKPT/CHCT/PICK/RDUN/SPEC/GREN etc. MUST
        // reject rather than supply an optimistic default.
        bool TryVerifyOriginalInstruction(OriginalJassFusionCommitOrder order,
            OriginalJassFusionInstruction instruction, int instructionIndex,
            out string reason);

        // These operations are tentative until Commit. Dispose without Commit
        // must restore all units, resources, inventories and effects.
        bool TryConsumeOriginalUnit(string expectedRawcode, long exactInstanceId,
            out string reason);
        bool TryDebitOriginalGold(int amount, out string reason);
        bool TryDebitOriginalLumber(int amount, out string reason);
        bool TryApplyOriginalSpecialInstruction(OriginalJassFusionCommitOrder order,
            OriginalJassFusionInstruction instruction, int instructionIndex,
            out string reason);

        // Must implement the exact original BGH/CHu/CHx effects, result
        // spawning, placement and special follow-up behavior; fail closed.
        bool TryFinishOriginalFusion(OriginalJassFusionCommitOrder order,
            out string reason);
        void Commit();
    }

    public interface IOriginalJassFusionOrderedSessionFactory
    {
        // Exclusive lock lasts until session disposal, including rollback.
        IOriginalJassFusionOrderedSession BeginExclusiveFusion();
    }

    // Concrete ordered transaction orchestration. Original game-specific
    // predicate/effect implementations remain mandatory in the world session.
    public sealed class OriginalJassFusionOrderedWorldAdapter
        : IOriginalJassFusionTransactionalWorld, IOriginalJassRecipeCommitScope
    {
        private readonly IOriginalJassFusionOrderedSessionFactory factory;

        public OriginalJassFusionOrderedWorldAdapter(
            IOriginalJassFusionOrderedSessionFactory verifiedFactory)
        {
            factory = verifiedFactory ?? throw new ArgumentNullException(nameof(verifiedFactory));
        }

        public bool CanCommitRecipe(string abilityRawcode)
        {
            var scoped = factory as IOriginalJassRecipeCommitScope;
            return scoped == null || scoped.CanCommitRecipe(abilityRawcode);
        }

        public IOriginalJassFusionWorldTransaction BeginFusionTransaction()
        {
            var session = factory.BeginExclusiveFusion();
            return session == null ? null : new Scope(session);
        }

        private sealed class Scope : IOriginalJassFusionWorldTransaction
        {
            private readonly IOriginalJassFusionOrderedSession session;
            private OriginalJassFusionCommitOrder verifiedOrder;
            private bool attempted;
            private bool applied;
            private bool committed;
            private bool disposed;

            internal Scope(IOriginalJassFusionOrderedSession transaction)
            {
                session = transaction;
            }

            public IReadOnlyList<MobileUnitSnapshot> OwnedUnits()
            {
                EnsureOpen();
                return session.OwnedUnits();
            }

            public bool TryReadBalances(out int gold, out int lumber)
            {
                EnsureOpen();
                return session.TryReadBalances(out gold, out lumber);
            }

            public bool TryChooseCastTarget(string abilityRawcode,
                IReadOnlyList<long> materialIds, out long targetId, out string reason)
            {
                EnsureOpen();
                return session.TryChooseCastTarget(abilityRawcode, materialIds,
                    out targetId, out reason);
            }

            public bool TryVerifyOriginalConditions(OriginalJassFusionCommitOrder order,
                out string reason)
            {
                EnsureOpen();
                reason = null;
                if (verifiedOrder != null || attempted || !ValidateBindings(order, out reason))
                    return Reject(ref reason, "원본 조합 주문 또는 재료 결합 정보가 유효하지 않습니다.");

                // Independently verify EVERY real owned instance's original
                // object rawcode before touching any unit or currency.
                // JASS display-name mappings and catalog IDs alone never authorize
                // consumption. Duplicate instance IDs were already rejected.
                foreach (var binding in order.MaterialBindings)
                {
                    foreach (long instanceId in binding.InstanceIds)
                    {
                        string actualRawcode;
                        if (!session.TryReadOriginalUnitRawcode(instanceId,
                                out actualRawcode) ||
                            !String.Equals(actualRawcode, binding.Rawcode,
                                StringComparison.Ordinal))
                            return Reject(ref reason,
                                "원본 객체 rawcode와 선택 재료가 다릅니다: " +
                                binding.Rawcode);
                    }
                }

                // Verification of every instruction (even simple UNIT/GOLD/WOOD)
                // occurs on the same locked world as the subsequent payment.
                for (int i = 0; i < order.OrderedConditions.Count; ++i)
                {
                    if (!session.TryVerifyOriginalInstruction(order,
                            order.OrderedConditions[i], i, out reason))
                        return Reject(ref reason, "원본 조건 검증 미완료: " +
                            order.OrderedConditions[i].Kind);
                }
                verifiedOrder = order;
                return true;
            }

            public bool TryApplyOriginalFusion(OriginalJassFusionCommitOrder order,
                out string reason)
            {
                EnsureOpen();
                reason = null;
                if (attempted || verifiedOrder == null ||
                    !object.ReferenceEquals(verifiedOrder, order))
                    return Reject(ref reason, "검증한 주문과 실행 주문이 다릅니다.");
                attempted = true; // Any failure now requires disposal and rollback.

                var consumed = new Dictionary<string, int>(StringComparer.Ordinal);
                var bindings = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
                foreach (var binding in order.MaterialBindings)
                    bindings.Add(binding.Rawcode, binding.InstanceIds);

                for (int i = 0; i < order.OrderedConditions.Count; ++i)
                {
                    var instruction = order.OrderedConditions[i];
                    switch (instruction.Kind)
                    {
                        case "UNIT":
                            int next;
                            consumed.TryGetValue(instruction.Rawcode, out next);
                            var ids = bindings[instruction.Rawcode];
                            for (int k = 0; k < instruction.Count; ++k)
                            {
                                if (!session.TryConsumeOriginalUnit(instruction.Rawcode,
                                        ids[next + k], out reason))
                                    return Reject(ref reason, "원본 재료 소모 실패: " +
                                        instruction.Rawcode);
                            }
                            consumed[instruction.Rawcode] = next + instruction.Count;
                            break;
                        case "GOLD":
                            if (!session.TryDebitOriginalGold(instruction.Count, out reason))
                                return Reject(ref reason, "원본 골드 차감 실패");
                            break;
                        case "WOOD":
                            if (!session.TryDebitOriginalLumber(instruction.Count, out reason))
                                return Reject(ref reason, "원본 목재 차감 실패");
                            break;
                        default:
                            if (!session.TryApplyOriginalSpecialInstruction(order,
                                    instruction, i, out reason))
                                return Reject(ref reason, "원본 특수조건 효과 미구현: " +
                                    instruction.Kind);
                            break;
                    }
                }

                if (!session.TryFinishOriginalFusion(order, out reason))
                    return Reject(ref reason, "원본 생성·시전 후속 처리 실패");
                applied = true;
                return true;
            }

            public void Commit()
            {
                EnsureOpen();
                if (!applied || committed)
                    throw new InvalidOperationException(
                        "원본 조건·소모·생성 완료 전 조합 거래를 확정할 수 없습니다.");
                session.Commit();
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                session.Dispose(); // The authoritative session owns rollback.
            }

            private void EnsureOpen()
            {
                if (disposed || committed)
                    throw new InvalidOperationException("원본 조합 거래가 종료됐습니다.");
            }

            private static bool Reject(ref string reason, string fallback)
            {
                if (string.IsNullOrWhiteSpace(reason)) reason = fallback;
                return false;
            }

            private static bool ValidateBindings(OriginalJassFusionCommitOrder order,
                out string reason)
            {
                reason = null;
                if (order == null || string.IsNullOrWhiteSpace(order.AbilityRawcode) ||
                    string.IsNullOrWhiteSpace(order.ResultUnitId) ||
                    order.CastTargetInstanceId <= 0 || order.GoldCost < 0 ||
                    order.LumberCost < 0 || order.ResultInstructions.Count != 1 ||
                    order.ResultInstructions[0].Kind != "UNIT" ||
                    order.ResultInstructions[0].Count != 1 ||
                    order.ResultInstructions[0].Rawcode != order.ResultRawcode)
                    return Reject(ref reason, "지원되지 않거나 변형된 BGH 결과");

                var expected = new Dictionary<string, int>(StringComparer.Ordinal);
                int expectedGold = 0, expectedLumber = 0;
                foreach (var item in order.OrderedConditions)
                {
                    int old;
                    switch (item.Kind)
                    {
                        case "UNIT":
                            expected.TryGetValue(item.Rawcode, out old);
                            expected[item.Rawcode] = checked(old + item.Count);
                            break;
                        case "GOLD":
                            if (item.Rawcode != "GOLD")
                                return Reject(ref reason, "GOLD 태그가 원본과 다릅니다.");
                            expectedGold = checked(expectedGold + item.Count);
                            break;
                        case "WOOD":
                            if (item.Rawcode != "WOOD")
                                return Reject(ref reason, "WOOD 태그가 원본과 다릅니다.");
                            expectedLumber = checked(expectedLumber + item.Count);
                            break;
                    }
                }
                if (expectedGold != order.GoldCost || expectedLumber != order.LumberCost ||
                    order.MaterialBindings.Count != expected.Count)
                    return Reject(ref reason, "원본 조합 비용 또는 재료 묶음 불일치");

                var seenRawcodes = new HashSet<string>(StringComparer.Ordinal);
                var actualIds = new HashSet<long>();
                foreach (var binding in order.MaterialBindings)
                {
                    int count;
                    if (string.IsNullOrWhiteSpace(binding.Rawcode) ||
                        !seenRawcodes.Add(binding.Rawcode) ||
                        !expected.TryGetValue(binding.Rawcode, out count) ||
                        binding.InstanceIds.Count != count)
                        return Reject(ref reason, "원본 rawcode별 재료 수량 불일치");
                    foreach (long id in binding.InstanceIds)
                        if (id <= 0 || !actualIds.Add(id))
                            return Reject(ref reason, "원본 재료 인스턴스 중복");
                }
                if (actualIds.Count != order.MaterialInstanceIds.Count)
                    return Reject(ref reason, "선택한 재료 인스턴스 수 불일치");
                foreach (long id in order.MaterialInstanceIds)
                    if (!actualIds.Remove(id))
                        return Reject(ref reason, "선택한 재료와 원본 재료 결합 불일치");
                return actualIds.Count == 0;
            }
        }
    }
}
