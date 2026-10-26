using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Must read the ACTUAL original WC3 object for a live combat instance.
    // A catalog name, a grade and the preview reconciliation file never
    // prove the source rawcode for irreversible unit consumption.
    public interface IOriginalJassLiveObjectReader
    {
        bool TryReadOriginalRawcode(long instanceId, out string originalRawcode);
    }

    // This is the remaining native side of source-exact CHu/CHx: it must
    // stage unit removal, currency changes, BGH spawn, deployment/placement,
    // allied effects, RNG and events with complete rollback on Dispose.
    public interface IOriginalJassLiveEffectsTransaction : IDisposable
    {
        bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand command,
            out long createdInstanceId, out string reason);
        void Commit();
    }

    public interface IOriginalJassLiveEffectsFactory
    {
        // Acquires an EXCLUSIVE lock on the same real runtime + shared wallet,
        // not a second simulated inventory or an isolated copy.
        IOriginalJassLiveEffectsTransaction BeginExclusiveNativeFusion(
            CharacterCombatRuntime liveCombat, SharedEconomySession liveEconomy);
    }

    // Production-bound adapter for the existing original UNIT/GOLD/WOOD
    // transaction coordinator. Reads ALWAYS come from the actual runtime;
    // only a separately source-verified reversible native effects factory may
    // perform CHu/CHx world mutations. No generic RemoveCharacter/SpawnCharacter
    // calls are made here because they cannot restore skill timers on rollback.
    public sealed class OriginalJassLiveCombatFusionWorld :
        IOriginalJassSimpleFusionTransactionalWorld,
        IOriginalJassFusionResourceReader, IOriginalJassFusionCastPolicy
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginalJassLiveObjectReader objects;
        private readonly IOriginalJassFusionCastPolicy castPolicy;
        private readonly IOriginalJassLiveEffectsFactory effects;
        private bool busy;
        public event Action<long, string, string> FusionCommitted;
        public event Action<Exception> PostCommitObserverFailed;

        private void NotifyCommitted(long instanceId, string unitId, string rawcode)
        {
            if (FusionCommitted == null) return;
            foreach (Action<long, string, string> observer in FusionCommitted.GetInvocationList())
            {
                try { observer(instanceId, unitId, rawcode); }
                catch (Exception error)
                {
                    // An observation failure cannot turn an already committed
                    // fusion into a rejection and induce another material spend.
                    try { PostCommitObserverFailed?.Invoke(error); } catch { }
                }
            }
        }

        public OriginalJassLiveCombatFusionWorld(CharacterCombatRuntime liveCombat,
            SharedEconomySession sharedEconomy,
            IOriginalJassLiveObjectReader actualOriginalObjects,
            IOriginalJassFusionCastPolicy verifiedCastTarget,
            IOriginalJassLiveEffectsFactory reversibleNativeEffects)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            economy = sharedEconomy ?? throw new ArgumentNullException(nameof(sharedEconomy));
            objects = actualOriginalObjects ??
                throw new ArgumentNullException(nameof(actualOriginalObjects));
            castPolicy = verifiedCastTarget ??
                throw new ArgumentNullException(nameof(verifiedCastTarget));
            effects = reversibleNativeEffects ??
                throw new ArgumentNullException(nameof(reversibleNativeEffects));
            if (!Object.ReferenceEquals(combat.Roster, economy.Units))
                throw new InvalidOperationException(
                    "원본 조합은 전투 유닛과 공유 지갑의 동일한 로스터를 사용해야 합니다.");
        }

        public bool TryReadBalances(out int gold, out int lumber)
        {
            long g = economy.Wallet.Balance("gold");
            long w = economy.Wallet.Balance("wood");
            if (g < 0 || w < 0 || g > int.MaxValue || w > int.MaxValue)
            {
                gold = 0;
                lumber = 0;
                return false;
            }
            gold = (int)g;
            lumber = (int)w;
            return true;
        }

        public bool TryChooseCastTarget(string abilityRawcode,
            IReadOnlyList<long> exactMaterials, out long target,
            out string reason)
        {
            target = 0;
            reason = null;
            if (exactMaterials == null ||
                string.IsNullOrWhiteSpace(abilityRawcode))
            {
                reason = "원본 시전 능력·재료 목록이 없습니다.";
                return false;
            }
            var seen = new HashSet<long>();
            var now = combat.MobileUnitSnapshot();
            var live = new HashSet<long>();
            foreach (var row in now) live.Add(row.InstanceId);
            foreach (long id in exactMaterials)
                if (id <= 0 || !seen.Add(id) ||
                    !live.Contains(id) || combat.IsStoryDeployed(id))
                {
                    reason = "원본 시전 재료가 보유 목록과 일치하지 않습니다.";
                    return false;
                }
            // CHu/CHx GetSpellTargetUnit can legitimately name an actor
            // outside the material list. Do not invent a 'first material'
            // targeting rule: only the original verified target policy decides.
            if (!castPolicy.TryChooseCastTarget(abilityRawcode,
                    exactMaterials, out target, out reason) || target <= 0)
            {
                target = 0;
                if (string.IsNullOrWhiteSpace(reason))
                    reason = "원본 스킬 시전 대상 미검증";
                return false;
            }
            return true;
        }

        public IOriginalJassSimpleFusionLockedTransaction BeginExclusiveFusion()
        {
            if (busy)
                throw new InvalidOperationException("원본 조합 거래 중복 실행");
            busy = true;
            try
            {
                var native = effects.BeginExclusiveNativeFusion(combat, economy);
                if (native == null)
                {
                    busy = false;
                    return null;
                }
                try { return new Locked(this, native); }
                catch
                {
                    try { native.Dispose(); }
                    finally { busy = false; }
                    throw;
                }
            }
            catch
            {
                busy = false;
                throw;
            }
        }

        private sealed class Locked : IOriginalJassSimpleFusionLockedTransaction
        {
            private readonly OriginalJassLiveCombatFusionWorld owner;
            private readonly IOriginalJassLiveEffectsTransaction native;
            private readonly IReadOnlyList<MobileUnitSnapshot> beforeUnits;
            private readonly EconomyWalletState beforeWallet;
            private readonly double beforeTime;
            private readonly SessionPhase beforePhase;
            private readonly int beforeRound;
            private readonly Dictionary<long, UnitState> beforePositions =
                new Dictionary<long, UnitState>();
            private readonly Dictionary<long, bool> beforeDeployment =
                new Dictionary<long, bool>();
            private bool stageAttempted;
            private bool committed;
            private bool disposed;
            private long resultInstance;
            private string resultUnitId;
            private string resultRawcode;

            public Locked(OriginalJassLiveCombatFusionWorld owner,
                IOriginalJassLiveEffectsTransaction native)
            {
                this.owner = owner;
                this.native = native;
                beforeUnits = owner.combat.MobileUnitSnapshot();
                beforeWallet = owner.economy.Wallet.CaptureState();
                beforeTime = owner.combat.Time;
                beforePhase = owner.combat.Game.Phase;
                beforeRound = owner.combat.Game.CurrentWave;
                foreach (var unit in beforeUnits)
                {
                    beforePositions.Add(unit.InstanceId,
                        owner.combat.Roster.Get(unit.InstanceId));
                    beforeDeployment.Add(unit.InstanceId,
                        owner.combat.IsStoryDeployed(unit.InstanceId));
                }
            }

            private void Open()
            {
                if (disposed || committed)
                    throw new InvalidOperationException("원본 거래 세션이 종료됐습니다.");
            }

            private bool HasUnchangedLiveState()
            {
                if (owner.combat.Time != beforeTime ||
                    owner.combat.Game.Phase != beforePhase ||
                    owner.combat.Game.CurrentWave != beforeRound)
                    return false;
                var units = owner.combat.MobileUnitSnapshot();
                if (units.Count != beforeUnits.Count) return false;
                for (int i = 0; i < units.Count; ++i)
                {
                    var a = units[i];
                    var b = beforeUnits[i];
                    UnitState saved, position;
                    bool wasDeployed;
                    if (!beforePositions.TryGetValue(a.InstanceId, out saved) ||
                        !beforeDeployment.TryGetValue(a.InstanceId, out wasDeployed))
                        return false;
                    position = owner.combat.Roster.Get(a.InstanceId);
                    if (a.InstanceId != b.InstanceId || a.Unit.Id != b.Unit.Id ||
                        position.X != saved.X || position.Y != saved.Y ||
                        owner.combat.IsStoryDeployed(a.InstanceId) != wasDeployed)
                        return false;
                }
                var wallet = owner.economy.Wallet.CaptureState();
                if (wallet.Balances.Count != beforeWallet.Balances.Count ||
                    wallet.CompletedRewards.Count != beforeWallet.CompletedRewards.Count)
                    return false;
                for (int i = 0; i < wallet.Balances.Count; ++i)
                    if (wallet.Balances[i].Id != beforeWallet.Balances[i].Id ||
                        wallet.Balances[i].Amount != beforeWallet.Balances[i].Amount)
                        return false;
                for (int i = 0; i < wallet.CompletedRewards.Count; ++i)
                    if (wallet.CompletedRewards[i] != beforeWallet.CompletedRewards[i])
                        return false;
                return true;
            }

            public IReadOnlyList<MobileUnitSnapshot> OwnedUnits()
            {
                Open();
                return owner.combat.MobileUnitSnapshot();
            }

            public bool TryReadBalances(out int gold, out int lumber)
            {
                Open();
                return owner.TryReadBalances(out gold, out lumber);
            }

            public bool TryReadOriginalUnitRawcode(long id, out string code)
            {
                Open();
                code = null;
                if (id <= 0) return false;
                bool found = false;
                foreach (var unit in owner.combat.MobileUnitSnapshot())
                    if (unit.InstanceId == id)
                    { found = true; break; }
                return found && owner.objects.TryReadOriginalRawcode(id, out code) &&
                    code != null && code.Length == 4;
            }

            public bool TryChooseCastTarget(string ability,
                IReadOnlyList<long> materialIds, out long target, out string reason)
            {
                Open();
                if (!HasUnchangedLiveState())
                {
                    target = 0;
                    reason = "잠금 중 유닛·전투·재화 상태가 변경됐습니다.";
                    return false;
                }
                return owner.TryChooseCastTarget(ability, materialIds,
                    out target, out reason) && HasUnchangedLiveState();
            }

            public bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand command,
                out long created, out string reason)
            {
                Open();
                created = 0;
                reason = null;
                if (stageAttempted || command == null || !HasUnchangedLiveState())
                {
                    reason = "원본 조합 중복 실행 또는 잠금 상태가 변경됐습니다.";
                    return false;
                }
                if (command.MaterialInstanceIds.Count != command.MaterialRawcodes.Count)
                {
                    reason = "원본 재료 rawcode 결합 개수가 다릅니다.";
                    return false;
                }
                var used = new HashSet<long>();
                for (int i = 0; i < command.MaterialInstanceIds.Count; ++i)
                {
                    long id = command.MaterialInstanceIds[i];
                    string actual;
                    if (!used.Add(id) || !TryReadOriginalUnitRawcode(id, out actual) ||
                        !String.Equals(actual, command.MaterialRawcodes[i],
                            StringComparison.Ordinal) ||
                        owner.combat.IsStoryDeployed(id))
                    {
                        reason = "원본 재료 객체 또는 파견 상태가 변경됐습니다.";
                        return false;
                    }
                }
                stageAttempted = true;
                bool staged = native.TryStageOriginalFusion(command, out created, out reason);
                if (staged)
                {
                    resultInstance = created;
                    resultUnitId = command.ResultUnitId;
                    resultRawcode = command.ResultRawcode;
                }
                return staged;
            }

            public void Commit()
            {
                Open();
                if (!stageAttempted)
                    throw new InvalidOperationException("원본 제작 효과 미실행");
                // The caller (SimpleOrderedSessionFactory) already audited
                // the actual post-stage roster, currency and result rawcode.
                // ONLY the verified native transaction can finalize its
                // reversible CHu/CHx changes.
                native.Commit();
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { native.Dispose(); }
                finally { owner.busy = false; }
                if (committed && resultInstance > 0)
                    owner.NotifyCommitted(resultInstance, resultUnitId, resultRawcode);
            }
        }
    }
}
