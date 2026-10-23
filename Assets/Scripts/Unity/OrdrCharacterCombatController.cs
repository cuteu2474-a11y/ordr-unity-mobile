using System;
using UnityEngine;
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
        [SerializeField] private TextAsset verifiedFusionRecipesJson;
        [SerializeField] private TextAsset otherworldMysticFusionRecipesJson;
        [SerializeField] private TextAsset originalJassFusionRegistryJson;
        [SerializeField] private TextAsset originalUnitRawcodeLabelsJson;
        [SerializeField] private TextAsset verifiedMonsterWavesJson;
        [SerializeField] private bool enableMobileTouchPanel = true;
        [SerializeField] private bool enableUnitWorldMarkers = true;

        public CharacterUnitCatalog Units { get; private set; }
        public CharacterSkillCatalog Skills { get; private set; }
        public UnitImplementationRegistry UnitDefinitions { get; private set; }
        public OriginalUnitStatEvidenceCatalog StatEvidence { get; private set; }
        public OriginalMapBaseStatCatalog MapBaseStats { get; private set; }
        public VerifiedFusionCatalog VerifiedFusionRecipes { get; private set; }
        public OtherworldMysticFusionCatalog OtherworldMysticFusions { get; private set; }
        public OriginalJassFusionCatalog OriginalJassFusions { get; private set; }
        public OriginalUnitRawcodeLabelCatalog OriginalUnitRawcodes { get; private set; }
        public Original323VerifiedMonsterWaveCatalog VerifiedMonsterWaves { get; private set; }
        public CombatWaveDirector VerifiedWaveDirector { get; } = new CombatWaveDirector();
        public OriginalJassFusionPlanner OriginalFusionPlanner { get; private set; }
        public AssistedFusionService FusionAssistance { get; private set; }
        public CharacterCombatRuntime Combat { get; private set; }

        public MobileManualInteraction ManualActions { get; } = new MobileManualInteraction();

        private IOriginalPlacementRules placementRules;
        private IOriginalBossFocusRules bossFocusRules;
        private IOriginalAutoSkillRules autoSkillRules;
        private IOriginalStoryDispatchPort storyDispatchRules;
        private IOriginalVerifiedUnitAttackProvider verifiedUnitAttackSource;
        private IOriginalFusionPort originalFusionPort;
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
            ManualActions.ConfigureOriginalCommonSale(
                new OriginalLuffyCommonSaleCoordinator(originalWorld));
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
                Combat.MobileAutomation.Configure(placementRules, bossFocusRules, autoSkillRules);
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
            originalFusionPort = new OriginalJassFusionPort(OriginalFusionPlanner,
                () => Combat.MobileUnitSnapshot(), verifiedResourceReader,
                originalTransaction, originalCastPolicy);
            ManualActions.Configure(storyDispatchRules, originalFusionPort, originalEconomyPort);
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

        // 이세계 신비 도감 UI 연결. 공개 도감만으로는 실제 소모를 절대 실행하지 않는다.
        public void ConfigureOtherworldMysticFusionPreview(
            OtherworldMysticFusionCatalog mysticCatalog)
        {
            if (Combat == null)
                throw new InvalidOperationException("신비 조합 화면을 연결할 전투 월드가 없습니다.");
            if (mysticCatalog == null) throw new ArgumentNullException(nameof(mysticCatalog));
            mysticFusionPort = new OtherworldMysticFusionPreviewPort(
                mysticCatalog, id => Combat.IsStoryDeployed(id));
            originalFusionPort = FusionAssistance == null ?
                (IOriginalFusionPort)mysticFusionPort :
                new CombinedOriginalFusionPort(FusionAssistance, mysticFusionPort);
            ManualActions.Configure(storyDispatchRules, originalFusionPort, originalEconomyPort);
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
                    MapBaseStats = OriginalMapBaseStatCatalog.FromJson(mapBaseData.text, Units);
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
                UnitDefinitions = UnitImplementationRegistry.Combine(Units, Skills,
                    OriginalUnitRawcodes);
                if (Units.Count != 271 || Skills.CharacterCount != 200 ||
                    UnitDefinitions.Count != 271)
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
            ConfigureRuntimeWithWorld(Ordr323DamageWorldBinding.Create(
                Units, Skills, verifiedHits, verifiedModifiers, quantization, random));
        }

        // Opt-in only: a map object row is not itself evidence that JASS leaves
        // native attacks intact. A verified path, target defenses and HP
        // quantization must be supplied before common attacks may cause damage.
        public void ConfigureOriginalCommonNativeDamageWhenVerified(
            IOriginalCommonNativeAttackVerification verifiedNativePath,
            IOrdr323VerifiedModifierRules verifiedModifiers,
            IOrdr323VerifiedDamageQuantization verifiedQuantization,
            ICombatProcRandom diceRandom)
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
                MapBaseStats, verifiedNativePath, diceRandom);
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
            Combat = world;
            Combat.ConfigureVerifiedUnitAttackProvider(verifiedUnitAttackSource);
            if (MapBaseStats != null)
                Combat.ConfigureOriginalMapCommonAttackPreview(MapBaseStats);
            Combat.MobileAutomation.Configure(placementRules, bossFocusRules, autoSkillRules);
            Combat.ConfigureStoryDeployment(storyDispatchRules);
            if (OriginalJassFusions != null && OriginalUnitRawcodes != null)
                OriginalFusionPlanner = new OriginalJassFusionPlanner(
                    OriginalJassFusions, OriginalUnitRawcodes,
                    id => Combat.IsStoryDeployed(id), null, Units);
            if (VerifiedFusionRecipes != null)
                FusionAssistance = new AssistedFusionService(VerifiedFusionRecipes,
                    () => Combat.MobileUnitSnapshot(),
                    id => Combat.IsStoryDeployed(id));
            // 악몽 + 이세계모드는 항상 기본 모드다. 원본 트랜잭션이 없는
            // 상태에서는 신비 도감 13종의 재료 미리보기만 노출한다.
            if (originalFusionPort == null && OtherworldMysticFusions != null)
            {
                mysticFusionPort = new OtherworldMysticFusionPreviewPort(
                    OtherworldMysticFusions, id => Combat.IsStoryDeployed(id));
                originalFusionPort = FusionAssistance == null ?
                    (IOriginalFusionPort)mysticFusionPort :
                    new CombinedOriginalFusionPort(FusionAssistance, mysticFusionPort);
            }
            ManualActions.Configure(storyDispatchRules,
                originalFusionPort ?? FusionAssistance, originalEconomyPort);
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
            Combat.Start();
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
            if (Combat == null)
                throw new InvalidOperationException("전투 런타임이 없습니다.");
            Combat.ConfigureOtamaWoodWallet(sharedWallet);
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
            if (Combat == null || VerifiedMonsterWaves == null)
                throw new InvalidOperationException(
                    "원본 몬스터/라운드 검증 데이터가 아직 연결되지 않았습니다.");
            VerifiedWavePlan plan;
            if (!VerifiedMonsterWaves.TryGetPlan(waveNumber, out plan))
                throw new InvalidOperationException(
                    "요청한 라운드에는 검증된 2.323[C] 몬스터 데이터가 없습니다.");
            VerifiedWaveDirector.Begin(Combat, plan);
        }

        private void Update()
        {
            if (Combat == null || Combat.Game.Phase != Ordr.Mobile.Core.SessionPhase.Running)
                return;
            var results = VerifiedWaveDirector.IsActive
                ? VerifiedWaveDirector.Step(Combat, Time.deltaTime)
                : Combat.Step(Time.deltaTime);
            foreach (var item in results)
            {
                var handler = OnCombatEvent;
                if (handler != null) handler(item);
            }
        }
    }
}

