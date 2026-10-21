using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // The source of truth for spending is the original game's currency state.
    // Returning false means no confirmed balances exist; never substitute zero.
    public interface IOriginalJassFusionResourceReader
    {
        bool TryReadBalances(out int gold, out int lumber);
    }

    // Original CHu/CHx treat GetSpellTargetUnit separately from the unit
    // inventory. Choosing a mobile cast target is a rule, not a UI convenience.
    public interface IOriginalJassFusionCastPolicy
    {
        bool TryChooseCastTarget(string abilityRawcode,
            IReadOnlyList<long> exactMaterialInstanceIds,
            out long targetInstanceId, out string rejectionReason);
    }

    // The authoritative implementation must re-check all conditions under one
    // exclusive transaction, then debit currency, consume exact units and spawn
    // the result. It must rollback the whole transaction on failure.
    public interface IOriginalJassFusionAtomicCommit
    {
        ManualActionResult TryCommit(OriginalJassFusionCommitOrder order);
    }

    public sealed class OriginalJassFusionCommitOrder
    {
        public string AbilityRawcode { get; private set; }
        public string ResultUnitId { get; private set; }
        public string ResultRawcode { get; private set; }
        public int GoldCost { get; private set; }
        public int LumberCost { get; private set; }
        public long CastTargetInstanceId { get; private set; }
        public IReadOnlyList<long> MaterialInstanceIds { get; private set; }

        internal OriginalJassFusionCommitOrder(JassFusionReadiness assessment,
            long verifiedCastTargetInstanceId)
        {
            if (assessment == null || !assessment.IsReadyForUserConfirmation)
                throw new ArgumentException("원본 조건이 미확인인 조합은 최종 실행할 수 없습니다.");
            if (verifiedCastTargetInstanceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedCastTargetInstanceId));
            CastTargetInstanceId = verifiedCastTargetInstanceId;
            AbilityRawcode = assessment.AbilityRawcode;
            ResultUnitId = assessment.ResultUnitId;
            ResultRawcode = assessment.ResultRawcode;
            GoldCost = assessment.GoldCost;
            LumberCost = assessment.LumberCost;
            MaterialInstanceIds = new List<long>(assessment.SuggestedMaterialIds).AsReadOnly();
        }
    }

    // Alternative to web-dex-only seven recipes. Expose only output units for which
    // Bi8/Bi9 provide a name; unknown inputs and JASS rules remain visibly blocked.
    public sealed class OriginalJassFusionPort : IOriginalFusionPort
    {
        private readonly OriginalJassFusionPlanner planner;
        private readonly Func<IReadOnlyList<MobileUnitSnapshot>> ownedUnits;
        private readonly IOriginalJassFusionResourceReader resources;
        private readonly IOriginalJassFusionAtomicCommit transaction;
        private readonly IOriginalJassFusionCastPolicy castPolicy;

        public bool CanCommit { get { return transaction != null; } }

        public OriginalJassFusionPort(OriginalJassFusionPlanner planner,
            Func<IReadOnlyList<MobileUnitSnapshot>> ownedUnits,
            IOriginalJassFusionResourceReader resourceReader,
            IOriginalJassFusionAtomicCommit verifiedTransaction,
            IOriginalJassFusionCastPolicy originalCastPolicy)
        {
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.ownedUnits = ownedUnits ?? throw new ArgumentNullException(nameof(ownedUnits));
            this.resources = resourceReader;
            this.transaction = verifiedTransaction;
            this.castPolicy = originalCastPolicy ??
                throw new ArgumentNullException(nameof(originalCastPolicy));
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> owned)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            int? gold, lumber;
            ReadBalance(out gold, out lumber);
            var list = planner.AssessNamedResults(owned, gold, lumber);
            var previews = new List<MobileFusionPreview>();
            foreach (var assessment in list)
            {
                var missing = new List<string>(assessment.BlockingReasons);
                previews.Add(new MobileFusionPreview(assessment.AbilityRawcode,
                    assessment.ResultName + " (원본 JASS)",
                    BuildMaterialSummary(assessment),
                    assessment.SuggestedMaterialIds, missing));
            }
            return previews.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string abilityRawcode,
            IReadOnlyList<long> selectedMaterialIds)
        {
            if (transaction == null)
                return ManualActionResult.Unavailable("원본 조합 재료/자원 원자적 거래 서비스");
            if (selectedMaterialIds == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "선택한 조합 재료가 없습니다.");

            // Do not trust an earlier preview: the current world and verified
            // currency values are retrieved immediately before confirmation.
            var current = ownedUnits();
            if (current == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "보유 캐릭터 정보를 확인할 수 없습니다.");
            int? gold, lumber;
            ReadBalance(out gold, out lumber);
            JassFusionReadiness assessed;
            try { assessed = planner.Assess(abilityRawcode, current, gold, lumber); }
            catch (KeyNotFoundException)
            {
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 JASS에 존재하지 않는 조합 능력입니다.");
            }
            if (!assessed.IsReadyForUserConfirmation)
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 제작 조건이 충족되지 않았습니다: " +
                    assessed.BlockingReasons[0]);

            // For the assisted mobile workflow, deterministic material selection
            // must be identical to the fresh audited plan (no stale selection).
            var required = new HashSet<long>(assessed.SuggestedMaterialIds);
            var offered = new HashSet<long>(selectedMaterialIds);
            if (required.Count != offered.Count ||
                selectedMaterialIds.Count != required.Count ||
                !required.SetEquals(offered))
                return new ManualActionResult(ManualActionState.Rejected,
                    "선택 이후 재료가 변경됐습니다. 조합 목록을 갱신하세요.");

            long verifiedTarget;
            string reason;
            if (!castPolicy.TryChooseCastTarget(assessed.AbilityRawcode,
                assessed.SuggestedMaterialIds, out verifiedTarget, out reason) ||
                verifiedTarget <= 0)
                return new ManualActionResult(ManualActionState.Rejected,
                    string.IsNullOrWhiteSpace(reason)
                        ? "원본 조합 스킬 대상 판정 미완료"
                        : reason);

            // The external transaction handles currency debit, unit removal,
            // spawning/placement and full rollback as one indivisible operation.
            // It MUST recheck the approved target before committing.
            return transaction.TryCommit(
                new OriginalJassFusionCommitOrder(assessed, verifiedTarget)) ??
                new ManualActionResult(ManualActionState.Rejected,
                    "원본 조합 트랜잭션이 결과를 반환하지 않았습니다.");
        }

        private void ReadBalance(out int? gold, out int? lumber)
        {
            gold = null;
            lumber = null;
            int actualGold, actualLumber;
            if (resources != null &&
                resources.TryReadBalances(out actualGold, out actualLumber) &&
                actualGold >= 0 && actualLumber >= 0)
            {
                gold = actualGold;
                lumber = actualLumber;
            }
        }

        private static string BuildMaterialSummary(JassFusionReadiness preview)
        {
            var parts = new List<string>();
            foreach (var material in preview.Materials)
                parts.Add(material.Name + " x" + material.RequiredCount);
            return string.Join(" + ", parts.ToArray()) + " | 골드 " +
                preview.GoldCost + " / 목재 " + preview.LumberCost +
                " | 원본 추가조건 별도 검증";
        }
    }
}
