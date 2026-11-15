using System;
using System.Collections.Generic;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Unity
{
    // 씬의 빈 GameObject에 추가하는 데이터/전투 연결 진입점.
    // 원본 피해 계산기가 확인되기 전에는 런타임을 시작하지 않으며 임시 피해량을 쓰지 않는다.
    public sealed class OrdrCharacterCombatController : MonoBehaviour
    {
        [SerializeField] private TextAsset unitCatalogJson;
        [SerializeField] private TextAsset characterSkillsJson;
        [SerializeField] private TextAsset originalStatEvidenceJson;
        [SerializeField] private TextAsset originalMapBaseStatsJson;
        [SerializeField] private TextAsset originalNativePassivesJson;
        [SerializeField] private TextAsset originalAuraAuditJson;
        [SerializeField] private TextAsset verifiedFusionRecipesJson;
        [SerializeField] private TextAsset otherworldMysticFusionRecipesJson;
        [SerializeField] private TextAsset originalJassFusionRegistryJson;
        [SerializeField] private TextAsset originalFusionObjectReconciliationJson;
        [SerializeField] private TextAsset originalHelperAlchemyRecipesJson;
        [SerializeField] private TextAsset originalUnitRawcodeLabelsJson;
        [SerializeField] private TextAsset verifiedMonsterWavesJson;
        [SerializeField] private TextAsset originalMonsterWaveScriptJson;
        [SerializeField] private TextAsset originalMonsterObjectAbilitiesJson;
        [SerializeField] private TextAsset originalWispPoolsJson;
        [SerializeField] private bool enableMobileTouchPanel = true;
        [SerializeField] private bool enableUnitWorldMarkers = true;

        public CharacterUnitCatalog Units { get; private set; }
        public CharacterSkillCatalog Skills { get; private set; }
        public UnitImplementationRegistry UnitDefinitions { get; private set; }
        public OriginalUnitStatEvidenceCatalog StatEvidence { get; private set; }
        public OriginalMapBaseStatCatalog MapBaseStats { get; private set; }
        public Original323UncommonAbilityCatalog OriginalUncommonAbilities { get; private set; }
        public Original323NativePassiveCatalog OriginalNativePassives { get; private set; }
        public bool UsesOriginalNativePassives { get; private set; }
        public Original323SpecialAuraCatalog OriginalSpecialAuras { get; private set; }
        public Original323ArmorAuraCatalog OriginalArmorAuras { get; private set; }
        public Original323NativeSpeedAuraCatalog OriginalNativeSpeedAuras { get; private set; }
        public Original323NativeCommandAuraCatalog OriginalNativeCommandAuras { get; private set; }
        public Original323CompositeAuraCatalog OriginalCombinedAuras { get; private set; }
        public Original323MapObjectAuraCatalog OriginalMapObjectAuras { get; private set; }
        private IOriginal323SpecialAuraTargetRules specialAuraTargets;

        public void ConfigureVerifiedOriginalSpecialAuraTargets(
            IOriginal323SpecialAuraTargetRules verifiedTargets)
        {
            if (Combat != null || specialAuraTargets != null)
                throw new InvalidOperationException("원본 오라 대상 근거는 전투 생성 전에 연결하세요.");
            specialAuraTargets = verifiedTargets ?? throw new ArgumentNullException(nameof(verifiedTargets));
        }
        private IOriginalUncommonVerifiedProcWorld originalUncommonEffectWorld;
        private IOriginal323SpecialRareImpactWitness specialRareNativeImpactWitness;
        private IOriginal323SpecialRareNativeSequenceWitness specialRareNativeSequenceWitness;
        public Original323SpecialRareNativeSequenceWorld SpecialRareNativeSequence
        { get; private set; }
        private IOriginal323VerifiedNativeAttackCadence verifiedNativeCadence;
        private IOriginal323VerifiedNativeEnemyMovementStep verifiedNativeMovement;
        private Original323ObservedMonsterMovementWorld observedMonsterPositions;
        private Original323ObservedMonsterEscapeWorld observedMonsterEscapes;

        public void ConfigureObservedMonsterPositions(
            IOriginal323ObservedMonsterPositionSource originalPositionSource)
        {
            if (observedMonsterPositions != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "몬스터 원본 위치 관측은 게임 시작 전에 한 번만 등록하세요.");
            observedMonsterPositions = new Original323ObservedMonsterMovementWorld(
                originalPositionSource);
        }
        public void ConfigureObservedMonsterEscapes(
            IOriginal323ObservedMonsterEscapeSource originalNativeExits)
        {
            if (observedMonsterEscapes != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "원본 몬스터 이탈 이벤트는 게임 시작 전에 연결하세요.");
            observedMonsterEscapes =
                new Original323ObservedMonsterEscapeWorld(originalNativeExits);
        }

        private IOriginal323UncommonCriticalPrehitRules verifiedUncommonCritRules;

        // Native AOae attack speed and enemy movement need separate live
        // source timing/stacking/cap proofs. Object DataA/DataB alone does
        // not authorize setting game attack intervals or enemy trajectories.
        public void ConfigureVerifiedOriginal323NativeSpeedExecution(
            IOriginal323VerifiedNativeAttackCadence attackTiming = null,
            IOriginal323VerifiedNativeEnemyMovementStep enemyMovement = null)
        {
            if (Combat != null || verifiedNativeCadence != null ||
                verifiedNativeMovement != null)
                throw new InvalidOperationException(
                    "원본 전투 공속/이속 적용은 월드 생성 전에 설정하세요.");
            if (attackTiming == null && enemyMovement == null)
                throw new ArgumentException("원본 공속 또는 이동 규칙이 필요합니다.");
            verifiedNativeCadence = attackTiming;
            verifiedNativeMovement = enemyMovement;
        }

        // Opt-in real native impact bridge for Special / Rare units, including
        // characters with NO web tooltip skill profile. A source-observed
        // full cast + hit and exact original UNIT ability are required.
        public void ConfigureVerifiedOriginalSpecialRareNativeImpacts(
            IOriginal323SpecialRareImpactWitness verifiedNativeSource)
        {
            if (Combat != null || specialRareNativeImpactWitness != null)
                throw new InvalidOperationException(
                    "특별함·희귀함 원본 주문 타격은 전투 생성 전에 한 번만 연결하세요.");
            if (MapBaseStats == null)
                throw new InvalidOperationException(
                    "원본 유닛/객체 능력 데이터를 먼저 로드하세요.");
            specialRareNativeImpactWitness = verifiedNativeSource ??
                throw new ArgumentNullException(nameof(verifiedNativeSource));
        }

        // Source-observed direct attack lifecycles: a cast can resolve
        // multiple original hits at different native game times. This is
        // opt-in and never simulates an unverified proc or tick interval.
        public void ConfigureVerifiedOriginalSpecialRareSequences(
            IOriginal323SpecialRareNativeSequenceWitness sourceSequence)
        {
            if (Combat != null || specialRareNativeSequenceWitness != null)
                throw new InvalidOperationException(
                    "원본 연속 공격은 전투 월드 생성 전 한 번만 설정하세요.");
            if (MapBaseStats == null || specialRareNativeImpactWitness == null)
                throw new InvalidOperationException(
                    "원본 데이터와 개별 네이티브 타격 증거를 먼저 등록하세요.");
            specialRareNativeSequenceWitness = sourceSequence ??
                throw new ArgumentNullException(nameof(sourceSequence));
        }

        // This must be set BEFORE creating the damage runtime. A07I crit
        // changes the native attack's raw damage before armor conversion.
        public void ConfigureVerifiedOriginalUncommonCritical(
            IOriginal323UncommonCriticalPrehitRules verifiedRules)
        {
            if (Combat != null || verifiedUncommonCritRules != null)
                throw new InvalidOperationException(
                    "원본 육식 크리티컬 근거는 전투 월드 생성 전에 한 번만 연결하세요.");
            verifiedUncommonCritRules = verifiedRules ??
                throw new ArgumentNullException(nameof(verifiedRules));
        }

        // Register a native skill effect executor only after every effect,
        // target rule, armor behavior and hit timing has source evidence.
        // This must be called before creating the combat runtime.
        public void ConfigureVerifiedUncommonNativeSkills(
            IOriginalUncommonVerifiedProcWorld originalEffects)
        {
            if (Combat != null || originalUncommonEffectWorld != null)
                throw new InvalidOperationException(
                    "안흔함 네이티브 스킬 처리는 전투 월드 생성 전에 1회만 지정하세요.");
            originalUncommonEffectWorld = originalEffects ??
                throw new ArgumentNullException(nameof(originalEffects));
        }
        // Enable only the individually verified uncommon native skills.
        // Robin, Rankyaku and the shared AHbh handlers can be connected in
        // any subset without needing every source world to be ready.
        // A live Bash witness is mandatory for Brook/Tashigi/Hachi, and
        // separate final-HP conversion evidence is mandatory for A07R/Q/S.
        public void ConfigureVerifiedOriginalUncommonSkillModules(
            IOriginal323LiveBashTargetWitness liveBashWitness = null,
            IOriginal323UncommonBonusDamageRules verifiedBonusDamage = null,
            IOriginal323RankyakuVerifiedRules verifiedRankyaku = null,
            IOriginal323RobinVerifiedVolleyRules verifiedRobin = null)
        {
            IOriginal323UncommonBashTargetRules nativeTargets = null;
            if (liveBashWitness != null)
            {
                if (OriginalMonsterObjectAbilities == null)
                    throw new InvalidOperationException(
                        "원본 몬스터 100종 객체 목록을 먼저 로드하세요.");
                nativeTargets = new Original323LiveBashTargetRules(
                    OriginalMonsterObjectAbilities, liveBashWitness);
            }
            ConfigureVerifiedUncommonNativeSkills(
                Original323VerifiedUncommonSkillAssembly.Create(
                    nativeTargets, verifiedBonusDamage, verifiedRankyaku,
                    verifiedRobin));
        }

        // Brook A07P is a complete original-native stun once the enemy's
        // Warcraft hero and immunity flags are independently established.
        // This deliberately does NOT approve Ace, Tashigi, Hachi or rankyaku.
        public void ConfigureVerifiedOriginalBrookBash(
            IOriginal323UncommonBashTargetRules verifiedTargets)
        {
            ConfigureVerifiedUncommonNativeSkills(
                new Original323UncommonBashWorld(verifiedTargets));
        }

        // Brook + Ace + Tashigi + Hachi. Uses the same native hero/immunity
        // proof, but bonus damage requires a separate verified HP policy.
        // This does not authorize Lucci/Fukuro critical or rankyaku.
        public void ConfigureVerifiedOriginalUncommonBashBundle(
            IOriginal323UncommonBashTargetRules verifiedTargets,
            IOriginal323UncommonBonusDamageRules verifiedBonusDamage)
        {
            ConfigureVerifiedUncommonNativeSkills(
                new Original323UncommonBashBundleWorld(
                    verifiedTargets, verifiedBonusDamage));
        }

        // Routes only abilities whose COMPLETE native effects have been
        // separately verified: Brook+Bash and rankyaku are independent ports.
        public void ConfigureVerifiedOriginalUncommonEffectBundle(
            IOriginal323UncommonBashTargetRules verifiedTargets,
            IOriginal323UncommonBonusDamageRules verifiedBonusDamage,
            IOriginal323RankyakuVerifiedRules verifiedRankyaku)
        {
            ConfigureVerifiedUncommonNativeSkills(
                new Original323UncommonEffectRouter(
                    new Original323UncommonBashBundleWorld(
                        verifiedTargets, verifiedBonusDamage),
                    new Original323UncommonRankyakuWorld(verifiedRankyaku)));
        }

        // Robin A07M is one primary native shot plus at most two additional
        // source-resolved distinct targets. No mobile-radius approximation.
        public void ConfigureVerifiedOriginalRobinMultiTarget(
            IOriginal323RobinVerifiedVolleyRules originalVolley)
        {
            ConfigureVerifiedUncommonNativeSkills(
                new Original323RobinMultiTargetWorld(originalVolley));
        }

        // Complete combined uncommon handler, deliberately separate from
        // the A07I pre-armor critical-hit source.
        public void ConfigureVerifiedOriginalUncommonAllEffectBundle(
            IOriginal323UncommonBashTargetRules verifiedTargets,
            IOriginal323UncommonBonusDamageRules verifiedBonusDamage,
            IOriginal323RankyakuVerifiedRules verifiedRankyaku,
            IOriginal323RobinVerifiedVolleyRules verifiedRobin)
        {
            ConfigureVerifiedUncommonNativeSkills(
                new Original323UncommonEffectRouter(
                    new Original323UncommonBashBundleWorld(
                        verifiedTargets, verifiedBonusDamage),
                    new Original323UncommonRankyakuWorld(verifiedRankyaku),
                    new Original323RobinMultiTargetWorld(verifiedRobin)));
        }

        public VerifiedFusionCatalog VerifiedFusionRecipes { get; private set; }
        public OtherworldMysticFusionCatalog OtherworldMysticFusions { get; private set; }
        public OriginalJassFusionCatalog OriginalJassFusions { get; private set; }
        public OriginalFusionObjectPreviewCatalog OriginalFusionObjectPreview { get; private set; }
        public Ordr323OriginalAlchemyRefundCatalog OriginalAlchemyRefunds { get; private set; }
        public OriginalUnitRawcodeLabelCatalog OriginalUnitRawcodes { get; private set; }
        public Original323VerifiedMonsterWaveCatalog VerifiedMonsterWaves { get; private set; }
        public Original323MonsterWaveIdentityCatalog OriginalMonsterSpawnIdentities { get; private set; }
        public Original323MonsterObjectAbilityCatalog OriginalMonsterObjectAbilities { get; private set; }
        public Original323NightmareA04QSpawnState NightmareMonsterA04QSpawns { get; private set; }
        private IOriginal323LiveMonsterWaveEvidence liveMonsterWaveEvidence;
        // The shipped resources may lack final-HP monster plans.
        // Only independently reviewed, identity-matched plans may be supplied.
        private readonly Dictionary<int, VerifiedWavePlan> registeredLiveWavePlans =
            new Dictionary<int, VerifiedWavePlan>();
        private IOriginal323NightmareA04QEventWitness nightmareA04QEventWitness;
        private IOriginal323LiveMonsterA04QWitness liveMonsterA04QWitness;
        private IOriginal323LiveMonsterStatWitness liveMonsterStatWitness;
        public CombatWaveDirector VerifiedWaveDirector { get; } = new CombatWaveDirector();
        private IOriginal323ObservedRoundStartWitness observedRoundStartWitness;
        private IOriginal323BossDeadlineWitness originalBossDeadlineWitness;
        private Original323BossScoreService originalBossScores;
        private IOriginal323MonsterGoldDeathWitness originalMonsterGoldWitness;
        private Original323MonsterGoldService originalMonsterGold;
        public OfflineSessionDiagnostics Diagnostics { get; } =
            new OfflineSessionDiagnostics();

        public void RecordSessionIssue(string kind, string message)
        {
            Diagnostics.Record(Combat == null ? 0 : Combat.Game.CurrentWave,
                Combat == null ? 0 : Combat.Time, kind, message);
        }

        public string ExportSessionDiagnostics()
        {
            return Diagnostics.Export();
        }

        private void OnVerifiedMonsterDefeated(int sourceWave, string instanceId)
        {
            // Record only scheduled ordinary line kills, not bosses or escapes.
            string monsterRawcode;
            if (EvolutionProgress != null &&
                VerifiedWaveDirector.TryGetDefeatedMonsterRawcode(
                    sourceWave, instanceId, out monsterRawcode) &&
                !VerifiedWaveDirector.TryGetDefeatedBossRawcode(
                    sourceWave, instanceId, out monsterRawcode))
            {
                try
                {
                    EvolutionProgress.RecordVerifiedLineDefeat(sourceWave, instanceId, Combat.Time);
                }
                catch (Exception error)
                {
                    // Optional source witness errors cannot interrupt the
                    // original kill settlement and independent gold reward.
                    RecordSessionIssue("진화체 진행 실패", error.Message);
                    Debug.LogException(error);
                }
            }
            if (originalMonsterGold == null) return;
            try
            {
                int awarded;
                if (TryProcessObservedMonsterGold(sourceWave, instanceId, out awarded) &&
                    awarded > 0)
                    RecordSessionIssue("처치 보상", "몬스터 " + instanceId +
                        " / 골드 +" + awarded);
            }
            catch (Exception ex)
            {
                // The source RNG witness is external. A faulty adapter must
                // not corrupt the already-recorded original monster death.
                RecordSessionIssue("처치 보상 실패", ex.Message);
                Debug.LogException(ex);
            }
        }
        private readonly HashSet<int> checkedOriginalBossDeadlines =
            new HashSet<int>();

        // Gold is separate from boss score: process only actual monster
        // deaths witnessed in original shared BFi random order.
        public void ConfigureOriginalMonsterGoldEvents(
            IOriginal323MonsterGoldDeathWitness nativeGoldEvents)
        {
            if (Combat != null || originalMonsterGoldWitness != null)
                throw new InvalidOperationException(
                    "몬스터 골드 원본 이벤트는 전투 생성 전에 한 번만 연결해야 합니다.");
            originalMonsterGoldWitness = nativeGoldEvents ??
                throw new ArgumentNullException(nameof(nativeGoldEvents));
        }

        public bool TryProcessObservedMonsterGold(int sourceSpawnWave,
            string originalMonsterInstanceId, out int awardedGold)
        {
            awardedGold = 0;
            return Combat != null &&
                originalMonsterGold != null &&
                originalMonsterGold.TryProcessObservedDeath(
                    sourceSpawnWave, originalMonsterInstanceId, out awardedGold);
        }

        // External adapters must reflect native Warcraft timer/death events.
        // The original scores are SCORE POINTS, never Gold resource rewards.
        public void ConfigureOriginalBossScoreEvents(
            IOriginal323BossKillTimeWitness verifiedOriginalDeaths,
            IOriginal323BossScorePort originalScoreLedger)
        {
            if (Combat != null || originalBossScores != null)
                throw new InvalidOperationException(
                    "보스 점수 이벤트는 전투 생성 전에 한 번만 등록해야 합니다.");
            originalBossScores = new Original323BossScoreService(
                VerifiedWaveDirector, verifiedOriginalDeaths, originalScoreLedger);
        }

        public bool TryCreditObservedOriginalBossScore(int waveNumber, string bossId)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                originalBossScores != null &&
                originalBossScores.TryCreditDefeatedBoss(waveNumber, bossId);
        }

        public void ConfigureObservedOriginalBossDeadline(
            IOriginal323BossDeadlineWitness observedOriginalEvents)
        {
            if (Combat != null || originalBossDeadlineWitness != null)
                throw new InvalidOperationException(
                    "보스 제한시간 이벤트는 전투 생성 전에 한 번만 연결해야 합니다.");
            originalBossDeadlineWitness = observedOriginalEvents ??
                throw new ArgumentNullException(nameof(observedOriginalEvents));
        }

        // Original CU8/CU7: only after the observed l0 check may a still-
        // outstanding boss trigger defeat. Never infer timer expiry from FPS.
        public bool TryApplyObservedOriginalBossDeadline(int waveNumber)
        {
            if (Combat == null ||
                Combat.Game.Phase != Ordr.Mobile.Core.SessionPhase.Running ||
                Combat.Game.CurrentWave != waveNumber ||
                originalBossDeadlineWitness == null ||
                checkedOriginalBossDeadlines.Contains(waveNumber))
                return false;
            VerifiedWaveProgress progress;
            bool missedBoss;
            if (!VerifiedWaveDirector.TryGetWaveProgress(waveNumber, out progress) ||
                !Original323BossRoundRules.TryEvaluateObservedBossDeadline(
                    progress, out missedBoss))
                return false;
            string proof;
            if (!originalBossDeadlineWitness.TryConfirmInterRoundBossCheck(
                waveNumber, out proof) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(proof))
                return false;
            checkedOriginalBossDeadlines.Add(waveNumber);
            if (missedBoss) Combat.Game.Finish(false);
            return true;
        }



        // Original round-transition signals are not inferred from a cleared
        // combat board. Attach a live observed source before the next wave.
        public void ConfigureObservedRoundStart(
            IOriginal323ObservedRoundStartWitness originalEvents)
        {
            if (Combat != null || observedRoundStartWitness != null)
                throw new InvalidOperationException(
                    "원본 라운드 전환 이벤트는 전투 시작 전에 한 번만 연결하세요.");
            observedRoundStartWitness = originalEvents ??
                throw new ArgumentNullException(nameof(originalEvents));
        }

        // Keep independent final-stat/target/spawn approval separate from the
        // imported model or test fixture. Configure before the combat world.
        public void ConfigureVerifiedLiveMonsterWaves(
            IOriginal323LiveMonsterWaveEvidence verifiedSource)
        {
            if (Combat != null || liveMonsterWaveEvidence != null)
                throw new InvalidOperationException("몬스터 실전 승인 근거는 전투 생성 전에 한 번만 연결하세요.");
            liveMonsterWaveEvidence = verifiedSource ??
                throw new ArgumentNullException(nameof(verifiedSource));
        }

        // A verified original event adapter must observe the real JASS-equivalent
        // action; the source-only snapshot never fabricates game events.
        // An approved adapter can supply reviewed wave data without inventing
        // a static monster JSON. Original identity/cadence/role checks run
        // here; independent final-stat approval is checked at wave start.
        public void RegisterVerifiedLiveMonsterWavePlan(VerifiedWavePlan plan)
        {
            RegisterVerifiedLiveMonsterWavePlans(new[] { plan });
        }

        // Validate the ENTIRE source-reviewed batch before changing the
        // registered 1..65 plan set. Failed batches leave all earlier entries
        // untouched. Per-wave final HP/defense approval is repeated at start.
        public void RegisterVerifiedLiveMonsterWavePlans(
            IEnumerable<VerifiedWavePlan> sourcePlans)
        {
            if (sourcePlans == null)
                throw new ArgumentNullException(nameof(sourcePlans));
            if (liveMonsterWaveEvidence == null ||
                OriginalMonsterSpawnIdentities == null ||
                OriginalMonsterObjectAbilities == null)
                throw new InvalidOperationException(
                    "원본 출현 스크립트/몬스터 객체/실전 승인 공급자가 필요합니다.");
            var pending = new List<VerifiedWavePlan>();
            var seen = new HashSet<int>();
            foreach (var plan in sourcePlans)
            {
                if (plan == null || plan.WaveNumber < 1 || plan.WaveNumber > 65)
                    throw new ArgumentException("1~65라운드의 검증된 출현 계획이 필요합니다.");
                if (Combat != null && (Combat.Game.CurrentWave >= plan.WaveNumber ||
                    Combat.Game.Phase == SessionPhase.Won ||
                    Combat.Game.Phase == SessionPhase.Lost))
                    throw new InvalidOperationException(
                        "이미 시작했거나 종료된 라운드는 교체할 수 없습니다.");
                if (!seen.Add(plan.WaveNumber) ||
                    registeredLiveWavePlans.ContainsKey(plan.WaveNumber))
                    throw new InvalidOperationException("라운드 계획 중복 등록");
                VerifiedWavePlan imported;
                if (VerifiedMonsterWaves != null &&
                    VerifiedMonsterWaves.TryGetPlan(plan.WaveNumber, out imported))
                    throw new InvalidOperationException(
                        "이미 제공된 검증 JSON 라운드는 덮어쓸 수 없습니다.");
                if (!OriginalMonsterSpawnIdentities.MatchesSinglePlayerWave(plan) ||
                    !OriginalMonsterSpawnIdentities.MatchesSinglePlayerSpawnCadence(plan) ||
                    !OriginalMonsterObjectAbilities.MatchesWaveObjectRoles(plan))
                    throw new InvalidOperationException(
                        "원본 JASS 출현 수량/간격/몬스터 객체와 일치하지 않습니다.");
                pending.Add(plan);
            }
            if (pending.Count == 0)
                throw new ArgumentException("빈 라운드 계획은 등록하지 않습니다.");
            foreach (var plan in pending)
                registeredLiveWavePlans.Add(plan.WaveNumber, plan);
        }

        // Turn pinned JASS role/cadence identities into actual waves only
        // when a separate adapter supplies EACH effective final stat and
        // position and another independent source approves the entire plan.
        // No synthetic monster JSON or reference-engine model is accepted.
        public void RegisterOriginalMonsterSpawnBatch(
            IEnumerable<int> waves, IOriginal323VerifiedFinalMonsterSpawnSource originalLiveSpawns)
        {
            if (OriginalMonsterSpawnIdentities == null ||
                OriginalMonsterObjectAbilities == null ||
                liveMonsterWaveEvidence == null)
                throw new InvalidOperationException(
                    "원본 65라운드 ID·객체 자료와 실전 승인 공급자를 연결하세요.");
            var assembler = new Original323VerifiedMonsterWaveAssembler(
                OriginalMonsterSpawnIdentities, OriginalMonsterObjectAbilities,
                liveMonsterWaveEvidence);
            RegisterVerifiedLiveMonsterWavePlans(
                assembler.BuildBatch(waves, originalLiveSpawns));
        }

        public void RegisterOriginalMonsterSpawn(
            int wave, IOriginal323VerifiedFinalMonsterSpawnSource originalLiveSpawns,
            bool observedEnraged = false,
            IOriginal323ConditionalEnragedSpawnWitness enragedEvent = null)
        {
            if (OriginalMonsterSpawnIdentities == null ||
                OriginalMonsterObjectAbilities == null ||
                liveMonsterWaveEvidence == null)
                throw new InvalidOperationException(
                    "원본 65라운드 ID·객체 자료와 실전 승인 공급자를 연결하세요.");
            var assembler = new Original323VerifiedMonsterWaveAssembler(
                OriginalMonsterSpawnIdentities, OriginalMonsterObjectAbilities,
                liveMonsterWaveEvidence);
            RegisterVerifiedLiveMonsterWavePlan(assembler.Build(
                wave, originalLiveSpawns, observedEnraged, enragedEvent));
        }

        public void ConfigureObservedNightmareA04QEvents(
            IOriginal323NightmareA04QEventWitness verifiedEvents)
        {
            if (nightmareA04QEventWitness != null)
                throw new InvalidOperationException("악몽 마법 방어 이벤트 관측기를 이미 연결했습니다.");
            nightmareA04QEventWitness = verifiedEvents ??
                throw new ArgumentNullException(nameof(verifiedEvents));
        }

        public bool TryApplyObservedNightmareA04QEvent(
            Original323NightmareA04QEvent sourceEvent, int playerSlot)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                NightmareMonsterA04QSpawns != null &&
                NightmareMonsterA04QSpawns.TryApplyObservedEvent(
                    sourceEvent, playerSlot, nightmareA04QEventWitness);
        }

        // Explicit live-unit event adapter. The next-spawn LK/NJ state is NOT
        // evidence for changing a previously spawned unit's current A04Q.
        public void ConfigureObservedLiveMonsterA04Q(
            IOriginal323LiveMonsterA04QWitness verifiedEvents)
        {
            if (liveMonsterA04QWitness != null)
                throw new InvalidOperationException(
                    "개별 몬스터 A04Q 관측기를 이미 연결했습니다.");
            liveMonsterA04QWitness = verifiedEvents ??
                throw new ArgumentNullException(nameof(verifiedEvents));
        }

        public bool TryApplyObservedLiveMonsterA04Q(string enemyId,
            int expectedPreviousLevel, int observedCurrentLevel)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                liveMonsterA04QWitness != null &&
                Combat.TryApplyObservedEnemyA04QLevel(enemyId,
                    expectedPreviousLevel, observedCurrentLevel,
                    liveMonsterA04QWitness);
        }

        private IOriginal323MagicDefenseStackSequenceWitness
            liveMagicDefenseStackWitness;

        public void ConfigureObservedLiveMonsterMagicDefenseStacks(
            IOriginal323MagicDefenseStackSequenceWitness verifiedOrderedEvents)
        {
            if (liveMagicDefenseStackWitness != null)
                throw new InvalidOperationException(
                    "원본 몬스터 마법 저항 누적 관측기를 이미 연결했습니다.");
            liveMagicDefenseStackWitness = verifiedOrderedEvents ??
                throw new ArgumentNullException(nameof(verifiedOrderedEvents));
        }

        public bool TryApplyObservedLiveMonsterMagicDefenseStacks(
            string enemyId, int expectedPreviousLevel, int observedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseStackCall>
                originalOrderedEvents)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                liveMonsterA04QWitness != null &&
                liveMagicDefenseStackWitness != null &&
                Combat.TryApplyObservedEnemyMagicDefenseStack(
                    enemyId, expectedPreviousLevel, observedFinalLevel,
                    originalOrderedEvents, liveMagicDefenseStackWitness,
                    liveMonsterA04QWitness);
        }

        private IOriginal323MagicDefenseLevelMutationWitness
            observedMagicLevelMutationWitness;

        public void ConfigureObservedOriginalMagicLevelMutations(
            IOriginal323MagicDefenseLevelMutationWitness sourceEvents)
        {
            if (observedMagicLevelMutationWitness != null)
                throw new InvalidOperationException(
                    "원본 마법 저항 설정/누적 이벤트 공급자가 이미 연결됐습니다.");
            observedMagicLevelMutationWitness = sourceEvents ??
                throw new ArgumentNullException(nameof(sourceEvents));
        }

        public bool TryApplyObservedOriginalMagicLevelMutations(
            string enemyId, int expectedPreviousLevel, int observedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseLevelMutation>
                originalOrderedEvents)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                liveMonsterA04QWitness != null &&
                observedMagicLevelMutationWitness != null &&
                Combat.TryApplyObservedEnemyMagicDefenseLevelMutations(
                    enemyId, expectedPreviousLevel, observedFinalLevel,
                    originalOrderedEvents, observedMagicLevelMutationWitness,
                    liveMonsterA04QWitness);
        }

        // Explicit observations of changes to a living monster. These are
        // not generated from the unverified initial HP/armor research model.
        public void ConfigureObservedLiveMonsterStats(
            IOriginal323LiveMonsterStatWitness verifiedEvents)
        {
            if (liveMonsterStatWitness != null)
                throw new InvalidOperationException(
                    "개별 몬스터 체력/물리 방어 관측기를 이미 연결했습니다.");
            liveMonsterStatWitness = verifiedEvents ??
                throw new ArgumentNullException(nameof(verifiedEvents));
        }

        public bool TryApplyObservedLiveMonsterMaximumHealth(
            string enemyId, long expectedCurrentHealth, long expectedMaxHealth,
            long observedCurrentHealth, long observedMaxHealth)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                liveMonsterStatWitness != null &&
                Combat.TryApplyObservedEnemyMaximumHealthChange(enemyId,
                    expectedCurrentHealth, expectedMaxHealth,
                    observedCurrentHealth, observedMaxHealth,
                    liveMonsterStatWitness);
        }

        public bool TryApplyObservedLiveMonsterPhysicalArmor(
            string enemyId, double expectedArmor, string expectedArmorType,
            double observedArmor, string observedArmorType)
        {
            return Combat != null &&
                Combat.Game.Phase == Ordr.Mobile.Core.SessionPhase.Running &&
                liveMonsterStatWitness != null &&
                Combat.TryApplyObservedEnemyPhysicalArmorChange(enemyId,
                    expectedArmor, expectedArmorType,
                    observedArmor, observedArmorType,
                    liveMonsterStatWitness);
        }

        // A source-backed next-spawn estimate; NOT an independent live
        // monster defense/ability/target-immune approval.
        public bool TryGetNextOriginalMonsterMagicResistance(
            CharacterEnemyKind kind, int playerSlot, out double percent)
        {
            percent = 0;
            return OriginalMonsterObjectAbilities != null &&
                NightmareMonsterA04QSpawns != null &&
                NightmareMonsterA04QSpawns.TryGetNextSpawnMagicResistance(
                    kind, playerSlot, out percent);
        }

        public OriginalJassFusionPlanner OriginalFusionPlanner { get; private set; }
        public AssistedFusionService FusionAssistance { get; private set; }
        public Original323LowerGradeSaleService LowerGradeSales { get; private set; }
        public Original323UnitDecompositionService UnitDecompositions { get; private set; }
        private Original323CommonFusionPort commonSourceFusion;
        private bool usesBuiltInAutomaticUncommonWisps;
        private SourceModelBasicCombatRules sourceModelDamage;
        private IOriginalEquippedItemWitness originalEquippedItemWitness;
        private IOriginalEquippedInventorySnapshotWitness originalEquippedInventoryWitness;
        private IOriginalBulletArmadaLevelWitness originalBulletArmadaWitness;
        private IOriginal323CurrentNativeHeroAttributeWitness originalHeroStatWitness;
        public bool HasOriginalEquippedInventorySnapshotWitness =>
            originalEquippedInventoryWitness != null;

        // A real source adapter can supply all six GetHeroStatBJ true/false
        // values, including equipment and temporary attribute buffs. The
        // existing mobile attack/armor/speed item ledger is NOT a hero stat.
        public void ConfigureOriginalCurrentHeroStatWitness(
            IOriginal323CurrentNativeHeroAttributeWitness witness)
        {
            if (witness == null || originalHeroStatWitness != null ||
                StoryHeroExperience != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "원본 현재 영웅 능력치 관측기는 전투 시작 전에 한 번만 등록합니다.");
            originalHeroStatWitness = witness;
        }

        // Integration ports for a REAL native inventory/equipment purchase,
        // not a button/tooltip or an item issued from the personal missions.
        public void ConfigureOriginalEquippedItemWitness(IOriginalEquippedItemWitness witness)
        {
            if (PracticeBattle != null || originalEquippedItemWitness != null || witness == null)
                throw new InvalidOperationException("장비 증거 공급자는 연습 전투 전 한 번만 연결합니다.");
            originalEquippedItemWitness = witness;
        }


        // A complete, revisioned inventory is a separate native-authorized
        // input, useful when a loaded unit owned equipment before this combat
        // runtime existed. It never derives gear from the mobile shop UI.
        public void ConfigureOriginalEquippedInventorySnapshotWitness(
            IOriginalEquippedInventorySnapshotWitness witness)
        {
            if (PracticeBattle != null || originalEquippedInventoryWitness != null ||
                witness == null)
                throw new InvalidOperationException("원본 전체 장착 증거는 연습 전투 전에 한 번만 연결합니다.");
            originalEquippedInventoryWitness = witness;
        }

        public void ConfigureOriginalBulletArmadaLevelWitness(
            IOriginalBulletArmadaLevelWitness witness)
        {
            if (PracticeBattle != null || originalBulletArmadaWitness != null || witness == null)
                throw new InvalidOperationException("아르마다 증거 공급자는 연습 전투 전 한 번만 연결합니다.");
            originalBulletArmadaWitness = witness;
        }

        public bool TryRecordOriginalEquippedItemReceipt(string receipt,
            long unitInstanceId, string itemRawcode, int signedCount)
        {
            return Combat != null && Combat.TryRecordSourceModelEquippedItem(
                receipt, unitInstanceId, itemRawcode, signedCount);
        }


        public bool TrySynchronizeOriginalEquippedInventory(long unitInstanceId)
        {
            return Combat != null &&
                Combat.TrySynchronizeSourceModelEquippedInventory(unitInstanceId);
        }
        private IOriginalWispRandom sessionScriptRandom;
        // Each existing mobile native pool already had its own RNG owner.
        // Record them separately, not as invented shared Warcraft dice.
        private MobileRollbackWispRandom sessionWispPoolRandom;
        private MobileRollbackWispRandom sessionGraduatedRarePoolRandom;
        private MobileRollbackWispRandom sessionTierPoolRandom;
        private bool checkpointCaptureActive;
        public SourceModelPracticeBattle PracticeBattle { get; private set; }
        // The completed 65-round game count is shared by subsequent runs on
        // this installation. Round/mission/story clears never increment it.
        private const string AccumulatedClearPrefsKey = "ordr.nightmare-isekai.full-clears.v1";
        private bool accumulatedClearsLoaded;
        private int accumulatedClears;
        private bool fullClearRecordedForSession;

        public int AccumulatedClearCount
        {
            get
            {
                if (!accumulatedClearsLoaded)
                {
                    accumulatedClears = Math.Max(0,
                        PlayerPrefs.GetInt(AccumulatedClearPrefsKey, 0));
                    accumulatedClearsLoaded = true;
                }
                return accumulatedClears;
            }
        }

        private void RecordFullGameClearOnce()
        {
            if (fullClearRecordedForSession || PracticeBattle == null ||
                !PracticeBattle.IsFullGameClear)
                return;
            int previous = AccumulatedClearCount;
            if (previous == int.MaxValue)
            {
                fullClearRecordedForSession = true;
                return;
            }
            // Commit before marking this session. A failed platform write is
            // reported, never silently counted or retried in a tight GUI loop.
            try
            {
                PlayerPrefs.SetInt(AccumulatedClearPrefsKey, previous + 1);
                PlayerPrefs.Save();
                accumulatedClears = previous + 1;
                fullClearRecordedForSession = true;
                RecordSessionIssue("누적 클리어", accumulatedClears + "회 기록");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                RecordSessionIssue("누적 클리어 저장 실패", error.Message);
                fullClearRecordedForSession = true;
            }
        }
        public Original323CreepPracticeWorld PracticeCreeps { get; private set; }
        public Original323StoryBuildingPracticeWorld PracticeStoryBuildings { get; private set; }
        public Original323StoryBuildingCatalog StoryBuildingCatalog { get; private set; }
        public Original323SoloStoryScenario SoloStory { get; private set; }
        private IOriginal323StoryNativeRewardPort originalStoryNativeAwards;
        public Original323StoryMobileProgression StoryProgression { get; private set; }
        private Original323KumaWispChoiceWorld practiceStoryKumaChoice;
        public Original323MobileStoryHeroExperienceWorld StoryHeroExperience { get; private set; }
        public Original323HeroAttributeWorld OriginalHeroAttributes { get; private set; }
        public Original323ShirahoshiTraitPurchase ShirahoshiTrait { get; private set; }
        public long PendingPracticeStoryTranscendWisps =>
            SoloStory != null && SoloStory.HighestDestroyedStage >= 10 &&
            SharedEconomy != null
                ? SharedEconomy.Wallet.Balance(
                    Original323SoloStoryScenario.PracticeTranscendWisp)
                : 0;
        public long PendingAllStoryTranscendWisps =>
            checked(PendingPracticeStoryTranscendWisps +
                (OriginalSourceMaterialRewards == null ? 0 :
                 OriginalSourceMaterialRewards.PendingTranscendWisps));
        public void ConfigureOriginal323StoryNativeAwards(IOriginal323StoryNativeRewardPort port)
        {
            originalStoryNativeAwards = port ??
                throw new ArgumentNullException(nameof(port));
            if (StoryProgression != null)
            {
                StoryProgression.ConnectAdditionalNativeEffects(port);
                SoloStory?.NativeRewards.ApplyPending(StoryProgression);
            }
        }
        private IOriginal323StoryBuildingSpawnAuthorization originalStoryStages;
        private IOriginal323StoryBuildingSpawnWitness originalStorySpawnWitness;
        private IOriginal323StoryBuildingClearWitness originalStoryClearWitness;
        public bool CanRequestOriginalStoryBuilding =>
            PracticeStoryBuildings != null && originalStoryStages != null &&
            originalStorySpawnWitness != null;
        
        // Original runtime/replay source is mandatory. Practice mode does not
        // invent an unlock rule, final Nightmare stats or a story victory.
        public void ConfigureVerifiedStoryBuildingStages(
            IOriginal323StoryBuildingSpawnAuthorization stageAuthorization,
            IOriginal323StoryBuildingSpawnWitness spawnWitness,
            IOriginal323StoryBuildingClearWitness clearWitness)
        {
            if (stageAuthorization == null || spawnWitness == null || clearWitness == null)
                throw new ArgumentNullException("스토리 등장/파괴·정산 원본 공급자를 모두 연결해야 합니다.");
            if (originalStoryStages != null || originalStorySpawnWitness != null ||
                originalStoryClearWitness != null)
                throw new InvalidOperationException("스토리 원본 이벤트 공급자를 중복 연결할 수 없습니다.");
            originalStoryStages = stageAuthorization;
            originalStorySpawnWitness = spawnWitness;
            originalStoryClearWitness = clearWitness;
        }

        public ManualActionResult RequestOriginalStoryBuilding(int stage)
        {
            if (!CanRequestOriginalStoryBuilding)
                return ManualActionResult.Unavailable("원본 스토리 건물 출현");
            try
            {
                var building = PracticeStoryBuildings.SpawnAuthorizedBuilding(
                    stage, originalStoryStages, originalStorySpawnWitness);
                return new ManualActionResult(ManualActionState.Succeeded,
                    building.Building.Name + " · 원본 건물 출현 확인");
            }
            catch (ArgumentException error)
            {
                return new ManualActionResult(ManualActionState.Rejected, error.Message);
            }
            catch (InvalidOperationException error)
            {
                return new ManualActionResult(ManualActionState.Rejected, error.Message);
            }
        }

        // Called only after the external source has actually settled the full
        // original clear transaction. It does not issue reward from a model kill.
        public bool TryRecordObservedStoryBuildingSettlement(
            string enemyId, string settledEventId)
        {
            if (PracticeStoryBuildings == null || originalStoryClearWitness == null ||
                !PracticeStoryBuildings.TryRecordSettledClear(
                    enemyId, settledEventId, originalStoryClearWitness))
                return false;
            // This branch is source-settled only. Practice model story deaths
            // never auto-complete the original shared missions.
            var settled = PracticeStoryBuildings.FindEncounter(enemyId);
            if (settled != null && SharedMissionRewards != null)
            {
                Original323SharedMissionKind kind;
                if (settled.Building.Stage == 2)
                    kind = Original323SharedMissionKind.FastAlabasta;
                else if (settled.Building.Stage == 11)
                    kind = Original323SharedMissionKind.DestructionKing;
                else return true;
                SharedMissionRewards.TrySettle(kind, "story-settled:" +
                    settledEventId, Combat.Game.CurrentWave,
                    settled.SourceSettlementEvidence);
            }
            return true;
        }
        public SourceModelRegularMoneyGambling PracticeMoneyGambling { get; private set; }
        public SourceModelMoneyGamblingDesk PracticeMoneyGamblingDesk { get; private set; }
        public SourceModelOtherworldUnitGambling PracticeUnitGambling { get; private set; }
        public SourceModelGamblingNavigation PracticeGamblingNavigation { get; private set; }
        public SourceModelCompuExchange PracticeCompuExchange { get; private set; }
        public SourceModelItemExcavation PracticeItemExcavation { get; private set; }
        public ManualActionResult RequestItemExcavation(bool unique)
        {
            if (PracticeItemExcavation == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 아이템 발굴이 연결되지 않았습니다.");
            return PracticeItemExcavation.DrawItem(unique);
        }
        // Optional importer for additional witnessed source awards. Normal
        // CJS Compu accrual is already atomic in SourceModelUnitGambling.
        public void ConfigureCompuExchangeSources(IOriginal323CompuAwardWitness verifiedAwards)
        {
            if (PracticeCompuExchange == null || verifiedAwards == null)
                throw new InvalidOperationException("콤푸 환전소와 원본 보상 증빙이 필요합니다.");
            PracticeCompuExchange.ConfigureVerifiedAwardWitness(verifiedAwards);
        }

        public bool RecordSourceConfirmedCompuAward(string originalEventId, long amount) =>
            PracticeCompuExchange != null &&
            PracticeCompuExchange.RecordObservedAward(originalEventId, amount);

        public ManualActionResult RequestCompuExchange(string recipe)
        {
            if (PracticeCompuExchange == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "환전소 월드가 연결되지 않았습니다.");
            return PracticeCompuExchange.Exchange(recipe);
        }
        public SourceModelUnitGambling PracticeTierUnitGambling { get; private set; }
        public SourceModelMaterialSupport PracticeMaterialSupport { get; private set; }
        public SourceModelUnitReroll PracticeUnitReroll { get; private set; }
        public ManualActionResult RequestUnitReroll(long actor)
        {
            if (Combat == null || Combat.Roster.SelectedId != actor || PracticeUnitReroll == null)
                return new ManualActionResult(ManualActionState.Rejected, "리롤할 보유 유닛을 먼저 선택해 주세요.");
            try { return PracticeUnitReroll.TryReroll(actor); }
            catch (Exception error)
            {
                Debug.LogException(error);
                return new ManualActionResult(ManualActionState.Rejected, "리롤 처리 실패. 재료와 보류 결과를 유지합니다.");
            }
        }
        private Original323NavigationSelection originalNavigationSelection;
        public Original323NavigationSelection OriginalNavigationSelection
        {
            get
            {
                if (originalNavigationSelection == null && Combat != null)
                    originalNavigationSelection = new Original323NavigationSelection(Combat.Game);
                return originalNavigationSelection;
            }
        }

        // Source-confirmed creep placement and unit eligibility are not yet known.
        // Call only for an explicitly requested object-base practice encounter.
        public Original323CreepEncounter SpawnPracticeCreepStage(
            int stage, double x, double y)
        {
            if (PracticeCreeps == null)
                throw new InvalidOperationException("원본 기반 연습 전투를 먼저 시작하세요.");
            return PracticeCreeps.SpawnPracticeStage(stage, x, y);
        }

        // Explicit development encounter only. Real placement, stage unlocks and
        // source-authorized dispatch are separate prerequisites for retail play.
        public Original323StoryBuildingEncounter SpawnPracticeStoryBuilding(
            int stage, double x, double y)
        {
            if (PracticeStoryBuildings == null)
                throw new InvalidOperationException("스토리 건물 연습 전투를 먼저 시작하세요.");
            return PracticeStoryBuildings.SpawnPracticeBuilding(stage, x, y);
        }

        // The original CUM spawns Loguetown before wave one. The model arena
        // starts at unit preparation and retains its session across battle start.
        private void EnsureSoloStoryStarted(string pinnedStatsJson = null)
        {
            if (SoloStory != null) return;
            if (Combat == null || SharedEconomy == null ||
                Combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("스토리 시작 전 공유 세션이 필요합니다.");
            if (string.IsNullOrEmpty(pinnedStatsJson))
            {
                var asset = Resources.Load<TextAsset>(
                    "OrdrData/original-2323C-monster-object-stats");
                if (asset == null)
                    throw new InvalidOperationException("스토리 건물 원본 객체 자료가 APK에 없습니다.");
                pinnedStatsJson = asset.text;
            }
            var catalog = Original323StoryBuildingCatalog.FromJson(pinnedStatsJson);
            var buildings = new Original323StoryBuildingPracticeWorld(Combat, catalog);
            try
            {
                if (storyDispatchRules == null)
                    ConfigureVerifiedStoryBuildingDispatch(
                        new Original323SoloStoryMovement(Combat, MapBaseStats));
                var scenario = new Original323SoloStoryScenario(
                    Combat, buildings, SharedEconomy,
                    (minimum, maximum) => sessionScriptRandom.NextInclusive(minimum, maximum));
                var progression = new Original323StoryMobileProgression(
                    SharedEconomy.Wallet);
                try
                {
                    progression.ConnectStoryMaterialEffects(
                        new Original323AncientShipStoryRewardPort(Combat,
                            SharedEconomy, Units, MapBaseStats));
                    var heroExperience = new Original323MobileStoryHeroExperienceWorld(
                        Combat, SharedEconomy, MapBaseStats);
                    var shirahoshiTrait = new Original323ShirahoshiTraitPurchase(
                        SharedEconomy.Wallet, heroExperience.LevelStateOf);
                    heroExperience.ConnectOriginalShirahoshiFourPointTraitWitness(
                        shirahoshiTrait.HasActivated);
                    progression.ConnectMobileHeroExperience(heroExperience);
                    progression.ConfigureOtherworldGamblingNavigation(() =>
                        PracticeUnitGambling != null && PracticeUnitGambling.Navigation ==
                            Original323GamblingNavigation.Casino);
                    Combat.ConfigureSourceHeroLevelProgression(heroExperience);
                    // Original SLK numeric cells: 4 explicit hero triples,
                    // 66 missing triples decoded to the engine's sparse
                    // integer default (zero). Bonused stats stay unknown.
                    var originalHeroAttributes =
                        new Original323HeroAttributeWorld(heroExperience.LevelStateOf,
                            Combat.SourceModelHeroFormRevisionOf);
                    Original323UnitBalanceHeroInitialAttributes.RegisterVerifiedRows(
                        originalHeroAttributes);
                    Original323UnitBalanceHeroInitialAttributes.RegisterSparseNumericDefaults(
                        originalHeroAttributes);
                    if (originalHeroStatWitness != null)
                        originalHeroAttributes.ConnectOriginalCurrentNativeStats(
                            originalHeroStatWitness);
                    Combat.ConfigureSourceModelOriginalHeroAttributes(
                        originalHeroAttributes);
                    OriginalHeroAttributes = originalHeroAttributes;
                    StoryHeroExperience = heroExperience;
                    ShirahoshiTrait = shirahoshiTrait;
                    if (originalStoryNativeAwards != null)
                        progression.ConnectAdditionalNativeEffects(originalStoryNativeAwards);
                    scenario.ConfigureNativeAwardPort(progression);
                    scenario.Start();
                    // Do not grant at menu/open time. The original outcome
                    // is minted only after a committed stage-ten story clear.
                    practiceStoryKumaChoice = new Original323KumaWispChoiceWorld(
                        Combat, SharedEconomy, Units, MapBaseStats);
                }
                catch { scenario.Dispose(); throw; }
                StoryProgression = progression;
                StoryBuildingCatalog = catalog;
                PracticeStoryBuildings = buildings;
                SoloStory = scenario;
            }
            catch { buildings.Dispose(); throw; }
        }

        // Story buildings already exist during unit preparation; their
        // attack engine must share the SAME source model as the later 65R
        // battle. Enabling it only at wave 1 left deployed story characters
        // without attacks throughout preparation.
        private void EnsureSourceCombatReadyForStoryPreparation()
        {
            if (Combat == null || sourceModelDamage == null ||
                SharedEconomy == null || !SharedEconomy.HasStarted ||
                Combat.Game.Phase != SessionPhase.Running ||
                Combat.Game.CurrentWave != 0)
                throw new InvalidOperationException(
                    "스토리 공격 모델은 시작된 세션의 첫 라운드 전에만 준비합니다.");
            if (Combat.UsesSourceModelBasicAttacks) return;
            var originalResources = Resources.Load<TextAsset>(
                "OrdrData/original-2323C-unit-object-stats");
            if (originalResources == null)
                throw new InvalidOperationException(
                    "원본 유닛 HP/MP SLK 자료가 APK에 없습니다.");
            var sourcePool = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                originalResources.text, MapBaseStats);
            sourceModelDamage.Enable();
            Combat.ConfigureSourceModelBasicAttacks(sourceModelDamage);
            if (PlayerBuildingResearch != null)
                Combat.ConfigureSourceModelPlayerResearch(PlayerBuildingResearch);
            Combat.ConfigureSourceModelPlayerGold(() => SharedEconomy.Wallet.Balance("gold"));
            Combat.ConfigureSourceModelAccumulatedClearCount(() => AccumulatedClearCount);
            Combat.ConfigureSourceModelFusionNativeReceipts(
                id => Original323SourceItemPassiveRules.CombatReceiptBalance(
                    SharedEconomy.Wallet, id));
            Combat.BindExistingVerifiedAttacks();
            Combat.ConfigureSourceModelUnitResources(sourcePool);
        }

        public void BeginStandaloneBattle()
        {
            if (Combat == null) ConfigureCatalogOnlyWorld(unchecked((uint)Environment.TickCount));
            if (sourceModelDamage == null || PracticeBattle != null || Combat.Game.CurrentWave != 0 ||
                (Combat.Game.Phase != SessionPhase.NotStarted && !IsUnitPreparation))
                throw new InvalidOperationException("새 게임 또는 유닛 준비 상태에서 전투를 시작하세요.");
            // Standalone Unity simulation uses the source-derived 65-round
            // model directly. No Warcraft instance or external live-event
            // witness is required to drive combat. Parity flags stay false.
            var data = Resources.Load<TextAsset>("OrdrData/mobile-standalone-enemy-model");
            if (data == null) throw new InvalidOperationException("원본 기반 몬스터 전투 자료가 APK에 없습니다.");
            var creepData = Resources.Load<TextAsset>(
                "OrdrData/original-2323C-monster-object-stats");
            if (creepData == null)
                throw new InvalidOperationException("크립 원본 객체 자료가 APK에 없습니다.");
            var creepCatalog = Original323CreepObjectCatalog.FromJson(creepData.text);
            var prepared = new SourceModelPracticeBattle(Combat, VerifiedWaveDirector, SharedEconomy,
                new SourceModelPracticeCatalog(data.text, 65), OriginalMonsterSpawnIdentities,
                sourceModelDamage, sessionScriptRandom, () =>
                {
                    PersonalMissions?.OnRoundStarted();
                    SoloStory?.OnRoundStarted();
                    RecordSessionIssue("전투 라운드", Combat.Game.CurrentWave + "라운드 / 원본 최종 전투 미검증");
                }, mobileMainLane: true);
            if (Combat.Game.Phase == SessionPhase.NotStarted) BeginSession();
            EnsureSourceCombatReadyForStoryPreparation();
            if (originalEquippedItemWitness != null || originalEquippedInventoryWitness != null)
                Combat.ConfigureSourceModelEquippedItems(originalEquippedItemWitness,
                    originalEquippedInventoryWitness);
            if (originalBulletArmadaWitness != null)
                Combat.ConfigureSourceModelBulletArmadaLevel(originalBulletArmadaWitness);
            // Full equipment evidence may arrive during preparation;
            // bind it at the explicit wave start, before any 65R combat.
            if (originalEquippedInventoryWitness != null)
                Combat.SynchronizeExistingSourceModelEquippedInventories();
            EnsureSoloStoryStarted(creepData.text);
            PracticeBattle = prepared;
            prepared.MoneyGamblingBossDefeated = (enemyId, rawcode) =>
            {
                sourceModelDamage.PlayerUpgrades?.ObserveOriginalBossDefeated(rawcode);
                PracticeMoneyGamblingDesk?.RecordDefeatedSourceBoss(enemyId, rawcode);
            };
            PracticeCreeps = new Original323CreepPracticeWorld(Combat, creepCatalog);
            PracticeCreeps.EncounterDefeated += encounter =>
                PracticeUnitGambling?.RecordThirdCreepDefeated(encounter.OriginalObject.Rawcode);
            PracticeBattle.Start();
            var presenter = GetComponent<OrdrUnitWorldPresenter>();
            if (presenter != null) presenter.ShowAllOwnedUnits();
            RecordSessionIssue("원본 기반 독립 전투 시작", "65R · 외부 원작 실행 불필요 · 몬스터 HP/피해 최종 원작 대조 전");
        }
        public bool TryPlanOriginalStoryGreenBlood(long instanceId,
            out Original323GreenBloodPlan plan, out string unitName)
        {
            plan = null;
            unitName = null;
            return commonSourceFusion != null &&
                commonSourceFusion.TryPlanGreenBlood(instanceId, out plan, out unitName);
        }

        public ManualActionResult RequestOriginalStoryGreenBlood(long instanceId)
        {
            return commonSourceFusion == null
                ? ManualActionResult.Unavailable("원본 그린블러드 거래")
                : commonSourceFusion.UseGreenBloodOnOwnedUnit(instanceId);
        }

        public int ExecutableCommonFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableCommonRecipeCount;
        public int ExecutableSpecialFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableSpecialRecipeCount;
        public int ExecutableRareFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableRareRecipeCount;
        public int ExecutableLegendaryFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableLegendaryRecipeCount;
        public int ExecutableMysticFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableMysticRecipeCount;
        public int ExecutableHiddenFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableHiddenRecipeCount;
        public int ExecutableEternalFusionCount => commonSourceFusion == null ? 0 : commonSourceFusion.ExecutableEternalRecipeCount;
        public bool IsUnitPreparation => Combat != null && SharedEconomy != null &&
            SharedEconomy.HasStarted && Combat.Game.Phase == SessionPhase.Running &&
            Combat.Game.CurrentWave == 0;

        // The preparation stage has no MONSTER WAVES, but the original story
        // buildings already spawn here and dispatched units must be able
        // to hit them. Their combat clock shares the later 65R world.
        public void BeginUnitPreparation()
        {
            if (Combat == null) ConfigureCatalogOnlyWorld(unchecked((uint)Environment.TickCount));
            BeginSession();
            // External verified worlds retain their own original combat
            // authority; only the built-in standalone source model is armed.
            if (sourceModelDamage != null)
                EnsureSourceCombatReadyForStoryPreparation();
            EnsureSoloStoryStarted();
        }

        // One explicit grade button = one actual Brook consumed. The combat
        // runtime owns the bone ledger and removes the same roster instance
        // used by fusion, sales, summons and the battlefield.
        public ManualActionResult RequestSansBoneFeeding(long sansInstanceId,
            string brookCatalogId)
        {
            if (Combat == null || !Combat.UsesSourceModelBasicAttacks ||
                Combat.Game.Phase != SessionPhase.Running)
                return ManualActionResult.Unavailable("샌즈 체력본충");
            if (brookCatalogId != "U-03" && brookCatalogId != "R-18")
                return new ManualActionResult(ManualActionState.Rejected,
                    "브룩 안흔함 또는 희귀함을 선택해 주세요.");
            try
            {
                if (Combat.Roster.SelectedId != sansInstanceId ||
                    Combat.Roster.Get(sansInstanceId).UnitId != "MY-07")
                    return new ManualActionResult(ManualActionState.Rejected,
                        "먹이를 줄 샌즈를 먼저 선택해 주세요.");
                bool storyLane = Combat.IsStoryDeployed(sansInstanceId);
                long candidate = -1;
                foreach (var unit in Combat.MobileUnitSnapshot())
                    if (unit.Unit.Id == brookCatalogId &&
                        Combat.IsStoryDeployed(unit.InstanceId) == storyLane &&
                        (candidate < 0 || unit.InstanceId < candidate))
                        candidate = unit.InstanceId;
                if (candidate < 0)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "같은 전장에 해당 브룩이 없습니다.");
                int before = Combat.SourceModelSansBonesOf(sansInstanceId);
                if (!Combat.TryActivateSourceModelSansBoneQ(
                        sansInstanceId, candidate))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "브룩 소모 조건을 충족하지 못했습니다.");
                int after = Combat.SourceModelSansBonesOf(sansInstanceId);
                string grade = brookCatalogId == "U-03" ? "안흔함" : "희귀함";
                return new ManualActionResult(ManualActionState.Succeeded,
                    "브룩 " + grade + " 1기 소모 · 뼈 " + before + " → " + after +
                    (before < 30 && after >= 30 ? " · 샌즈 강화!" : ""));
            }
            catch (InvalidOperationException error)
            {
                return new ManualActionResult(ManualActionState.Rejected,
                    "체력본충 실패 · " + error.Message);
            }
            catch (KeyNotFoundException)
            {
                return new ManualActionResult(ManualActionState.Rejected,
                    "브룩 또는 샌즈가 이미 다른 거래에 사용됐습니다.");
            }
        }

        public ManualActionResult RequestLowerGradeSale(long instanceId)
        {
            if (Combat == null || LowerGradeSales == null)
                return ManualActionResult.Unavailable("하위 등급 판매");
            foreach (var row in Combat.MobileUnitSnapshot())
                if (row.InstanceId == instanceId && row.Unit.Grade == "흔함")
                    return ManualActions.RequestCommonSale(instanceId);
            return LowerGradeSales.Sell(instanceId);
        }

        // Explicit, bounded player request. Each individual source draw keeps
        // its original alternating candidates, debit and rollback. Stop on
        // shortage/failure/pending pirate ship; never retry or spend tomorrow.
        public ManualActionResult RequestCommonWispBatch(int requested)
        {
            if (requested < 1 || requested > 100)
                return new ManualActionResult(ManualActionState.Rejected, "한 번에 1~100개를 선택해 주세요.");
            int done = 0;
            string stopped = null;
            string lastSuccess = null;
            while (done < requested)
            {
                if (!CanRecruitVerifiedCommonWisp) { stopped = VerifiedCommonWispUnavailableReason; break; }
                var result = RequestVerifiedCommonWispRecruitment();
                if (result.State != ManualActionState.Succeeded) { stopped = result.Message; break; }
                lastSuccess = result.Message;
                done++;
            }
            return new ManualActionResult(done == 0 ? ManualActionState.Rejected : ManualActionState.Succeeded,
                done == 1 && requested == 1 ? lastSuccess :
                "랜덤위습 소환 " + done + "/" + requested + "회" +
                (done < requested ? " · 남은 위습 보존" + (string.IsNullOrEmpty(stopped) ? "" : " · " + stopped) : ""));
        }
        public CharacterCombatRuntime Combat { get; private set; }

        // Player-requested 1/2/4x practice pacing is a multiplier of the
        // SINGLE authoritative gameplay clock. It never alters original
        // attack/cast timings, target speed or cooldown values themselves.
        // A new scene/controller begins at 1x; this UI preference is not
        // an entitlement/reward and does not enter the savegame ledger.
        private readonly MobileGameSpeedClock gameplaySpeed = new MobileGameSpeedClock();
        public int GameplaySpeedMultiplier => gameplaySpeed.Multiplier;

        public void SetGameplaySpeed(int multiplier)
        {
            if (Combat == null || Combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException(
                    "게임을 시작한 후에만 배속을 선택할 수 있습니다.");
            gameplaySpeed.SetMultiplier(multiplier);
#if UNITY_EDITOR
            if (IsEditorPreview) EditorPreviewSpeed = multiplier;
#endif
        }

        // Used only when the shared RunGate permits simulation. Background
        // delays or long render stalls are bounded, never caught up as free
        // attacks/rewards on the next frame.
        public double GameDeltaFromUnscaled(double deltaSeconds) =>
            gameplaySpeed.Scale(deltaSeconds, runGate.CanAdvance);
        // Session-only evidence for the original chat scoreboard's common-draw page.
        // This is not a persistent save counter or a total-clear score ledger.
        public MobileOriginalChatScoreboard ChatScoreboard { get; } =
            new MobileOriginalChatScoreboard();
        public bool HasSourceVerifiedDamageRuntime { get; private set; }
#if UNITY_EDITOR
        // Never serialized or exposed in Android/iOS player assemblies.
        // Only the explicitly launched Editor sandbox may mark itself as
        // using non-retail monster stats, native timings and RNG outcomes.
        public bool IsEditorPreview { get; private set; }
        public int EditorPreviewSpeed { get; private set; } = 1;

        public void MarkEditorPreview()
        {
            if (Combat == null || Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException(
                    "에디터 체험 표시는 게임 시작 전의 테스트 월드에만 설정할 수 있습니다.");
            IsEditorPreview = true;
            gameplaySpeed.SetMultiplier(1);
            EditorPreviewSpeed = 1;
        }

        // Editor sandbox only; original JASS timing and production build are untouched.
        public void SetEditorPreviewSpeed(int speed)
        {
            if (!IsEditorPreview || Combat == null ||
                Combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException(
                    "명시적으로 시작한 Unity Editor 체험에서만 속도를 변경할 수 있습니다.");
            if (speed != 1 && speed != 2 && speed != 4)
                throw new ArgumentOutOfRangeException(nameof(speed),
                    "Unity Editor 연습 속도는 1배·2배·4배만 가능합니다.");
            // Do not set Unity's global timeScale: source-model simulation,
            // Editor preview rounds, and Android must share one time source,
            // while UI visuals and Unity fixed physics keep normal time.
            SetGameplaySpeed(speed);
        }
#endif
        private readonly OfflineSessionRunGate runGate = new OfflineSessionRunGate();
        public bool IsGameplayPaused => !runGate.CanAdvance;

        public void SetManualGameplayPause(bool paused)
        {
            if (Combat == null || Combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("진행 중인 게임에서만 일시정지할 수 있습니다.");
            runGate.SetManualPause(paused);
        }

        private void OnDestroy()
        {
            if (Combat != null)
            {
                Combat.CharacterRemoved -= OnPoneglyphUnitRemoved;
                Combat.CharacterRemovalObserverFailed -= Debug.LogException;
            }
            Wisps?.Dispose();
            PracticeBattle?.Dispose();
            SoloStory?.Dispose();
            PracticeCreeps?.Dispose();
            PracticeStoryBuildings?.Dispose();
            SharedObjectives?.Dispose();
            UnitDecompositions?.Dispose();
        }

        private void OnApplicationPause(bool paused)
        {
            runGate.SetApplicationPause(paused);
        }

        private void OnApplicationFocus(bool focused)
        {
            runGate.SetFocus(focused);
        }

        public MobileManualInteraction ManualActions { get; } = new MobileManualInteraction();
        public MobileDedicatedSkillActions DedicatedSkills { get; } =
            new MobileDedicatedSkillActions();

        // Explicit tap-only, source-verified actions. Do not repurpose Q/W/R.
        public void ConfigureOriginalDedicatedSkills(IOriginalDedicatedSkillPort originalWorld)
        {
            DedicatedSkills.Configure(originalWorld);
        }
        public SharedEconomySession SharedEconomy { get; private set; }

        // Capture on a single synchronous logical boundary. RunGate pauses
        // battle ticks but retains its previous MANUAL state on every path;
        // app-pause/focus flags remain untouched. No gameplay reward or
        // scene transition occurs while the draft is assembled.
        private OrdrCoreCheckpointState CaptureCheckpointAtSafeBoundary(
            Func<OrdrCoreCheckpointState> create)
        {
            if (create == null) throw new ArgumentNullException(nameof(create));
            if (checkpointCaptureActive || Combat == null || SharedEconomy == null ||
                PersonalMissions == null || !SharedEconomy.HasStarted ||
                Combat.Game.Phase != SessionPhase.Running ||
                Combat.PendingSimulationSeconds != 0)
                throw new InvalidOperationException(
                    "트랜잭션/전투 처리 중 또는 끝난 세션에서는 스냅샷을 만들 수 없습니다.");
            bool manualWasPaused = runGate.IsManuallyPaused;
            checkpointCaptureActive = true;
            runGate.SetManualPause(true);
            try
            {
                double clock = Combat.Time;
                string gameBefore = JsonUtility.ToJson(Combat.Game.CaptureState());
                string walletBefore = JsonUtility.ToJson(SharedEconomy.Wallet.CaptureState());
                string rosterBefore = JsonUtility.ToJson(SharedEconomy.Units.CaptureState());
                string missionsBefore = JsonUtility.ToJson(PersonalMissions.CaptureState());
                OrdrCoreCheckpointState result = create();
                if (Combat.Time != clock || result.LogicalTime != clock ||
                    Combat.PendingSimulationSeconds != 0 ||
                    !string.Equals(gameBefore,
                        JsonUtility.ToJson(Combat.Game.CaptureState()),
                        StringComparison.Ordinal) ||
                    !string.Equals(walletBefore,
                        JsonUtility.ToJson(SharedEconomy.Wallet.CaptureState()),
                        StringComparison.Ordinal) ||
                    !string.Equals(rosterBefore,
                        JsonUtility.ToJson(SharedEconomy.Units.CaptureState()),
                        StringComparison.Ordinal) ||
                    !string.Equals(missionsBefore,
                        JsonUtility.ToJson(PersonalMissions.CaptureState()),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "스냅샷 읽기 중 게임/지갑/유닛/미션 상태가 변경되어 저장을 취소했습니다.");
                return result;
            }
            finally
            {
                runGate.SetManualPause(manualWasPaused);
                checkpointCaptureActive = false;
            }
        }

        // S01/S02 core ledger draft only; no real Continue button.
        public OrdrCoreCheckpointState CaptureCoreCheckpointDraft()
        {
            return CaptureCheckpointAtSafeBoundary(() =>
                OrdrCoreCheckpoint.Capture(Combat.Game, SharedEconomy,
                    PersonalMissions, Combat.Time));
        }

        public void WriteCoreCheckpointDraftForDiagnostics()
        {
            OrdrCoreCheckpointStore.ForApplication().WriteCore(
                CaptureCoreCheckpointDraft());
        }

        private Original323TreasureSessionState CaptureTreasureCheckpointDraft()
        {
            if(OriginalTreasureHunt==null) return null;
            if(standaloneTreasure==null || MobileTreasureGacha==null ||
                TreasureAlarms==null ||
                !(sessionScriptRandom is MobileRollbackWispRandom))
                throw new NotSupportedException(
                    "외부 보물 월드/난수의 전체 복원 증빙이 없어 저장을 차단합니다.");
            var dream=GaimonDream.Capture();
            return new Original323TreasureSessionState {
                Hunt=OriginalTreasureHunt.CaptureState(),
                Mobile=standaloneTreasure.CaptureState(),
                Gacha=MobileTreasureGacha.CaptureState(),
                Alarms=TreasureAlarms.CaptureState(),
                Reveals=GaimonReveal?.CaptureState(),
                DreamApplied=dream.Applied,
                NamiAtDream=dream.NamiBonusWhenApplied
            };
        }

        // Same frozen frame for live combat, commerce, pending rolls, story
        // and wave ledgers. Anonymous timed Actions still reject capture.
        public OrdrCoreCheckpointState CaptureWorldCheckpointDraft()
        {
            return CaptureCheckpointAtSafeBoundary(() =>
            {
                var runtime = Combat.CaptureRuntimeDraft();
                var wave = VerifiedWaveDirector.CaptureState();
                var practice = PracticeBattle?.CaptureState();
                var script = sessionScriptRandom as MobileRollbackWispRandom;
                var money = PracticeMoneyGamblingDesk == null ? null :
                    PracticeMoneyGamblingDesk.CaptureState();
                if (money != null && script == null)
                    throw new NotSupportedException(
                        "원본 외부 BFi 난수는 복원 커서가 없어 저장할 수 없습니다.");
                if (money != null && (sessionWispPoolRandom == null ||
                    sessionGraduatedRarePoolRandom == null ||
                    sessionTierPoolRandom == null))
                    throw new NotSupportedException(
                        "원본 모바일 위습/졸업 상점/유닛 도박의 난수 기록이 부족합니다.");
                return OrdrCoreCheckpoint.Capture(Combat.Game, SharedEconomy,
                    PersonalMissions, Combat.Time, runtime, wave, practice,
                    script?.CaptureState(), money,
                    PracticeTierUnitGambling?.CaptureState(),
                    PracticeUnitGambling?.CaptureState(),
                    PracticeUnitReroll?.CaptureState(),
                    PlayerBuildingResearch?.CaptureState(),
                    EvolutionProgress?.CaptureState(),
                    Poneglyph?.CaptureState(),
                    PracticeCreeps?.CaptureState(),
                    PracticeStoryBuildings?.CaptureState(),
                    SoloStory?.CaptureState(),
                    StoryProgression == null ? null :
                        StoryProgression.CaptureState(SoloStory.NativeRewards),
                    sessionTierPoolRandom?.CaptureState(),
                    sessionWispPoolRandom?.CaptureState(),
                    sessionGraduatedRarePoolRandom?.CaptureState(),
                    CaptureTreasureCheckpointDraft());
            });
        }

        public void WriteWorldCheckpointDraftForDiagnostics()
        {
            OrdrCoreCheckpointStore.ForApplication().WriteCore(
                CaptureWorldCheckpointDraft());
        }

        public Original323PlayerUpgradeSession PlayerBuildingUpgrades { get; private set; }
        public Original323PlayerResearchSession PlayerBuildingResearch { get; private set; }
        private IReadOnlyList<Original323VerifiedUpgradeStep> verifiedUpgradeSteps;
        private IOriginal323VerifiedUpgradeWorld verifiedUpgradeWorld;

        // Supply every level's exact W3X cost/effect witness before runtime init.
        // An absent witness never turns a decorative upgrade card into a purchase.
        public void ConfigureOriginal323VerifiedPlayerUpgrades(
            IEnumerable<Original323VerifiedUpgradeStep> sourceSteps,
            IOriginal323VerifiedUpgradeWorld effectWorld)
        {
            if (Combat != null) throw new InvalidOperationException(
                "강화소 원본 강화 단계는 전투 구성 전에 연결하세요.");
            if (sourceSteps == null || effectWorld == null)
                throw new ArgumentNullException("원본 강화 비용/효과 연결이 필요합니다.");
            verifiedUpgradeSteps = new List<Original323VerifiedUpgradeStep>(sourceSteps);
            verifiedUpgradeWorld = effectWorld;
        }
        private Original323CommonWispRecruitmentService commonWispRecruitment;
        private IOriginal323CommonWispRecruitmentAvailability commonWispAvailability;
        private bool commonWispUsesBuiltInSource;
        public int? VerifiedCommonWispRecruitmentCost =>
            commonWispRecruitment == null ? (int?)null : commonWispRecruitment.Cost;
        public bool CanRecruitVerifiedCommonWisp =>
            commonWispRecruitment != null && commonWispRecruitment.CanRecruit;
        public string VerifiedCommonWispUnavailableReason =>
            commonWispAvailability == null ? null :
            commonWispAvailability.UnavailableReason;

        // Source-backed randomness, price and live spawn must all come from
        // the same reviewed original map adapter; there is no placeholder roll.
        public void ConfigureVerifiedCommonWispRecruitment(
            IOriginal323CommonWispRecruitmentWorld originalWorld)
        {
            if (Combat == null || SharedEconomy == null || MapBaseStats == null ||
                (commonWispRecruitment != null && !commonWispUsesBuiltInSource) ||
                Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException(
                    "원본 흔함 유닛 소환은 시작 전 공유 전투·경제 월드에 한 번만 연결하세요.");
            commonWispRecruitment = new Original323CommonWispRecruitmentService(
                Combat, SharedEconomy, MapBaseStats, originalWorld);
            commonWispUsesBuiltInSource = false;
            commonWispAvailability =
                originalWorld as IOriginal323CommonWispRecruitmentAvailability;
        }

        public ManualActionResult RequestVerifiedCommonWispRecruitment()
        {
            if (commonWispRecruitment == null)
                return ManualActionResult.Unavailable("랜덤위습 흔함 유닛 소환");
            try
            {
                var result = commonWispRecruitment.RecruitOne();
                if (result == null)
                {
                    RecordSessionIssue("위습 소환 거절", "원본 거래 미승인 또는 위습 부족");
                    return new ManualActionResult(ManualActionState.Rejected,
                        VerifiedCommonWispUnavailableReason ??
                        "랜덤위습이 부족하거나 원본 소환을 완료하지 못했습니다.");
                }
                var recruited = Units == null ? null : Units.Find(result.CatalogId);
                ChatScoreboard.RecordCommittedCommonWispDraw(result.CatalogId);
                RecordSessionIssue("위습 소환", recruited == null ? result.CatalogId :
                    recruited.Name);
                return new ManualActionResult(ManualActionState.Succeeded,
                    (result.CatalogId == Original323SourceCommonWispWorld.PirateShipCatalogId
                        ? "해적선 소환 완료: " : "흔함 유닛 소환 완료: ") +
                    (recruited == null ? result.CatalogId : recruited.Name) +
                    " (랜덤위습 " + result.SpentRandomWisps + "개 사용)");
            }
            catch (Exception error)
            {
                RecordSessionIssue("위습 소환 실패", error.Message);
                Debug.LogException(error);
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 흔함 소환 실패: 거래를 취소하고 재화를 복구했습니다.");
            }
        }
        // Only the roster identity of the source-pinned non-combat e0HP.
        // This is not an ordinary attacking character or a Wisp resource.
        // Pinned JASS CQB/CSN and CJK/CJM/CGk/CCk/CKJ noncombat
        // acquisition bridge. A runtime native/replay witness is mandatory.
        // The default practice game does NOT manufacture story victories,
        // gambling failures or free source-material units.
        public Original323SourceMaterialRewardWorld OriginalSourceMaterialRewards
        { get; private set; }

        public Original323LuckyTokenService LuckyTokens { get; private set; }
        public bool CanExchangeLuckyTokens => LuckyTokens != null &&
            LuckyTokens.CanExchange && runGate.CanAdvance && !IsUnitPreparation;

        public ManualActionResult RequestLuckyTokenWispExchange()
        {
            if (LuckyTokens == null)
                return ManualActionResult.Unavailable("행운의 토큰 경제 월드");
            if (!runGate.CanAdvance || IsUnitPreparation)
                return new ManualActionResult(ManualActionState.Rejected,
                    "전투 진행 중에만 행운의 토큰을 교환할 수 있습니다.");
            try
            {
                string wispId;
                string reason;
                if (!LuckyTokens.TryExchangeThree(out wispId, out reason))
                    return new ManualActionResult(ManualActionState.Rejected, reason);
                return new ManualActionResult(ManualActionState.Succeeded,
                    "행운의 토큰 3개 사용 · " +
                    (wispId == WispResourceIds.Special
                        ? "특별함 선택위습" : "희귀함 선택위습") + " 1개 지급");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                RecordSessionIssue("행운의 토큰 교환 실패", error.Message);
                return new ManualActionResult(ManualActionState.Rejected,
                    "토큰 교환 실패 · 원본 보유 상태를 확인해 주세요.");
            }
        }

        public void ConfigureVerifiedOriginalSourceMaterialEvents(
            IOriginal323SourceMaterialEventWitness sourceEvents)
        {
            if (Combat == null || Units == null || MapBaseStats == null ||
                SharedEconomy == null || OriginalSourceMaterialRewards != null)
                throw new InvalidOperationException(
                    "원본 재료 보상은 실제 전투 월드 준비 후 한 번만 연결하세요.");
            OriginalSourceMaterialRewards = new Original323SourceMaterialRewardWorld(
                Combat, SharedEconomy, Units, MapBaseStats, sourceEvents,
                OriginalJassFusions);
        }

        public bool TryRecordObservedStoryTranscendWisp(string sourceEventId)
        {
            return OriginalSourceMaterialRewards != null &&
                OriginalSourceMaterialRewards.TryRecordObservedStoryClearWisp(sourceEventId);
        }

        public bool TryRecordObservedLuckToken(string sourceEventId,
            string originalFunction, int originalCount, out IReadOnlyList<long> actorIds)
        {
            actorIds = Array.Empty<long>();
            return OriginalSourceMaterialRewards != null &&
                OriginalSourceMaterialRewards.TryRecordObservedTokenAward(
                    sourceEventId, originalFunction, originalCount, out actorIds);
        }

        // Source-confirmed CJM success: one real A800/RDUN random actor.
        // Neither this API nor CWR grants a free manual IS01..IS14 craft.
        public bool TryRecordObservedOtherworldRandomUnit(string sourceEventId,
            string resultRawcode, out long actorId)
        {
            actorId = 0;
            return OriginalSourceMaterialRewards != null &&
                OriginalSourceMaterialRewards.TryRecordObservedOtherworldRandomUnit(
                    sourceEventId, resultRawcode, out actorId);
        }

        // The caller must be a trusted original callback/replay bridge whose
        // event attests the source h0AN parent reward and any original costs.
        public bool TryRecordObservedSeraphimMaterial(string sourceEventId,
            string originalFunction, out long actorId)
        {
            actorId = 0;
            return OriginalSourceMaterialRewards != null &&
                OriginalSourceMaterialRewards.TryRecordObservedSeraphimMaterial(
                    sourceEventId, originalFunction, out actorId);
        }

        // The original e01A has three mutually exclusive B6/B7/B8 outcomes.
        // Source-observed and solo-story tokens retain separate receipts.
        public ManualActionResult RequestOriginalTranscendRewardBoxChoice()
        {
            return RequestOriginalTranscendWispChoice(
                Original323TranscendWispOutcome.RewardBox);
        }

        public ManualActionResult RequestOriginalTranscendKumaChoice()
        {
            return RequestOriginalTranscendWispChoice(
                Original323TranscendWispOutcome.Kuma);
        }

        public ManualActionResult RequestOriginalTranscendRayleighShipChoice()
        {
            return RequestOriginalTranscendWispChoice(
                Original323TranscendWispOutcome.RayleighAndShip);
        }

        public ManualActionResult RequestOriginalTranscendBoxResourcesChoice()
        {
            return RequestOriginalTranscendWispChoice(
                Original323TranscendWispOutcome.RewardBoxResources);
        }

        public ManualActionResult RequestOriginalTranscendBoxItemChoice(
            string itemRawcode)
        {
            if (Original323FusionSourceItemLedger.FindShell(itemRawcode) == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 꾸러미 아이템 목록에 없는 선택입니다.");
            return RequestOriginalTranscendWispChoice(
                Original323TranscendWispOutcome.RewardBox, itemRawcode);
        }

        private ManualActionResult RequestOriginalTranscendWispChoice(
            Original323TranscendWispOutcome outcome,
            string chosenItemRawcode = null)
        {
            if (!runGate.CanAdvance || IsUnitPreparation)
                return new ManualActionResult(ManualActionState.Rejected,
                    "전투 진행 중에만 초월위습을 사용할 수 있습니다.");
            if (PendingAllStoryTranscendWisps < 1)
                return new ManualActionResult(ManualActionState.Rejected,
                    "획득한 스토리 초월위습이 없습니다.");
            IReadOnlyList<long> actorIds = Array.Empty<long>();
            long previousGold = SharedEconomy.Wallet.Balance("gold");
            long previousWood = SharedEconomy.Wallet.Balance("wood");
            try
            {
                bool succeeded;
                if (OriginalSourceMaterialRewards != null &&
                    OriginalSourceMaterialRewards.PendingTranscendWisps > 0)
                {
                    if (chosenItemRawcode != null)
                        succeeded = OriginalSourceMaterialRewards.TryChooseBoxItem(
                            chosenItemRawcode);
                    else switch (outcome)
                    {
                        case Original323TranscendWispOutcome.RewardBox:
                            succeeded = OriginalSourceMaterialRewards.TryChooseRewardBox(out actorIds);
                            break;
                        case Original323TranscendWispOutcome.RewardBoxResources:
                            succeeded = OriginalSourceMaterialRewards.TryChooseBoxResources();
                            break;
                        case Original323TranscendWispOutcome.Kuma:
                            long kumaActor;
                            succeeded = OriginalSourceMaterialRewards.TryChooseKuma(out kumaActor);
                            if (succeeded) actorIds = new[] { kumaActor };
                            break;
                        case Original323TranscendWispOutcome.RayleighAndShip:
                            succeeded = OriginalSourceMaterialRewards.TryChooseRayleighAndShip(out actorIds);
                            break;
                        default:
                            succeeded = false;
                            break;
                    }
                }
                else if (chosenItemRawcode != null)
                    succeeded = practiceStoryKumaChoice != null &&
                        practiceStoryKumaChoice.TryChooseBoxItem(
                            Original323SoloStoryScenario.PracticeTranscendWisp,
                            chosenItemRawcode);
                else
                    succeeded = practiceStoryKumaChoice != null &&
                        practiceStoryKumaChoice.TryChoose(
                            Original323SoloStoryScenario.PracticeTranscendWisp,
                            outcome, out actorIds);
                if (!succeeded)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "초월위습을 선택하지 못했습니다. 재료와 유닛 상태를 확인해 주세요.");

                string message;
                if (chosenItemRawcode != null)
                    message = "초월위습 1개 사용 · 고유 아이템 " +
                        chosenItemRawcode + " 획득 · 목재 +" +
                        Original323KumaWispChoiceWorld.BoxItemWoodBonus;
                else switch (outcome)
                {
                    case Original323TranscendWispOutcome.RewardBoxResources:
                        message = "초월위습 1개 사용 · 골드 +" +
                            (SharedEconomy.Wallet.Balance("gold") - previousGold) +
                            ", 목재 +" +
                            (SharedEconomy.Wallet.Balance("wood") - previousWood) +
                            " (원본 CRL 난수 보상)";
                        break;
                    case Original323TranscendWispOutcome.RewardBox:
                        message = "스토리 초월위습 1개 사용 · 꾸러미 상자 - 초월 1기 획득";
                        break;
                    case Original323TranscendWispOutcome.Kuma:
                        message = "스토리 초월위습 1개 사용 · 바솔로뮤 쿠마 1기 획득";
                        break;
                    default:
                        message = "스토리 초월위습 1개 사용 · 실버즈 레일리 + 해적선 2기 획득";
                        break;
                }
                return new ManualActionResult(ManualActionState.Succeeded, message);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                RecordSessionIssue("스토리 초월위습 선택 실패", error.Message);
                return new ManualActionResult(ManualActionState.Rejected,
                    "초월위습 교환 처리 확인이 필요합니다. 보유 수량을 다시 확인해 주세요.");
            }
        }

        public long? StartingEvolutionActorId { get; private set; }
        public Original323EvolutionProgress EvolutionProgress { get; private set; }
        private Original323EvolutionService evolutionService;
        private IOriginal323NativeUnitPoolRandom evolutionNativeRandom;
        private float nextAutoEvolutionAttempt;
        public string EvolutionNotice { get; private set; }
        public float EvolutionNoticeShownAt { get; private set; } = -99f;

        public void ConfigureOriginal323EvolutionNativePool(IOriginal323NativeUnitPoolRandom source)
        {
            if (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException("진화체 난수 공급자는 게임 전에 지정해야 합니다.");
            evolutionNativeRandom = source ?? throw new ArgumentNullException(nameof(source));
        }

        public ManualActionResult RequestActivateStartingEvolution()
        {
            if (evolutionService == null)
                return ManualActionResult.Unavailable("시작 진화체");
            try
            {
                string message;
                bool succeeded = evolutionService.TryActivate(out message);
                if (succeeded)
                {
                    StartingEvolutionActorId = null;
                    RecordSessionIssue("진화체 사용", message);
                    EvolutionNotice = "진화체 성장 시작";
                    EvolutionNoticeShownAt = Time.unscaledTime;
                }
                return new ManualActionResult(succeeded ? ManualActionState.Succeeded :
                    ManualActionState.Rejected, message);
            }
            catch (Exception error)
            {
                RecordSessionIssue("진화체 사용 실패", error.Message);
                Debug.LogException(error);
                return new ManualActionResult(ManualActionState.Rejected,
                    "진화체 사용 실패 · 기존 결과를 유지하고 재시도할 수 있습니다.");
            }
        }

        private void TryFinishOriginal323Evolution()
        {
            if (evolutionService == null || EvolutionProgress == null ||
                !EvolutionProgress.CanRelease(Combat.Time)) return;
            try
            {
                long id;
                if (evolutionService.TryRelease(Combat.Time, out id))
                {
                    var unit = Combat.Roster.Get(id);
                    var catalog = Units.Find(unit.UnitId);
                    string name = catalog == null ? unit.UnitId : catalog.Name;
                    RecordSessionIssue("진화체 해방", name);
                    EvolutionNotice = "진화체 해방 · " + name;
                    EvolutionNoticeShownAt = Time.unscaledTime;
                }
            }
            catch (Exception error)
            {
                RecordSessionIssue("진화체 해방 실패", error.Message);
                Debug.LogException(error);
            }
        }

        public PersonalMissionGameplay PersonalMissions { get; private set; }
        private IOriginalMissionRandom missionRandom;

        // A native/replay provider may replace only the mission draw lane.
        // It must not borrow the combat BFh stream without source proof.
        public void ConfigurePersonalMissionRandom(IOriginalMissionRandom source)
        {
            if (PersonalMissions != null)
                throw new InvalidOperationException("개인 미션 배정 후 난수를 변경할 수 없습니다.");
            missionRandom = source ?? throw new ArgumentNullException(nameof(source));
        }

        public PersonalMissionGameplayState CapturePersonalMissionProgress()
        {
            if (PersonalMissions == null) throw new InvalidOperationException("게임 시작 후 저장하세요.");
            return PersonalMissions.CaptureState();
        }

        // The caller restores the SAME combat/roster/round snapshot first.
        public void RestorePersonalMissionProgress(PersonalMissionGameplayState state)
        {
            if (Combat == null || SharedEconomy == null)
                throw new InvalidOperationException("실제 전투 저장 상태를 먼저 복원하세요.");
            PersonalMissions = PersonalMissionGameplay.Restore(Combat.Game,
                SharedEconomy.Wallet, state, missionRandom);
        }
        public OriginalRandomWispResourceExchange RandomWispExchange { get; private set; }
        public OriginalHelperCowManaRecharge HelperCowRecharge { get; private set; }
        public OriginalHelperCowAutoAreaCasting HelperCowAutoArea { get; private set; }
        public Ordr323OriginalHelperCowActor Original323HelperCow { get; private set; }
        public Ordr323OriginalHelperNavigation Original323HelperNavigation { get; private set; }
        public Ordr323OriginalHelperManaPotion Original323ManaPotion { get; private set; }
        public Ordr323OriginalAlchemyCastService Original323AlchemyCasting { get; private set; }

        // The original SOLO CUo h08A unit is a noncombat support actor.
        // Create only after BeginSession succeeds, never at menu preview.
        private void AttachVerifiedSoloHelperCow()
        {
            if (originalHelperCowWorld != null) return; // explicit external real actor wins
            if (Original323HelperCow != null)
                throw new InvalidOperationException("원본 도움소가 이미 만들어졌습니다.");
            var actor = new Ordr323OriginalHelperCowActor(0);
            Original323HelperCow = actor;
            Original323HelperNavigation = new Ordr323OriginalHelperNavigation(Combat.Game, actor, OriginalNavigationSelection);
            ConfigureOriginalHelperCowWorld(new OriginalHelperCowManaWorld(
                () => Original323HelperCow, 0));
        }

        // Original CKX -> CKS(1) grants upgrades + A0UY on a user choice
        // at wave 21 or later. It must never be selected automatically.
        public bool ChooseOriginal323MaximumOutput()
        {
            return Original323HelperNavigation != null &&
                Original323HelperNavigation.TryChooseMaximumOutput();
        }

        public bool ChooseOriginal323Alchemy()
        {
            return Original323HelperNavigation != null &&
                Original323HelperNavigation.TryChooseAlchemy();
        }

        // The real world must expose independently verified target objects and
        // an exclusive rollback-capable inventory/mana/spawn transaction.
        // JASS name links are preview evidence, not authority to remove units.
        public void ConfigureOriginal323AlchemyCasting(
            IOrdr323AlchemyTransactionalWorld originalWorld)
        {
            if (Combat == null || Original323HelperCow == null ||
                OriginalAlchemyRefunds == null)
                throw new InvalidOperationException(
                    "원본 도움소·전투 월드·CJY 환급표가 먼저 필요합니다.");
            if (originalWorld == null) throw new ArgumentNullException(nameof(originalWorld));
            if (Original323AlchemyCasting != null)
                throw new InvalidOperationException("연금술 거래 월드가 이미 연결되었습니다.");
            Original323AlchemyCasting = new Ordr323OriginalAlchemyCastService(
                Combat.Game, Original323HelperCow, OriginalAlchemyRefunds,
                originalWorld, () => Combat.Time);
        }

        public ManualActionResult TryUseOriginal323Alchemy(long targetInstanceId)
        {
            return Original323AlchemyCasting == null ?
                ManualActionResult.Unavailable("연금술") :
                Original323AlchemyCasting.TryCast(targetInstanceId);
        }

        // This is intentionally not supplied by default: A0UY's SLK does
        // not prove the inherited AOsf mana cost, so a guessed free cast
        // would distort the actual gameplay economy.
        public void ConfigureOriginal323ManaPotionCasting(
            IOrdr323VerifiedManaPotionCastPort originalCastWorld)
        {
            if (Combat == null || Original323HelperCow == null)
                throw new InvalidOperationException("게임 시작과 원본 도움소 생성이 먼저 필요합니다.");
            if (Original323ManaPotion != null)
                throw new InvalidOperationException("마나 포션 시전 처리기가 이미 연결되었습니다.");
            Original323ManaPotion = new Ordr323OriginalHelperManaPotion(
                Combat.Game, Original323HelperCow, originalCastWorld);
        }

        public bool UseOriginal323ManaPotion()
        {
            return Original323ManaPotion != null && Original323ManaPotion.TryUse();
        }

        // A0BZ/A07T original range, mana and cooldown now come directly
        // from Units/AbilityData.slk. Damage/immunity and A04R's physical
        // reduction semantics must still come from an approved combat port.
        public void ConfigureOriginal323HelperAreaCombat(
            IOriginalHelperCowVerifiedAreaDamage originalDamage,
            IOriginalA04REffectEngine originalA04REffects = null)
        {
            if (Combat == null || Original323HelperCow == null)
                throw new InvalidOperationException(
                    "검증된 게임 시작과 원본 도움소 유닛 생성 후에 연결할 수 있습니다.");
            if (originalDamage == null)
                throw new ArgumentNullException(nameof(originalDamage));
            ConfigureOriginalHelperCowAreaWorld(new OriginalHelperCowLiveCombatWorld(
                Combat, () => Original323HelperCow,
                new Ordr323OriginalHelperCastDataSource(),
                originalDamage, 0, originalA04REffects));
        }

        private IOriginalHelperCowAreaSpellWorld originalHelperCowAreaWorld;

        // Requires a real h08A and source-verified cast range / readiness /
        // target eligibility / damage / stun. No fake spells are added here.
        public void ConfigureOriginalHelperCowAreaWorld(
            IOriginalHelperCowAreaSpellWorld verifiedWorld)
        {
            if (verifiedWorld == null) throw new ArgumentNullException(nameof(verifiedWorld));
            if (originalHelperCowAreaWorld != null)
                throw new InvalidOperationException("도움소 범위시전 월드 중복 연결");
            originalHelperCowAreaWorld = verifiedWorld;
            if (Combat != null)
                AttachOriginalHelperCowArea();
        }

        private void AttachOriginalHelperCowArea()
        {
            if (HelperCowAutoArea != null) return;
            HelperCowAutoArea = new OriginalHelperCowAutoAreaCasting(
                Combat.Game, () => Combat.Time, () => Combat.MobileEnemySnapshot(),
                originalHelperCowAreaWorld);
        }

        // End-to-end adapter for ONE real source-verified h08A actor.
        // Manual Wisp mana recharge and automatic area casts always read/write
        // the same physical mana state; there are no test-only fake actors.
        public void ConfigureVerifiedHelperCowGameplay(
            Func<IOriginalHelperCowCombatActor> actualHelperCow,
            IOriginalHelperCowVerifiedCastDataSource originalObjectValues,
            IOriginalHelperCowVerifiedAreaDamage originalHitRules,
            int ownerPlayerId = 0,
            IOriginalA04REffectEngine originalA04RMechanics = null)
        {
            if (Combat == null || SharedEconomy == null)
                throw new InvalidOperationException(
                    "도움소 실제 연결에는 전투 월드와 공유 경제 지갑이 필요합니다.");
            if (actualHelperCow == null || originalObjectValues == null ||
                originalHitRules == null)
                throw new ArgumentNullException("원본 도움소와 능력/피해 검증기가 필요합니다.");
            if (originalHelperCowAreaWorld != null || originalHelperCowWorld != null)
                throw new InvalidOperationException("이미 도움소 월드가 연결되어 있습니다.");

            // Construct and validate both connections before publishing either.
            var mana = new OriginalHelperCowManaWorld(
                () => actualHelperCow(), ownerPlayerId);
            var area = new OriginalHelperCowLiveCombatWorld(Combat,
                actualHelperCow, originalObjectValues, originalHitRules,
                ownerPlayerId, originalA04RMechanics);
            ConfigureOriginalHelperCowWorld(mana);
            ConfigureOriginalHelperCowAreaWorld(area);
        }

        private IOriginalHelperCowManaWorld originalHelperCowWorld;

        // The actual h08A's current/max mana is supplied by the game world.
        // This port can be configured before or after the combat world exists.
        public void ConfigureOriginalHelperCowWorld(IOriginalHelperCowManaWorld originalWorld)
        {
            if (originalWorld == null) throw new ArgumentNullException(nameof(originalWorld));
            if (originalHelperCowWorld != null)
                throw new InvalidOperationException("도움소 마나 월드 중복 연결");
            originalHelperCowWorld = originalWorld;
            if (Combat != null && SharedEconomy != null)
                AttachOriginalHelperCow();
        }

        private void AttachOriginalHelperCow()
        {
            if (HelperCowRecharge != null) return;
            HelperCowRecharge = new OriginalHelperCowManaRecharge(
                SharedEconomy.Wallet, Combat.Game, originalHelperCowWorld);
            ManualActions.ConfigureHelperCowRecharge(HelperCowRecharge);
        }

        // Optional source-compatible RNG. Default mobile RNG preserves the
        // original probabilities, not the exact Warcraft random sequence.
        private IOriginalWispRandom randomWispResourceRng;
        private Original323SharedBfiRandom originalSharedScriptRandom;
        private Original323MoneyGamblingService originalMoneyGambling;
        private Original323ScriptAttackProcRolls originalScriptAttackSkillRolls;
        private IOriginal323VerifiedAttackDummySpellWorld verifiedOriginalAttackDummySpells;

        // Strict map-owned custom random stream: gold / rare Wisp branch /
        // random Wisp resources / observed gambling events all share H6.
        // A native GetRandomInt seed and observed draw witness are REQUIRED.
        public void ConfigureOriginal323SharedScriptRandom(
            Original323SharedBfiRandom originalVerifiedStream)
        {
            if (Combat != null || randomWispResourceRng != null ||
                originalSharedScriptRandom != null)
                throw new InvalidOperationException(
                    "원본 공통 난수는 세션/위습 난수 설정 전에 한 번만 연결하세요.");
            originalSharedScriptRandom = originalVerifiedStream ??
                throw new ArgumentNullException(nameof(originalVerifiedStream));
            randomWispResourceRng = originalSharedScriptRandom.ForPurpose(
                Original323ScriptRandomPurpose.RandomWispResource);
        }

        public bool HasOriginal323SharedScriptRandom
        {
            get { return originalSharedScriptRandom != null; }
        }

        // These are source-backed adapters, NOT a generic gambling unlock.
        // Native shop inventory, currency cost and sold-unit cleanup remain
        // obligations of the verified atomic settlement port.
        public void ConfigureOriginal323MoneyGambling(
            IOriginal323MoneyGamblingPurchaseWitness originalPurchases,
            IOriginal323MoneyGamblingSettlement verifiedNativeSettlement)
        {
            if (Combat != null || originalMoneyGambling != null ||
                originalSharedScriptRandom == null)
                throw new InvalidOperationException(
                    "원본 공통 난수/구매 트랜잭션을 전투 시작 전에 연결하세요.");
            originalMoneyGambling = new Original323MoneyGamblingService(
                originalSharedScriptRandom, originalPurchases,
                verifiedNativeSettlement);
        }

        public bool TrySettleObservedMoneyGambling(string purchaseId,
            Original323MoneyGamblingTier tier,
            out Original323MoneyGamblingOutcome outcome)
        {
            outcome = null;
            return Combat != null &&
                Combat.Game.Phase == SessionPhase.Running &&
                originalMoneyGambling != null &&
                originalMoneyGambling.TrySettle(purchaseId, tier, out outcome);
        }

        // These three attacks explicitly use the map's own BFi(1,100)
        // sequence. Warcraft native crits MUST keep their native RNG.
        public void ConfigureOriginal323ScriptAttackProcRolls(
            IOriginal323ScriptAttackProcWitness verifiedAttackEvents)
        {
            if (Combat != null || originalScriptAttackSkillRolls != null ||
                originalSharedScriptRandom == null)
                throw new InvalidOperationException(
                    "원본 공통 난수와 전투 이벤트를 전투 시작 전 연결하세요.");
            originalScriptAttackSkillRolls = new Original323ScriptAttackProcRolls(
                originalSharedScriptRandom, verifiedAttackEvents);
        }

        // This execution port must recreate the original dummy spell and
        // resolve its validated Warcraft collision/damage/immunity in Unity.
        // Original orders are bound automatically on each eligible attack.
        // Source-backed object entries from AbilityData.slk and
        // UnitAbilities.slk; the external witness must still supply verified
        // Warcraft target/immunity/final-HP and native impact evidence.
        private Original323DeferredNativeDummySpellWorld observedDummyImpactWorld;

        // A separate adapter for actual projectiles and lightning impact
        // moments. Register BEFORE game runtime creation; confirmed impacts
        // may arrive after the original attacked target is already dead.
        // Live per-monster A04Q (magic damage-taken factor) reconciliation
        // for two original native dummy spells. The external source must
        // separately prove pre-A04Q damage, applicability and HP conversion.
        public void ConfigureOriginal323VerifiedNativeMagicDummyWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323NativeMagicImpactWitness verifiedMagicImpacts,
            IOriginal323NativeMagicHpConversion verifiedNativeHp)
        {
            ConfigureOriginal323VerifiedNativeMagicDummyWorld(verifiedCasts,
                verifiedMagicImpacts, verifiedNativeHp, null);
        }

        // Source-backed concurrent buffs may be present. The external
        // observer must establish their exact native impact-time effect
        // before any damage is applied; no arithmetic guesses are allowed.
        public void ConfigureOriginal323VerifiedNativeMagicDummyWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323NativeMagicImpactWitness verifiedMagicImpacts,
            IOriginal323NativeMagicHpConversion verifiedNativeHp,
            IOriginal323NativeMagicModifierWitness verifiedNativeModifiers)
        {
            ConfigureOriginal323VerifiedNativeMagicDummyWorld(
                verifiedCasts, verifiedMagicImpacts, verifiedNativeHp,
                verifiedNativeModifiers, null);
        }

        public void ConfigureOriginal323VerifiedNativeMagicDummyWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323NativeMagicImpactWitness verifiedMagicImpacts,
            IOriginal323NativeMagicHpConversion verifiedNativeHp,
            IOriginal323NativeMagicModifierWitness verifiedNativeModifiers,
            IOriginal323DummySpellTerminationWitness verifiedNativeEnding)
        {
            ConfigureOriginal323DeferredDummySpellWorld(verifiedCasts,
                new Original323VerifiedNativeMagicImpactAdapter(
                    verifiedMagicImpacts, verifiedNativeHp,
                    verifiedNativeModifiers), verifiedNativeEnding);
        }

        public void ConfigureOriginal323DeferredDummySpellWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323DummySpellImpactWitness verifiedImpacts)
        {
            ConfigureOriginal323DeferredDummySpellWorld(
                verifiedCasts, verifiedImpacts, null);
        }

        // The source's e0KD native lifetime witness must certify an actual
        // completion, despawn or rejected Warcraft order. This does not
        // invent a timeout or treat the victim's death as cast expiration.
        public void ConfigureOriginal323DeferredDummySpellWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323DummySpellImpactWitness verifiedImpacts,
            IOriginal323DummySpellTerminationWitness nativeTermination)
        {
            if (Combat != null || verifiedOriginalAttackDummySpells != null)
                throw new InvalidOperationException(
                    "원본 주문 시전/타격 공급자는 전투 시작 전에 한 번만 연결하세요.");
            var bridge = new Original323DeferredNativeDummySpellWorld(
                verifiedCasts, verifiedImpacts, nativeTermination);
            ConfigureOriginal323AttackedDummySpellWorld(bridge);
            observedDummyImpactWorld = bridge;
        }

        // Does NOT choose new target positions or approximate projectile
        // travel. The native impact observer must report this instant.
        public bool TryApplyObservedOriginalDummySpellImpact(
            string sourceCastId, out long observedHpDamage)
        {
            observedHpDamage = 0;
            return Combat != null &&
                observedDummyImpactWorld != null &&
                Combat.Game.Phase == SessionPhase.Running &&
                observedDummyImpactWorld.TryApplyObservedImpact(
                    Combat, sourceCastId, out observedHpDamage);
        }

        // Enel's original forked lightning (A086) can resolve with no
        // final HP when the native dummy despawns, the native order fails,
        // or the completed spell had no eligible damage targets. The
        // verified native terminal event is required to free the cast slot.
        public bool TryConfirmObservedOriginalDummyCastEnd(string sourceCastId)
        {
            return Combat != null &&
                observedDummyImpactWorld != null &&
                Combat.Game.Phase == SessionPhase.Running &&
                observedDummyImpactWorld.TryConfirmObservedNativeCastEnd(
                    Combat, sourceCastId);
        }

        public void ConfigureOriginal323AttackedDummyHitWorld(
            IOriginal323NativeDummySpellHitWitness nativeSpellHits)
        {
            ConfigureOriginal323AttackedDummySpellWorld(
                new Original323ObservedAttackDummySpellWorld(nativeSpellHits));
        }

        public void ConfigureOriginal323AttackedDummySpellWorld(
            IOriginal323VerifiedAttackDummySpellWorld verifiedNativeSpellWorld)
        {
            if (Combat != null || verifiedOriginalAttackDummySpells != null)
                throw new InvalidOperationException(
                    "원본 공격 주문 월드는 전투 생성 전에 한 번만 연결하세요.");
            verifiedOriginalAttackDummySpells = verifiedNativeSpellWorld ??
                throw new ArgumentNullException(nameof(verifiedNativeSpellWorld));
        }

        public bool TryRollObservedOriginalScriptAttack(string functionId,
            string attackerId, string targetId, long eventSequence,
            out bool triggered)
        {
            triggered = false;
            return Combat != null && Combat.Game.Phase == SessionPhase.Running &&
                originalScriptAttackSkillRolls != null &&
                originalScriptAttackSkillRolls.TryObserveAndRoll(
                    functionId, attackerId, targetId, eventSequence,
                    out triggered);
        }

        public void ConfigureRandomWispResourceRng(IOriginalWispRandom verifiedRandom)
        {
            if (Combat != null)
                throw new InvalidOperationException("위습 난수는 경제 월드 생성 전에 연결하세요.");
            randomWispResourceRng = verifiedRandom ??
                throw new ArgumentNullException(nameof(verifiedRandom));
        }
        public MobileWispService Wisps { get; private set; }
        private Original323WispDeferredAbilityRegistry deferredWispAbilities;
        // Original reward UNIT identities are playable as empty mobile shells.
        // A0YE native ability behavior and exact Warcraft seed are not faked.
        public bool UsesWispShellMode { get; private set; }
        public bool CanProcessAutomaticWisps => Wisps != null && Combat != null &&
            Combat.Game.Phase == SessionPhase.Running && runGate.CanAdvance;
        public ManualActionResult RequestAutomaticWispRetry()
        {
            if (!CanProcessAutomaticWisps)
                return new ManualActionResult(ManualActionState.Rejected, "게임 진행 중에 위습 지급을 재시도할 수 있습니다.");
            int count = Wisps.FlushAutomaticSafely(true);
            bool pending = false;
            foreach (string id in new[] { WispResourceIds.Uncommon, WispResourceIds.Special,
                WispResourceIds.Rare, WispResourceIds.Legendary })
                pending |= Wisps.AutomaticFailure(id) != null;
            return new ManualActionResult(pending ? ManualActionState.Rejected : ManualActionState.Succeeded, count + "개 지급 완료" +
                (pending ? " · 남은 위습의 보류 사유를 확인해 주세요." : ""));
        }
        public int DeferredWispA0yeCount =>
            deferredWispAbilities == null ? 0 : deferredWispAbilities.PendingCount;
        private IVerifiedChoiceWispMergeWorld choiceWispWorld;
        private VerifiedEconomyStartProfile verifiedStartingProfile;
        private IOriginalStartEvolutionWorld startEvolutionWorld;

        public void ConfigureOriginal323SoloStart(IOriginalStartEvolutionWorld originalEvolution)
        {
            startEvolutionWorld = originalEvolution ??
                throw new ArgumentNullException(nameof(originalEvolution));
            ConfigureVerifiedStartingEconomy(Original323SoloStartProfile.Create());
        }

        // Requires the source-derived roll catalog, a session RNG and a real
        // transactional world that maps rawcodes to the live Combat.Roster.
        // No automatic rewards run before all three are connected.
        public void ConfigureVerifiedWispWorld(IOriginalWispRandom originalRng,
            IWispRewardWorld world, IVerifiedChoiceWispMergeWorld choiceMerge)
        {
            if (originalSharedScriptRandom != null)
                throw new InvalidOperationException(
                    "원본 공통 난수가 연결되면 반드시 별도 워크래프트 유닛 풀 난수를 지정하세요.");
            ConfigureWispWorldInternal(originalRng, null, world, choiceMerge);
        }

        // Explicit two-lane source-parity route. Native selection is supplied
        // by its own original Warcraft-side adapter; no guessed native PRNG.
        public void ConfigureOriginal323SharedWispWorld(
            IOriginal323NativeUnitPoolRandom originalNativePool,
            IWispRewardWorld world, IVerifiedChoiceWispMergeWorld choiceMerge)
        {
            if (originalSharedScriptRandom == null)
                throw new InvalidOperationException(
                    "원본 공통 난수열을 먼저 연결하세요.");
            if (originalNativePool == null)
                throw new ArgumentNullException(nameof(originalNativePool));
            ConfigureWispWorldInternal(originalSharedScriptRandom.ForPurpose(
                Original323ScriptRandomPurpose.RareWispBranch),
                originalNativePool, world, choiceMerge);
        }

        private void ConfigureWispWorldInternal(IOriginalWispRandom scriptRng,
            IOriginal323NativeUnitPoolRandom nativePool,
            IWispRewardWorld world, IVerifiedChoiceWispMergeWorld choiceMerge)
        {
            if (SharedEconomy == null || Combat == null)
                throw new InvalidOperationException("공유 경제/전투 월드를 먼저 구성해야 합니다.");
            if (Wisps != null && !usesBuiltInAutomaticUncommonWisps)
                throw new InvalidOperationException("위습 거래 월드 중복 연결");
            if (Wisps != null && Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException("게임 시작 후 위습 월드를 교체할 수 없습니다.");
            if (world == null || choiceMerge == null)
                throw new ArgumentNullException(nameof(world), "위습 보상과 Merge 원본 월드 모두 필요");
            var source = originalWispPoolsJson != null ? originalWispPoolsJson :
                Resources.Load<TextAsset>("OrdrData/original-2323C-wisp-pools");
            if (source == null)
                throw new InvalidOperationException("원본 JASS 위습 풀 JSON을 찾을 수 없습니다.");
            var roller = nativePool == null
                ? OriginalWispRewardCatalog.FromOriginalJassJson(
                    source.text, scriptRng)
                : OriginalWispRewardCatalog.FromOriginalJassJson(
                    source.text, scriptRng, nativePool);
            Wisps?.Dispose();
            Wisps = new MobileWispService(SharedEconomy.Wallet, roller, world, true);
            usesBuiltInAutomaticUncommonWisps = false;
            UsesWispShellMode = false;
            deferredWispAbilities = null;
            choiceWispWorld = choiceMerge;
            // Existing balances are drained by Update after session start.
        }

        // A verified rare-Wisp path is enabled only when ALL original
        // dependencies exist. The default game keeps Rare pending.
        public void ConfigureOriginal323VerifiedRareWispRoster(
            IOriginal323NativeUnitPoolRandom nativePool,
            IOriginalWispSpawnPositionPort originalRarePosition,
            IOriginalWispBonusAbilityPort verifiedA0ye,
            IVerifiedChoiceWispMergeWorld choiceMerge)
        {
            if (Combat == null || SharedEconomy == null || MapBaseStats == null ||
                Units == null || originalSharedScriptRandom == null ||
                Combat.Game.Phase != SessionPhase.NotStarted ||
                (Wisps != null && !usesBuiltInAutomaticUncommonWisps))
                throw new InvalidOperationException(
                    "희귀위습은 게임 시작 전 원본 공통 BFi·유닛 추첨 월드를 연결해야 합니다.");
            if (nativePool==null || originalRarePosition==null ||
                verifiedA0ye==null || choiceMerge==null)
                throw new ArgumentNullException(nameof(nativePool));
            var source=originalWispPoolsJson!=null ? originalWispPoolsJson :
                Resources.Load<TextAsset>("OrdrData/original-2323C-wisp-pools");
            if (source==null)
                throw new InvalidOperationException("원본 위습 풀 데이터 없음");
            var automatic=new Original323AutomaticUncommonWisps(source.text,
                originalSharedScriptRandom.ForPurpose(
                    Original323ScriptRandomPurpose.RareWispBranch),
                nativePool,originalRarePosition,verifiedA0ye);
            var identityAsset = Resources.Load<TextAsset>(
                "OrdrData/original-2323C-wisp-character-identity-assignments");
            if (identityAsset == null)
                throw new InvalidOperationException("원본 위습 156종 ID 매핑 데이터가 없습니다.");
            var wispMap = Original323WispAssignedRosterMap.FromSourceJson(
                identityAsset.text, source.text, Units, MapBaseStats);
            var destination=new OriginalWispCombatRewardWorld(Combat,
                wispMap,automatic,automatic);
            Wisps?.Dispose();
            Wisps=new MobileWispService(SharedEconomy.Wallet,automatic,destination,true);
            usesBuiltInAutomaticUncommonWisps=false;
            UsesWispShellMode = false;
            deferredWispAbilities = null;
            choiceWispWorld=choiceMerge;
            // Existing balances are drained by Update after session start.
        }

        // Uses the exact same roster as CharacterCombatRuntime; unreviewed
        // rawcode mappings, spawn positions and missing A0YE block the grant.
        public void ConfigureVerifiedWispCombatRoster(IOriginalWispRandom originalRng,
            IVerifiedUnitRawcodeMap reviewedObjectMap,
            IOriginalWispSpawnPositionPort originalPositions,
            IOriginalWispBonusAbilityPort originalAbilities,
            IVerifiedChoiceWispMergeWorld choiceMerge)
        {
            if (Combat == null) throw new InvalidOperationException("전투 월드 필요");
            ConfigureVerifiedWispWorld(originalRng,
                new OriginalWispCombatRewardWorld(Combat, reviewedObjectMap,
                    originalPositions, originalAbilities), choiceMerge);
        }

        private readonly Original323GaimonDreamState originalGaimonDream =
            new Original323GaimonDreamState();
        private IOriginal323GaimonNamiBonusWitness originalGaimonYlSource;

        // Source CCP sets the per-player dream once when h0BG is committed.
        // It is never inferred from a Nami unit or a UI tap.
        public Original323GaimonDreamState GaimonDream => originalGaimonDream;

        public void ConfigureOriginalGaimonDreamNamiState(
            IOriginal323GaimonNamiBonusWitness originalYlWitness)
        {
            if (Combat == null || originalGaimonYlSource != null ||
                Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException(
                    "가이몬 꿈 원본 YL 상태는 게임 시작 전 한 번만 연결할 수 있습니다.");
            originalGaimonYlSource = originalYlWitness ??
                throw new ArgumentNullException(nameof(originalYlWitness));
        }

        // The pinned original Gaimon is h0BG; never substitute another unit.
        public bool HasOwnedOriginalGaimon
        {
            get
            {
                if (Combat == null || MapBaseStats == null) return false;
                foreach (var unit in Combat.MobileUnitSnapshot())
                {
                    var original = MapBaseStats.Find(unit.Unit.Id);
                    if (original != null && original.OriginalRawcode == "h0BG" &&
                        !Combat.IsStoryDeployed(unit.InstanceId))
                        return true;
                }
                return false;
            }
        }

        public Original323TreasureHunt OriginalTreasureHunt { get; private set; }
        public Original323TreasureAlarmJournal TreasureAlarms { get; private set; }
        public string TreasureAlarmNotice { get; private set; }
        public long TreasureAlarmNoticeSequence { get; private set; }
        public Original323PoneglyphSession Poneglyph { get; private set; }
        public Original323SharedMissionRewardWorld SharedMissionRewards { get; private set; }
        public Original323SharedObjectiveWorld SharedObjectives { get; private set; }
        private IOriginal323SharedObjectiveWitness originalSharedObjectiveWitness;
        private IOriginal323SharedMissionFollowUpWitness originalSharedMissionFollowUpWitness;

        // Native admiral buffs are applied by the verified original-world
        // adapter, never manufactured from a mobile mission reward marker.
        public void ConfigureOriginal323SharedMissionFollowUps(
            IOriginal323SharedMissionFollowUpWitness witness)
        {
            if (witness == null || originalSharedMissionFollowUpWitness != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "공유 미션 후속 사건 공급자는 시작 전 한 번만 연결해야 합니다.");
            originalSharedMissionFollowUpWitness = witness;
        }

        // A real runtime/replay must authorize the full Nightmare spawn plan
        // and independently certify the matching death. No default model
        // substitutes basic SLK HP for unknown final objective HP.
        public void ConfigureOriginal323SharedObjectiveWitness(
            IOriginal323SharedObjectiveWitness witness)
        {
            if (witness == null || originalSharedObjectiveWitness != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "공유 목표 증거 공급자는 게임 시작 전 한 번만 연결해야 합니다.");
            originalSharedObjectiveWitness = witness;
        }

        public Original323SharedObjectiveEncounter ObserveOriginal323SharedObjectiveSpawn(
            Original323SharedObjectiveSpawnPlan plan)
        {
            if (SharedObjectives == null)
                throw new InvalidOperationException("공유 목표 원본 사건 공급자 미연결");
            return SharedObjectives.ObserveSpawn(plan);
        }

        public bool ObserveOriginal323SharedObjectiveDefeat(
            string enemyId, string sourceDefeatEventId) =>
            SharedObjectives != null &&
            SharedObjectives.TrySettleDefeat(enemyId, sourceDefeatEventId);

        // The source witness certifies a buff already applied to the
        // actual native game state, not a reward inferred from boss death.
        public bool ObserveOriginal323AppliedAdmiralBuff(
            string enemyId, string sourceEventId) =>
            SharedObjectives != null &&
            originalSharedMissionFollowUpWitness != null &&
            SharedObjectives.TryObserveAppliedAdmiralBuff(
                enemyId, sourceEventId, originalSharedMissionFollowUpWitness);

        public string RequestSharedObjectiveDispatch(long instanceId, string enemyId)
        {
            if (Combat == null || SharedObjectives == null ||
                Combat.Game.Phase != SessionPhase.Running ||
                !SharedObjectives.IsLiveTarget(enemyId))
                return "실제 출현이 확인된 공유 목표가 없습니다.";
            if (MapBaseStats == null)
                return "원본 유닛 자료가 연결되지 않았습니다.";
            string catalogId = null;
            foreach (var owned in Combat.MobileUnitSnapshot())
                if (owned.InstanceId == instanceId && owned.Unit != null)
                {
                    catalogId = owned.Unit.Id;
                    break;
                }
            if (catalogId == null) return "보유한 유닛을 먼저 선택해 주세요.";
            var mapped = MapBaseStats.Find(catalogId);
            if (mapped == null || mapped.OriginalAbilityRawcodes == null)
                return "원본 객체 신원이 확인되지 않은 유닛입니다.";
            foreach (var ability in mapped.OriginalAbilityRawcodes)
                if (ability == "Aloc")
                    return "이동할 수 없는 원본 유닛입니다.";
            return Combat.TryDispatchToSharedObjective(instanceId, enemyId)
                ? "공유 목표 출전 · 유닛 #" + instanceId
                : "이미 다른 전장에 있거나 공유 목표 출전이 불가능합니다.";
        }

        public string RequestSharedObjectiveRecall(long instanceId) =>
            Combat != null && Combat.TryRecallFromSharedObjective(instanceId)
                ? "공유 목표에서 복귀했습니다."
                : "복귀할 공유 목표 출전 유닛이 없습니다.";
        private IOriginal323PoneglyphEventWitness originalPoneglyphWitness;
        private Original323MobileTreasureSession standaloneTreasure;
        private IOriginal323TreasureMapPrizeWitness treasureMapPrizeWitness;
        private IOriginal323MobileTreasureA05DCast originalMobileA05d;
        private IOriginal323GaimonCcqEventWitness originalGaimonCcqSource;
        public MobileTreasureGacha MobileTreasureGacha { get; private set; }
        public Original323GaimonRevealPort GaimonReveal { get; private set; }

        // Shared-mission completion, the three interpreter commands and
        // per-instance dynamic abilities require a real committed event witness.
        // The default standalone model does not invent completion receipts.
        public void ConfigureOriginal323PoneglyphEvents(
            IOriginal323PoneglyphEventWitness witness)
        {
            if (witness == null) throw new ArgumentNullException(nameof(witness));
            if (originalPoneglyphWitness != null ||
                (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted))
                throw new InvalidOperationException(
                    "공유 도전과제 원본 이벤트는 게임 시작 전 한 번만 연결하세요.");
            originalPoneglyphWitness = witness;
        }

        public bool ObserveOriginal323SharedMission(
            Original323SharedMissionKind kind, string sourceEventId)
        {
            // Objective deaths cannot skip the observed enemy's real HP,
            // its actual combat exit and its independent native death witness.
            // Only story deadline / treasure collection missions use this lane.
            if (Original323SharedObjectiveSpawnPlan.RequiresCombatDefeat(kind))
                return false;
            if (Poneglyph == null || originalPoneglyphWitness == null ||
                Combat == null || Combat.Game.Phase != SessionPhase.Running)
                return false;
            string evidence;
            int round = Combat.Game.CurrentWave;
            return originalPoneglyphWitness.TryConfirmSharedMission(
                    kind, sourceEventId, round, out evidence) &&
                SharedMissionRewards != null &&
                SharedMissionRewards.TrySettle(kind, sourceEventId, round, evidence);
        }

        public bool ObserveOriginal323InterpreterCommand(
            string originalCommand, string sourceEventId)
        {
            if (Poneglyph == null || originalPoneglyphWitness == null ||
                Combat == null || Combat.Game.Phase != SessionPhase.Running)
                return false;
            string evidence;
            return originalPoneglyphWitness.TryConfirmInterpreterCommand(
                    originalCommand, sourceEventId, out evidence) &&
                Original323BossRoundRules.TrustedRuntimeEvidence(evidence) &&
                Poneglyph.EnableVerifiedInterpreter(originalCommand,
                    sourceEventId, evidence);
        }

        private void OnPoneglyphUnitRemoved(long instanceId)
        {
            // A committed fusion, sale or direct combat removal destroys the
            // actual instance. Its dynamic ability overrides cannot leak into
            // the next actor, nor may its decoding channel remain active.
            Poneglyph?.ForgetUnit(instanceId);
        }

        private bool TryGetOwnedPoneglyphUnit(long instanceId, out UnitState unit)
        {
            unit = default(UnitState);
            if (Combat == null) return false;
            foreach (var entry in Combat.MobileUnitSnapshot())
                if (entry.InstanceId == instanceId)
                {
                    unit = Combat.Roster.Get(instanceId);
                    return true;
                }
            return false;
        }

        public bool ObserveOriginal323PoneglyphAbilityState(long instanceId,
            string abilityRawcode, bool present, string sourceEventId)
        {
            UnitState ownedUnit;
            if (Poneglyph == null || originalPoneglyphWitness == null ||
                Combat == null || Combat.Game.Phase != SessionPhase.Running ||
                !TryGetOwnedPoneglyphUnit(instanceId, out ownedUnit)) return false;
            string evidence;
            if (!originalPoneglyphWitness.TryConfirmUnitAbilityState(
                    instanceId, abilityRawcode, present, sourceEventId,
                    out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            Poneglyph.RecordVerifiedAbilityState(instanceId, abilityRawcode,
                present);
            return true;
        }

        public bool CanDecodePoneglyph(long instanceId, out string reason)
        {
            if (Poneglyph == null || Combat == null || MapBaseStats == null)
            {
                reason = "원본 해독 상태 미연결";
                return false;
            }
            UnitState unit;
            if (!TryGetOwnedPoneglyphUnit(instanceId, out unit) ||
                Combat.IsStoryDeployed(instanceId) ||
                Combat.IsSharedObjectiveDeployed(instanceId))
            {
                reason = "메인 전장의 보유 유닛 필요";
                return false;
            }
            OriginalMapCharacterBasics original =
                MapBaseStats.Find(unit.UnitId);
            if (original == null || string.IsNullOrEmpty(original.OriginalRawcode) ||
                original.OriginalAbilityRawcodes == null)
            {
                reason = "원본 유닛 기술 매핑 미확인";
                return false;
            }
            return Poneglyph.IsEligible(instanceId,
                original.OriginalAbilityRawcodes, out reason);
        }

        public string RequestStartPoneglyph(long instanceId)
        {
            if (Combat == null || Poneglyph == null ||
                Combat.Game.Phase != SessionPhase.Running ||
                IsUnitPreparation || Combat.Game.CurrentWave < 1)
                return "진행 중인 전투 라운드에서만 해독할 수 있습니다.";
            string reason;
            if (!CanDecodePoneglyph(instanceId, out reason)) return reason;
            UnitState unit;
            if (!TryGetOwnedPoneglyphUnit(instanceId, out unit))
                return "해독 유닛이 존재하지 않습니다.";
            var original = MapBaseStats.Find(unit.UnitId);
            if (!Poneglyph.TryStart(instanceId, original.OriginalAbilityRawcodes,
                Combat.Time, out reason)) return reason;
            try
            {
                Combat.SetOriginalPoneglyphChanneling(instanceId, true);
            }
            catch (Exception ex)
            {
                Poneglyph.Interrupt("해독 시작 불가 · " + ex.Message);
                return Poneglyph.LastMessage;
            }
            return reason;
        }

        public string RequestStopPoneglyph()
        {
            if (Poneglyph == null || !Poneglyph.ActiveUnitId.HasValue)
                return "진행 중인 해독이 없습니다.";
            long unitId = Poneglyph.ActiveUnitId.Value;
            Poneglyph.Interrupt("해독 중단 · 재시도 가능");
            Combat?.SetOriginalPoneglyphChanneling(unitId, false);
            return Poneglyph.LastMessage;
        }

        // A verified original CCQ/R event is observed by the native
        // adapter, NOT by a new mobile manual button. Never pay from reveal.
        public void ConfigureOriginal323GaimonReveal(
            IOriginal323GaimonCcqEventWitness sourceEvent)
        {
            if (Combat == null || GaimonReveal != null ||
                originalGaimonCcqSource != null ||
                Combat.Game.Phase != SessionPhase.NotStarted || sourceEvent==null)
                throw new InvalidOperationException(
                    "CCQ 원본 사건은 세션 시작 전에 한 번만 연결합니다.");
            originalGaimonCcqSource=sourceEvent;
            if(OriginalTreasureHunt!=null)
                GaimonReveal=new Original323GaimonRevealPort(
                    OriginalTreasureHunt,sourceEvent);
        }

        public Original323GaimonRevealResult ObserveOriginal323GaimonCcq(
            string originalNativeEventId)
        {
            if (Combat == null || OriginalTreasureHunt == null ||
                GaimonReveal == null ||
                Combat.Game.Phase != SessionPhase.Running ||
                Combat.Game.CurrentWave < 1 ||
                IsUnitPreparation)
                return new Original323GaimonRevealResult(false,0,0,
                    "원본 CCQ 시전/전투 라운드 관측 대기");
            var result=GaimonReveal.ObserveOriginalCcq(
                originalNativeEventId,Combat.Game.CurrentWave,
                HasOwnedOriginalGaimon,GaimonDream.Applied);
            // CCQ uses BeN(..., false): location reveal and a 297-second
            // BJI reminder, but never an A05D loot search or second payment.
            if (result.Authorized && TreasureAlarms != null)
                TreasureAlarms.RegisterAuthorizedCcq(
                    originalNativeEventId,
                    OriginalTreasureHunt.LastAuthorizedCastEvidence,Combat.Time);
            return result;
        }

        public void ConfigureOriginal323MobileA05DCast(
            IOriginal323MobileTreasureA05DCast originalCast)
        {
            if(Combat==null || Combat.Game.Phase!=SessionPhase.NotStarted ||
                originalMobileA05d!=null || originalCast==null)
                throw new InvalidOperationException(
                    "원본 A05D 마나·쿨타임·시전 조건 공급자는 시작 전에 연결합니다.");
            originalMobileA05d=originalCast;
        }

        // Only register a source-calibrated gacha before gameplay begins.
        // The default APK has no invented selection/cost/probability provider.
        public void ConfigureMobileTreasureGacha(
            IOriginal323MobileTreasureGachaPolicy policy)
        {
            if (Combat == null || OriginalTreasureHunt == null ||
                MobileTreasureGacha != null ||
                Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException(
                    "원본 보물 월드를 먼저 연결하고 전투 시작 전에 가챠를 설정하세요.");
            if (policy == null || !policy.IsCalibrationApproved ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    policy.CalibrationEvidence))
                throw new InvalidOperationException(
                    "원작 보물 수급·탐색 기회를 검증한 공급자가 필요합니다.");
            MobileTreasureGacha=new MobileTreasureGacha(OriginalTreasureHunt,policy);
        }

        // The external original source, if configured, always takes precedence.
        // Otherwise use the explicit local A05D mobile interaction adapter.
        private void ConfigureStandaloneTreasureIfAbsent()
        {
            if(OriginalTreasureHunt!=null || Combat==null ||
                SharedEconomy==null || sessionScriptRandom==null) return;
            standaloneTreasure=new Original323MobileTreasureSession(
                sessionScriptRandom,
                () => GaimonDream.Applied,
                () => GaimonDream.NamiBonusWhenApplied,
                () => {
                    bool yl; string proof;
                    return originalGaimonYlSource != null &&
                        originalGaimonYlSource.TryReadOriginalYlFlag(out yl,out proof) &&
                        Original323BossRoundRules.TrustedRuntimeEvidence(proof) && yl;
                },originalMobileA05d,originalGaimonCcqSource);
            ConfigureOriginal323TreasureHunt(
                new Original323TreasureWalletWorld(
                    SharedEconomy.Wallet,standaloneTreasure),sessionScriptRandom);
            MobileTreasureGacha=new MobileTreasureGacha(
                OriginalTreasureHunt,standaloneTreasure);
            if(GaimonReveal==null && originalGaimonCcqSource!=null)
                GaimonReveal=new Original323GaimonRevealPort(
                    OriginalTreasureHunt,standaloneTreasure);
        }

        private void SpawnStandaloneTreasureForRound(int round)
        {
            if(standaloneTreasure!=null && OriginalTreasureHunt!=null &&
                round>=1 && round<=65)
                standaloneTreasure.SpawnThroughRound(OriginalTreasureHunt,round);
        }

        // CRW bounty treasure maps are granted by witnessed chest outcomes,
        // not a new button or an imaginary free-use inventory item.
        public void ConfigureOriginal323TreasureMapPrizes(
            IOriginal323TreasureMapPrizeWitness witness)
        {
            if(Combat==null || Combat.Game.Phase!=SessionPhase.NotStarted ||
                treasureMapPrizeWitness!=null || witness==null)
                throw new InvalidOperationException(
                    "보물지도 원본 사건 공급자는 세션 시작 전에 한 번만 연결합니다.");
            treasureMapPrizeWitness=witness;
        }

        public bool ObserveOriginal323TreasureMapPrize(string sourceEventId)
        {
            if(Combat==null || Combat.Game.Phase!=SessionPhase.Running ||
                standaloneTreasure==null || OriginalTreasureHunt==null ||
                treasureMapPrizeWitness==null ||
                string.IsNullOrWhiteSpace(sourceEventId)) return false;
            int remaining; string proof;
            if(!treasureMapPrizeWitness.TryConfirmMapPrize(sourceEventId,
                Combat.Game.CurrentWave,out remaining,out proof))
                return false;
            // Fifth CRW map is a distinct One Piece/native ability reward,
            // not a fifth BeM(3). Do not award fabricated native effects.
            if(remaining==0)
            {
                RecordSessionIssue("보물지도", "마지막 지도: 원피스 원본 능력 보상 연결 필요");
                return false;
            }
            return standaloneTreasure.RegisterObservedTreasureMapPrize(
                OriginalTreasureHunt,sourceEventId,Combat.Game.CurrentWave,
                remaining,proof);
        }

        public MobileTreasureGachaResult RequestMobileTreasureGacha()
        {
            if (Combat == null || IsUnitPreparation ||
                Combat.Game.Phase != SessionPhase.Running ||
                Combat.Game.CurrentWave < 1 ||
                MobileTreasureGacha == null)
                return new MobileTreasureGachaResult(
                    MobileTreasureOutcome.Unavailable,0,0,
                    "원작 보물 가챠 확률·탐색 기회 검증 대기");
            SpawnStandaloneTreasureForRound(Combat.Game.CurrentWave);
            var recovered = MobileTreasureGacha.TryReconcilePending();
            if(recovered!=null)
            {
                // Never register a second BJI alarm or spend another ticket.
                TrySettleTreasureHunterAfterCollection(
                    recovered.Collected>0,recovered.Collected);
                return recovered;
            }
            var result = MobileTreasureGacha.TrySearch(Combat.Game.CurrentWave,
                GaimonDream != null && GaimonDream.Applied,
                GaimonDream != null && GaimonDream.Applied && GaimonDream.NamiBonusWhenApplied,
                GaimonReveal == null ? null :
                GaimonReveal.ActiveRevealedChests(Combat.Game.CurrentWave));
            // Misses also trigger BJI, but no alarm is forged for an
            // unapproved ticket or an unidentifiable native caster.
            TryScheduleWitnessedTreasureAlarm(
                result.Outcome == MobileTreasureOutcome.Miss ||
                result.Outcome == MobileTreasureOutcome.Found ||
                result.Outcome == MobileTreasureOutcome.Partial);
            TrySettleTreasureHunterAfterCollection(result.Collected > 0, result.Collected);
            return result;
        }

        // Source-verified BFj coordinates, actual BeN spell cast and atomic
        // native reward settlement are required. Nothing is enabled by default.
        public void ConfigureOriginal323TreasureHunt(
            IOriginal323TreasureNativeWorld liveNativeWorld,
            IOriginalWispRandom verifiedSharedScriptRandom)
        {
            if (liveNativeWorld == null || verifiedSharedScriptRandom == null)
                throw new ArgumentNullException(nameof(liveNativeWorld));
            if (Combat == null || OriginalTreasureHunt != null ||
                Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException(
                    "원본 보물 탐색은 전투 시작 전 한 번만 연결할 수 있습니다.");
            OriginalTreasureHunt = new Original323TreasureHunt(
                liveNativeWorld, verifiedSharedScriptRandom);
            TreasureAlarms = new Original323TreasureAlarmJournal();
            TreasureAlarmNotice = null;
            TreasureAlarmNoticeSequence = 0;
        }

        // A source witness invokes this ONLY for a real native BeM spawn.
        public void ObserveOriginalTreasureSpawn(string chestId,int round,
            double x,double y,string originalBfjProof)
        {
            if (OriginalTreasureHunt == null)
                throw new InvalidOperationException("원본 보물 월드 미연결");
            OriginalTreasureHunt.RegisterObservedSpawn(
                chestId,round,x,y,originalBfjProof);
        }

        // Atomic source-observed BeM/BFj packet at the live wave; never
        // generate a synthetic chest or pretend a client tap is an event.
        public int ObserveOriginalTreasureSpawnBatch(int spawnRound,
            IReadOnlyList<Original323ObservedTreasureSpawn> packet)
        {
            if (Combat == null || OriginalTreasureHunt == null ||
                Combat.Game.Phase != SessionPhase.Running ||
                Combat.Game.CurrentWave < 1)
                throw new InvalidOperationException(
                    "실제 전투 라운드 및 원본 보물 출현 증빙 필요");
            return OriginalTreasureHunt.RegisterObservedSpawnBatch(
                spawnRound,Combat.Game.CurrentWave,packet);
        }

        // A source-confirmed CJm / CCQ call, never a free UI treasure draw.
        public Original323TreasureSearchResult RequestObservedTreasureSearch(
            double castX,double castY,bool gaimon,bool gaimonAndNami,
            bool extraQuantityRoll,bool berryDisabled,bool revealOnly)
        {
            if (OriginalTreasureHunt == null || Combat == null)
                return new Original323TreasureSearchResult(0,0,false,
                    "원본 보물 탐색 월드가 연결되지 않았습니다.");
            var search = OriginalTreasureHunt.SearchObserved(
                Combat.Game.CurrentWave,castX,castY,gaimon,
                gaimonAndNami,extraQuantityRoll,berryDisabled,revealOnly);
            // CCQ has its own 297-second reveal alarm. This journal covers
            // CJm collection casts only; never pass reveal-only events here.
            TryScheduleWitnessedTreasureAlarm(
                !revealOnly && search.WasAuthorized);
            TrySettleTreasureHunterAfterCollection(
                !revealOnly && search.WasAuthorized, search.Collected);
            return search;
        }

        private void TryScheduleWitnessedTreasureAlarm(bool authorized)
        {
            if (!authorized || OriginalTreasureHunt == null ||
                TreasureAlarms == null || Combat == null ||
                SharedEconomy == null) return;
            bool recordGuidanceCaster;
            if (!OriginalTreasureHunt.TryIdentifyLastAuthorizedCaster(
                    out recordGuidanceCaster)) return;
            TreasureAlarms.RegisterAuthorizedCast(
                OriginalTreasureHunt.LastAuthorizedCastEvidence,
                Combat.Time, recordGuidanceCaster, SharedEconomy.Wallet);
        }

        private void AdvanceWitnessedTreasureAlarms()
        {
            if (TreasureAlarms == null || Combat == null) return;
            var due = TreasureAlarms.DrainDue(Combat.Time);
            if (due.Count == 0) return;
            // No cooldown, damage, treasure spawn or resource mutation.
            bool anySearch = false;
            bool anyReveal = false;
            foreach (var alarm in due)
            {
                if (alarm.RevealOnly) anyReveal = true;
                else anySearch = true;
            }
            TreasureAlarmNotice = "[알람타이머] 보물찾기" +
                (due.Count > 1 ? " · " + due.Count + "건" : "") +
                (anyReveal ? " · 위치 공개" : "") +
                (anySearch
                    ? " · 다시 탐색 가능 여부는 실제 시전 조건에 따릅니다."
                    : " · 위치 공개 알림은 보상을 지급하지 않습니다.");
            TreasureAlarmNoticeSequence++;
        }

        // Both UI-originated search and source-observed casts share the SAME
        // original YI=16 settlement, never a mere reveal or preview.
        private void TrySettleTreasureHunterAfterCollection(
            bool sourceConfirmed, int collected)
        {
            if (!sourceConfirmed || collected <= 0 ||
                Combat == null || OriginalTreasureHunt == null ||
                OriginalTreasureHunt.CollectedCount < 16 ||
                SharedMissionRewards == null ||
                string.IsNullOrWhiteSpace(
                    OriginalTreasureHunt.LastCollectedCastEvidence))
                return;
            SharedMissionRewards.TrySettle(
                Original323SharedMissionKind.TreasureHunter,
                "treasure:found-16", Combat.Game.CurrentWave,
                OriginalTreasureHunt.LastCollectedCastEvidence);
        }

        public void ConfigureOriginal323TreasureWalletWorld(
            IOriginal323TreasureLiveCastWitness sourceCastWitness,
            string verifiedBerryResourceId = null,
            string berryResourceEvidence = null)
        {
            if (originalSharedScriptRandom == null)
                throw new InvalidOperationException(
                    "원본 BeI 보물 보상의 공통 BFi 난수 시드·순서 검증이 필요합니다.");
            if (SharedEconomy == null || Combat == null)
                throw new InvalidOperationException("실제 전투·공유 경제 상태가 필요합니다.");
            var walletWorld = new Original323TreasureWalletWorld(
                SharedEconomy.Wallet, sourceCastWitness,
                verifiedBerryResourceId, berryResourceEvidence);
            ConfigureOriginal323TreasureHunt(walletWorld,
                originalSharedScriptRandom.ForPurpose(
                    Original323ScriptRandomPurpose.OtherOriginalScriptCall));
        }

        public long WispBalance(string id) => SharedEconomy == null ? 0 :
            SharedEconomy.Wallet.Balance(id);

        public IReadOnlyList<VerifiedChoiceWispTarget> ChoiceWispTargets =>
            choiceWispWorld == null ? new VerifiedChoiceWispTarget[0] :
                choiceWispWorld.AvailableTargets() ?? new VerifiedChoiceWispTarget[0];

        public ManualActionResult RequestChoiceWispMerge(string originalUnitRawcode)
        {
            if (Wisps == null || choiceWispWorld == null)
                return ManualActionResult.Unavailable("선택위습 Merge");
            try
            {
                if (!Wisps.MergeChoice(originalUnitRawcode, choiceWispWorld))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "위습이 부족하거나 원본 Merge 조건을 충족하지 못했습니다.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                return new ManualActionResult(ManualActionState.Rejected,
                    "Merge 거래 실패: 원본 유닛/재화 변경을 롤백했습니다.");
            }
            // A committed Merge stays successful if its mission observer fails.
            try { PersonalMissions?.RecordChoiceWispUse(); }
            catch (Exception error) { Debug.LogException(error); }
            return new ManualActionResult(ManualActionState.Succeeded, "선택위습 Merge 완료");
        }

        public void ConfigureVerifiedStartingEconomy(VerifiedEconomyStartProfile profile)
        {
            if (profile == null || !profile.IsSourceVerified)
                throw new ArgumentException("원본 검증된 시작 경제 프로필이 필요합니다.");
            if (Combat != null && Combat.Game.Phase != SessionPhase.NotStarted)
                throw new InvalidOperationException("게임 시작 후 시작 재화 설정을 바꿀 수 없습니다.");
            verifiedStartingProfile = profile;
        }

        private IOriginalPlacementRules placementRules;
        private IOriginalBossFocusRules bossFocusRules;
        private IOriginalAutoSkillRules autoSkillRules;
        private IOriginalStoryDispatchPort storyDispatchRules;
        private IOriginalVerifiedUnitAttackProvider verifiedUnitAttackSource;
        private IOriginalFusionPort originalFusionPort;
        private OriginalJassFusionPort originalJassFusionPort;
        private OriginalJassReadOnlyFusionPort originalJassReadOnlyFusionPort;
        private OtherworldMysticFusionPreviewPort mysticFusionPort;
        private IOriginalEconomyPort originalEconomyPort;

        // The original reward is a physical e0IX Wisp, not a synthetic wallet
        // item. Connect only a session-wide transaction with rollback and
        // authoritative ownership/LY state (never a TEST_ONLY fake world).
        public void ConfigureOriginalCommonSaleWorld(
            IOriginalCommonSaleTransactionalWorld originalWorld)
        {
            if (originalWorld == null) throw new ArgumentNullException(nameof(originalWorld));
            if (Combat == null) throw new InvalidOperationException(
                "원본 판매 대상의 실제 월드를 먼저 구성해야 합니다.");
            if (MapBaseStats == null) throw new InvalidOperationException(
                "원본 흔함 9종의 A09G 객체 근거를 먼저 로드해야 합니다.");
            ManualActions.ConfigureOriginalCommonSale(
                new OriginalCommonSaleCoordinator(originalWorld, MapBaseStats));
        }

        // All unit grades use the same automatic attack pipeline once their
        // original attack power, interval, range, rawcode and initial delay
        // have been independently verified.
        public void ConfigureVerifiedAllUnitAttacks(
            IOriginalVerifiedUnitAttackProvider originalSource)
        {
            verifiedUnitAttackSource = originalSource ??
                throw new ArgumentNullException(nameof(originalSource));
            if (Combat != null)
            {
                Combat.ConfigureVerifiedUnitAttackProvider(originalSource);
                Combat.BindExistingVerifiedAttacks();
            }
        }

        public void ConfigureMobileAutomation(IOriginalPlacementRules originalPlacement,
            IOriginalBossFocusRules originalBossFocus,
            IOriginalAutoSkillRules originalQwrAutoUse)
        {
            placementRules = originalPlacement;
            bossFocusRules = originalBossFocus;
            autoSkillRules = originalQwrAutoUse;
            // Config may arrive before the combat world exists.
            // Only remember the policies here; world-linked services are built
            // once in ConfigureRuntime (or updated after it has been created).
            if (Combat != null)
                Combat.MobileAutomation.Configure(placementRules, bossFocusRules,
                    autoSkillRules ?? new MobileUnlimitedAutoSkillTargetRules());
        }

        // Call only with an original-rule-backed movement transaction provider.
        // No fallback dispatch is installed for unverified practice sessions.
        public Original323StoryManualDispatch ConfigureVerifiedStoryBuildingDispatch(
            IOriginal323StoryUnitMovement sourceMovement)
        {
            if (Combat == null || sourceMovement == null)
                throw new InvalidOperationException(
                    "원본 스토리 이동 공급자와 전투 월드가 필요합니다.");
            if (storyDispatchRules != null)
                throw new InvalidOperationException("스토리 파견 공급자가 이미 연결됐습니다.");
            var dispatch = new Original323StoryManualDispatch(Combat, sourceMovement);
            ConfigureManualMobileSystems(dispatch, originalFusionPort, originalEconomyPort);
            return dispatch;
        }

        public void ConfigureManualMobileSystems(IOriginalStoryDispatchPort originalStory,
            IOriginalFusionPort originalFusion, IOriginalEconomyPort originalEconomy)
        {
            storyDispatchRules = originalStory;
            originalFusionPort = originalFusion;
            originalEconomyPort = originalEconomy;
            ManualActions.Configure(originalStory, originalFusion ?? FusionAssistance,
                originalEconomy);
            if (Combat != null) Combat.ConfigureStoryDeployment(originalStory);
        }

        // Use the original JASS recipes when the game has authoritative
        // object mapping, special-condition checks and atomic economy/spawn.
        // The Bi8/Bi9 label map alone is intentionally insufficient.
        public void ConfigureOriginalJassCrafting(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassSpecialConditionEvaluator conditionEvaluator,
            IOriginalJassFusionResourceReader verifiedResourceReader,
            IOriginalJassFusionAtomicCommit originalTransaction,
            IOriginalJassFusionCastPolicy originalCastPolicy)
        {
            if (Combat == null || OriginalFusionPlanner == null)
                throw new InvalidOperationException(
                    "원본 JASS 등록식 로딩과 전투 월드 구성이 먼저 필요합니다.");
            if (verifiedObjectMap == null || conditionEvaluator == null ||
                verifiedResourceReader == null || originalTransaction == null ||
                originalCastPolicy == null)
                throw new ArgumentNullException(nameof(verifiedObjectMap),
                    "원작 객체 매핑·추가조건·재화·원자적 조합 서비스가 모두 필요합니다.");
            OriginalFusionPlanner.SetVerifiedObjectMap(verifiedObjectMap);
            OriginalFusionPlanner.SetSpecialRules(conditionEvaluator);
            originalJassFusionPort = new OriginalJassFusionPort(OriginalFusionPlanner,
                () => Combat.MobileUnitSnapshot(), verifiedResourceReader,
                originalTransaction, originalCastPolicy);
            RebuildVerifiedFusionCatalog();
        }

        // Authoritative world integration: after object data, all special
        // conditions and CHu/CHx effects have been implemented, the same
        // transaction lock must cover final inspection, payment and spawning.
        // Do not provide a mock world in production to bypass unverified rules.
        public void ConfigureOriginalJassCraftingWorld(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassSpecialConditionEvaluator conditionEvaluator,
            IOriginalJassFusionResourceReader verifiedResourceReader,
            IOriginalJassFusionTransactionalWorld originalWorld,
            IOriginalJassFusionCastPolicy originalCastPolicy)
        {
            if (originalWorld == null) throw new ArgumentNullException(nameof(originalWorld));
            if (OriginalFusionPlanner == null)
                throw new InvalidOperationException("원본 조합 플래너가 아직 구성되지 않았습니다.");
            ConfigureOriginalJassCrafting(verifiedObjectMap, conditionEvaluator,
                verifiedResourceReader,
                new OriginalJassFusionAtomicCoordinator(OriginalFusionPlanner, originalWorld),
                originalCastPolicy);
        }

        // Ordered CWR execution path. The supplied session factory MUST use the
        // real original-object inventory, JASS condition readers and an atomic
        // rollback capable world; an absent handler rejects its recipe.
        public void ConfigureOriginalJassCraftingOrderedWorld(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassSpecialConditionEvaluator conditionEvaluator,
            IOriginalJassFusionResourceReader verifiedResourceReader,
            IOriginalJassFusionOrderedSessionFactory originalSessionFactory,
            IOriginalJassFusionCastPolicy originalCastPolicy)
        {
            if (originalSessionFactory == null)
                throw new ArgumentNullException(nameof(originalSessionFactory));
            ConfigureOriginalJassCraftingWorld(verifiedObjectMap, conditionEvaluator,
                verifiedResourceReader,
                new OriginalJassFusionOrderedWorldAdapter(originalSessionFactory),
                originalCastPolicy);
        }

        // The minimal real-world bridge implements ordered UNIT/GOLD/WOOD
        // mutations, verifies the staged resulting roster and shares one
        // rollback-capable lock. It cannot execute unverified JASS special tags.
        // Production worlds must still supply exact original rawcodes, native
        // target rules and original CHu/CHx placement in the atomic session.
        public void ConfigureOriginalJassSimpleCraftingWorld(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassFusionResourceReader verifiedResourceReader,
            IOriginalJassSimpleFusionTransactionalWorld world,
            IOriginalJassFusionCastPolicy castPolicy)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (OriginalJassFusions == null)
                throw new InvalidOperationException("원본 CWR 등록식을 먼저 로드하세요.");
            ConfigureOriginalJassCraftingOrderedWorld(verifiedObjectMap,
                new OriginalJassNoSpecialConditions(),
                verifiedResourceReader,
                new OriginalJassSimpleOrderedSessionFactory(OriginalJassFusions, world),
                castPolicy);
        }

        // Binds the REAL in-game combat roster and shared wallet to the
        // simple CWR transaction. Native source-object reads, original spell
        // targeting and an EXCLUSIVE reversible CHu/CHx mutation factory remain
        // mandatory; no preview-only object names are accepted as authorization.
        public void ConfigureOriginalJassLiveCombatCraftingWorld(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassLiveObjectReader actualOriginalObjects,
            IOriginalJassFusionCastPolicy originalCastPolicy,
            IOriginalJassLiveEffectsFactory reversibleNativeEffects)
        {
            if (Combat == null || SharedEconomy == null)
                throw new InvalidOperationException(
                    "전투 월드와 공유 재화 세션 구성이 먼저 필요합니다.");
            var live = new OriginalJassLiveCombatFusionWorld(
                Combat, SharedEconomy, actualOriginalObjects,
                originalCastPolicy, reversibleNativeEffects);
            live.FusionCommitted += OnCommittedOriginalFusion;
            live.PostCommitObserverFailed += Debug.LogException;
            ConfigureOriginalJassSimpleCraftingWorld(
                verifiedObjectMap, live, live, live);
        }

        // The shared Wallet now executes and reverses the ACTUAL gold/wood
        // debit. A rollback-capable source-verified native unit world remains
        // mandatory for original CHu/CHx, unit instance identities and timers.
        public void ConfigureOriginalJassSharedWalletCraftingWorld(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassLiveObjectReader actualOriginalObjects,
            IOriginalJassFusionCastPolicy originalCastPolicy,
            IOriginalJassReversibleUnitFusionWorld verifiedNativeUnits)
        {
            if (verifiedNativeUnits == null)
                throw new ArgumentNullException(nameof(verifiedNativeUnits));
            ConfigureOriginalJassLiveCombatCraftingWorld(
                verifiedObjectMap, actualOriginalObjects, originalCastPolicy,
                new OriginalJassSharedWalletFusionEffects(verifiedNativeUnits));
        }

        // Complete live mobile composition path. This DOES replace actual
        // combat instances after an independently verified source CHu/CHx
        // placement and post-effect witness authorizes the specific recipe.
        // It never treats the read-only W3X name reconciliation as permission.
        public void ConfigureOriginalJassCombatUnitCrafting(
            IVerifiedUnitRawcodeMap verifiedObjectMap,
            IOriginalJassVerifiedFusionSwapRules verifiedOriginalNativeRules,
            IOriginalJassFusionCastPolicy verifiedCastTarget)
        {
            if (verifiedOriginalNativeRules == null)
                throw new ArgumentNullException(nameof(verifiedOriginalNativeRules));
            ConfigureOriginalJassSharedWalletCraftingWorld(
                verifiedObjectMap, verifiedOriginalNativeRules, verifiedCastTarget,
                new OriginalJassCombatUnitFusionWorld(verifiedOriginalNativeRules));
        }

        // 이세계 신비 도감 UI 연결. 공개 도감만으로는 실제 소모를 절대 실행하지 않는다.
        public void ConfigureOtherworldMysticFusionPreview(
            OtherworldMysticFusionCatalog mysticCatalog)
        {
            if (Combat == null)
                throw new InvalidOperationException("신비 조합 화면을 연결할 전투 월드가 없습니다.");
            if (mysticCatalog == null) throw new ArgumentNullException(nameof(mysticCatalog));
            mysticFusionPort = new OtherworldMysticFusionPreviewPort(
                mysticCatalog, id => Combat.IsLockedFromRosterTransactions(id), null,
                resource => SharedEconomy.Wallet.Balance(resource));
            RebuildVerifiedFusionCatalog();
        }

        private void RebuildVerifiedFusionCatalog()
        {
            // Connecting original JASS execution must not hide the Mystic
            // recipe page or the previously loaded regular recipes.
            var sources = new List<IOriginalFusionPort>();
            if (FusionAssistance != null) sources.Add(FusionAssistance);
            // Preview evidence is never promoted to a consumption source.
            // A separately verified original transaction explicitly REPLACES
            // the read-only JASS port without duplicating recipe IDs.
            if (originalJassFusionPort != null)
                sources.Add(originalJassFusionPort);
            else if (commonSourceFusion != null)
                sources.Add(commonSourceFusion);
            else if (originalJassReadOnlyFusionPort != null)
                sources.Add(originalJassReadOnlyFusionPort);
            if (mysticFusionPort != null) sources.Add(mysticFusionPort);
            originalFusionPort = sources.Count == 0 ? null :
                sources.Count == 1 ? sources[0] :
                (IOriginalFusionPort)new CombinedOriginalFusionPort(sources.ToArray());
            ManualActions.Configure(storyDispatchRules,
                originalFusionPort, originalEconomyPort);
        }

        // 원본 아이템·rawcode·시전·CHu/CHx 후속 처리까지 구현한 실제
        // 독점/롤백 거래 월드를 확보한 뒤에만 활성화한다. 테스트 모의 월드는 금지.
        public void ConfigureVerifiedOtherworldMysticFusionWorld(
            OtherworldMysticFusionCatalog mysticCatalog,
            IOriginalOtherworldMysticTransactionalWorld originalWorld)
        {
            if (originalWorld == null) throw new ArgumentNullException(nameof(originalWorld));
            ConfigureOtherworldMysticFusionPreview(mysticCatalog);
            mysticFusionPort.AttachVerifiedCommit(
                new OtherworldMysticFusionAtomicCoordinator(mysticCatalog, originalWorld));
        }

        // 원본 조합 엔진이 실제 재료/재화 트랜잭션을 구현했을 때만 연결.
        // 웹 도감 재료 미리보기만으로 유닛을 소비하지 않는다.
        public void ConfigureVerifiedFusionCommit(IVerifiedOriginalFusionCommit originalCommit)
        {
            if (FusionAssistance == null)
                throw new InvalidOperationException("조합 미리보기 카탈로그를 먼저 로드해야 합니다.");
            FusionAssistance.AttachOriginalCommit(originalCommit);
        }

        // 전투 UI/로그가 스킬·피해·상태 적용 이벤트를 구독할 수 있다.
        public event Action<CharacterCombatEvent> OnCombatEvent;

        private void Awake()
        {
            // Unity 씬에 별도의 UI 프리팹이 없어도 터치 운용 메뉴를 사용할 수 있게 한다.
            if (enableMobileTouchPanel && GetComponent<OrdrMobileTouchPanel>() == null)
                gameObject.AddComponent<OrdrMobileTouchPanel>();
            if (enableUnitWorldMarkers && GetComponent<OrdrUnitWorldPresenter>() == null)
                gameObject.AddComponent<OrdrUnitWorldPresenter>();
            // 동봉된 TextAsset이 없으면 Resources 폴더에서 읽는다.
            var unitsData = unitCatalogJson != null ? unitCatalogJson :
                Resources.Load<TextAsset>("OrdrData/units");
            var skillsData = characterSkillsJson != null ? characterSkillsJson :
                Resources.Load<TextAsset>("OrdrData/character-skill-profiles");
            if (unitsData == null || skillsData == null)
            {
                Debug.LogError("ORDR 데이터 미포함: Assets/Resources/OrdrData 내 JSON을 동기화하거나 Inspector에 TextAsset을 지정하세요.");
                enabled = false;
                return;
            }
            try
            {
                Units = CharacterUnitCatalog.FromJson(unitsData.text);
                Skills = CharacterSkillCatalog.FromJson(skillsData.text);
                var evidenceData = originalStatEvidenceJson != null ? originalStatEvidenceJson :
                    Resources.Load<TextAsset>("OrdrData/original-stat-evidence");
                if (evidenceData != null)
                    StatEvidence = OriginalUnitStatEvidenceCatalog.FromJson(evidenceData.text, Units);
                var mapBaseData = originalMapBaseStatsJson != null ? originalMapBaseStatsJson :
                    Resources.Load<TextAsset>("OrdrData/original-2323C-map-base-stats");
                if (mapBaseData != null)
                {
                    MapBaseStats = OriginalMapBaseStatCatalog.FromJson(mapBaseData.text, Units);
                    OriginalUncommonAbilities =
                        new Original323UncommonAbilityCatalog(MapBaseStats);
                    var nativePassives = originalNativePassivesJson != null ? originalNativePassivesJson :
                        Resources.Load<TextAsset>("OrdrData/original-2323C-native-pure-passives");
                    if (nativePassives != null)
                        OriginalNativePassives = Original323NativePassiveCatalog.FromJson(
                            nativePassives.text, MapBaseStats);
                    OriginalSpecialAuras = new Original323SpecialAuraCatalog(MapBaseStats);
                    var auraData = originalAuraAuditJson != null ? originalAuraAuditJson :
                        Resources.Load<TextAsset>("OrdrData/original-2323C-all-unit-aura-audit");
                    if (auraData != null)
                    {
                        // One source-audited original file now supplies BOTH
                        // enemy armor and AOae speed/slow passive definitions.
                        // The composite removes the three older pinned six-
                        // skill duplicates rather than applying them twice.
                        OriginalArmorAuras = Original323ArmorAuraCatalog.FromJson(
                            auraData.text, MapBaseStats, OriginalSpecialAuras);
                        OriginalNativeSpeedAuras =
                            Original323NativeSpeedAuraCatalog.FromJson(
                                auraData.text, MapBaseStats);
                        // Native ACac includes Chopper Guard 7% and Horn
                        // 25% as distinct source-owned command auras.
                        // The existing S-25 pinned command aura is deduped
                        // by rawcode + buff + original level.
                        OriginalNativeCommandAuras =
                            Original323NativeCommandAuraCatalog.FromJson(
                                auraData.text, MapBaseStats);
                        OriginalCombinedAuras = new Original323CompositeAuraCatalog(
                            MapBaseStats, OriginalArmorAuras, OriginalNativeSpeedAuras,
                            OriginalNativeCommandAuras);
                        OriginalMapObjectAuras = Original323MapObjectAuraCatalog.FromJson(
                            auraData.text, MapBaseStats);
                    }
                }
                var fusionData = verifiedFusionRecipesJson != null ? verifiedFusionRecipesJson :
                    Resources.Load<TextAsset>("OrdrData/verified-mobile-fusion-recipes");
                if (fusionData != null)
                    VerifiedFusionRecipes = VerifiedFusionCatalog.FromJson(fusionData.text, Units);
                var mysticData = otherworldMysticFusionRecipesJson != null ?
                    otherworldMysticFusionRecipesJson :
                    Resources.Load<TextAsset>("OrdrData/otherworld-2323C-mystic-web-recipes");
                if (mysticData != null)
                {
                    OtherworldMysticFusions = OtherworldMysticFusionCatalog.FromJson(
                        mysticData.text, Units);
                    if (OtherworldMysticFusions.Count != 13)
                        throw new FormatException("악몽 이세계 신비 조합 데이터는 13종이어야 합니다.");
                }
                else Debug.LogWarning(
                    "악몽 이세계 신비 조합 JSON 누락: 조합 탭에 13종 재료 안내가 표시되지 않습니다.");
                // 압축된 원본 등록식은 개발 자료. 순서 보존 추출 JSON이 있을 때만 로딩한다.
                var originalJassData = originalJassFusionRegistryJson != null ?
                    originalJassFusionRegistryJson :
                    Resources.Load<TextAsset>("OrdrData/original-jass-fusion-registry");
                if (originalJassData != null)
                    OriginalJassFusions = OriginalJassFusionCatalog.FromJson(originalJassData.text);
                var previewObjectsData = originalFusionObjectReconciliationJson != null ?
                    originalFusionObjectReconciliationJson :
                    Resources.Load<TextAsset>("OrdrData/original-fusion-object-reconciliation");
                if (OriginalJassFusions != null && previewObjectsData != null)
                    OriginalFusionObjectPreview = OriginalFusionObjectPreviewCatalog.FromJson(
                        previewObjectsData.text, OriginalJassFusions, Units);
                var alchemyData = originalHelperAlchemyRecipesJson != null ?
                    originalHelperAlchemyRecipesJson :
                    Resources.Load<TextAsset>("OrdrData/original-2323C-helper-alchemy-recipes");
                if (alchemyData != null)
                    OriginalAlchemyRefunds = Ordr323OriginalAlchemyRefundCatalog.FromJson(
                        alchemyData.text);
                var labelsData = originalUnitRawcodeLabelsJson != null ?
                    originalUnitRawcodeLabelsJson :
                    Resources.Load<TextAsset>("OrdrData/original-unit-rawcode-label-links");
                if (labelsData != null)
                    OriginalUnitRawcodes = OriginalUnitRawcodeLabelCatalog.FromJson(
                        labelsData.text, Units, OriginalJassFusions);
                // Optional: no verified monster dataset ships yet. Never
                // substitute playable units' HP or an invented wave schedule.
                var monsterData = verifiedMonsterWavesJson != null ?
                    verifiedMonsterWavesJson :
                    Resources.Load<TextAsset>("OrdrData/original-2323C-verified-monster-waves");
                if (monsterData != null)
                    VerifiedMonsterWaves = Original323VerifiedMonsterWaveCatalog
                        .FromJson(monsterData.text);
                // Original JASS rawcodes/counts are identity evidence only.
                // Do not use its object-base HP/armor as verified live stats.
                var originalSpawnData = originalMonsterWaveScriptJson != null ?
                    originalMonsterWaveScriptJson :
                    Resources.Load<TextAsset>("OrdrData/original-2323C-monster-wave-script-evidence");
                if (originalSpawnData != null)
                    OriginalMonsterSpawnIdentities =
                        Original323MonsterWaveIdentityCatalog.FromJson(originalSpawnData.text);
                // Base object ability IDs come from UnitAbilities.slk, never
                // directly from final live immunity or the current A04Q level.
                var monsterAbilitiesData = originalMonsterObjectAbilitiesJson != null ?
                    originalMonsterObjectAbilitiesJson :
                    Resources.Load<TextAsset>(
                        "OrdrData/original-2323C-monster-object-abilities");
                if (monsterAbilitiesData != null)
                {
                    OriginalMonsterObjectAbilities =
                        Original323MonsterObjectAbilityCatalog.FromJson(
                            monsterAbilitiesData.text);
                    NightmareMonsterA04QSpawns =
                        new Original323NightmareA04QSpawnState();
                }
                // Original map object identities are checked before additions.
                // An extension identity does not grant gameplay combat rules.
                int pinnedSourceCatalogCount = Units.Count;
                var wikiExtensionAsset = Resources.Load<TextAsset>(
                    "OrdrData/wiki-unit-catalog-extension");
                if (wikiExtensionAsset == null)
                    throw new InvalidOperationException("위키 유닛 확장 데이터가 APK에 없습니다.");
                Units.ApplyWikiExtension(wikiExtensionAsset.text);
                UnitDefinitions = UnitImplementationRegistry.Combine(Units, Skills,
                    OriginalUnitRawcodes);
                // Source map stats remain the pinned 277 original entries.
                // Additional forms/support identities have no invented stats.
                if (Units.Count != 288 || Skills.CharacterCount != 200 ||
                    UnitDefinitions.Count != Units.Count ||
                    (MapBaseStats != null && MapBaseStats.Count != pinnedSourceCatalogCount))
                    throw new FormatException("2.323[C] 캐릭터 데이터 등록 수가 기대값과 다릅니다.");
                // This is already pinned to the actual CUo/CUn/CJ source.
                // Do not require an external installer for verified start
                // gold, random Wisps or the non-combat e0HP evolution actor.
                // The e0HP actor itself is registered once when the real
                // combat roster is supplied; loading never grants currency.
                if (verifiedStartingProfile == null)
                    ConfigureVerifiedStartingEconomy(Original323SoloStartProfile.Create());
                Debug.Log("ORDR 전체 유닛 정의 " + UnitDefinitions.Count +
                    "종 / 상세 스킬 프로필 " + UnitDefinitions.DetailedProfiles +
                    "종 / 스킬 " + UnitDefinitions.CollectedSkills +
                    "개 / 패시브 " + UnitDefinitions.CollectedPassives +
                    "개 / JASS 이름-능력 연동 " + UnitDefinitions.LinkedJassLabels +
                    "종 / 원본 공격 스탯 전체 확정 " +
                    UnitDefinitions.VerifiedBasicAttacks + "종 / 맵 기본 능력치 ID 연결 " +
                    (MapBaseStats != null ? MapBaseStats.LinkedCount : 0) + "종. " +
                    "데이터 등록은 전투 구현 완료가 아닙니다.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                enabled = false;
            }
        }

        // 원본 2.323[C] 맵에서 추출한 상수를 사용하는 전투 계산기 연동.
        // 각 캐릭터·적의 수치와 미확정 공식에 대해서는 검증된 공급자가 필요.
        public void ConfigureOriginal323DamageRuntime(
            IOrdr323VerifiedHitSource verifiedHits,
            IOrdr323VerifiedModifierRules verifiedModifiers,
            IOrdr323VerifiedDamageQuantization quantization,
            ICombatProcRandom random)
        {
            if (Units == null || Skills == null)
                throw new InvalidOperationException("도감 JSON 로딩이 완료되지 않았습니다.");
            if (Combat != null)
                throw new InvalidOperationException("이미 전투 런타임이 구성됐습니다.");
            IOrdr323VerifiedHitSource appliedHits = verifiedHits;
            // This source is known to return UNMODIFIED native object dice.
            // A custom hit provider may already include criticals, so it is
            // never silently wrapped and multiplied a second time.
            bool nativePassives = verifiedHits is OriginalMapCommonNativeAttackBinding &&
                OriginalNativePassives != null;
            var externallyHandled = new List<string>();
            if (originalUncommonEffectWorld != null && OriginalUncommonAbilities != null &&
                originalUncommonEffectWorld.SupportsCompleteEffect(
                    OriginalUncommonAbilities.FindProc("U-03", "A07P:bash")))
                externallyHandled.Add("A07P"); // existing complete Brook handler wins
            if (verifiedUncommonCritRules != null)
            {
                if (OriginalUncommonAbilities == null)
                    throw new InvalidOperationException("원본 육식 크리티컬 카탈로그가 없습니다.");
                appliedHits = new Original323UncommonCriticalHitSource(
                    verifiedHits, OriginalUncommonAbilities,
                    verifiedUncommonCritRules, random);
                externallyHandled.Add("A07I"); // existing pre-hit crit handler wins
            }
            ConfigureRuntimeWithWorld(Ordr323DamageWorldBinding.Create(
                Units, Skills, appliedHits, verifiedModifiers, quantization, random,
                nativePassives ? OriginalNativePassives : null, externallyHandled));
            UsesOriginalNativePassives = nativePassives;
            HasSourceVerifiedDamageRuntime = true;
        }

        // Opt-in only: a map object row is not itself evidence that JASS leaves
        // native attacks intact. A verified path, target defenses and HP
        // quantization must be supplied before common attacks may cause damage.
        public void ConfigureOriginalCommonNativeDamageWhenVerified(
            IOriginalCommonNativeAttackVerification verifiedNativePath,
            IOrdr323VerifiedModifierRules verifiedModifiers,
            IOrdr323VerifiedDamageQuantization verifiedQuantization,
            ICombatProcRandom diceRandom,
            IOriginal323NativeAttackBuffRules verifiedAttackBuffRules = null)
        {
            if (Combat != null)
                throw new InvalidOperationException("이미 전투 런타임이 구성됐습니다.");
            if (MapBaseStats == null)
                throw new InvalidOperationException("원본 맵 흔함 공격 자료를 먼저 로드해야 합니다.");
            if (verifiedNativePath == null || verifiedQuantization == null ||
                diceRandom == null)
                throw new ArgumentNullException(nameof(verifiedNativePath),
                    "원본 평타 경로·HP 정수화·난수 근거가 모두 필요합니다.");
            var native = new OriginalMapCommonNativeAttackBinding(
                MapBaseStats, verifiedNativePath, diceRandom, verifiedAttackBuffRules);
            ConfigureVerifiedAllUnitAttacks(native);
            ConfigureOriginal323DamageRuntime(native, verifiedModifiers,
                verifiedQuantization, diceRandom);
        }

        // 다른 별도 검증 계산기를 사용하던 기존 호출자와 호환.
        public void ConfigureRuntime(IOriginalDamageResolver verifiedBasicFormula,
            IVerifiedCharacterSkillDamageResolver verifiedSkillFormula,
            ICombatProcRandom random)
        {
            if (Units == null || Skills == null)
                throw new InvalidOperationException("도감 JSON 로딩이 완료되지 않았습니다.");
            if (Combat != null)
                throw new InvalidOperationException("이미 전투 런타임이 구성됐습니다.");
            ConfigureRuntimeWithWorld(new CharacterCombatRuntime(Units, Skills,
                verifiedBasicFormula, verifiedSkillFormula, random));
        }

        private void ConfigureRuntimeWithWorld(CharacterCombatRuntime world)
        {
            if (Units == null || Skills == null || world == null)
                throw new InvalidOperationException("전투 데이터와 원본 계산기가 필요합니다.");
            if (Combat != null)
                throw new InvalidOperationException("이미 전투 런타임이 구성됐습니다.");
            if (SharedEconomy != null)
                throw new InvalidOperationException("이미 공유 경제 세션이 있습니다.");
            SharedEconomy = new SharedEconomySession(new EconomyWallet(), world.Roster);
            Combat = world;
            PlayerBuildingUpgrades = new Original323PlayerUpgradeSession(
                SharedEconomy.Wallet, Combat.Game, () => Combat.Time,
                verifiedUpgradeSteps, verifiedUpgradeWorld,
                verifiedUpgradeSteps == null && verifiedUpgradeWorld == null
                    ? sourceModelDamage?.PlayerUpgrades : null);
            if (sourceModelDamage != null)
            {
                PlayerBuildingResearch = new Original323PlayerResearchSession(
                    SharedEconomy.Wallet, Combat.Game,
                    rawcode => rawcode != "Rhfc" ||
                        Combat.HasVerifiedNativeMovementForResearch);
                sourceModelDamage.ConfigurePlayerResearch(PlayerBuildingResearch);
            }
            if (Combat.UsesSourceModelBasicAttacks)
                Combat.ConfigureSourceModelGiftWallet(SharedEconomy.Wallet);
            VerifiedWaveDirector.VerifiedMonsterDefeated += OnVerifiedMonsterDefeated;
            // Wave/HUD counters already record ordinary escapes. Avoid
            // flooding the bounded diagnostic error log with every mob exit.
            // The source-pinned e0HP is a non-combat session actor. A caller
            // can provide a richer original evolution port before creating
            // the combat runtime; otherwise use the authoritative roster.
            if (startEvolutionWorld == null)
                ConfigureOriginal323SoloStart(
                    new Original323RosterStartEvolutionWorld(world.Roster));
            ManualActions.CommonSaleSucceeded += OnCommittedCommonSale;
            ManualActions.PostCommitObserverFailed += Debug.LogException;
            if (verifiedNativeCadence != null)
                Combat.ConfigureVerifiedOriginal323NativeAttackCadence(
                    verifiedNativeCadence);
            if (verifiedNativeMovement != null)
                Combat.ConfigureVerifiedOriginal323NativeEnemyMovement(
                    verifiedNativeMovement);
            if (specialRareNativeImpactWitness != null)
                Combat.ConfigureOriginal323SpecialRareSkillImpacts(
                    MapBaseStats, specialRareNativeImpactWitness);
            if (specialRareNativeSequenceWitness != null)
                SpecialRareNativeSequence =
                    new Original323SpecialRareNativeSequenceWorld(
                        Combat, MapBaseStats, specialRareNativeSequenceWitness,
                        specialRareNativeImpactWitness);
            if (originalScriptAttackSkillRolls != null &&
                verifiedOriginalAttackDummySpells != null)
                Combat.ConfigureOriginal323ScriptAttackEffectWorld(
                    originalScriptAttackSkillRolls,
                    verifiedOriginalAttackDummySpells);
            if (originalMonsterGoldWitness != null)
                originalMonsterGold = new Original323MonsterGoldService(
                    VerifiedWaveDirector, world.Game, SharedEconomy.Wallet, 0,
                    originalMonsterGoldWitness);
            if (sourceModelDamage == null && OriginalCombinedAuras != null)
                Combat.ConfigureOriginalAuras(OriginalCombinedAuras, specialAuraTargets);
            else if (sourceModelDamage == null && OriginalArmorAuras != null)
                Combat.ConfigureOriginalAuras(OriginalArmorAuras, specialAuraTargets);
            else if (sourceModelDamage == null && OriginalSpecialAuras != null)
                Combat.ConfigureOriginalSpecialAuras(OriginalSpecialAuras, specialAuraTargets);
            if (sourceModelDamage == null && OriginalUncommonAbilities != null)
                Combat.ConfigureOriginalUncommonAbilities(
                    OriginalUncommonAbilities, originalUncommonEffectWorld);
            IOriginalWispRandom sharedMobileWispRandom =
                randomWispResourceRng ?? new MobileRollbackWispRandom();
            sessionScriptRandom = sharedMobileWispRandom;
            sessionWispPoolRandom = new MobileRollbackWispRandom();
            sessionGraduatedRarePoolRandom = new MobileRollbackWispRandom();
            sessionTierPoolRandom = new MobileRollbackWispRandom();
            if (sourceModelDamage != null && originalEconomyPort == null)
            {
                PracticeMoneyGamblingDesk = new SourceModelMoneyGamblingDesk(
                    SharedEconomy.Wallet, Combat.Game, sharedMobileWispRandom,
                    () => Combat.Time, () => runGate.CanAdvance);
                PracticeMoneyGamblingDesk.PostCommitObserverFailed += Debug.LogException;
                PracticeMoneyGambling = PracticeMoneyGamblingDesk.Regular;
                originalEconomyPort = PracticeMoneyGamblingDesk;
            }
            RandomWispExchange = new OriginalRandomWispResourceExchange(
                SharedEconomy.Wallet, world.Game, sharedMobileWispRandom);
            ManualActions.ConfigureRandomWispResourceExchange(RandomWispExchange);
            // BIQ/BIR/BIS/BIU/BIY/CRy are fully specified in the original
            // JASS. Use the same per-session random stream as resource Wisp
            // actions; a different approved original world may override this
            // before the game starts. A rare pirate ship is now a separate
            // source-object unit; it never rerolls into an ordinary common.
            if (MapBaseStats != null && Units != null)
            {
                var originalDrawWorld = new Original323SourceCommonWispWorld(
                    Combat, Units, MapBaseStats, sharedMobileWispRandom);
                commonWispRecruitment = new Original323CommonWispRecruitmentService(
                    Combat, SharedEconomy, MapBaseStats, originalDrawWorld);
                commonWispAvailability = originalDrawWorld;
                commonWispUsesBuiltInSource = true;
            }
            if (MapBaseStats != null)
            {
                UnitDecompositions = new Original323UnitDecompositionService(
                    Combat, SharedEconomy, MapBaseStats,
                    originalSharedScriptRandom == null ? sharedMobileWispRandom :
                        originalSharedScriptRandom.ForPurpose(Original323ScriptRandomPurpose.OtherOriginalScriptCall));
                ManualActions.ConfigureUnitDecomposition(UnitDecompositions);
                LowerGradeSales = new Original323LowerGradeSaleService(
                    Combat, SharedEconomy, MapBaseStats,
                    originalSharedScriptRandom == null ? sharedMobileWispRandom :
                        originalSharedScriptRandom.ForPurpose(Original323ScriptRandomPurpose.OtherOriginalScriptCall));
                if (!ManualActions.HasOriginalCommonSaleService)
                    ManualActions.ConfigureOriginalCommonSale(LowerGradeSales);
            }
            Combat.ConfigureOtamaWoodWallet(SharedEconomy.Wallet);
            // One shared interface for Otama's previously implemented
            // one-time reward and Vegapunk's JASS-priced satellite choices.
            // External verified dedicated-action providers remain replaceable.
            if (!DedicatedSkills.IsConnected)
                DedicatedSkills.Configure(new Original323SourceDedicatedSkillPort(
                    Combat, SharedEconomy.Wallet));
            Combat.ConfigureVerifiedUnitAttackProvider(verifiedUnitAttackSource);
            if (MapBaseStats != null)
                Combat.ConfigureOriginalMapCommonAttackPreview(MapBaseStats);
            Combat.MobileAutomation.Configure(placementRules, bossFocusRules,
                autoSkillRules ?? new MobileUnlimitedAutoSkillTargetRules());
            Combat.ConfigureStoryDeployment(storyDispatchRules);
            if (OriginalJassFusions != null && OriginalUnitRawcodes != null)
                OriginalFusionPlanner = new OriginalJassFusionPlanner(
                    OriginalJassFusions, OriginalUnitRawcodes,
                    id => Combat.IsLockedFromRosterTransactions(id), null, Units);
            if (VerifiedFusionRecipes != null)
                FusionAssistance = new AssistedFusionService(VerifiedFusionRecipes,
                    () => Combat.MobileUnitSnapshot(),
                    id => Combat.IsLockedFromRosterTransactions(id));
            // Display the full 265 source-registered recipes and their unit
            // deficits even when an irreversible original transaction is NOT
            // connected. The preview adapter is always non-executable.
            if (OriginalJassFusions != null && OriginalFusionObjectPreview != null)
                originalJassReadOnlyFusionPort = new OriginalJassReadOnlyFusionPort(
                    OriginalJassFusions, OriginalFusionObjectPreview,
                    id => Combat.IsLockedFromRosterTransactions(id));
            if (originalJassReadOnlyFusionPort != null && MapBaseStats != null &&
                OriginalUnitRawcodes != null)
            {
                commonSourceFusion = new Original323CommonFusionPort(Combat, SharedEconomy,
                    Units, MapBaseStats, OriginalJassFusions, OriginalUnitRawcodes,
                    originalJassReadOnlyFusionPort);
                commonSourceFusion.FusionCommitted += OnCommittedOriginalFusion;
                commonSourceFusion.ObserverFailed += Debug.LogException;
            }
            // Automatic Wisp rewards must NOT depend on executable fusion
            // recipes. All 156 source pool entries have separate original
            // W3X identity assignments.
            if (Wisps == null && MapBaseStats != null && Units != null)
            {
                var pool = originalWispPoolsJson != null ? originalWispPoolsJson :
                    Resources.Load<TextAsset>("OrdrData/original-2323C-wisp-pools");
                var identities = Resources.Load<TextAsset>(
                    "OrdrData/original-2323C-wisp-character-identity-assignments");
                if (pool == null || identities == null)
                    Debug.LogError("원본 위습 보상표/156종 매핑이 없어 자동 지급을 중단합니다.");
                else
                {
                    var map = Original323WispAssignedRosterMap.FromSourceJson(
                        identities.text, pool.text, Units, MapBaseStats);
                    if (originalSharedScriptRandom == null)
                    {
                        // Explicit result-only mobile mode. The script lane
                        // shares resource/common rolls; Warcraft native pool
                        // selection has a separate uniform stream. A0YE is
                        // recorded as pending on an empty character shell.
                        var roller = new Original323MobileWispOutcomeRoller(
                            pool.text, sessionScriptRandom,
                            new Original323MobileNativePoolRandom(
                                sessionWispPoolRandom));
                        deferredWispAbilities =
                            new Original323WispDeferredAbilityRegistry(Combat);
                        Wisps = new MobileWispService(SharedEconomy.Wallet, roller,
                            new OriginalWispCombatRewardWorld(Combat, map,
                                roller, roller, deferredWispAbilities), true);
                        UsesWispShellMode = true;
                    }
                    else
                    {
                        // Source-observed H6 mode keeps Rare pending until
                        // the original native pool and A0YE ports are supplied.
                        var roller = new Original323AutomaticUncommonWisps(
                            pool.text, new MobileUniformWispRandom());
                        Wisps = new MobileWispService(SharedEconomy.Wallet, roller,
                            new OriginalWispCombatRewardWorld(Combat, map,
                                roller, roller), true);
                    }
                    if (PracticeMoneyGamblingDesk != null)
                    {
                        // The graduated CJF pool is different from e019:
                        // 43 rare units including h05X and 7 specials.
                        if (deferredWispAbilities == null)
                            deferredWispAbilities = new Original323WispDeferredAbilityRegistry(Combat);
                        var shopBonus = (IOriginalWispDeferredBonusPort)deferredWispAbilities;
                        PracticeMoneyGamblingDesk.ConfigureRareReward(
                            new Original323GraduatedRareShopWorld(
                                Combat, Units, MapBaseStats, map,
                                sessionScriptRandom, sessionGraduatedRarePoolRandom,
                                shopBonus),
                            () => StoryProgression != null &&
                                StoryProgression.HasResearch("R01N"));
                        if (OriginalJassFusions != null)
                        {
                            PracticeGamblingNavigation = new SourceModelGamblingNavigation(
                                SharedEconomy.Wallet, OriginalNavigationSelection, () => runGate.CanAdvance,
                                () => (PracticeUnitGambling != null && PracticeUnitGambling.HasPendingDraw) ||
                                    (PracticeTierUnitGambling != null && PracticeTierUnitGambling.HasPendingDraw) ||
                                    (PracticeUnitReroll != null && PracticeUnitReroll.HasPendingDraw),
                                // CJS supplies Compu directly in the paid, witnessed middle-tier
                                // wallet transaction; no external award witness is required.
                                () => PracticeCompuExchange != null &&
                                    PracticeTierUnitGambling != null);
                            PracticeGamblingNavigation.PostCommitObserverFailed += Debug.LogException;
                            var gamblingRoutes = new Original323CwrAcquisitionRoutes(OriginalJassFusions, Units, MapBaseStats);
                            PracticeCompuExchange = new SourceModelCompuExchange(
                                SharedEconomy.Wallet, Combat.Game, sessionScriptRandom,
                                () => runGate.CanAdvance,
                                () => PracticeGamblingNavigation.Navigation,
                                new Original323CompuUnitWorld(
                                    Combat, Units, MapBaseStats, gamblingRoutes));
                            PracticeItemExcavation = new SourceModelItemExcavation(
                                SharedEconomy.Wallet, Combat.Game, sessionScriptRandom,
                                () => runGate.CanAdvance);
                            var gamblingRoster = new Original323UnitGamblingWorld(Combat, Units, MapBaseStats,
                                map, gamblingRoutes, deferredWispAbilities);
                            PracticeMaterialSupport = new SourceModelMaterialSupport(
                                SharedEconomy.Wallet, Combat.Game, sessionScriptRandom,
                                gamblingRoster,
                                () => runGate.CanAdvance);
                            PracticeMaterialSupport.PostCommitObserverFailed += Debug.LogException;
                            PracticeMoneyGamblingDesk.ConfigureMaterialSupport(PracticeMaterialSupport);
                            PracticeTierUnitGambling = new SourceModelUnitGambling(SharedEconomy.Wallet, Combat.Game,
                                sessionScriptRandom, sessionTierPoolRandom, gamblingRoster, () => runGate.CanAdvance,
                                () => PracticeGamblingNavigation.Navigation,
                                (receipt, tier, won) => PersonalMissions?.RecordGamble(receipt, tier, won));
                            PracticeTierUnitGambling.PostCommitObserverFailed += Debug.LogException;
                            PracticeMoneyGamblingDesk.ConfigureUnitTiers(PracticeTierUnitGambling);
                            PracticeUnitReroll = new SourceModelUnitReroll(Combat, SharedEconomy.Wallet, gamblingRoster,
                                deferredWispAbilities, sessionScriptRandom, () => runGate.CanAdvance,
                                () => PracticeGamblingNavigation.Navigation);
                            PracticeUnitReroll.PostCommitObserverFailed += Debug.LogException;
                            PracticeUnitGambling = new SourceModelOtherworldUnitGambling(
                                SharedEconomy.Wallet, Combat.Game, sessionScriptRandom,
                                () => runGate.CanAdvance,
                                new Original323OtherworldGamblingWorld(Combat, Units, MapBaseStats,
                                    new Original323CwrAcquisitionRoutes(OriginalJassFusions, Units, MapBaseStats),
                                    map, shopBonus), () => PracticeGamblingNavigation.Navigation);
                            PracticeUnitGambling.PostCommitObserverFailed += Debug.LogException;
                            PracticeMoneyGamblingDesk.ConfigureOtherworld(PracticeUnitGambling);
                        }
                    }
                    usesBuiltInAutomaticUncommonWisps = true;
                }
            }
            if (mysticFusionPort == null && OtherworldMysticFusions != null)
                mysticFusionPort = new OtherworldMysticFusionPreviewPort(
                    OtherworldMysticFusions, id => Combat.IsLockedFromRosterTransactions(id),
                    null, resource => SharedEconomy.Wallet.Balance(resource));
            if (originalFusionPort == null)
                RebuildVerifiedFusionCatalog();
            else
                ManualActions.Configure(storyDispatchRules,
                    originalFusionPort, originalEconomyPort);
            if (originalHelperCowWorld != null)
                AttachOriginalHelperCow();
            if (originalHelperCowAreaWorld != null)
                AttachOriginalHelperCowArea();

            // Physical LT-01 actors are consumed by the same transactional
            // staging as original fusion. The deferred automatic Wisp service
            // settles the result only after the wallet/unit commit.
            if (MapBaseStats != null && Units != null)
            {
                LuckyTokens = new Original323LuckyTokenService(
                    Combat, SharedEconomy, Units, MapBaseStats,
                    originalSharedScriptRandom == null ? sharedMobileWispRandom :
                        originalSharedScriptRandom.ForPurpose(
                            Original323ScriptRandomPurpose.OtherOriginalScriptCall),
                    id => Wisps != null && Wisps.CanAutomaticallyResolve(id));
                LuckyTokens.PostCommitObserverFailed += Debug.LogException;
            }
        }

        // 원작 피해 공식 없이도 캐릭터 소환·선택·배치·웨이브 월드를 구성할 수 있다.
        // 피해 공식은 반드시 미구현 예외로 보류된다. 실제 난이도/전투 검증에 사용하지 않는다.
        public void ConfigureCatalogOnlyWorld(uint seed)
        {
            if (Combat != null || MapBaseStats == null)
                throw new InvalidOperationException("원본 객체가 로드된 새 월드에서만 구성하세요.");
            var random = new SeededNonOriginalProcRandom(seed);
            sourceModelDamage = new SourceModelBasicCombatRules(MapBaseStats, random);
            if (verifiedUpgradeSteps == null && verifiedUpgradeWorld == null)
            {
                var upgradeData = Resources.Load<TextAsset>("OrdrData/original-2323C-player-upgrade-runtime");
                if (upgradeData == null) throw new InvalidOperationException("강화소 원본 모델 자료가 APK에 없습니다.");
                sourceModelDamage.ConfigurePlayerUpgrades(new SourceModelPlayerUpgrades(upgradeData.text));
            }
            if (OriginalNativePassives != null && OriginalCombinedAuras != null)
                sourceModelDamage.ConfigureUnitEffects(new SourceModelUnitEffects(
                    OriginalNativePassives, OriginalCombinedAuras, random,
                    Resources.Load<TextAsset>("OrdrData/practice-native-inherited-defaults")?.text,
                    OriginalMapObjectAuras));
            ConfigureRuntime(sourceModelDamage, sourceModelDamage, random);
            Debug.Log("ORDR 유닛 준비: 몬스터 웨이브는 시작 전까지 비활성, 스토리 건물 전투는 준비 단계부터 실행됩니다.");
        }

        public void BeginSession()
        {
            if (Combat == null)
                throw new InvalidOperationException("원작 피해 공식 구현체 연결이 필요합니다.");
            if (Combat.Game.Phase != SessionPhase.NotStarted || PersonalMissions != null)
                throw new InvalidOperationException("이미 시작한 게임입니다.");
            if (SharedEconomy == null || verifiedStartingProfile == null ||
                !verifiedStartingProfile.IsSourceVerified)
                throw new InvalidOperationException(
                    "원본 악몽 이세계 시작 재화·유닛 프로필 검증 전에는 세션을 시작하지 않습니다.");
            if (!Original323SoloStartProfile.MatchesPinnedSource(verifiedStartingProfile) ||
                startEvolutionWorld == null)
                throw new InvalidOperationException("원본 시작 e0HP 진화체 월드가 미연결입니다.");
            Original323EvolutionSourceRules.ValidateCatalog(Units, MapBaseStats);
            var preparedMissions = PersonalMissionGameplay.Prepare(Combat.Game,
                SharedEconomy.Wallet, missionRandom ??
                    new MobileMissionRandom(unchecked((uint)Environment.TickCount)),
                Guid.NewGuid().ToString("N"));
            var external = new HashSet<long>();
            long? evolutionId = null;
            SharedEconomy.ApplyVerifiedStart(verifiedStartingProfile,
                (catalogId, x, y) =>
                {
                    if (catalogId != Original323SoloStartProfile.EvolutionRawcode)
                        return Combat.SpawnCharacter(catalogId, x, y);
                    long id = startEvolutionWorld.SpawnAndTrack(SharedEconomy, catalogId, x, y);
                    if (id > 0) external.Add(id);
                    if (id > 0) evolutionId = id;
                    return id;
                },
                id =>
                {
                    if (external.Contains(id))
                        startEvolutionWorld.RemoveAndUntrack(SharedEconomy, id);
                    else Combat.RemoveCharacter(id);
                });
            ConfigureStandaloneTreasureIfAbsent();
            Combat.Start();
            runGate.SetManualPause(false);
            RecordSessionIssue("게임 시작", "악몽 · 이세계모드");
            StartingEvolutionActorId = evolutionId;
            EvolutionProgress = evolutionId.HasValue
                ? new Original323EvolutionProgress(evolutionId.Value) : null;
            EvolutionNotice = null;
            EvolutionNoticeShownAt = -99f;
            nextAutoEvolutionAttempt = 0f;
            evolutionService = EvolutionProgress == null ? null :
                new Original323EvolutionService(Combat, SharedEconomy,
                    startEvolutionWorld, EvolutionProgress, Units, MapBaseStats,
                    originalSharedScriptRandom == null ? sessionScriptRandom :
                        originalSharedScriptRandom.ForPurpose(
                            Original323ScriptRandomPurpose.OtherOriginalScriptCall),
                    evolutionNativeRandom ?? new Original323MobileNativePoolRandom(
                        new MobileUniformWispRandom()));
            PersonalMissions = preparedMissions;
            Poneglyph = new Original323PoneglyphSession();
            Combat.CharacterRemoved += OnPoneglyphUnitRemoved;
            Combat.CharacterRemovalObserverFailed += Debug.LogException;
            SharedMissionRewards = new Original323SharedMissionRewardWorld(
                Combat, SharedEconomy, MapBaseStats, Poneglyph,
                originalSharedScriptRandom == null ? sessionScriptRandom :
                    originalSharedScriptRandom.ForPurpose(
                        Original323ScriptRandomPurpose.OtherOriginalScriptCall));
            if (originalSharedObjectiveWitness != null)
                SharedObjectives = new Original323SharedObjectiveWorld(
                    Combat, SharedMissionRewards, Poneglyph,
                    originalSharedObjectiveWitness);
            AttachVerifiedSoloHelperCow();
        }

        private void OnCommittedCommonSale(long instanceId)
        {
            PersonalMissions?.RecordCommonSale(instanceId);
        }

        private void OnCommittedOriginalFusion(long resultId, string catalogId, string rawcode)
        {
            // The transaction is committed before this observer fires. Attach
            // native fusion-only passives even when personal missions are off.
            Combat?.TryAttachSourceModelNativeResearchOnFusion(resultId,
                catalogId, rawcode);
            Combat?.RecordSourceModelKobyFusionRound(resultId,
                catalogId, rawcode);
            if (PersonalMissions == null || MapBaseStats == null) return;
            var result = MapBaseStats.Find(catalogId);
            if (result == null || result.OriginalRawcode != rawcode) return;
            // Native result identity is committed and its original rawcode
            // matches the pinned object. The original top-tier category is
            // 초월/불멸/영원/제한/신비; this drives Q014/Q015 instead of
            // silently dropping every verified top-tier fusion event.
            var unit = Units == null ? null : Units.Find(catalogId);
            bool highest = unit != null && !unit.IsAlternateForm &&
                (unit.Grade == "초월함" || unit.Grade == "불멸의" ||
                 unit.Grade == "영원한" || unit.Grade == "제한됨" ||
                 unit.Grade == "신비함");
            // Hero identity (Q007) remains independent of this category.
            PersonalMissions.RecordFusion("fusion-result:" + resultId, rawcode,
                false, result.OriginalAbilityRawcodes, highest);
        }

        // 지갑 저장 이력까지 복원한 뒤 전투 자동 시전을 시작해야 한다.
        // 개발 로그용. 원본 검증 미승인, 방어력 미검증, 공식 보류를 별도 구분.
        public string ExportBasicAttackDiagnosticReport()
        {
            if (Combat == null) return "전투 월드가 아직 구성되지 않았습니다.";
            var lines = new System.Collections.Generic.List<string>();
            foreach (var state in Combat.CaptureBasicAttackDiagnostics())
                lines.Add(state.InstanceId + " (" + state.CatalogId + ") " +
                    state.Kind + ": " + state.Detail);
            return string.Join("\n", lines.ToArray());
        }

        public void ConfigureOtamaWoodRewards(Ordr.Mobile.Core.EconomyWallet sharedWallet)
        {
            if (Combat == null || SharedEconomy == null ||
                !ReferenceEquals(sharedWallet, SharedEconomy.Wallet))
                throw new InvalidOperationException(
                    "오타마 목재 보상은 공유 세션 지갑만 사용할 수 있습니다.");
            Combat.ConfigureOtamaWoodWallet(SharedEconomy.Wallet);
        }

        public void ConfigureSpecialGradeMobileServices(ISpecialGradeLawCastService law,
            IManualUnitDecompositionPort decomposition)
        {
            if (Combat == null)
                throw new InvalidOperationException("특별함 서비스를 연결할 전투 월드가 없습니다.");
            if (law != null) Combat.ConfigureSpecialGradeLawCasting(law);
            if (decomposition != null)
            {
                UnitDecompositions?.Dispose();
                UnitDecompositions = null;
                ManualActions.ConfigureUnitDecomposition(decomposition);
            }
        }

        // Begins only a wave whose spawn positions, final HP and armor have
        // individually reviewed source evidence. Unknown waves stay disabled.
        public void BeginVerifiedMonsterWave(int waveNumber)
        {
            VerifiedWavePlan plan = RequireVerifiedMonsterWavePlan(waveNumber);
            string roundStartEvidence;
            if (!Original323ObservedRoundStartGate.TryAuthorize(
                Combat.Game, waveNumber, observedRoundStartWitness,
                out roundStartEvidence))
                throw new InvalidOperationException(
                    "다음 라운드의 원본 시작 이벤트가 확인되지 않았습니다.");
            BeginAuthorizedVerifiedMonsterWave(waveNumber, plan);
        }

        // Only the independent original event witness may authorize a
        // transition. This does not infer timer events from enemy kills.
        public bool TryBeginNextObservedMonsterWave()
        {
            if (Combat == null ||
                !Original323ObservedRoundStartGate.MayObserveNext(
                    Combat.Game, VerifiedWaveDirector, observedRoundStartWitness))
                return false;
            int next = Combat.Game.CurrentWave + 1;
            VerifiedWavePlan plan;
            // The original observer may supply later wave plans on demand.
            // When none is installed yet, do not allocate and catch an
            // exception on EVERY Unity frame (notably the last preview wave).
            VerifiedWavePlan available;
            if (!registeredLiveWavePlans.TryGetValue(next, out available) &&
                (VerifiedMonsterWaves == null ||
                 !VerifiedMonsterWaves.TryGetPlan(next, out available)))
                return false;
            try
            {
                // Check source identity/cadence/final stats before consulting
                // the possibly consuming original event source.
                plan = RequireVerifiedMonsterWavePlan(next);
            }
            catch (InvalidOperationException)
            {
                // The next wave's live evidence may be delivered later.
                return false;
            }
            string sourceEvent;
            if (!Original323ObservedRoundStartGate.TryAuthorize(
                Combat.Game, next, observedRoundStartWitness, out sourceEvent))
                return false;
            BeginAuthorizedVerifiedMonsterWave(next, plan);
            return true;
        }

#if UNITY_EDITOR
        // Only the explicitly activated, non-retail Editor practice may
        // advance without an independently observed Warcraft timer event.
        // Production BeginVerifiedMonsterWave and the JASS-backed observer
        // retain all of their original evidence requirements.
        public void BeginEditorPreviewWave(int waveNumber)
        {
            if (!IsEditorPreview || Combat == null ||
                Combat.Game.Phase != SessionPhase.Running ||
                waveNumber < 2 || waveNumber > 3 ||
                waveNumber != Combat.Game.CurrentWave + 1)
                throw new InvalidOperationException(
                    "Unity Editor 체험 모드에서는 순서대로 2~3라운드만 시작할 수 있습니다.");
            var previous = VerifiedWaveDirector.CurrentProgress;
            if (!VerifiedWaveDirector.IsActive ||
                VerifiedWaveDirector.RemainingSpawns != 0 ||
                previous == null ||
                previous.WaveNumber != Combat.Game.CurrentWave ||
                !previous.AllSpawnsIssued)
                throw new InvalidOperationException(
                    "이전 체험 라운드의 출현이 모두 완료되지 않았습니다.");
            // The registered plan is still checked against source IDs,
            // cadence, enemy roles, HP and the explicitly supplied observer.
            // Do NOT call or counterfeit the original round event witness.
            VerifiedWavePlan plan = RequireVerifiedMonsterWavePlan(waveNumber);
            BeginAuthorizedVerifiedMonsterWave(waveNumber, plan, false);
        }
#endif

        private void BeginAuthorizedVerifiedMonsterWave(
            int waveNumber, VerifiedWavePlan plan, bool originalEventConfirmed = true)
        {
            // BGo dispatches l0 before the next round is entered. CU8/CU7
            // defeats a player whose previous boss remains outstanding.
            // Only a trusted observed round-start signal can reach this path.
            if (waveNumber > 1)
            {
                VerifiedWaveProgress previous;
                if (!VerifiedWaveDirector.TryGetWaveProgress(
                    waveNumber - 1, out previous))
                    throw new InvalidOperationException(
                        "이전 라운드의 실제 몬스터 출현·처치 기록이 없습니다.");
                if (previous.BossPlanned > 0)
                {
                    bool failed;
                    if (!Original323BossRoundRules.TryEvaluateObservedBossDeadline(
                        previous, out failed))
                        throw new InvalidOperationException(
                            "이전 보스 라운드 출현이 완료되지 않았습니다.");
                    checkedOriginalBossDeadlines.Add(previous.WaveNumber);
                    if (failed)
                    {
                        Combat.Game.Finish(false);
                        throw new InvalidOperationException(
                            "원본 보스 제한시간 경과: 보스를 처치하지 못해 패배했습니다.");
                    }
                }
            }
            VerifiedWaveDirector.Begin(Combat, plan);
            SpawnStandaloneTreasureForRound(waveNumber);
            if (originalEventConfirmed)
                commonSourceFusion?.ConfirmObservedOriginalRound(waveNumber);
            RecordSessionIssue("라운드 시작", waveNumber + "라운드 / 원본 이벤트 승인");
            PersonalMissions?.OnRoundStarted();
        }

        // This is a READ-ONLY list of distinct first-play blockers.
        // Never manufacture damage, final monster stats or live approval
        // just to make a green start button. Do not query an event witness
        // during OnGUI: the original source may consume an event.
        public IReadOnlyList<string> GetFirstWaveBlockers()
        {
            var blockers = new List<string>();
            if (Units == null || Skills == null || UnitDefinitions == null)
                blockers.Add("원본 캐릭터 도감·스킬 데이터 로딩");
            if (Combat == null || !HasSourceVerifiedDamageRuntime)
                blockers.Add("원본 검증된 피해 계산기 및 전투 월드 연결");
            if (verifiedStartingProfile == null ||
                !Original323SoloStartProfile.MatchesPinnedSource(verifiedStartingProfile))
                blockers.Add("CUo/CUn 원본 시작 재화·진화체 자료 등록");
            if (Combat != null)
            {
                if (startEvolutionWorld == null)
                    blockers.Add("원본 e0HP 진화체를 실제 보유 명단에 연결");
                if (!IsUnitPreparation && (Combat.Game.Phase != SessionPhase.NotStarted ||
                    SharedEconomy == null || SharedEconomy.HasStarted ||
                    PersonalMissions != null))
                    blockers.Add("새 게임 세션·공유 지갑 초기 상태 복원");
            }
            VerifiedWavePlan first;
            if ((VerifiedMonsterWaves == null ||
                 !VerifiedMonsterWaves.TryGetPlan(1, out first)) &&
                !registeredLiveWavePlans.ContainsKey(1))
                blockers.Add("1라운드 몬스터 최종 체력·방어·면역·출현 위치 검증");
            if (OriginalMonsterSpawnIdentities == null ||
                OriginalMonsterObjectAbilities == null)
                blockers.Add("원본 몬스터 출현 ID·타이밍·객체 자료 로드");
            if (liveMonsterWaveEvidence == null)
                blockers.Add("1라운드 몬스터 능력치의 독립 실전 승인 공급자 연결");
            return blockers.AsReadOnly();
        }

        public string ExportFirstWaveReadiness()
        {
            var blockers = GetFirstWaveBlockers();
            if (blockers.Count == 0)
                return "원랜디 모바일: 원본 첫 라운드 시작 사전 조건 충족 (실기기 검증 전)";
            return "원랜디 모바일 첫 라운드 차단 요인 " + blockers.Count +
                "건:\n- " + string.Join("\n- ", blockers);
        }

        // This is deliberately non-consuming. Only actual session start
        // consults the source-backed wave witness and final monster stats.
        public bool CanBeginVerifiedFirstWave(out string reason)
        {
            var blockers = GetFirstWaveBlockers();
            reason = blockers.Count == 0 ? null : blockers[0];
            return blockers.Count == 0;
        }

        // A single first-play action connects the verified start wallet,
        // non-combat evolution actor, missions and first wave. All source
        // preflight checks precede the irreversible session start.
        public void BeginVerifiedFirstWave()
        {
            string reason;
            if (!CanBeginVerifiedFirstWave(out reason))
                throw new InvalidOperationException(reason);
            VerifiedWavePlan first = RequireVerifiedMonsterWavePlan(1);
            if (!IsUnitPreparation) BeginSession();
            VerifiedWaveDirector.Begin(Combat, first);
            SpawnStandaloneTreasureForRound(1);
            commonSourceFusion?.ConfirmObservedOriginalRound(1);
            RecordSessionIssue("라운드 시작", "1라운드 / 원본 이벤트 승인");
            PersonalMissions?.OnRoundStarted();
        }

        private VerifiedWavePlan RequireVerifiedMonsterWavePlan(int waveNumber)
        {
            if (Combat == null)
                throw new InvalidOperationException(
                    "원본 몬스터/전투 런타임이 아직 연결되지 않았습니다.");
            VerifiedWavePlan plan;
            if ((VerifiedMonsterWaves == null ||
                !VerifiedMonsterWaves.TryGetPlan(waveNumber, out plan)) &&
                !registeredLiveWavePlans.TryGetValue(waveNumber, out plan))
                throw new InvalidOperationException(
                    "요청한 라운드에는 검증된 2.323[C] 몬스터 데이터가 없습니다.");
            string approvalEvidence;
            if (OriginalMonsterSpawnIdentities == null ||
                !OriginalMonsterSpawnIdentities.MatchesSinglePlayerWave(plan))
                throw new InvalidOperationException(
                    "요청한 라운드의 몬스터 ID·역할·수량이 원본 JASS와 일치하지 않습니다.");
            if (OriginalMonsterObjectAbilities == null ||
                !OriginalMonsterObjectAbilities.MatchesWaveObjectRoles(plan))
                throw new InvalidOperationException(
                    "몬스터 원본 객체의 기본 능력/역할 자료가 누락되었거나 불일치합니다.");
            if (!OriginalMonsterSpawnIdentities.MatchesSinglePlayerSpawnCadence(plan))
                throw new InvalidOperationException(
                    "원본 타이머 기준 일반 몬스터·보스·광폭화 출현 시각과 다릅니다.");
            if (!Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                plan, liveMonsterWaveEvidence, out approvalEvidence))
                throw new InvalidOperationException(
                    "최종 몬스터 체력·방어·대상 면역·출현의 독립 실전 근거가 없습니다.");
            return plan;
        }

        // Observation-only. A fully defeated wave does not automatically
        // grant resources, finish the game, or change the original round clock.
        public bool TryGetVerifiedMonsterWaveProgress(int waveNumber,
            out VerifiedWaveProgress progress)
        {
            return VerifiedWaveDirector.TryGetWaveProgress(waveNumber, out progress);
        }

        private void Update()
        {
            if (Combat == null) return;
            if (Combat.Game.Phase != Ordr.Mobile.Core.SessionPhase.Running)
            {
                RecordFullGameClearOnce();
                PersonalMissions?.OnGameFinished();
                UnitDecompositions?.Advance(0); // Cancel source casts after game end.
                if (Poneglyph != null && Poneglyph.ActiveUnitId.HasValue)
                    RequestStopPoneglyph();
                return;
            }
            // CCP applies only after a committed Gaimon is visible in the
            // live roster. The YL witness is mandatory and read once.
            // A failed / unverified rare-Wisp transaction cannot trigger it.
            if (!GaimonDream.Applied)
            {
                if (originalGaimonYlSource != null)
                    GaimonDream.TryApplyAfterOwnedGaimon(
                        HasOwnedOriginalGaimon, originalGaimonYlSource);
                else if (standaloneTreasure != null)
                    GaimonDream.TryApplyMobileAfterOwnedGaimon(
                        HasOwnedOriginalGaimon);
            }
            SpawnStandaloneTreasureForRound(Combat.Game.CurrentWave);
            // Never catch up elapsed wall-clock time after backgrounding.
            // Combat, round events, shared mana regeneration and source RNG
            // remain on one paused logical clock.
            if (!runGate.CanAdvance) return;
            // This is the only wall -> game clock conversion. A fixed source
            // second remains one combat second at every selected playback rate.
            double frameGameSeconds = GameDeltaFromUnscaled(Time.unscaledDeltaTime);
            // Mobile policy: e0HP activates automatically; no second tap.
            // Source RNG is consumed at most once, and retries are throttled.
            if (evolutionService != null && EvolutionProgress != null &&
                !EvolutionProgress.IsActivated && Time.unscaledTime >= nextAutoEvolutionAttempt)
            {
                nextAutoEvolutionAttempt = Time.unscaledTime + 2f;
                RequestActivateStartingEvolution();
            }
            // Grant callbacks may run inside source transactions or combat
            // enumeration. Redeem only after those callers have unwound.
            Wisps?.FlushAutomaticSafely();
            // Before wave 1, the story building and deployed attackers
            // are already live. Advance their common combat clock without
            // creating a wave or granting any invented monster rewards.
            bool preparing = IsUnitPreparation;
            if (preparing && (!Combat.UsesSourceModelBasicAttacks ||
                              SoloStory == null))
            {
                UnitDecompositions?.Advance(frameGameSeconds);
                return;
            }
            double timeBeforeStep = Combat.Time;
            IReadOnlyList<CharacterCombatEvent> results;
            try
            {
                results = PracticeBattle != null ? PracticeBattle.Advance(frameGameSeconds) :
                    VerifiedWaveDirector.IsActive ? VerifiedWaveDirector.Step(Combat, frameGameSeconds) :
                    Combat.Step(frameGameSeconds);
            }
            catch (Exception ex)
            {
                runGate.SetManualPause(true);
                RecordSessionIssue("전투 중단", ex.Message);
                Debug.LogException(ex);
                return;
            }
            // The last enemy can die during Advance itself. Record once at
            // the transition, before the result panel is next rendered.
            RecordFullGameClearOnce();
            // BJI/BJH counts only advancing battle time; a paused battle
            // never catches up the alarm against the wall clock.
            AdvanceWitnessedTreasureAlarms();
            if (observedMonsterPositions != null && Combat.PendingSimulationSeconds == 0)
            {
                try { observedMonsterPositions.ApplyAtCurrentCombatTime(Combat); }
                catch (Exception ex)
                {
                    // Never keep advancing the battle with stale positions.
                    Debug.LogException(ex);
                    RecordSessionIssue("몬스터 이동 관측 실패", ex.Message);
                    runGate.SetManualPause(true);
                    return;
                }
            }
            if (observedMonsterEscapes != null &&
                Combat.PendingSimulationSeconds == 0)
            {
                try
                {
                    observedMonsterEscapes.ApplyAtCurrentCombatTime(
                        Combat, VerifiedWaveDirector);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    RecordSessionIssue("몬스터 이탈 관측 실패", ex.Message);
                    runGate.SetManualPause(true);
                    return;
                }
            }
            // Story timers use simulation time, not wall-clock time.
            // This also progresses the exact JASS 120s spawn / 240s deadline.
            try { SoloStory?.Advance(); }
            catch (Exception ex)
            {
                runGate.SetManualPause(true);
                RecordSessionIssue("스토리 진행 중단", ex.Message);
                Debug.LogException(ex);
                return;
            }
            TryFinishOriginal323Evolution();
            if (Combat.PendingSimulationSeconds == 0)
            {
                try { LuckyTokens?.AdvanceAutoElizabethQ(); }
                catch (Exception ex)
                {
                    runGate.SetManualPause(true);
                    RecordSessionIssue("엘리자베스 토큰 스킬 중단", ex.Message);
                    Debug.LogException(ex);
                    return;
                }
            }
            if ((PracticeBattle != null ||
                 (preparing && SoloStory != null && Combat.UsesSourceModelBasicAttacks)) &&
                Combat.Game.Phase == SessionPhase.Running &&
                Combat.PendingSimulationSeconds == 0)
            {
                try
                {
                    Combat.AutoCastApprovedSourceModelActives();
                    Combat.AutoCastSourceRareLawAcceleration();
                    Combat.AutoCastSourceManualSupportAndBurst();
                    Combat.AutoCastSourceModelManualUtility();
                }
                catch (Exception ex)
                {
                    // Auto cast failures are not silently ignored or retried
                    // during a partly settled combat transaction.
                    RecordSessionIssue("유닛 자동 스킬 중단", ex.Message);
                    Debug.LogException(ex);
                    runGate.SetManualPause(true);
                    return;
                }
            }
            UnitDecompositions?.Advance(Math.Max(0.0, Combat.Time - timeBeforeStep));
            if (Poneglyph != null && Poneglyph.ActiveUnitId.HasValue)
            {
                long activeId = Poneglyph.ActiveUnitId.Value;
                UnitState activeUnit;
                bool exists = TryGetOwnedPoneglyphUnit(activeId, out activeUnit);
                bool remainsInDecodeLane = exists &&
                    !Combat.IsStoryDeployed(activeId) &&
                    !Combat.IsSharedObjectiveDeployed(activeId);
                if (Poneglyph.Advance(Combat.Time, remainsInDecodeLane,
                    OriginalTreasureHunt, Combat.Game.CurrentWave))
                {
                    // Always unlock, including death/removal and zero-chest completion.
                    Combat.SetOriginalPoneglyphChanneling(activeId, false);
                    // A successful decoder is still alive: retain its native
                    // ability changes until the character is actually removed.
                    RecordSessionIssue("포네그리프", Poneglyph.LastMessage);
                }
            }
            // The same advancing combat clock drives helper mana regen.
            // UI redraws and wall time must not regenerate extra mana.
            if (Original323HelperCow != null)
                Original323HelperCow.AdvanceBaseManaRegeneration(
                    Math.Max(0.0, Combat.Time - timeBeforeStep),
                    SharedEconomy == null ? 0 :
                    Original323SourceItemPassiveRules.HelperCowExtraManaPerSecond(
                        SharedEconomy.Wallet));
            foreach (var item in results)
            {
                var handler = OnCombatEvent;
                if (handler != null) handler(item);
            }
            // Source-verified helper-cow point spells are cast ONLY by the
            // real live-world port, after the current enemy positions update.
            if (HelperCowAutoArea != null)
                HelperCowAutoArea.Tick();
            // Never synthesize the next round from a kill count or assumed
            // 5-second schedule: require an explicit original start witness.
            try
            {
                if (!preparing && PracticeBattle == null)
                    TryBeginNextObservedMonsterWave();
            }
            catch (Exception ex)
            {
                runGate.SetManualPause(true);
                RecordSessionIssue("라운드 전환 중단", ex.Message);
                Debug.LogException(ex);
            }
        }
    }
}


