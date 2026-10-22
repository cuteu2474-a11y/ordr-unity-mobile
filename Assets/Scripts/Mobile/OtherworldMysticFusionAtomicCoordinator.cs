using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Exclusive transaction: Dispose without Commit must roll back every mutation.
    public interface IOriginalOtherworldMysticFusionTransaction : IDisposable
    {
        IReadOnlyList<MobileUnitSnapshot> OwnedUnits();
        bool IsStoryDeployed(long instanceId);
        bool TryReadBalances(out int gold, out int lumber);
        // Must independently verify exact original JASS rawcode mappings, extra
        // abilities/research, named requirements such as token/ship, and cast rules.
        // The web-dex recipe alone must NEVER satisfy this check.
        bool TryVerifyOriginalConditions(OtherworldMysticRecipe recipe,
            IReadOnlyList<long> materialInstanceIds, out string rejectionReason);
        // Includes all original costs/items, unit removals, resulting spawn and
        // any side effects under the same exclusive/rollback-capable transaction.
        bool TryApplyOriginalFusion(OtherworldMysticRecipe recipe,
            IReadOnlyList<long> materialInstanceIds, out string rejectionReason);
        void Commit();
    }

    public interface IOriginalOtherworldMysticTransactionalWorld
    {
        IOriginalOtherworldMysticFusionTransaction BeginMysticFusionTransaction();
    }

    // Executes a Mystic recipe ONLY through an independently verified original
    // world implementation. Until one is attached, the preview port remains inert.
    public sealed class OtherworldMysticFusionAtomicCoordinator : IVerifiedOtherworldMysticFusionCommit
    {
        private readonly OtherworldMysticFusionCatalog catalog;
        private readonly IOriginalOtherworldMysticTransactionalWorld world;

        public OtherworldMysticFusionAtomicCoordinator(OtherworldMysticFusionCatalog catalog,
            IOriginalOtherworldMysticTransactionalWorld verifiedWorld)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            world = verifiedWorld ?? throw new ArgumentNullException(nameof(verifiedWorld));
        }

        public ManualActionResult TryCommit(OtherworldMysticRecipe recipe,
            IReadOnlyList<long> exactMaterialInstanceIds)
        {
            if (recipe == null || !ReferenceEquals(recipe, catalog.Find(recipe.ResultUnitId)))
                return Reject("원본 신비 도감에 등록된 동일한 조합식이 아닙니다.");
            if (exactMaterialInstanceIds == null)
                return Reject("선택한 재료가 없습니다.");
            try
            {
                using (var transaction = world.BeginMysticFusionTransaction())
                {
                    if (transaction == null) return Reject("원본 월드 잠금을 획득하지 못했습니다.");
                    var owned = transaction.OwnedUnits();
                    if (owned == null) return Reject("원본 보유 유닛을 읽을 수 없습니다.");
                    var live = new Dictionary<long, MobileUnitSnapshot>();
                    foreach (var unit in owned)
                    {
                        if (unit == null || unit.InstanceId <= 0 || live.ContainsKey(unit.InstanceId))
                            return Reject("잠긴 원본 유닛 목록이 잘못되었습니다.");
                        live.Add(unit.InstanceId, unit);
                    }

                    var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
                    var unique = new HashSet<long>();
                    foreach (long id in exactMaterialInstanceIds)
                    {
                        MobileUnitSnapshot unit;
                        if (id <= 0 || !unique.Add(id) || !live.TryGetValue(id, out unit))
                            return Reject("없는 재료 또는 중복된 인스턴스가 있습니다.");
                        if (transaction.IsStoryDeployed(id))
                            return Reject("스토리 파견 중인 유닛은 재료로 소모할 수 없습니다.");
                        int count;
                        chosen.TryGetValue(unit.Unit.Id, out count);
                        chosen[unit.Unit.Id] = checked(count + 1);
                    }
                    int requiredTotal = 0;
                    int wildcardRandoms = 0;
                    foreach (var material in recipe.Ingredients)
                    {
                        requiredTotal = checked(requiredTotal + material.Count);
                        if (material.Kind == "any-random")
                        {
                            wildcardRandoms = checked(wildcardRandoms + material.Count);
                            continue;
                        }
                        int count;
                        chosen.TryGetValue(material.UnitId, out count);
                        if (count < material.Count)
                            return Reject("고정 조합 재료가 부족합니다: " + material.DisplayName);
                        chosen[material.UnitId] = count - material.Count;
                    }
                    if (exactMaterialInstanceIds.Count != requiredTotal)
                        return Reject("원본 조합의 총 재료 개수가 일치하지 않습니다.");

                    foreach (var pair in chosen)
                    {
                        if (pair.Value == 0) continue;
                        // By construction every selected ID belongs to the locked
                        // owned list, so lookup never needs an external catalog.
                        string grade = null;
                        foreach (long id in exactMaterialInstanceIds)
                            if (live[id].Unit.Id == pair.Key)
                            {
                                grade = live[id].Unit.Grade;
                                break;
                            }
                        if (grade != "랜덤")
                            return Reject("원본 조합식에 없는 유닛이 추가되었습니다.");
                        wildcardRandoms -= pair.Value;
                    }
                    if (wildcardRandoms != 0)
                        return Reject("추가 랜덤 등급 재료의 수량이 다릅니다.");

                    int gold, lumber;
                    if (!transaction.TryReadBalances(out gold, out lumber) ||
                        gold < recipe.Gold || lumber < recipe.Lumber)
                        return Reject("원본 골드/목재 잔액을 충족하지 못했습니다.");
                    string reason;
                    if (!transaction.TryVerifyOriginalConditions(recipe,
                            exactMaterialInstanceIds, out reason))
                        return Reject("원본 JASS 특수 조건 미충족: " + SafeReason(reason));
                    if (!transaction.TryApplyOriginalFusion(recipe,
                            exactMaterialInstanceIds, out reason))
                        return Reject("원본 조합 거래 실패: " + SafeReason(reason));
                    transaction.Commit();
                    return new ManualActionResult(ManualActionState.Succeeded,
                        "원본 신비 조합 거래가 완료됐습니다.");
                }
            }
            catch (Exception e)
            {
                // The transaction's Dispose rolls back when Commit was not called.
                return Reject("원본 거래가 중단되어 롤백했습니다: " + e.Message);
            }
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ? "검증 구현체에서 거절" : reason;
        }

        private static ManualActionResult Reject(string reason)
        {
            return new ManualActionResult(ManualActionState.Rejected, reason);
        }
    }
}
