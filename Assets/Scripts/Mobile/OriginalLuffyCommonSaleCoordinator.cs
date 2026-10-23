using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // A single exclusive transaction must encompass the exact owned unit
    // removal (BdD), LY counter update, Bcz spawn of original e0IX Wisp,
    // and Bci award of lumber, including rollback on failure.
    // The original JASS proves all three operations; a wallet-only token is
    // NOT equivalent to creating the original Wisp object.
    public interface IOriginalCommonSaleTransaction : IDisposable
    {
        // Read the ACTUAL instance/rawcode/owner under the locked world,
        // not merely the character's display-name.
        bool TryReadUnit(long instanceId, out string catalogId,
            out string originalRawcode, out bool storyDeployed, out string reason);
        bool TryReadSaleCount(out int confirmedSales, out string reason);
        // Called ONLY on sale #3, #6, #9...; inclusive 1..100.
        bool TryRollOriginalBonusWood(out int result, out string reason);
        // Must stage all original effects; rollback Dispose on false/throw.
        bool TryApplySale(long instanceId, int newConfirmedSales,
            string originalWispRawcode, double wispSpawnX, double wispSpawnY,
            int wispCount, int woodCount,
            out string reason);
        void Commit();
    }

    public interface IOriginalCommonSaleTransactionalWorld
    {
        // Implementation must provide an exclusive player/session snapshot and
        // restore it on Dispose unless Commit completed successfully.
        IOriginalCommonSaleTransaction BeginCommonSale();
    }

    // Only the C-01 (h003) path is authorized during the common-first rollout.
    // Other common units are deferred until their own source proof is consumed.
    public sealed class OriginalLuffyCommonSaleCoordinator : IOriginalCommonSalePort
    {
        public const string UnitCatalogId = "C-01";
        public const string UnitOriginalRawcode = "h003";
        // war3map.j L318: constant integer YR=$65304958 ('e0IX')
        public const string OriginalWispRawcode = "e0IX";
        // war3map.j L14767 CJ=Rect(-3584,-9824,-3328,-9568);
        // Bcz L8291..8293 creates e0IX at the center of this rectangle.
        public const double OriginalWispSpawnX = -3456.0;
        public const double OriginalWispSpawnY = -9696.0;

        private readonly IOriginalCommonSaleTransactionalWorld world;

        public OriginalLuffyCommonSaleCoordinator(
            IOriginalCommonSaleTransactionalWorld verifiedWorld)
        {
            world = verifiedWorld ?? throw new ArgumentNullException(nameof(verifiedWorld));
        }

        public ManualActionResult ConfirmSale(long exactInstanceId)
        {
            if (exactInstanceId <= 0)
                return Reject("판매할 루피 인스턴스 ID가 유효하지 않습니다.");

            try
            {
                using (var transaction = world.BeginCommonSale())
                {
                    if (transaction == null)
                        return Reject("공유 게임 거래 잠금을 확보하지 못했습니다.");
                    string catalog, rawcode, reason;
                    bool deployed;
                    if (!transaction.TryReadUnit(exactInstanceId, out catalog,
                            out rawcode, out deployed, out reason) ||
                        catalog != UnitCatalogId || rawcode != UnitOriginalRawcode)
                        return Reject("판매할 유닛이 원본 h003 루피가 아니거나 소유하지 않았습니다.");
                    if (deployed)
                        return Reject("스토리 파견 중인 루피는 판매할 수 없습니다.");

                    int alreadySold;
                    if (!transaction.TryReadSaleCount(out alreadySold, out reason) ||
                        alreadySold < 0 || alreadySold == int.MaxValue)
                        return Reject("판매 누적 LY 상태가 올바르지 않습니다.");

                    int? roll = null;
                    if (OriginalCommonSaleRules.RequiresBonusWoodRoll(alreadySold))
                    {
                        int sampled;
                        if (!transaction.TryRollOriginalBonusWood(out sampled, out reason))
                            return Reject("원본 1~100 판매 목재 난수 확인 실패");
                        roll = sampled;
                    }
                    // This enforces the inclusive 35% boundary and guards
                    // against invalid/extra RNG. No resource is applied here.
                    OriginalCommonSaleOutcome outcome =
                        OriginalCommonSaleRules.PlanAfterConfirmedSale(alreadySold, roll);
                    if (!transaction.TryApplySale(exactInstanceId, outcome.TotalSold,
                            OriginalWispRawcode, OriginalWispSpawnX,
                            OriginalWispSpawnY, outcome.RandomWispsAwarded,
                            outcome.BonusWoodAwarded, out reason))
                        return Reject(string.IsNullOrWhiteSpace(reason)
                            ? "원본 판매 유닛 제거·위습/목재 지급 거래를 완료하지 못했습니다."
                            : reason);
                    transaction.Commit();
                    return new ManualActionResult(ManualActionState.Succeeded,
                        "루피 판매: 누적 " + outcome.PointsTowardNextWisp + "/3, " +
                        "위습 " + outcome.RandomWispsAwarded + ", 목재 " +
                        outcome.BonusWoodAwarded);
                }
            }
            catch (Exception ex)
            {
                // The transactional world MUST roll back any tentative unit,
                // RNG, LY, Wisp spawn and wood change on disposal/failure.
                return Reject("원본 판매 거래 롤백: " + ex.Message);
            }
        }

        private static ManualActionResult Reject(string reason)
        {
            return new ManualActionResult(ManualActionState.Rejected, reason);
        }
    }
}
