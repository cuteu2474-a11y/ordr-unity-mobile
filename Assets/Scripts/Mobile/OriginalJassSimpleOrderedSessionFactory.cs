using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // The external WORLD, not a catalog ID or mobile UI, owns this lock.
    // It must rollback ALL staged native CHu/CHx effects, unit state and
    // wallet debits on Dispose unless Commit succeeds.
    public interface IOriginalJassSimpleFusionLockedTransaction : IDisposable
    {
        IReadOnlyList<MobileUnitSnapshot> OwnedUnits();
        bool TryReadBalances(out int gold, out int lumber);
        bool TryReadOriginalUnitRawcode(long instanceId, out string rawcode);
        bool TryChooseCastTarget(string abilityRawcode,
            IReadOnlyList<long> materials, out long targetId, out string reason);

        // Stage source-accurate placement, result object, material removal,
        // wallet debits and every native post-effect under ONE exclusive lock.
        // Do not publish state before Commit. Failure/exception rolls back.
        bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand command,
            out long spawnedInstanceId, out string reason);
        void Commit();
    }

    public interface IOriginalJassSimpleFusionTransactionalWorld
    {
        IOriginalJassSimpleFusionLockedTransaction BeginExclusiveFusion();
    }

    // An immutable, SOURCE-ORDERED set of operations for one simple CWR
    // recipe. It is NOT authority to consume or approximate native effects.
    public sealed class OriginalJassSimpleFusionCommand
    {
        public string AbilityRawcode { get; private set; }
        public string ResultRawcode { get; private set; }
        public string ResultUnitId { get; private set; }
        public long CastTargetInstanceId { get; private set; }
        public int Gold { get; private set; }
        public int Lumber { get; private set; }
        public IReadOnlyList<long> MaterialInstanceIds { get; private set; }
        public IReadOnlyList<string> MaterialRawcodes { get; private set; }
        public IReadOnlyList<OriginalJassFusionInstruction> OriginalConditions
        { get; private set; }

        internal OriginalJassSimpleFusionCommand(OriginalJassFusionCommitOrder order,
            IList<long> ids, IList<string> rawcodes)
        {
            AbilityRawcode = order.AbilityRawcode;
            ResultRawcode = order.ResultRawcode;
            ResultUnitId = order.ResultUnitId;
            CastTargetInstanceId = order.CastTargetInstanceId;
            Gold = order.GoldCost;
            Lumber = order.LumberCost;
            MaterialInstanceIds = new List<long>(ids).AsReadOnly();
            MaterialRawcodes = new List<string>(rawcodes).AsReadOnly();
            OriginalConditions = new List<OriginalJassFusionInstruction>(
                order.OrderedConditions).AsReadOnly();
        }
    }

    // Planner cannot authorize any missing special JASS rule.
    public sealed class OriginalJassNoSpecialConditions :
        IOriginalJassSpecialConditionEvaluator
    {
        public bool CanSatisfy(OriginalJassFusionInstruction instruction,
            string abilityRawcode, out string rejectionReason)
        {
            rejectionReason = "원본 추가 조건은 단순 UNIT/GOLD/WOOD 실행에서 지원하지 않습니다.";
            return false;
        }
    }

    // Implements the FULL ordering/consumption preparation for original CWR
    // recipes containing ONLY UNIT/GOLD/WOOD. The remaining 260 recipes must
    // use a separately source-verified general JASS world.
    public sealed class OriginalJassSimpleOrderedSessionFactory :
        IOriginalJassFusionOrderedSessionFactory, IOriginalJassRecipeCommitScope
    {
        private readonly OriginalJassFusionCatalog source;
        private readonly IOriginalJassSimpleFusionTransactionalWorld world;

        public OriginalJassSimpleOrderedSessionFactory(OriginalJassFusionCatalog catalog,
            IOriginalJassSimpleFusionTransactionalWorld originalWorld)
        {
            source = catalog ?? throw new ArgumentNullException(nameof(catalog));
            world = originalWorld ?? throw new ArgumentNullException(nameof(originalWorld));
            if (source.SourceSha256 != OriginalVerifiedUnitObjectMap.ExpectedJassSha256)
                throw new ArgumentException("검증된 2.323[C] 원본 등록식이 아닙니다.");
        }

        public bool CanCommitRecipe(string abilityRawcode)
        {
            var r = source.FindByAbility(abilityRawcode);
            if (r == null || r.Results.Count != 1 ||
                r.Results[0].Kind != "UNIT" || r.Results[0].Count != 1)
                return false;
            foreach (var c in r.OrderedConditions)
                if (c.Kind != "UNIT" && c.Kind != "GOLD" && c.Kind != "WOOD")
                    return false;
            return true;
        }

        public IOriginalJassFusionOrderedSession BeginExclusiveFusion()
        {
            var locked = world.BeginExclusiveFusion();
            return locked == null ? null : new Session(this, locked);
        }

        private sealed class Session : IOriginalJassFusionOrderedSession
        {
            private readonly OriginalJassSimpleOrderedSessionFactory factory;
            private readonly IOriginalJassSimpleFusionLockedTransaction live;
            private readonly List<long> stagedIds = new List<long>();
            private readonly List<string> stagedRawcodes = new List<string>();
            private readonly HashSet<long> unique = new HashSet<long>();
            private int stagedGold;
            private int stagedWood;
            private bool finished;
            private bool committed;
            private bool disposed;
            private OriginalJassFusionCommitOrder order;

            public Session(OriginalJassSimpleOrderedSessionFactory parent,
                IOriginalJassSimpleFusionLockedTransaction original)
            {
                factory = parent;
                live = original;
            }

            private void Open()
            {
                if (disposed || committed)
                    throw new InvalidOperationException("조합 거래가 이미 종료됐습니다.");
            }

            public IReadOnlyList<MobileUnitSnapshot> OwnedUnits()
            {
                Open();
                return live.OwnedUnits();
            }

            public bool TryReadBalances(out int gold, out int lumber)
            {
                Open();
                return live.TryReadBalances(out gold, out lumber);
            }

            public bool TryChooseCastTarget(string abilityRawcode,
                IReadOnlyList<long> materials, out long targetId, out string reason)
            {
                Open();
                return live.TryChooseCastTarget(abilityRawcode,
                    materials, out targetId, out reason);
            }

            public bool TryReadOriginalUnitRawcode(long id, out string rawcode)
            {
                Open();
                return live.TryReadOriginalUnitRawcode(id, out rawcode);
            }

            public bool TryVerifyOriginalInstruction(OriginalJassFusionCommitOrder candidate,
                OriginalJassFusionInstruction instruction, int index, out string reason)
            {
                Open();
                reason = null;
                if (finished || !factory.CanCommitRecipe(candidate.AbilityRawcode))
                {
                    reason = "원본에 추가 JASS 조건이 있는 조합은 미지원입니다.";
                    return false;
                }
                var registered = factory.source.FindByAbility(candidate.AbilityRawcode);
                if (index < 0 || index >= registered.OrderedConditions.Count ||
                    !Object.ReferenceEquals(instruction, registered.OrderedConditions[index]) ||
                    candidate.OrderedConditions.Count != registered.OrderedConditions.Count ||
                    candidate.ResultRawcode != registered.Results[0].Rawcode)
                {
                    reason = "원본 CWR 조건/결과 순서와 다릅니다.";
                    return false;
                }
                // All instructions are checked before staging anything.
                int gold, lumber;
                if (!live.TryReadBalances(out gold, out lumber) ||
                    gold < candidate.GoldCost || lumber < candidate.LumberCost)
                {
                    reason = "원본 재화 잔액 불충분 또는 미확인";
                    return false;
                }
                if (order != null && !Object.ReferenceEquals(order, candidate))
                {
                    reason = "원본 조합 주문이 교체됐습니다.";
                    return false;
                }
                order = candidate;
                return true;
            }

            public bool TryConsumeOriginalUnit(string expectedRawcode,
                long exactInstanceId, out string reason)
            {
                Open();
                reason = null;
                string actual;
                if (order == null || finished || exactInstanceId <= 0 ||
                    !unique.Add(exactInstanceId))
                {
                    reason = "중복·누락된 유닛 재료 또는 조합 상태";
                    return false;
                }
                if (!live.TryReadOriginalUnitRawcode(exactInstanceId, out actual) ||
                    !String.Equals(actual, expectedRawcode, StringComparison.Ordinal))
                {
                    reason = "실제 원본 유닛 객체와 재료가 일치하지 않습니다.";
                    return false;
                }
                stagedIds.Add(exactInstanceId);
                stagedRawcodes.Add(expectedRawcode);
                return true;
            }

            public bool TryDebitOriginalGold(int amount, out string reason)
            {
                Open();
                return StageCost(amount, true, out reason);
            }

            public bool TryDebitOriginalLumber(int amount, out string reason)
            {
                Open();
                return StageCost(amount, false, out reason);
            }

            private bool StageCost(int amount, bool goldCost, out string reason)
            {
                reason = null;
                int gold, wood;
                if (order == null || finished || amount < 0 ||
                    !live.TryReadBalances(out gold, out wood))
                {
                    reason = "원본 재화 또는 조합 순서 미확인";
                    return false;
                }
                int next;
                try { next = checked((goldCost ? stagedGold : stagedWood) + amount); }
                catch (OverflowException)
                {
                    reason = "원본 재화 수량 오버플로";
                    return false;
                }
                if (next > (goldCost ? gold : wood))
                {
                    reason = "원본 재화 부족";
                    return false;
                }
                if (goldCost) stagedGold = next;
                else stagedWood = next;
                return true;
            }

            public bool TryApplyOriginalSpecialInstruction(OriginalJassFusionCommitOrder candidate,
                OriginalJassFusionInstruction instruction, int index, out string reason)
            {
                Open();
                reason = "검증되지 않은 원본 특수 조건: " + instruction.Kind;
                return false;
            }

            public bool TryFinishOriginalFusion(OriginalJassFusionCommitOrder candidate,
                out string reason)
            {
                Open();
                reason = null;
                if (!Object.ReferenceEquals(candidate, order) || finished ||
                    stagedGold != order.GoldCost || stagedWood != order.LumberCost ||
                    stagedIds.Count != order.MaterialInstanceIds.Count)
                {
                    reason = "원본 조합 재료·비용이 주문과 일치하지 않습니다.";
                    return false;
                }
                var expected = new HashSet<long>(order.MaterialInstanceIds);
                foreach (long id in stagedIds)
                    if (!expected.Remove(id))
                    {
                        reason = "원본 재료 인스턴스가 변경됐습니다.";
                        return false;
                    }
                if (expected.Count != 0)
                {
                    reason = "일부 재료가 누락됐습니다.";
                    return false;
                }
                finished = true;
                return true;
            }

            public void Commit()
            {
                Open();
                if (!finished || order == null)
                    throw new InvalidOperationException("원본 제작 주문이 미완성입니다.");

                // ONLY NOW may any game world be mutated, under the same
                // exclusive rollback-capable transaction that was verified.
                var before = live.OwnedUnits();
                int originalGold, originalWood;
                if (before == null ||
                    !live.TryReadBalances(out originalGold, out originalWood) ||
                    originalGold < stagedGold || originalWood < stagedWood)
                    throw new InvalidOperationException("월드의 재료/재화 상태가 무효화됐습니다.");
                var idsBefore = new HashSet<long>();
                foreach (var u in before)
                    if (u == null || !idsBefore.Add(u.InstanceId))
                        throw new InvalidOperationException("원본 보유 유닛이 중복됐습니다.");
                foreach (long id in stagedIds)
                    if (!idsBefore.Contains(id))
                        throw new InvalidOperationException("조합 재료가 이미 사라졌습니다.");

                var command = new OriginalJassSimpleFusionCommand(order,
                    stagedIds, stagedRawcodes);
                long created;
                string reason;
                if (!live.TryStageOriginalFusion(command, out created, out reason))
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(reason)
                            ? "원본 BGH/CHu/CHx 생성 실행 미확인" : reason);
                if (created <= 0 || idsBefore.Contains(created))
                    throw new InvalidOperationException("원본 결과 인스턴스 ID가 유효하지 않습니다.");
                var after = live.OwnedUnits();
                int afterGold, afterWood;
                string afterRawcode;
                if (after == null ||
                    !live.TryReadBalances(out afterGold, out afterWood) ||
                    afterGold != originalGold - stagedGold ||
                    afterWood != originalWood - stagedWood ||
                    !live.TryReadOriginalUnitRawcode(created, out afterRawcode) ||
                    !String.Equals(afterRawcode, order.ResultRawcode,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("원본 재화 또는 BGH 생성 결과가 일치하지 않습니다.");

                // Not even a successful callback is enough. Audit the
                // exact live instance set before publishing the transaction.
                var expectedAfter = new HashSet<long>(idsBefore);
                foreach (long id in stagedIds) expectedAfter.Remove(id);
                expectedAfter.Add(created);
                if (after.Count != expectedAfter.Count)
                    throw new InvalidOperationException("실제 유닛 소비/생성 개수가 다릅니다.");
                bool foundResult = false;
                foreach (var unit in after)
                {
                    if (unit == null || !expectedAfter.Remove(unit.InstanceId))
                        throw new InvalidOperationException("실제 원본 유닛 목록이 다릅니다.");
                    if (unit.InstanceId == created)
                    {
                        foundResult = true;
                        if (unit.Unit == null ||
                            unit.Unit.Id != order.ResultUnitId)
                            throw new InvalidOperationException("생성된 유닛 등급/ID가 다릅니다.");
                    }
                }
                if (!foundResult || expectedAfter.Count != 0)
                    throw new InvalidOperationException("일부 원본 조합 유닛이 누락됐습니다.");

                live.Commit();
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                live.Dispose();
            }
        }
    }
}
