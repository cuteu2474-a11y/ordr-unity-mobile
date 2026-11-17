using System;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Mobile
{
    // No public Continue entry point, scene transfer or source event replay.
    // These dependencies are supplied for a NEW private game graph only.
    // Never pass a factory or witness bound to a live published game.
    public sealed class OrdrUnpublishedWorldRestoreInputs
    {
        public CharacterUnitCatalog Units;
        public CharacterSkillCatalog Profiles;
        public IOriginalDamageResolver BasicDamage;
        public IVerifiedCharacterSkillDamageResolver SkillDamage;
        public ICombatProcRandom CombatRandom;
        public Action<CharacterCombatRuntime> ConfigureCombat;
        public Func<int, VerifiedWavePlan> OriginalWavePlan;

        public Func<CharacterCombatRuntime,
            Original323WispDeferredAbilityRegistry> CreateAbilities;
        public Func<CharacterCombatRuntime, Original323WispDeferredAbilityRegistry,
            Original323UnitGamblingWorld> CreateUnitWorld;
        public Func<CharacterCombatRuntime, Original323WispDeferredAbilityRegistry,
            IOriginal323OtherworldGamblingWorld> CreateOtherworldWorld;
        public Func<CharacterCombatRuntime, IOriginalWispRandom, IOriginalWispRandom,
            Original323WispDeferredAbilityRegistry,
            IOriginal323GraduatedRareShopWorld> CreateRareWorld;
        public Func<OrdrRestoredCoreCheckpoint, string, bool> ResearchCanApply;
        public Func<OrdrUnpublishedAuxiliaryRestoreGraph, bool> HasStageFiveResearch;

        public Original323CreepObjectCatalog CreepCatalog;
        public IOriginal323CreepDispatchAuthority CreepDispatchAuthority;
        public Original323StoryBuildingCatalog StoryCatalog;
        public SourceModelPracticeCatalog PracticeCatalog;
        public Original323MonsterWaveIdentityCatalog OriginalMonsterCatalog;
        public SourceModelBasicCombatRules SourceDamage;
        public OriginalMapBaseStatCatalog SharedOriginalObjects;
        public IOriginal323SharedObjectiveWitness SharedObjectiveWitness;
        public IOriginal323SharedBfiRestoreWitness OriginalStateWitness;
        public IOriginal323ScriptRandomDrawWitness OriginalDrawWitness;
        public IOriginal323ScriptAttackProcWitness OriginalAttackEventWitness;
        public Func<CharacterCombatRuntime,
            IOriginal323VerifiedAttackDummySpellWorld> CreateOriginalAttackSpellWorld;
    }

    // Owns the source event subscriptions created by auxiliary reconstruction.
    // No graph is ever assigned to an active scene/controller by this class.
    public sealed class OrdrUnpublishedWorldRestoreGraph : IDisposable
    {
        public OrdrUnpublishedCombatRestoreGraph Combat { get; private set; }
        public OrdrUnpublishedCommerceRestoreGraph Commerce { get; private set; }
        public OrdrUnpublishedAuxiliaryRestoreGraph Auxiliary { get; private set; }
        public OrdrUnpublishedTreasureRestoreGraph Treasure { get; private set; }
        public Original323ScriptAttackProcRolls OriginalAttackRolls
            { get; private set; }
        public string SessionId { get; private set; }
        public double LogicalTime { get; private set; }
        public bool IsDisposed { get; private set; }

        internal OrdrUnpublishedWorldRestoreGraph(
            OrdrUnpublishedCombatRestoreGraph combat,
            OrdrUnpublishedCommerceRestoreGraph commerce,
            OrdrUnpublishedAuxiliaryRestoreGraph auxiliary,
            OrdrUnpublishedTreasureRestoreGraph treasure,
            Original323ScriptAttackProcRolls originalAttackRolls,
            string sessionId, double logicalTime)
        {
            Combat = combat;
            Commerce = commerce;
            Auxiliary = auxiliary;
            Treasure = treasure;
            OriginalAttackRolls = originalAttackRolls;
            SessionId = sessionId;
            LogicalTime = logicalTime;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Auxiliary?.Dispose();
        }
    }

    public static class OrdrWorldRestoreGraphBuilder
    {
        // Must reject known missing owners BEFORE constructing anything:
        // the treasure hunt's native map / dig after-effects still lack
        // an authoritative restore transaction. No omitted owner may reset
        // quietly to an empty service.
        public static void ValidatePrerequisites(OrdrCoreCheckpointState save,
            OrdrUnpublishedWorldRestoreInputs inputs)
        {
            OrdrCommerceRestoreGraphBuilder.ValidateCommerceDraft(save);
            if (save.Treasure != null)
            {
                // Reconstruct only a disposable scratch wallet before any
                // source factory is invoked. This must never grant initial
                // bounty maps or apply pending native One Piece abilities.
                var scratch = new EconomyWallet();
                scratch.RestoreState(save.MissionGameplay.Wallet);
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    save, scratch);
            }
            if (save.Practice == null && save.Game.CurrentWave > 0)
                throw new NotSupportedException(
                    "진행된 라운드에는 원본 65R 출현·전투 진행 장부가 필요합니다.");
            if (save.SoloStory != null && save.StoryBuildings == null)
                throw new NotSupportedException(
                    "스토리 출전은 건물 실물·원본 보상 기록과 함께 복원해야 합니다.");
            if (inputs == null || inputs.Units == null ||
                inputs.Profiles == null || inputs.BasicDamage == null ||
                inputs.SkillDamage == null || inputs.CombatRandom == null ||
                inputs.ConfigureCombat == null || inputs.OriginalWavePlan == null ||
                inputs.CreateAbilities == null || inputs.CreateUnitWorld == null ||
                inputs.CreateOtherworldWorld == null ||
                inputs.CreateRareWorld == null ||
                inputs.ResearchCanApply == null ||
                inputs.HasStageFiveResearch == null)
                throw new ArgumentNullException(nameof(inputs),
                    "새 전투/원본 거래/시설 공급자 없이는 복원할 수 없습니다.");
            if (save.OriginalScriptRandom != null &&
                (inputs.OriginalStateWitness == null ||
                 inputs.OriginalDrawWitness == null))
                throw new NotSupportedException(
                    "원본 H6 난수는 현재 세션의 상태와 향후 호출 목격자가 모두 필요합니다.");
            if (save.OriginalScriptAttackProcs != null &&
                (inputs.OriginalAttackEventWitness == null ||
                 inputs.CreateOriginalAttackSpellWorld == null))
                throw new NotSupportedException(
                    "원본 Bnm/Bnp/Bnr 사건은 같은 전투의 원본 충돌·공격 목격 공급자가 필요합니다.");
            if (save.Creeps != null && inputs.CreepCatalog == null ||
                save.Combat.CreepDeployments.Count > 0 &&
                    inputs.CreepDispatchAuthority == null ||
                save.SoloStory != null && inputs.StoryCatalog == null ||
                save.Practice != null && (inputs.PracticeCatalog == null ||
                    inputs.OriginalMonsterCatalog == null ||
                    inputs.SourceDamage == null ||
                    !ReferenceEquals(inputs.SourceDamage, inputs.BasicDamage)) ||
                save.SharedObjectives != null &&
                    (inputs.SharedOriginalObjects == null ||
                     inputs.SharedObjectiveWitness == null))
                throw new NotSupportedException(
                    "출전/라운드/공유 목표의 원본 실물·출처 공급자가 부족합니다.");
        }

        // The new graph is built in strict dependency order:
        //  1. fresh core/actors and original combat clocks
        //  2. ONE restored BFi owner + native pool RNG + pending commerce
        //  3. story/creeps/Poneglyph/shared missions/practice sharing that BFi
        //  4. read-only cross-service equality, BEFORE returning any graph
        // Failure closes source listeners on the temporary world. The live
        // game, wallet, roster and A/B checkpoint remain untouched.
        public static OrdrUnpublishedWorldRestoreGraph Prepare(
            OrdrCoreCheckpointState save,
            OrdrUnpublishedWorldRestoreInputs inputs)
        {
            ValidatePrerequisites(save, inputs);
            var combat = OrdrCombatRestoreGraphBuilder.Prepare(save,
                inputs.Units, inputs.Profiles, inputs.BasicDamage,
                inputs.SkillDamage, inputs.CombatRandom,
                inputs.ConfigureCombat, inputs.OriginalWavePlan);
            OrdrUnpublishedAuxiliaryRestoreGraph auxiliary = null;
            try
            {
                var selectedNavigation = new Func<Original323GamblingNavigation>(
                    () => (Original323GamblingNavigation)combat.Core.Economy.Wallet
                        .Balance(SourceModelGamblingNavigation.NavigationResource));
                var commerce = OrdrCommerceRestoreGraphBuilder.Prepare(
                    combat, save, inputs.CreateAbilities,
                    inputs.CreateUnitWorld, inputs.CreateOtherworldWorld,
                    inputs.CreateRareWorld,
                    () => false, // immutable until entire world is published
                    selectedNavigation,
                    () => auxiliary != null ?
                        inputs.HasStageFiveResearch(auxiliary) :
                        throw new InvalidOperationException(
                            "스토리 연구가 복원되기 전 거래 조건을 읽을 수 없습니다."),
                    null, // never attach a live mission-commit callback
                    inputs.OriginalStateWitness, inputs.OriginalDrawWitness);

                Original323ScriptAttackProcRolls originalAttackRolls = null;
                if (save.OriginalScriptAttackProcs != null)
                {
                    originalAttackRolls = new Original323ScriptAttackProcRolls(
                        commerce.OriginalScriptRandom,
                        inputs.OriginalAttackEventWitness);
                    originalAttackRolls.RestoreState(
                        save.OriginalScriptAttackProcs);
                    var spellWorld = inputs.CreateOriginalAttackSpellWorld(
                        combat.Combat);
                    if (spellWorld == null)
                        throw new NotSupportedException(
                            "원본 공격 충돌 처리기를 새 전투에서 재생성하지 못했습니다.");
                    combat.Combat.ConfigureOriginal323ScriptAttackEffectWorldForRestore(
                        save.Combat, originalAttackRolls, spellWorld);
                }
                var treasure = OrdrTreasureRestoreGraphBuilder.Prepare(
                    save, combat, commerce);
                // Both handles below share the exact H6 instance in original
                // mode. No script RNG, source draw or reward is consumed.
                var scriptOther = commerce.RandomForPurpose(
                    Original323ScriptRandomPurpose.OtherOriginalScriptCall);
                var monsterGold = commerce.RandomForPurpose(
                    Original323ScriptRandomPurpose.MonsterDeathGold);
                auxiliary = OrdrAuxiliaryRestoreGraphBuilder.Prepare(
                    combat, save, inputs.CreepCatalog,
                    rawcode => inputs.ResearchCanApply(combat.Core, rawcode),
                    inputs.StoryCatalog,
                    (minimum, maximum) => scriptOther.NextInclusive(minimum, maximum),
                    inputs.PracticeCatalog, inputs.OriginalMonsterCatalog,
                    inputs.SourceDamage, monsterGold, null,
                    inputs.CreepDispatchAuthority, inputs.SharedOriginalObjects,
                    inputs.SharedObjectiveWitness, scriptOther);
                ValidateReconstructedState(
                    save, combat, commerce, auxiliary, treasure,
                    originalAttackRolls);
                return new OrdrUnpublishedWorldRestoreGraph(
                    combat, commerce, auxiliary, treasure,
                    originalAttackRolls, save.SessionId, save.LogicalTime);
            }
            catch
            {
                // Auxiliary owns all subscriptions opened during staging.
                // Cleanup cannot hide the original failed restore reason.
                try { auxiliary?.Dispose(); }
                catch { }
                throw;
            }
        }

        // All values are checkpoint DTOs with deterministic public fields.
        // This is a read-only assertion of identity, not a second restore.
        // Passing local component validation is insufficient when a staged
        // service refers to the wrong combat, wallet or RNG epoch.
        internal static void AssertSameState<T>(
            string owner, T original, T restored)
            where T : class
        {
            if (original == null && restored == null) return;
            if (original == null || restored == null ||
                !string.Equals(JsonUtility.ToJson(original),
                    JsonUtility.ToJson(restored), StringComparison.Ordinal))
                throw new FormatException(
                    owner + " 복원 후 상태/원본 영수증이 저장 시점과 다릅니다.");
        }

        public static void ValidateReconstructedState(
            OrdrCoreCheckpointState save,
            OrdrUnpublishedCombatRestoreGraph combat,
            OrdrUnpublishedCommerceRestoreGraph commerce,
            OrdrUnpublishedAuxiliaryRestoreGraph auxiliary,
            OrdrUnpublishedTreasureRestoreGraph treasure = null,
            Original323ScriptAttackProcRolls originalAttackRolls = null)
        {
            if (save == null || combat == null || commerce == null ||
                auxiliary == null || combat.Core == null ||
                combat.Combat == null || combat.Waves == null ||
                !string.Equals(save.SessionId,
                    combat.Core.Missions.SessionId, StringComparison.Ordinal) ||
                combat.Core.LogicalTime != save.LogicalTime ||
                combat.Combat.Time != save.LogicalTime ||
                !ReferenceEquals(combat.Combat.Game, combat.Core.Game) ||
                !ReferenceEquals(combat.Combat.Roster, combat.Core.Economy.Units))
                throw new FormatException(
                    "통합 복원에서 전투·시계·보유 유닛 소유자가 다릅니다.");
            if ((save.ScriptRandom == null) !=
                    (commerce.OriginalScriptRandom != null) ||
                (save.ScriptRandom != null) !=
                    (commerce.ScriptRandom != null))
                throw new FormatException(
                    "원본 공통 BFi/모바일 난수 소유권이 중복되거나 누락됐습니다.");

            AssertSameState("게임", save.Game, combat.Core.Game.CaptureState());
            AssertSameState("개인미션/보상 영수증", save.MissionGameplay,
                combat.Core.MissionGameplay.CaptureState());
            AssertSameState("로스터", save.Roster,
                combat.Core.Economy.Units.CaptureState());
            AssertSameState("지갑", save.MissionGameplay.Wallet,
                combat.Core.Economy.Wallet.CaptureState());
            AssertSameState("전투", save.Combat, combat.Combat.CaptureRuntimeDraft());
            AssertSameState("웨이브", save.Waves, combat.Waves.CaptureState());
            AssertSameState("도박소", save.MoneyDesk, commerce.MoneyDesk.CaptureState());
            AssertSameState("유닛 도박", save.UnitGambling,
                commerce.UnitTiers.CaptureState());
            AssertSameState("이세계 도박", save.OtherworldGambling,
                commerce.Otherworld.CaptureState());
            AssertSameState("리롤", save.UnitReroll, commerce.Reroll.CaptureState());
            AssertSameState("위습/도박 원본 능력 소유권",
                save.WispDeferredAbilities, commerce.DeferredAbilities.CaptureState());
            AssertSameState("공통 모바일 난수", save.ScriptRandom,
                commerce.ScriptRandom?.CaptureState());
            AssertSameState("원본 H6 커서", save.OriginalScriptRandom,
                commerce.OriginalScriptRandom?.CaptureCheckpoint());
            if (save.OriginalScriptAttackProcs != null &&
                (originalAttackRolls == null ||
                 !ReferenceEquals(originalAttackRolls.SharedRandom,
                    commerce.OriginalScriptRandom)))
                throw new FormatException(
                    "공격 이벤트와 원본 BFi 난수의 소유자가 다릅니다.");
            AssertSameState("원본 공격 확률 영수증",
                save.OriginalScriptAttackProcs,
                originalAttackRolls?.CaptureState());
            AssertSameState("위습 풀 난수", save.WispPoolRandom,
                commerce.WispPoolRandom.CaptureState());
            AssertSameState("졸업 상점 난수", save.GraduatedRarePoolRandom,
                commerce.GraduatedRarePoolRandom.CaptureState());
            AssertSameState("유닛 등급 난수", save.TierPoolRandom,
                commerce.TierPoolRandom.CaptureState());
            AssertSameState("해독", save.Poneglyph, auxiliary.Poneglyph?.CaptureState());
            AssertSameState("연구", save.Research, auxiliary.Research?.CaptureState());
            AssertSameState("진화", save.Evolution, auxiliary.Evolution?.CaptureState());
            AssertSameState("크립", save.Creeps, auxiliary.Creeps?.CaptureState());
            AssertSameState("스토리 건물", save.StoryBuildings,
                auxiliary.StoryBuildings?.CaptureState());
            AssertSameState("스토리", save.SoloStory, auxiliary.SoloStory?.CaptureState());
            AssertSameState("스토리 연구/보상", save.StoryProgression,
                auxiliary.StoryProgression == null ? null :
                auxiliary.StoryProgression.CaptureState(
                    auxiliary.SoloStory.NativeRewards));
            AssertSameState("공유 목표", save.SharedObjectives,
                auxiliary.SharedObjectives?.CaptureState());
            AssertSameState("65R 출현", save.Practice, auxiliary.Practice?.CaptureState());
            AssertSameState("보물/가이몬/알람", save.Treasure,
                treasure?.CaptureState());
            // No session events, cast, loot or reward may be replayed here.
        }
    }
}
