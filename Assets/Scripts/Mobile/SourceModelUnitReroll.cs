using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    [Serializable]
    public sealed class SourceModelUnitRerollState
    {
        public int Version = 1;
        public long PendingActor;
        public string PendingRawcode;
        public Original323GamblingNavigation PendingNavigation;
    }

    // CRj/CRl are manual unit replacements, not attack buffs.
    public sealed class SourceModelUnitReroll
    {
        private readonly CharacterCombatRuntime combat;
        private readonly EconomyWallet wallet;
        private readonly Original323UnitGamblingWorld world;
        private readonly Original323WispDeferredAbilityRegistry abilities;
        private readonly IOriginalWispRandom random;
        private readonly Func<bool> gate;
        private readonly Func<Original323GamblingNavigation> navigation;
        private bool processing;
        private long pendingActor;
        private string pendingRawcode;
        private Original323GamblingNavigation pendingNavigation;
        public SourceModelUnitRerollState CaptureState()
        {
            if (processing) throw new InvalidOperationException("리롤 중에는 저장할 수 없습니다.");
            ClearMissingPendingActor();
            var state = new SourceModelUnitRerollState {
                PendingActor = pendingActor, PendingRawcode = pendingRawcode,
                PendingNavigation = pendingNavigation
            };
            ValidateState(state);
            return state;
        }

        public static void ValidateState(SourceModelUnitRerollState state)
        {
            if (state == null || state.Version != 1 ||
                !Enum.IsDefined(typeof(Original323GamblingNavigation),
                    state.PendingNavigation) || state.PendingActor < 0 ||
                (state.PendingActor == 0 && state.PendingRawcode != null) ||
                (state.PendingActor == 0 && state.PendingNavigation !=
                    Original323GamblingNavigation.None))
                throw new FormatException("유닛 리롤 저장 정보가 올바르지 않습니다.");
            if (state.PendingRawcode == null) return;
            bool known = false;
            foreach (var raw in Original323OtherworldGamblingRules.CasinoRareRawcodes)
                if (raw == state.PendingRawcode) known = true;
            foreach (var raw in Original323OtherworldGamblingRules.RandomRawcodes)
                if (raw == state.PendingRawcode) known = true;
            if (!known) throw new FormatException("리롤 결과가 원본 대상 풀에 없습니다.");
        }

        public void RestoreState(SourceModelUnitRerollState state)
        {
            ValidateState(state);
            if (processing || pendingActor != 0 || pendingRawcode != null ||
                (state.PendingActor != 0 && (!Exists(state.PendingActor) ||
                    state.PendingNavigation != navigation() ||
                    !HasRerollAbility(state.PendingActor))))
                throw new InvalidOperationException("유닛 리롤은 복원된 실제 원본 유닛/항법이 필요합니다.");
            pendingActor = state.PendingActor;
            pendingRawcode = state.PendingRawcode;
            pendingNavigation = state.PendingNavigation;
        }

        public bool HasPendingDraw
        {
            get
            {
                ClearMissingPendingActor();
                return processing || pendingActor != 0;
            }
        }
        public event Action<Exception> PostCommitObserverFailed;
        public SourceModelUnitReroll(CharacterCombatRuntime runtime, EconomyWallet shared,
            Original323UnitGamblingWorld rewards, Original323WispDeferredAbilityRegistry registry,
            IOriginalWispRandom scriptRandom, Func<bool> interaction,
            Func<Original323GamblingNavigation> selectedNavigation)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            world = rewards ?? throw new ArgumentNullException(nameof(rewards));
            abilities = registry ?? throw new ArgumentNullException(nameof(registry));
            random = scriptRandom ?? throw new ArgumentNullException(nameof(scriptRandom));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            navigation = selectedNavigation ?? throw new ArgumentNullException(nameof(selectedNavigation));
        }
        private bool Exists(long id)
        { try { combat.Roster.Get(id); return true; } catch (KeyNotFoundException) { return false; } }
        private void ClearMissingPendingActor()
        {
            if (!processing && pendingActor != 0 && !Exists(pendingActor))
            { pendingActor = 0; pendingRawcode = null;
              pendingNavigation = Original323GamblingNavigation.None; }
        }
        private long AvailableToken()
        {
            foreach (var unit in combat.MobileUnitSnapshot())
                if (unit.Unit.Id == Original323SourceMaterialRewardWorld.TokenCatalogId &&
                    !combat.IsStoryDeployed(unit.InstanceId)) return unit.InstanceId;
            return 0;
        }
        public bool HasRerollAbility(long actor) => Exists(actor) &&
            (abilities.HasAbility(actor, "A0YE") || abilities.HasAbility(actor, "A0YZ"));
        public string CostLabel(long actor)
        {
            bool risk = navigation() == Original323GamblingNavigation.RiskHedge;
            return abilities.HasAbility(actor, "A0YE") ?
                "희귀 리롤 1회" + (risk ? " · 목재 무료" : " + 목재 2개") :
                risk ? "랜덤전용 1회 교체 · 토큰 무료" : "행운의 토큰 1개 · 랜덤전용 1회 교체";
        }
        public bool CanReroll(long actor)
        {
            ClearMissingPendingActor();
            if (processing || combat.Game.Phase != SessionPhase.Running || !gate() ||
                !HasRerollAbility(actor) || combat.IsStoryDeployed(actor) ||
                (pendingActor != 0 && pendingActor != actor)) return false;
            bool risk = navigation() == Original323GamblingNavigation.RiskHedge;
            return abilities.HasAbility(actor, "A0YE") ?
                wallet.Balance(SourceModelGamblingNavigation.RareRerollsResource) > 0 &&
                (risk || wallet.Balance("wood") >= 2) : risk || AvailableToken() > 0;
        }
        public ManualActionResult TryReroll(long actor)
        {
            if (!CanReroll(actor)) return Reject("리롤 횟수·재료·파견/일시정지 상태를 확인해 주세요.");
            processing = true;
            try
            {
                bool rare = abilities.HasAbility(actor, "A0YE");
                bool risk = navigation() == Original323GamblingNavigation.RiskHedge;
                var source = combat.Roster.Get(actor);
                var pool = rare ? Original323OtherworldGamblingRules.CasinoRareRawcodes :
                    Original323OtherworldGamblingRules.RandomRawcodes;
                if (pendingActor == 0) { pendingActor = actor; pendingNavigation = navigation(); }
                if (pendingNavigation != navigation()) return Reject("보류된 리롤의 항법이 다릅니다.");
                // Preserve the source's rejection sampling (including RNG consumption).
                // A broken injected random cannot hang the UI forever.
                for (int tries = 0; pendingRawcode == null && tries < 128; tries++)
                {
                    int index = random.NextInclusive(0, pool.Count - 1);
                    if (index < 0 || index >= pool.Count) throw new InvalidOperationException("리롤 난수 범위 오류");
                    if (world.CatalogId(pool[index]) != source.UnitId) pendingRawcode = pool[index];
                }
                if (pendingRawcode == null) return Reject("다른 유닛 추첨을 완료하지 못했습니다. 같은 유닛으로 재시도해 주세요.");
                var materials = new List<long> { actor };
                if (!rare && !risk)
                {
                    long token = AvailableToken();
                    if (token <= 0) return Reject("행운의 토큰 1개가 필요합니다.");
                    materials.Add(token);
                }
                var costs = new ResourceBundle();
                if (rare)
                {
                    costs.Add(SourceModelGamblingNavigation.RareRerollsResource, 1);
                    if (!risk) costs.Add("wood", 2);
                }
                string countId = rare ? "source:reroll:CRj:uses" : "source:reroll:CRl:uses";
                string receipt = "source:reroll:" + actor;
                var beforeWallet = wallet.CaptureState();
                long result = 0;
                bool committed = false;
                Exception observer = null;
                try
                {
                    using (var swap = combat.BeginOriginalFusionUnitSwap(materials,
                        world.CatalogId(pendingRawcode), source.X, source.Y))
                    {
                        result = swap.ResultInstanceId;
                        // CRj re-adds A0YE; CRl does NOT re-add A0YZ.
                        if (rare && !abilities.TryDeferOriginalAbility(result, "A0YE"))
                            throw new InvalidOperationException("희귀 리롤 능력 기록 실패");
                        bool paid;
                        try { paid = wallet.TryExchangeOnce(receipt, costs, new ResourceBundle().Add(countId, 1)); }
                        catch (Exception error)
                        { if (!wallet.HasCompletedTransaction(receipt)) throw; paid = true; observer = error; }
                        if (!paid) return Reject("리롤 거래를 완료할 수 없습니다.");
                        swap.Commit();
                        committed = true;
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        if (result > 0) abilities.Forget(result);
                        wallet.RestoreState(beforeWallet);
                    }
                }
                abilities.Forget(actor);
                string name = world.DisplayName(pendingRawcode);
                pendingActor = 0; pendingRawcode = null;
                pendingNavigation = Original323GamblingNavigation.None;
                combat.Roster.Select(result);
                if (observer != null) Report(observer);
                return new ManualActionResult(ManualActionState.Succeeded, name + "(으)로 교체했습니다.");
            }
            finally { processing = false; }
        }
        private static ManualActionResult Reject(string reason) => new ManualActionResult(ManualActionState.Rejected, reason);
        private void Report(Exception error) { try { PostCommitObserverFailed?.Invoke(error); } catch { } }
    }
}
