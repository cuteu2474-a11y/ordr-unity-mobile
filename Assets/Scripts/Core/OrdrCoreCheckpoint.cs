using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Core
{
    // S01 core ledger contract only. NEVER expose this as a resumable game:
    // combat actors, enemies, timers, RNG and world services are not here yet.
    [Serializable]
    public sealed class OrdrCoreCheckpointState
    {
        public int Version = 1;
        public string SourceVersion = OrdrCoreCheckpoint.SourceVersion;
        public string SourceW3xSha256 = OrdrCoreCheckpoint.SourceW3xSha256;
        public string ModeId = GameSession.ModeId;
        public string SessionId;
        public double LogicalTime;
        public GameSessionState Game;
        public UnitRosterState Roster;
        // Same wallet, event receipts and mobile RNG stream as live gameplay.
        public PersonalMissionGameplayState MissionGameplay;
        // S02 extension: live world draft, never represented as a playable
        // checkpoint until every world subsystem has an atomic restore path.
        public SourceCombatRuntimeCheckpoint Combat;
        public CombatWaveDirectorState Waves;
        public SourceModelPracticeBattleState Practice;
        // Stage S03: independent state for commerce and the shared BFi stream.
        // These are not sufficient to enable game resume by themselves.
        public MobileRollbackWispRandomState ScriptRandom;
        public SourceModelMoneyGamblingDeskState MoneyDesk;
        public SourceModelUnitGamblingState UnitGambling;
        public SourceModelOtherworldUnitGamblingState OtherworldGambling;
        public SourceModelUnitRerollState UnitReroll;
    }

    public sealed class OrdrRestoredCoreCheckpoint
    {
        public GameSession Game { get; private set; }
        public SharedEconomySession Economy { get; private set; }
        public PersonalMissionGameplay MissionGameplay { get; private set; }
        public OriginalPersonalMissionSession Missions => MissionGameplay.Missions;
        public double LogicalTime { get; private set; }

        internal OrdrRestoredCoreCheckpoint(GameSession game,
            SharedEconomySession economy, PersonalMissionGameplay missions,
            double logicalTime)
        {
            Game = game; Economy = economy; MissionGameplay = missions;
            LogicalTime = logicalTime;
        }
    }

    public static class OrdrCoreCheckpoint
    {
        public const string SourceVersion = "ORDR_S2_2.323[C]";
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        public static OrdrCoreCheckpointState Capture(GameSession game,
            SharedEconomySession economy, PersonalMissionGameplay missions,
            double logicalTime,
            SourceCombatRuntimeCheckpoint combat = null,
            CombatWaveDirectorState waves = null,
            SourceModelPracticeBattleState practice = null,
            MobileRollbackWispRandomState scriptRandom = null,
            SourceModelMoneyGamblingDeskState moneyDesk = null,
            SourceModelUnitGamblingState unitGambling = null,
            SourceModelOtherworldUnitGamblingState otherworldGambling = null,
            SourceModelUnitRerollState unitReroll = null)
        {
            if (game == null || economy == null || missions == null ||
                !economy.HasStarted || game.Phase == SessionPhase.NotStarted ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                logicalTime < 0)
                throw new InvalidOperationException(
                    "진행 중인 세션과 유효한 논리 시계가 필요합니다.");
            var state = new OrdrCoreCheckpointState {
                SessionId = missions.Missions.SessionId, LogicalTime = logicalTime,
                Game = game.CaptureState(), Roster = economy.Units.CaptureState(),
                MissionGameplay = missions.CaptureState(),
                Combat = combat, Waves = waves, Practice = practice,
                ScriptRandom = scriptRandom, MoneyDesk = moneyDesk,
                UnitGambling = unitGambling, OtherworldGambling = otherworldGambling,
                UnitReroll = unitReroll
            };
            // The mission gameplay component carries the SAME shared wallet.
            // A different wallet or a torn cross-service snapshot is rejected.
            if (!SameWallet(economy.Wallet.CaptureState(),
                    state.MissionGameplay.Wallet))
                throw new InvalidOperationException(
                    "경제와 개인미션 지갑이 같은 저장 시점을 사용하지 않습니다.");
            Validate(state);
            return state;
        }

        private static bool SameWallet(EconomyWalletState a, EconomyWalletState b)
        {
            if (a == null || b == null || a.Balances == null ||
                b.Balances == null || a.CompletedRewards == null ||
                b.CompletedRewards == null || a.PendingWispRewards == null ||
                b.PendingWispRewards == null ||
                a.Balances.Count != b.Balances.Count ||
                a.CompletedRewards.Count != b.CompletedRewards.Count ||
                a.PendingWispRewards.Count != b.PendingWispRewards.Count)
                return false;
            for (int i = 0; i < a.Balances.Count; i++)
                if (a.Balances[i].Id != b.Balances[i].Id ||
                    a.Balances[i].Amount != b.Balances[i].Amount)
                    return false;
            for (int i = 0; i < a.CompletedRewards.Count; i++)
                if (a.CompletedRewards[i] != b.CompletedRewards[i]) return false;
            for (int i = 0; i < a.PendingWispRewards.Count; i++)
                if (a.PendingWispRewards[i].SourceWispId !=
                        b.PendingWispRewards[i].SourceWispId ||
                    a.PendingWispRewards[i].UnitRawcode !=
                        b.PendingWispRewards[i].UnitRawcode ||
                    a.PendingWispRewards[i].BonusAbilityRawcode !=
                        b.PendingWispRewards[i].BonusAbilityRawcode)
                    return false;
            return true;
        }

        // A dry validation returns a new object graph, never mutating the
        // running game, and never paying start/mission rewards on recovery.
        public static void Validate(OrdrCoreCheckpointState state)
        {
            var restored = RestoreCoreOnly(state);
            ValidateCombatDraft(state);
            if (state.ScriptRandom != null)
                MobileRollbackWispRandom.Restore(state.ScriptRandom);
            if (state.UnitGambling != null)
                SourceModelUnitGambling.ValidateState(state.UnitGambling);
            if (state.OtherworldGambling != null)
                SourceModelOtherworldUnitGambling.ValidateState(state.OtherworldGambling);
            if (state.UnitReroll != null)
            {
                SourceModelUnitReroll.ValidateState(state.UnitReroll);
                if (state.UnitReroll.PendingActor > 0)
                {
                    bool exists = false;
                    foreach (var unit in state.Roster.Units)
                        if (unit.InstanceId == state.UnitReroll.PendingActor)
                            exists = true;
                    if (!exists)
                        throw new FormatException("보류된 리롤의 실물 유닛 ID가 없습니다.");
                }
            }
            if (state.OtherworldGambling?.Pending != null &&
                state.OtherworldGambling.Pending.RiskBonus !=
                    restored.Economy.Wallet.Balance(
                        Original323OtherworldGamblingRules.RiskBonusResource))
                throw new FormatException("이세계 도박 위험 보너스와 공유 지갑이 다릅니다.");
            if ((state.UnitGambling != null || state.OtherworldGambling != null ||
                state.UnitReroll != null) && state.ScriptRandom == null)
                throw new FormatException("유닛 도박 보류 결과의 공통 난수열이 누락됐습니다.");
            if (state.MoneyDesk != null)
            {
                SourceModelMoneyGamblingDesk.ValidateState(state.MoneyDesk);
                var wallet = restored.Economy.Wallet;
                if (state.MoneyDesk.High.Purchases != wallet.Balance(
                        SourceModelMoneyGamblingDesk.HighCountResource) ||
                    state.MoneyDesk.High.Returned != wallet.Balance(
                        SourceModelMoneyGamblingDesk.HighReturnResource) ||
                    (state.MoneyDesk.MaterialSupport != null &&
                     state.MoneyDesk.MaterialSupport.PurchasedCount != wallet.Balance(
                        SourceModelMaterialSupport.PurchaseCountResource)))
                    throw new FormatException("도박소 구매 장부와 공통 지갑이 다릅니다.");
                if (state.ScriptRandom == null)
                    throw new FormatException("도박소 상태를 복원할 공통 난수 기록이 없습니다.");
                if (state.MoneyDesk.LastClock > state.LogicalTime ||
                    state.MoneyDesk.Regular.LastClock > state.LogicalTime ||
                    state.MoneyDesk.High.LastClock > state.LogicalTime)
                    throw new FormatException("도박소 시계가 게임 시계보다 앞서 있습니다.");
            }
        }

        private static void ValidateCombatDraft(OrdrCoreCheckpointState state)
        {
            if (state.Combat == null && state.Waves == null &&
                state.Practice == null) return; // S01 compatibility.
            var world = state.Combat;
            var waves = state.Waves;
            if (world == null || waves == null ||
                world.Version != 1 || world.Attacks == null ||
                world.Timed == null || world.Actors == null ||
                world.Enemies == null || waves.Version != 1 ||
                world.Time != state.LogicalTime ||
                world.Time != world.Attacks.Scheduler?.Time ||
                world.Time != world.Attacks.Skills?.Time ||
                world.Time != world.Timed.Time ||
                waves.ActiveWave != state.Game.CurrentWave)
                throw new FormatException(
                    "전투 예약·라운드/게임 시계가 일치하지 않습니다.");
            CombatScheduler.ValidateState(world.Attacks.Scheduler);
            CombatSkillTriggerEngine.ValidateState(world.Attacks.Skills);
            if (world.Attacks.Version != 1 || world.Attacks.Events == null)
                throw new FormatException("전투 공격 순번 형식이 잘못됐습니다.");
            CombatTimedEffectQueue.ValidateState(world.Timed);
            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in world.Attacks.Events)
                if (item == null || string.IsNullOrWhiteSpace(item.UnitId) ||
                    item.Sequence < 0 || !registered.Add(item.UnitId))
                    throw new FormatException("전투 공격자 이벤트 순번이 중복됐습니다.");
            foreach (var timer in world.Attacks.Scheduler.Attackers)
                if (!registered.Contains(timer.UnitId))
                    throw new FormatException(
                        "공격자 타이머와 이벤트 순번이 다릅니다.");
            if (registered.Count != world.Attacks.Scheduler.Attackers.Count)
                throw new FormatException(
                    "공격자 타이머 수가 원본 전투와 다릅니다.");
            var allUnits = new HashSet<long>();
            foreach (var actor in world.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    string.IsNullOrWhiteSpace(actor.CatalogId) ||
                    actor.Abilities == null || actor.Status == null ||
                    !allUnits.Add(actor.InstanceId))
                    throw new FormatException("전투 유닛 ID/상태가 잘못됐습니다.");
            foreach (var actor in world.Actors)
                if (actor.AttackReady !=
                    registered.Contains(actor.InstanceId.ToString()))
                    throw new FormatException(
                        "공격 가능 상태와 등록된 타이머가 일치하지 않습니다.");
            if (allUnits.Count != state.Roster.Units.Count)
                throw new FormatException("공통 로스터와 전투 유닛 수가 다릅니다.");
            foreach (var unit in state.Roster.Units)
            {
                if (!allUnits.Contains(unit.InstanceId)) 
                    throw new FormatException("전투 로스터와 저장 유닛이 다릅니다.");
                bool match = false;
                foreach (var actor in world.Actors)
                    if (actor.InstanceId == unit.InstanceId)
                    {
                        match = actor.CatalogId == unit.UnitId;
                        break;
                    }
                if (!match) throw new FormatException(
                    "전투 로스터의 원본 유닛이 다릅니다.");
            }
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            int waveEnemies = 0;
            foreach (var enemy in world.Enemies)
            {
                if (enemy == null ||
                    string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    enemy.Health <= 0 || enemy.MaximumHealth < enemy.Health ||
                    enemy.Status == null || enemy.Defenses == null ||
                    !enemies.Add(enemy.EnemyId))
                    throw new FormatException("전투 대상 체력/ID가 잘못됐습니다.");
                CombatStatusEngine.Restore(enemy.Status);
                enemy.Defenses.ValidateAndRecreate();
                if (enemy.Kind == CharacterEnemyKind.Line ||
                    enemy.Kind == CharacterEnemyKind.Boss ||
                    enemy.Kind == CharacterEnemyKind.Enraged)
                    waveEnemies++;
            }
            if (waveEnemies != state.Game.AliveEnemies)
                throw new FormatException(
                    "라운드에서 살아있는 적 숫자가 저장된 전장과 다릅니다.");
            foreach (var actor in world.Actors)
            {
                CombatStatusEngine.Restore(actor.Status);
                if (actor.SharedObjectiveEnemyId != null &&
                    !enemies.Contains(actor.SharedObjectiveEnemyId))
                    throw new FormatException(
                        "공유 목표 파견 대상이 실제 전장에 없습니다.");
            }
            if (state.Practice != null)
                SourceModelPracticeBattle.ValidateState(state.Practice, 65,
                    state.Game.CurrentWave, state.LogicalTime);
        }

        public static OrdrRestoredCoreCheckpoint Restore(
            OrdrCoreCheckpointState state)
        {
            if (state != null && (state.Combat != null ||
                state.Waves != null || state.Practice != null))
                throw new NotSupportedException(
                    "전장 전체의 소환/효과/난수 복원 전까지 이어하기를 허용하지 않습니다.");
            return RestoreCoreOnly(state);
        }

        private static OrdrRestoredCoreCheckpoint RestoreCoreOnly(
            OrdrCoreCheckpointState state)
        {
            if (state == null || state.Version != 1 ||
                state.SourceVersion != SourceVersion ||
                state.SourceW3xSha256 != SourceW3xSha256 ||
                state.ModeId != GameSession.ModeId ||
                string.IsNullOrWhiteSpace(state.SessionId) ||
                double.IsNaN(state.LogicalTime) ||
                double.IsInfinity(state.LogicalTime) ||
                state.LogicalTime < 0 ||
                state.Game == null || state.Roster == null ||
                state.MissionGameplay == null ||
                state.MissionGameplay.Missions == null ||
                state.MissionGameplay.Missions.SessionId != state.SessionId ||
                !state.MissionGameplay.Missions.Otherworld)
                throw new FormatException(
                    "원본 버전·세션 ID·게임 상태를 확인할 수 없습니다.");

            var game = GameSession.Restore(state.Game);
            if (game.Phase == SessionPhase.NotStarted ||
                state.MissionGameplay.Missions.CurrentRound != game.CurrentWave)
                throw new FormatException(
                    "라운드와 개인미션 진행 상태가 서로 다릅니다.");
            // First restore all independent economic state. Neither the new
            // wallet nor the new roster is published to gameplay until every
            // component validates successfully.
            var economy = SharedEconomySession.RestoreStarted(
                state.MissionGameplay.Wallet, state.Roster);
            var missions = PersonalMissionGameplay.Restore(
                game, economy.Wallet, state.MissionGameplay);
            return new OrdrRestoredCoreCheckpoint(game, economy, missions,
                state.LogicalTime);
        }
    }
}
