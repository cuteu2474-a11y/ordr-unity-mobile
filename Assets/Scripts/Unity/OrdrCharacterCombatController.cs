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
                    "원본 272종 유닛/객체 능력 데이터를 먼저 로드하세요.");
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

        private void OnVerifiedMonsterEscaped(int sourceWave, string instanceId)
        {
            RecordSessionIssue("몬스터 이탈",
                "출현 라운드 " + sourceWave + " / 몬스터 " + instanceId);
        }

        private void OnVerifiedMonsterDefeated(int sourceWave, string instanceId)
        {
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
        public CharacterCombatRuntime Combat { get; private set; }
        public bool HasSourceVerifiedDamageRuntime { get; private set; }
        private readonly OfflineSessionRunGate runGate = new OfflineSessionRunGate();
        public bool IsGameplayPaused => !runGate.CanAdvance;

        public void SetManualGameplayPause(bool paused)
        {
            if (Combat == null || Combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("진행 중인 게임에서만 일시정지할 수 있습니다.");
            runGate.SetManualPause(paused);
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
                RecordSessionIssue("위습 소환", recruited == null ? result.CatalogId :
                    recruited.Name);
                return new ManualActionResult(ManualActionState.Succeeded,
                    "흔함 유닛 소환 완료: " +
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
        public long? StartingEvolutionActorId { get; private set; }
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
            Original323HelperNavigation = new Ordr323OriginalHelperNavigation(Combat.Game, actor);
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
            if (Wisps != null)
                throw new InvalidOperationException("위습 거래 월드 중복 연결");
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
            Wisps = new MobileWispService(SharedEconomy.Wallet, roller, world);
            choiceWispWorld = choiceMerge;
            Wisps.FlushAutomatic();
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
                mysticCatalog, id => Combat.IsStoryDeployed(id));
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
                UnitDefinitions = UnitImplementationRegistry.Combine(Units, Skills,
                    OriginalUnitRawcodes);
                if (Units.Count != 272 || Skills.CharacterCount != 200 ||
                    UnitDefinitions.Count != 272)
                    throw new FormatException("2.323[C] 캐릭터 데이터 등록 수가 기대값과 다릅니다.");
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
            VerifiedWaveDirector.VerifiedMonsterDefeated += OnVerifiedMonsterDefeated;
            VerifiedWaveDirector.VerifiedMonsterEscaped += OnVerifiedMonsterEscaped;
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
            if (OriginalCombinedAuras != null)
                Combat.ConfigureOriginalAuras(OriginalCombinedAuras, specialAuraTargets);
            else if (OriginalArmorAuras != null)
                Combat.ConfigureOriginalAuras(OriginalArmorAuras, specialAuraTargets);
            else if (OriginalSpecialAuras != null)
                Combat.ConfigureOriginalSpecialAuras(OriginalSpecialAuras, specialAuraTargets);
            if (OriginalUncommonAbilities != null)
                Combat.ConfigureOriginalUncommonAbilities(
                    OriginalUncommonAbilities, originalUncommonEffectWorld);
            IOriginalWispRandom sharedMobileWispRandom =
                randomWispResourceRng ?? new MobileUniformWispRandom();
            RandomWispExchange = new OriginalRandomWispResourceExchange(
                SharedEconomy.Wallet, world.Game, sharedMobileWispRandom);
            ManualActions.ConfigureRandomWispResourceExchange(RandomWispExchange);
            // BIQ/BIR/BIS/BIU/BIY/CRy are fully specified in the original
            // JASS. Use the same per-session random stream as resource Wisp
            // actions; a different approved original world may override this
            // before the game starts. A rare pirate ship is held pending and
            // never silently converted into an ordinary common unit.
            if (MapBaseStats != null && Units != null)
            {
                var originalDrawWorld = new Original323SourceCommonWispWorld(
                    Combat, Units, MapBaseStats, sharedMobileWispRandom);
                commonWispRecruitment = new Original323CommonWispRecruitmentService(
                    Combat, SharedEconomy, MapBaseStats, originalDrawWorld);
                commonWispAvailability = originalDrawWorld;
                commonWispUsesBuiltInSource = true;
            }
            Combat.ConfigureOtamaWoodWallet(SharedEconomy.Wallet);
            Combat.ConfigureVerifiedUnitAttackProvider(verifiedUnitAttackSource);
            if (MapBaseStats != null)
                Combat.ConfigureOriginalMapCommonAttackPreview(MapBaseStats);
            Combat.MobileAutomation.Configure(placementRules, bossFocusRules,
                autoSkillRules ?? new MobileUnlimitedAutoSkillTargetRules());
            Combat.ConfigureStoryDeployment(storyDispatchRules);
            if (OriginalJassFusions != null && OriginalUnitRawcodes != null)
                OriginalFusionPlanner = new OriginalJassFusionPlanner(
                    OriginalJassFusions, OriginalUnitRawcodes,
                    id => Combat.IsStoryDeployed(id), null, Units);
            if (VerifiedFusionRecipes != null)
                FusionAssistance = new AssistedFusionService(VerifiedFusionRecipes,
                    () => Combat.MobileUnitSnapshot(),
                    id => Combat.IsStoryDeployed(id));
            // Display the full 265 source-registered recipes and their unit
            // deficits even when an irreversible original transaction is NOT
            // connected. The preview adapter is always non-executable.
            if (OriginalJassFusions != null && OriginalFusionObjectPreview != null)
                originalJassReadOnlyFusionPort = new OriginalJassReadOnlyFusionPort(
                    OriginalJassFusions, OriginalFusionObjectPreview,
                    id => Combat.IsStoryDeployed(id));
            if (mysticFusionPort == null && OtherworldMysticFusions != null)
                mysticFusionPort = new OtherworldMysticFusionPreviewPort(
                    OtherworldMysticFusions, id => Combat.IsStoryDeployed(id));
            if (originalFusionPort == null)
                RebuildVerifiedFusionCatalog();
            else
                ManualActions.Configure(storyDispatchRules,
                    originalFusionPort, originalEconomyPort);
            if (originalHelperCowWorld != null)
                AttachOriginalHelperCow();
            if (originalHelperCowAreaWorld != null)
                AttachOriginalHelperCowArea();
        }

        // 원작 피해 공식 없이도 캐릭터 소환·선택·배치·웨이브 월드를 구성할 수 있다.
        // 피해 공식은 반드시 미구현 예외로 보류된다. 실제 난이도/전투 검증에 사용하지 않는다.
        public void ConfigureCatalogOnlyWorld(uint seed)
        {
            var unavailable = new RejectUnverifiedDamageFormula();
            ConfigureRuntime(unavailable, unavailable, new SeededNonOriginalProcRandom(seed));
            Debug.LogWarning("ORDR 도감/월드 전용 모드: 평타·스킬 피해 공식은 미구현으로 보류됩니다.");
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
            Combat.Start();
            runGate.SetManualPause(false);
            RecordSessionIssue("게임 시작", "악몽 · 이세계모드");
            StartingEvolutionActorId = evolutionId;
            PersonalMissions = preparedMissions;
            AttachVerifiedSoloHelperCow();
        }

        private void OnCommittedCommonSale(long instanceId)
        {
            PersonalMissions?.RecordCommonSale(instanceId);
        }

        private void OnCommittedOriginalFusion(long resultId, string catalogId, string rawcode)
        {
            if (PersonalMissions == null || MapBaseStats == null) return;
            var result = MapBaseStats.Find(catalogId);
            if (result == null || result.OriginalRawcode != rawcode) return;
            // Native result identity is authoritative; hero type and Q7's
            // separate highest-tier condition still need their actual source.
            PersonalMissions.RecordFusion("fusion-result:" + resultId, rawcode,
                false, result.OriginalAbilityRawcodes, false);
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
            if (decomposition != null) ManualActions.ConfigureUnitDecomposition(decomposition);
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

        private void BeginAuthorizedVerifiedMonsterWave(
            int waveNumber, VerifiedWavePlan plan)
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
            RecordSessionIssue("라운드 시작", waveNumber + "라운드 / 원본 이벤트 승인");
            PersonalMissions?.OnRoundStarted();
        }

        // Readiness is deliberately non-consuming. In particular, the live
        // monster witness is consulted only when the user starts the game,
        // never by the repeated GUI repaint cycle.
        public bool CanBeginVerifiedFirstWave(out string reason)
        {
            reason = null;
            if (Units == null || Skills == null)
                reason = "원본 캐릭터 데이터 로딩 대기";
            else if (Combat == null || !HasSourceVerifiedDamageRuntime)
                reason = "원본 검증된 피해 계산기 연결 대기";
            else if (Combat.Game.Phase != SessionPhase.NotStarted ||
                SharedEconomy == null || SharedEconomy.HasStarted || PersonalMissions != null)
                reason = "게임이 이미 시작되었거나 시작 상태가 일치하지 않습니다.";
            else if (verifiedStartingProfile == null || startEvolutionWorld == null ||
                !Original323SoloStartProfile.MatchesPinnedSource(verifiedStartingProfile))
                reason = "시작 재화·진화체 등록 확인 대기";
            else if ((VerifiedMonsterWaves == null ||
                !VerifiedMonsterWaves.TryGetPlan(1, out _)) &&
                !registeredLiveWavePlans.ContainsKey(1))
                reason = "1라운드의 검증된 최종 몬스터 체력·방어·위치 자료 대기";
            else if (OriginalMonsterSpawnIdentities == null ||
                OriginalMonsterObjectAbilities == null)
                reason = "원본 몬스터 ID·출현 규칙 자료 대기";
            else if (liveMonsterWaveEvidence == null)
                reason = "1라운드 몬스터 실전 승인 공급자 연결 대기";
            return reason == null;
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
            BeginSession();
            VerifiedWaveDirector.Begin(Combat, first);
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
                PersonalMissions?.OnGameFinished();
                return;
            }
            // Never catch up elapsed wall-clock time after backgrounding.
            // Combat, round events, shared mana regeneration and source RNG
            // remain on one paused logical clock.
            if (!runGate.CanAdvance) return;
            double timeBeforeStep = Combat.Time;
            IReadOnlyList<CharacterCombatEvent> results;
            try
            {
                results = VerifiedWaveDirector.IsActive
                    ? VerifiedWaveDirector.Step(Combat, Time.deltaTime)
                    : Combat.Step(Time.deltaTime);
            }
            catch (Exception ex)
            {
                runGate.SetManualPause(true);
                RecordSessionIssue("전투 중단", ex.Message);
                Debug.LogException(ex);
                return;
            }
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
            // The same advancing combat clock drives helper mana regen.
            // UI redraws and wall time must not regenerate extra mana.
            if (Original323HelperCow != null)
                Original323HelperCow.AdvanceBaseManaRegeneration(
                    Math.Max(0.0, Combat.Time - timeBeforeStep));
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

