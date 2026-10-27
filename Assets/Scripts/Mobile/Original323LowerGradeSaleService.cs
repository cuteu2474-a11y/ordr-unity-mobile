using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Source: pinned war3map.j CRO/CRP/CRQ/CRR + CRX registrations.
    // Mobile Wisps are the existing shared-wallet resource, never free units.
    // Randomness shares the recruitment/exchange lane; distribution parity,
    // not a claim of identical Warcraft native seeds/global call ordering.
    public sealed class Original323LowerGradeSaleService : IOriginalCommonSalePort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog objects;
        private readonly IOriginalWispRandom random;
        private readonly Dictionary<long, ResourceBundle> pending =
            new Dictionary<long, ResourceBundle>();
        private bool busy;
        public int CommonSold { get; private set; }
        public int CommonPoints => CommonSold % 3;

        public Original323LowerGradeSaleService(CharacterCombatRuntime world,
            SharedEconomySession shared, OriginalMapBaseStatCatalog source,
            IOriginalWispRandom sharedRandom)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            objects = source ?? throw new ArgumentNullException(nameof(source));
            random = sharedRandom ?? throw new ArgumentNullException(nameof(sharedRandom));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("판매와 전투는 같은 보유 목록을 사용해야 합니다.");
        }

        public bool CanSell(MobileUnitSnapshot unit)
        {
            if (unit == null || combat.IsStoryDeployed(unit.InstanceId)) return false;
            var original = objects.Find(unit.Unit.Id);
            string ability = SaleAbility(unit.Unit.Grade);
            return original != null && ability != null &&
                Contains(original.OriginalAbilityRawcodes, ability);
        }

        public ManualActionResult ConfirmSale(long id)
        {
            var unit = Find(id);
            if (unit == null || unit.Unit.Grade != "흔함")
                return Reject("보유한 흔함 유닛을 선택해 주세요.");
            return Sell(id);
        }

        public ManualActionResult Sell(long id)
        {
            if (busy || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return Reject("진행 중인 세션에서만 판매할 수 있습니다.");
            foreach (long obsolete in new List<long>(pending.Keys))
                if (Find(obsolete) == null) pending.Remove(obsolete);
            if (pending.Count > 0 && !pending.ContainsKey(id))
                return Reject("이전 판매 결과가 대기 중입니다. 같은 유닛의 판매를 먼저 완료해 주세요.");
            var unit = Find(id);
            if (!CanSell(unit)) return Reject("판매 가능한 보유 유닛이 아니거나 파견 중입니다.");
            busy = true;
            // Mobile fallback rolls can rewind when a sale is rejected. A
            // witnessed original RNG has no rewind authority: keep the
            // already-selected outcome in pending for that source instead.
            var rewindable = random as IOriginal323RollbackWispRandom;
            int checkpoint = rewindable == null ? -1 : rewindable.CaptureCheckpoint();
            bool completed = false;
            try
            {
                int nextCommon = unit.Unit.Grade == "흔함" ? checked(CommonSold + 1) : CommonSold;
                using (var removal = combat.BeginOriginalUnitRemoval(id))
                {
                    ResourceBundle reward;
                    if (!pending.TryGetValue(id, out reward))
                    {
                        reward = Plan(unit.Unit.Grade);
                        pending.Add(id, reward); // Retain a rolled result after a failed commit.
                    }
                    var before = economy.Wallet.CaptureState();
                    try
                    {
                        economy.Wallet.Grant(reward);
                        removal.Commit();
                    }
                    catch
                    {
                        economy.Wallet.RestoreState(before);
                        throw;
                    }
                    CommonSold = nextCommon;
                    pending.Remove(id);
                    completed = true;
                    return new ManualActionResult(ManualActionState.Succeeded,
                        unit.Unit.Name + " 판매 · 랜덤위습 " + reward.Get(WispResourceIds.Random) +
                        "개 · 목재 " + reward.Get("wood") + "개" +
                        (unit.Unit.Grade == "흔함" ? " · 판매 포인트 " + CommonPoints + "/3" : ""));
                }
            }
            catch (Exception error) { return Reject("판매를 취소했습니다: " + error.Message); }
            finally
            {
                try
                {
                    if (!completed && rewindable != null)
                    {
                        rewindable.RestoreCheckpoint(checkpoint);
                        pending.Remove(id);
                    }
                }
                finally { busy = false; }
            }
        }

        private ResourceBundle Plan(string grade)
        {
            int wisps = 0, wood = 0;
            switch (grade)
            {
                case "흔함":
                    var common = OriginalCommonSaleRules.PlanAfterConfirmedSale(CommonSold,
                        OriginalCommonSaleRules.RequiresBonusWoodRoll(CommonSold) ? (int?)Roll() : null);
                    wisps = common.RandomWispsAwarded; wood = common.BonusWoodAwarded; break;
                case "안흔함":
                    wisps = Roll() <= 50 ? 1 : 0;
                    wood = Roll() <= 20 ? 1 : 0; break;
                case "특별함": wisps = 1; wood = Roll() <= 35 ? 1 : 0; break;
                case "희귀함": wisps = 2; wood = 1; break;
                default: throw new InvalidOperationException("원본 판매 규칙 미확인");
            }
            var result = new ResourceBundle();
            if (wisps > 0) result.Add(WispResourceIds.Random, wisps);
            if (wood > 0) result.Add("wood", wood);
            return result;
        }
        private int Roll()
        {
            int value = random.NextInclusive(1, 100);
            if (value < 1 || value > 100) throw new InvalidOperationException("판매 난수 범위 오류");
            return value;
        }
        public static string SaleDescription(string grade)
        {
            switch (grade)
            {
                case "흔함": return "3기 판매마다 랜덤위습 1개 · 그때 목재 35%";
                case "안흔함": return "랜덤위습 1개 50% · 별도로 목재 1개 20%";
                case "특별함": return "랜덤위습 1개 · 목재 1개 35%";
                case "희귀함": return "랜덤위습 2개 · 목재 1개";
                default: return null;
            }
        }
        private static string SaleAbility(string grade)
        {
            switch (grade)
            {
                case "흔함": return "A09G";
                case "안흔함": return "A0B8";
                case "특별함": return "A0BA";
                case "희귀함": return "A0B9";
                default: return null;
            }
        }
        private MobileUnitSnapshot Find(long id)
        {
            foreach (var row in combat.MobileUnitSnapshot()) if (row.InstanceId == id) return row;
            return null;
        }
        private static bool Contains(IReadOnlyList<string> list, string value)
        { foreach (var item in list) if (item == value) return true; return false; }
        private static ManualActionResult Reject(string text)
        { return new ManualActionResult(ManualActionState.Rejected, text); }
    }
}
