using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Mandatory source-verified native witness. CWR output rawcode alone
    // cannot prove CHu/CHx spawn placement or all follow-up effects.
    // Preview reconciliation mappings must NOT implement this contract.
    public interface IOriginalJassVerifiedFusionSwapRules :
        IOriginalJassLiveObjectReader
    {
        // Authorizes a complete native result for this exact ORIGINAL order,
        // including original result placement and any post-craft side effects.
        // Return false if ANY required CHu/CHx behavior remains unverified.
        bool TryResolveCompleteOriginalFusion(
            OriginalJassSimpleFusionCommand order,
            IReadOnlyList<MobileUnitSnapshot> originalMaterials,
            out double verifiedX, out double verifiedY, out string reason);

        // Binds the actual newly spawned actor to the source-original UNIT
        // rawcode, not a Korean display name or grade. Must fail closed.
        bool TryBindStagedResult(long spawnedInstanceId,
            OriginalJassSimpleFusionCommand order, out string reason);

        // Undo the generated identity if the transaction never commits.
        void DiscardStagedResult(long spawnedInstanceId);
    }

    // Concrete unit-world implementation: uses the same CharacterCombatRuntime
    // instance as the shared-wallet bridge, NOT an independent fake roster.
    public sealed class OriginalJassCombatUnitFusionWorld :
        IOriginalJassReversibleUnitFusionWorld
    {
        private readonly IOriginalJassVerifiedFusionSwapRules originalRules;
        private bool busy;

        public OriginalJassCombatUnitFusionWorld(
            IOriginalJassVerifiedFusionSwapRules verifiedNativeRules)
        {
            originalRules = verifiedNativeRules ??
                throw new ArgumentNullException(nameof(verifiedNativeRules));
        }

        public IOriginalJassReversibleUnitFusionTransaction BeginExclusiveUnitFusion(
            CharacterCombatRuntime combat, SharedEconomySession economy)
        {
            if (combat == null || economy == null ||
                !Object.ReferenceEquals(combat.Roster, economy.Units))
                throw new InvalidOperationException(
                    "조합 재료가 공유 전투 로스터의 실제 인스턴스가 아닙니다.");
            if (busy)
                throw new InvalidOperationException("이미 원본 유닛 조합 거래가 실행 중입니다.");
            busy = true;
            return new Transaction(this, combat, originalRules);
        }

        private sealed class Transaction : IOriginalJassReversibleUnitFusionTransaction
        {
            private readonly OriginalJassCombatUnitFusionWorld owner;
            private readonly CharacterCombatRuntime combat;
            private readonly IOriginalJassVerifiedFusionSwapRules rules;
            private CharacterCombatRuntime.OriginalFusionUnitSwap swap;
            private long resultId;
            private bool attempted;
            private bool staged;
            private bool committed;
            private bool disposed;

            internal Transaction(OriginalJassCombatUnitFusionWorld owner,
                CharacterCombatRuntime combat, IOriginalJassVerifiedFusionSwapRules rules)
            {
                this.owner = owner;
                this.combat = combat;
                this.rules = rules;
            }

            public bool TryStageOriginalUnitFusion(
                OriginalJassSimpleFusionCommand command,
                out long created, out string reason)
            {
                created = 0;
                reason = null;
                if (disposed || committed || attempted || command == null)
                {
                    reason = "원본 유닛 조합 중복 요청 또는 누락된 명령";
                    return false;
                }
                attempted = true;
                if (command.MaterialInstanceIds.Count == 0 ||
                    command.MaterialInstanceIds.Count != command.MaterialRawcodes.Count ||
                    command.CastTargetInstanceId <= 0)
                {
                    reason = "원본 조합 재료와 대상 정보가 불완전합니다.";
                    return false;
                }
                var current = combat.MobileUnitSnapshot();
                var byId = new Dictionary<long, MobileUnitSnapshot>();
                foreach (var item in current)
                {
                    if (item == null || byId.ContainsKey(item.InstanceId))
                    {
                        reason = "실제 보유 유닛이 중복되거나 손상됐습니다.";
                        return false;
                    }
                    byId.Add(item.InstanceId, item);
                }
                var materials = new List<MobileUnitSnapshot>();
                var used = new HashSet<long>();
                for (int i = 0; i < command.MaterialInstanceIds.Count; i++)
                {
                    long id = command.MaterialInstanceIds[i];
                    MobileUnitSnapshot unit;
                    string original;
                    if (!used.Add(id) || !byId.TryGetValue(id, out unit) ||
                        combat.IsStoryDeployed(id) ||
                        !rules.TryReadOriginalRawcode(id, out original) ||
                        !String.Equals(original, command.MaterialRawcodes[i],
                            StringComparison.Ordinal))
                    {
                        reason = "원본 재료 실물 ID 또는 파견 상태를 확인할 수 없습니다.";
                        return false;
                    }
                    materials.Add(unit);
                }

                double x, y;
                if (!rules.TryResolveCompleteOriginalFusion(
                    command, materials.AsReadOnly(), out x, out y, out reason))
                {
                    if (string.IsNullOrWhiteSpace(reason))
                        reason = "원본 CHu/CHx 결과·후속효과가 아직 검증되지 않았습니다.";
                    return false;
                }
                if (double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y))
                {
                    reason = "원본 조합 결과 생성 위치가 유효하지 않습니다.";
                    return false;
                }

                // LIVE effect: create a real CharacterInstance and temporarily
                // hide source units from the fusion-owned inventory view.
                // Their attack timers, auras and skills stay intact on abort.
                swap = combat.BeginOriginalFusionUnitSwap(
                    command.MaterialInstanceIds, command.ResultUnitId, x, y);
                resultId = swap.ResultInstanceId;
                string actualRawcode;
                if (!rules.TryBindStagedResult(resultId, command, out reason) ||
                    !rules.TryReadOriginalRawcode(resultId, out actualRawcode) ||
                    !String.Equals(actualRawcode, command.ResultRawcode,
                        StringComparison.Ordinal))
                {
                    reason = string.IsNullOrWhiteSpace(reason)
                        ? "원본 결과 객체 ID와 생성된 전투 유닛이 다릅니다."
                        : reason;
                    return false;
                }
                staged = true;
                created = resultId;
                return true;
            }

            public void Commit()
            {
                if (disposed || committed || !staged || swap == null)
                    throw new InvalidOperationException(
                        "원본 결과 유닛 검증을 완료하지 않았습니다.");
                // This is the FIRST irreversible source teardown: it runs
                // after higher-level unit-set/rawcode/wallet audits pass.
                swap.Commit();
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (swap != null) swap.Dispose();
                }
                finally
                {
                    try
                    {
                        if (!committed && resultId > 0)
                            rules.DiscardStagedResult(resultId);
                    }
                    finally { owner.busy = false; }
                }
            }
        }
    }
}
