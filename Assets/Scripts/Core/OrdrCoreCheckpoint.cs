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
        // Exclusive with ScriptRandom: original H6 BFi needs a same-session
        // witness on restore, never a new guessed System.Random seed.
        public Original323SharedBfiCheckpoint OriginalScriptRandom;
        // Original Bnm/Bnp/Bnr attacked-event receipts share the same H6
        // cursor; event duplicates must survive a session restoration.
        public Original323ScriptAttackProcState OriginalScriptAttackProcs;
        // Three distinct local fallback-native draw streams. They MUST NOT
        // be merged with the source BFi lane or with each other on resume.
        public MobileRollbackWispRandomState WispPoolRandom;
        public MobileRollbackWispRandomState GraduatedRarePoolRandom;
        public MobileRollbackWispRandomState TierPoolRandom;
        public SourceModelMoneyGamblingDeskState MoneyDesk;
        public SourceModelUnitGamblingState UnitGambling;
        public SourceModelOtherworldUnitGamblingState OtherworldGambling;
        public SourceModelUnitRerollState UnitReroll;
        public Original323PlayerResearchState Research;
        public Original323EvolutionProgressState Evolution;
        public Original323PoneglyphState Poneglyph;
        public Original323CreepPracticeState Creeps;
        public Original323StoryBuildingPracticeState StoryBuildings;
        public Original323SoloStoryState SoloStory;
        public Original323StoryMobileProgressionState StoryProgression;
        // Source-verified snapshots for treasure only; never enable Continue.
        public Original323TreasureSessionState Treasure;
        public Original323SharedObjectiveWorldState SharedObjectives;
        // Original A0YE/A0YZ pending ability ownership for actual unit IDs.
        // This is NOT evidence that the native buff has been applied.
        public Original323WispDeferredAbilityRegistryState WispDeferredAbilities;
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
            SourceModelUnitRerollState unitReroll = null,
            Original323PlayerResearchState research = null,
            Original323EvolutionProgressState evolution = null,
            Original323PoneglyphState poneglyph = null,
            Original323CreepPracticeState creeps = null,
            Original323StoryBuildingPracticeState storyBuildings = null,
            Original323SoloStoryState soloStory = null,
            Original323StoryMobileProgressionState storyProgression = null,
            MobileRollbackWispRandomState tierPoolRandom = null,
            MobileRollbackWispRandomState wispPoolRandom = null,
            MobileRollbackWispRandomState graduatedRarePoolRandom = null,
            Original323TreasureSessionState treasure = null,
            Original323SharedObjectiveWorldState sharedObjectives = null,
            Original323SharedBfiCheckpoint originalScriptRandom = null,
            Original323WispDeferredAbilityRegistryState wispDeferredAbilities = null,
            Original323ScriptAttackProcState originalScriptAttackProcs = null)
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
                ScriptRandom = scriptRandom,
                OriginalScriptRandom = originalScriptRandom,
                OriginalScriptAttackProcs = originalScriptAttackProcs,
                TierPoolRandom = tierPoolRandom,
                WispPoolRandom = wispPoolRandom,
                GraduatedRarePoolRandom = graduatedRarePoolRandom,
                MoneyDesk = moneyDesk,
                UnitGambling = unitGambling, OtherworldGambling = otherworldGambling,
                UnitReroll = unitReroll,
                Research = research, Evolution = evolution,
                Poneglyph = poneglyph, Creeps = creeps,
                StoryBuildings = storyBuildings, SoloStory = soloStory,
                StoryProgression = storyProgression,
                Treasure = treasure, SharedObjectives = sharedObjectives,
                WispDeferredAbilities = wispDeferredAbilities
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
            if (state.Combat != null)
            {
                CharacterCombatRuntime.ValidateExternalLaneState(
                    state.Combat, state.Roster,
                    state.Poneglyph, state.SharedObjectives);
                if (state.SharedObjectives != null)
                {
                    if (state.Poneglyph == null)
                        throw new FormatException(
                            "원본 공유 목표의 공유 미션 원장이 누락됐습니다.");
                    Original323SharedObjectiveWorld.ValidateState(
                        state.SharedObjectives, state.Combat, state.Poneglyph);
                }
                else
                    foreach (var enemy in state.Combat.Enemies)
                        if (enemy.EnemyId != null &&
                            enemy.EnemyId.StartsWith(
                                "shared:objective:", StringComparison.Ordinal))
                            throw new FormatException(
                                "공유 목표의 원본 출현/격파 이력이 누락됐습니다.");
            }
            else if (state.SharedObjectives != null)
                throw new FormatException("전투 실물 없는 공유 목표는 저장할 수 없습니다.");
            if (state.Combat != null)
                CharacterCombatRuntime.ValidateCreepDeploymentState(
                    state.Combat, state.Creeps, state.Roster);
            if (state.WispDeferredAbilities != null)
                Original323WispDeferredAbilityRegistry.ValidateState(
                    state.WispDeferredAbilities, state.Roster);
            if (state.ScriptRandom != null &&
                state.OriginalScriptRandom != null)
                throw new FormatException(
                    "원본 공통 BFi와 모바일 임시 난수는 함께 저장할 수 없습니다.");
            if (state.ScriptRandom != null)
                MobileRollbackWispRandom.Restore(state.ScriptRandom);
            if (state.OriginalScriptRandom != null)
                Original323SharedBfiRandom.ValidateCheckpoint(
                    state.OriginalScriptRandom);
            if (state.OriginalScriptAttackProcs != null)
            {
                if (state.OriginalScriptRandom == null || state.Combat == null)
                    throw new FormatException(
                        "원본 공격 이벤트에는 같은 전투/H6 난수 저장본이 필요합니다.");
                Original323ScriptAttackProcRolls.ValidateState(
                    state.OriginalScriptAttackProcs, state.OriginalScriptRandom);
            }
            if (state.WispPoolRandom != null)
                MobileRollbackWispRandom.Restore(state.WispPoolRandom);
            if (state.GraduatedRarePoolRandom != null)
                MobileRollbackWispRandom.Restore(state.GraduatedRarePoolRandom);
            if (state.TierPoolRandom != null)
                MobileRollbackWispRandom.Restore(state.TierPoolRandom);
            if(state.Treasure != null)
            {
                if(state.ScriptRandom==null || state.Treasure.Hunt==null ||
                    state.Treasure.Mobile==null || state.Treasure.Gacha==null ||
                    state.Treasure.Alarms==null ||
                    (!state.Treasure.DreamApplied && state.Treasure.NamiAtDream))
                    throw new FormatException("보물 전체 상태/난수 복원 자료 부족");
                Original323TreasureHunt.ValidateState(
                    state.Treasure.Hunt,restored.Economy.Wallet);
                Original323MobileTreasureSession.ValidateState(
                    state.Treasure.Mobile,state.Treasure.Hunt);
                MobileTreasureGacha.ValidateState(
                    state.Treasure.Gacha,state.Treasure.Mobile);
                Original323TreasureAlarmJournal.ValidateState(
                    state.Treasure.Alarms);
                if(state.Treasure.Reveals!=null)
                    Original323GaimonRevealPort.ValidateState(
                        state.Treasure.Reveals,state.Treasure.Hunt);
            }
            if (state.StoryBuildings != null ||
                state.SoloStory != null || state.StoryProgression != null)
            {
                if (state.StoryBuildings == null || state.SoloStory == null ||
                    state.StoryProgression == null || state.Combat == null)
                    throw new FormatException("스토리 실물·상태·보상 기록은 함께 저장해야 합니다.");
                Original323StoryBuildingPracticeWorld.ValidateState(state.StoryBuildings);
                Original323SoloStoryScenario.ValidateState(state.SoloStory,
                    state.LogicalTime);
                var queue = new Original323StoryNativeRewardQueue();
                queue.RestoreState(state.SoloStory.NativeRewards);
                Original323StoryMobileProgression.ValidateState(
                    state.StoryProgression, queue, restored.Economy.Wallet);
                var living = new HashSet<string>(StringComparer.Ordinal);
                foreach (var enemy in state.Combat.Enemies)
                    if (enemy.Kind == CharacterEnemyKind.Story) living.Add(enemy.EnemyId);
                var issued = new HashSet<int>();
                var destroyed = new HashSet<int>();
                foreach (var entry in state.StoryBuildings.Encounters)
                {
                    bool found = living.Remove(entry.EnemyId);
                    if ((entry.State == Original323StoryBuildingState.Alive) != found)
                        throw new FormatException("스토리 건물과 실물 적의 상태가 다릅니다.");
                    issued.Add(entry.Stage);
                    if (entry.State == Original323StoryBuildingState.Destroyed)
                        destroyed.Add(entry.Stage);
                }
                if (living.Count != 0)
                    throw new FormatException("스토리 건물 장부에 없는 적이 있습니다.");
                foreach (int stage in state.SoloStory.IssuedStages)
                    if (!issued.Contains(stage))
                        throw new FormatException("출현 기록 없는 스토리 건물이 있습니다.");
                foreach (int stage in state.SoloStory.CompletedStages)
                    if (!destroyed.Contains(stage) ||
                        !restored.Economy.Wallet.HasCompletedTransaction(
                            "practice:story-stage:" + stage))
                        throw new FormatException("스토리 파괴/보상 영수증이 다릅니다.");
            }
            if (state.Research != null)
                new Original323PlayerResearchSession(restored.Economy.Wallet,
                    restored.Game, rawcode => true).RestoreState(state.Research);
            if (state.Evolution != null)
                Original323EvolutionProgress.ValidateState(state.Evolution,
                    state.LogicalTime);
            if (state.Poneglyph != null)
            {
                Original323PoneglyphSession.ValidateState(state.Poneglyph,
                    state.LogicalTime);
                if (state.Poneglyph.HasActiveUnit)
                {
                    bool found = false, channeling = false;
                    foreach (var unit in state.Roster.Units)
                        if (unit.InstanceId == state.Poneglyph.ActiveUnitId)
                            found = true;
                    if (state.Combat != null)
                        foreach (var actor in state.Combat.Actors)
                            if (actor.InstanceId == state.Poneglyph.ActiveUnitId &&
                                actor.PoneglyphChanneling)
                                channeling = true;
                    if (!found || (state.Combat != null && !channeling))
                        throw new FormatException(
                            "포네그리프 해독 유닛 신원이 전투 장부와 다릅니다.");
                }
            }
            if (state.Creeps != null)
            {
                Original323CreepPracticeWorld.ValidateState(state.Creeps);
                if (state.Combat == null)
                    throw new FormatException("실물 전장 없는 크립 기록은 허용되지 않습니다.");
                var living = new HashSet<string>(StringComparer.Ordinal);
                foreach (var enemy in state.Combat.Enemies)
                    if (enemy.Kind == CharacterEnemyKind.Creep)
                        living.Add(enemy.EnemyId);
                foreach (var encounter in state.Creeps.Encounters)
                {
                    bool exists = living.Remove(encounter.EnemyId);
                    if ((encounter.State == Original323CreepEncounterState.Alive) != exists)
                        throw new FormatException("크립 처치 장부와 실물 전장이 다릅니다.");
                }
                if (living.Count != 0)
                    throw new FormatException("크립 기록에 없는 살아 있는 크립이 있습니다.");
            }
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
                state.UnitReroll != null) && state.ScriptRandom == null &&
                state.OriginalScriptRandom == null)
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
                if (state.ScriptRandom == null &&
                    state.OriginalScriptRandom == null)
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
                world.Version != 4 || world.Attacks == null ||
                world.Timed == null || world.Actors == null ||
                world.Enemies == null || waves.Version != 1 ||
                world.Time != state.LogicalTime ||
                world.Time != world.Attacks.Scheduler?.Time ||
                world.Time != world.Attacks.Skills?.Time ||
                world.Time != world.Timed.Time ||
                waves.ActiveWave != state.Game.CurrentWave)
                throw new FormatException(
                    "전투 예약·라운드/게임 시계가 일치하지 않습니다.");
            CharacterCombatRuntime.ValidateManualOwnerState(world);
            CharacterCombatRuntime.ValidateSourceSidecars(world);
            CharacterCombatRuntime.ValidateEquipmentState(world);
            CombatScheduler.ValidateState(world.Attacks.Scheduler);
            CombatSkillTriggerEngine.ValidateState(world.Attacks.Skills);
            if (world.Attacks.Version != 1 || world.Attacks.Events == null)
                throw new FormatException("전투 공격 순번 형식이 잘못됐습니다.");
            CombatTimedEffectQueue.ValidateState(world.Timed);
            CharacterCombatRuntime.ValidateMariaPulseState(world);
            CharacterCombatRuntime.ValidateKumaAutoMissileState(world);
            CharacterCombatRuntime.ValidateSourceTransientState(world);
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
                    (actor.SourceManagedResources &&
                        (actor.Abilities.Resources == null ||
                         !world.HasSourceModelBasicAttacks || !actor.AttackReady)) ||
                    actor.Abilities.Time > state.LogicalTime ||
                    !allUnits.Add(actor.InstanceId))
                    throw new FormatException("전투 유닛 ID/상태가 잘못됐습니다.");
            foreach (var actor in world.Actors)
                if (actor.AttackReady !=
                    registered.Contains(actor.InstanceId.ToString()))
                    throw new FormatException(
                        "공격 가능 상태와 등록된 타이머가 일치하지 않습니다.");
            var expectedAttackers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actor in world.Actors)
                if (actor.AttackReady)
                    expectedAttackers.Add(actor.InstanceId.ToString());
            foreach (var summon in world.Summons)
                if (summon.HasIndependentCombat &&
                    !expectedAttackers.Add("practice-summon:" + summon.Id))
                    throw new FormatException("소환수 공격 ID가 중복됐습니다.");
            if (!expectedAttackers.SetEquals(registered))
                throw new FormatException("소환수·캐릭터와 실제 공격 시계의 ID가 다릅니다.");
            // The source e0HP evolution marker is owned by the evolution
            // service, not by the combat character registry. Accept only a
            // matching, validated evolution receipt for that ONE extra entry.
            if (state.Evolution != null)
                Original323EvolutionProgress.ValidateState(
                    state.Evolution, state.LogicalTime);
            int externalEvolution = 0;
            foreach (var unit in state.Roster.Units)
            {
                if (!allUnits.Contains(unit.InstanceId))
                {
                    if (state.Evolution == null ||
                        unit.InstanceId != state.Evolution.ActorId ||
                        unit.UnitId != Original323SoloStartProfile.EvolutionRawcode ||
                        ++externalEvolution > 1 ||
                        !allUnits.Add(unit.InstanceId))
                        throw new FormatException("전투 로스터와 저장 유닛이 다릅니다.");
                    continue;
                }
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
            if (allUnits.Count != state.Roster.Units.Count)
                throw new FormatException("공통 로스터와 전투 유닛 수가 다릅니다.");
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
                // Reconstruct every native-target mask and A04R witness in
                // isolation; malformed data must not reach live GameSession.
                CombatEnemy.RestoreSnapshot(enemy);
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
            {
                SourceModelPracticeBattle.ValidateState(state.Practice, 65,
                    state.Game.CurrentWave, state.LogicalTime);
                if (state.Practice.DeathCountActive &&
                    state.Game.AliveEnemies < 70)
                    throw new FormatException(
                        "데스카운트가 실행 중인데 실제 몬스터 수는 제한 미만입니다.");
            }
        }

        // Build ONLY the core dependency graph for a future complete
        // world owner. The public Restore() below keeps rejecting partial
        // battlefield recovery. No reconstructed result is published yet.
        public static OrdrRestoredCoreCheckpoint PrepareWorldRestore(
            OrdrCoreCheckpointState state)
        {
            Validate(state);
            if (state.Combat == null || state.Waves == null ||
                state.Combat.Version != 4)
                throw new FormatException("복원할 전장 상태가 존재하지 않습니다.");
            return RestoreCoreOnly(state);
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
