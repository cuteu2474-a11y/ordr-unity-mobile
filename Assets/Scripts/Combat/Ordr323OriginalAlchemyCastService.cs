using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Read from the real target object in the LOCKED world, not from a
    // catalogue name match. A0BA/A0B9 can be granted dynamically by JASS.
    public sealed class Ordr323AlchemyTargetEvidence
    {
        public long InstanceId { get; private set; }
        public string OriginalRawcode { get; private set; }
        public int OwnerPlayerId { get; private set; }
        public int A0BALevel { get; private set; }
        public int A0B9Level { get; private set; }

        public Ordr323AlchemyTargetEvidence(long instanceId, string rawcode,
            int ownerPlayerId, int a0baLevel, int a0b9Level)
        {
            if (instanceId <= 0 || string.IsNullOrWhiteSpace(rawcode) || rawcode.Length != 4 ||
                ownerPlayerId < 0 || a0baLevel < 0 || a0b9Level < 0)
                throw new ArgumentException("원본 연금술 대상 인스턴스/소유권/능력 근거가 잘못되었습니다.");
            InstanceId = instanceId;
            OriginalRawcode = rawcode;
            OwnerPlayerId = ownerPlayerId;
            A0BALevel = a0baLevel;
            A0B9Level = a0b9Level;
        }
    }

    // The exclusive transaction must restore *all* unit, mana, cooldown,
    // inventory, spawn, and original-effect changes on Dispose without Commit.
    // Apply must reproduce CJZ -> BdD -> CJY, including player start location,
    // holdposition and undead A07Y. Null recipe means no R3 entry: the
    // ORIGINAL CJZ still consumes an otherwise eligible target, with no refund.
    public interface IOrdr323AlchemyTransaction : IDisposable
    {
        bool TryInspectTarget(long instanceId, out Ordr323AlchemyTargetEvidence target);
        bool TryVerifyOriginalNativeCast(IOriginalHelperCowCombatActor helper,
            long targetInstanceId, double originalCastRange, out string reason);
        bool TryApplyOriginalAlchemy(long targetInstanceId,
            Ordr323AlchemyRefundRecipe originalRecipe, double originalManaCost,
            double originalCooldownSeconds, out string reason);
        void Commit();
    }

    public interface IOrdr323AlchemyTransactionalWorld
    {
        IOrdr323AlchemyTransaction BeginAlchemyTransaction();
    }

    // No source-backed object proof or atomic world means NO cast. The
    // inspected target, native approval, mana payment and refund are processed
    // inside one lock, then a successful commit starts the 5-second cooldown.
    public sealed class Ordr323OriginalAlchemyCastService
    {
        private readonly GameSession session;
        private readonly IOriginalHelperCowCombatActor helper;
        private readonly Ordr323OriginalAlchemyRefundCatalog refunds;
        private readonly IOrdr323AlchemyTransactionalWorld world;
        private readonly Func<double> gameTime;
        private bool busy;
        private double nextReadyAt;

        public Ordr323OriginalAlchemyCastService(GameSession game,
            IOriginalHelperCowCombatActor liveHelper,
            Ordr323OriginalAlchemyRefundCatalog originalRefunds,
            IOrdr323AlchemyTransactionalWorld verifiedWorld, Func<double> now)
        {
            session = game ?? throw new ArgumentNullException(nameof(game));
            helper = liveHelper ?? throw new ArgumentNullException(nameof(liveHelper));
            refunds = originalRefunds ?? throw new ArgumentNullException(nameof(originalRefunds));
            world = verifiedWorld ?? throw new ArgumentNullException(nameof(verifiedWorld));
            gameTime = now ?? throw new ArgumentNullException(nameof(now));
        }

        public double CooldownRemaining
        {
            get { return Math.Max(0, nextReadyAt - gameTime()); }
        }

        public bool CanAttempt => !busy && session.Phase == SessionPhase.Running &&
            ValidHelper() && helper.CurrentMana >= Ordr323OriginalHelperAlchemy.ManaCost &&
            Finite(gameTime()) && gameTime() >= 0 && CooldownRemaining <= 0;

        public ManualActionResult TryCast(long targetInstanceId)
        {
            if (targetInstanceId <= 0)
                return Reject("연금술 대상을 선택해 주세요.");
            if (!CanAttempt)
                return Reject("연금술 사용 불가: 도움소/마나/쿨타임/진행 상태를 확인해 주세요.");
            busy = true;
            try
            {
                // Only the exclusive original-world snapshot authorizes a
                // rawcode and its dynamically granted A0BA/A0B9 levels.
                using (var tx = world.BeginAlchemyTransaction())
                {
                    if (tx == null) return Reject("원본 연금술 거래 잠금을 얻지 못했습니다.");
                    Ordr323AlchemyTargetEvidence target;
                    if (!tx.TryInspectTarget(targetInstanceId, out target) || target == null ||
                        target.InstanceId != targetInstanceId)
                        return Reject("선택한 유닛이 원본 월드에 존재하지 않습니다.");
                    if (!Ordr323OriginalHelperAlchemy.IsOriginalTargetEligible(
                            helper.OwnerPlayerId, target.OwnerPlayerId,
                            target.OriginalRawcode, target.A0BALevel, target.A0B9Level))
                        return Reject("원본 연금술은 소유한 특별/희귀 대상에게만 사용 가능합니다.");
                    if (!ValidHelper() ||
                        helper.CurrentMana < Ordr323OriginalHelperAlchemy.ManaCost ||
                        CooldownRemaining > 0)
                        return Reject("거래 확정 전 도움소의 마나 또는 능력 상태가 변경되었습니다.");
                    string reason;
                    if (!tx.TryVerifyOriginalNativeCast(helper, targetInstanceId,
                            Ordr323OriginalHelperAlchemy.CastRange, out reason))
                        return Reject(SafeReason(reason, "원본 시전 가능 조건 미충족"));
                    // A valid eligible target with no R3 recipe is *still*
                    // consumed by CJZ; never fabricate a refund.
                    var recipe = refunds.Find(target.OriginalRawcode);
                    if (!tx.TryApplyOriginalAlchemy(targetInstanceId, recipe,
                            Ordr323OriginalHelperAlchemy.ManaCost,
                            Ordr323OriginalHelperAlchemy.CooldownSeconds, out reason))
                        return Reject(SafeReason(reason, "연금술 소비/환급 거래 거절"));
                    double committedAt = gameTime();
                    if (!Finite(committedAt) || committedAt < 0)
                        return Reject("시전 중 원본 전투 시간이 유효하지 않게 되었습니다.");
                    tx.Commit();
                    nextReadyAt = committedAt + Ordr323OriginalHelperAlchemy.CooldownSeconds;
                    return new ManualActionResult(ManualActionState.Succeeded,
                        recipe == null ? "연금술 완료: 원본 R3 재료 등록이 없어 환급은 없습니다." :
                        "원본 연금술 소비·재료 환급이 완료되었습니다.");
                }
            }
            catch (Exception ex)
            {
                // Dispose rolls back even a partial Apply/failed Commit.
                return Reject("연금술 거래 중단(롤백): " + ex.Message);
            }
            finally { busy = false; }
        }

        private bool ValidHelper()
        {
            return helper.IsAlive &&
                helper.OriginalRawcode == Ordr323OriginalHelperSource.Rawcode &&
                helper.GetAbilityLevel(Ordr323OriginalHelperAlchemy.AbilityRawcode) == 1 &&
                helper.IsOriginalCastReady(Ordr323OriginalHelperAlchemy.AbilityRawcode) &&
                !double.IsNaN(helper.CurrentMana) &&
                !double.IsInfinity(helper.CurrentMana) &&
                helper.CurrentMana >= 0 && helper.CurrentMana <= helper.MaximumMana;
        }

        private static bool Finite(double number)
        {
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static string SafeReason(string reason, string fallback)
        {
            return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
        }

        private static ManualActionResult Reject(string reason)
        {
            return new ManualActionResult(ManualActionState.Rejected, reason);
        }
    }
}
