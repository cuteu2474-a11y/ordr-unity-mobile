using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // 271종 카탈로그를 소환하고, 200종 상세 정보가 존재하면 공격·발동 스킬과 연결.
    // 원작 평타 수치/피해 공식 미확인 유닛은 의도적으로 공격을 시작하지 않는다.
    public enum BasicAttackReadinessKind
    {
        UnverifiedSource, UnverifiedTarget, FormulaDeferred,
        NoBattlefieldTarget, Cooldown, Ready
    }

    public sealed class BasicAttackReadiness
    {
        public long InstanceId { get; private set; }
        public string CatalogId { get; private set; }
        public BasicAttackReadinessKind Kind { get; private set; }
        public string Detail { get; private set; }
        public double? NextAttackAtSeconds { get; private set; }
        internal BasicAttackReadiness(long id, string catalog, BasicAttackReadinessKind kind,
            string detail, double? nextAt)
        {
            InstanceId = id; CatalogId = catalog; Kind = kind;
            Detail = detail; NextAttackAtSeconds = nextAt;
        }
    }

    public sealed class CharacterCombatRuntime
    {
        private sealed class CharacterInstance
        {
            public long Id;
            public CatalogUnit Catalog;
            public CharacterSkillProfile Profile;
            public CharacterCombatMapping Mapping;
            public CharacterActiveAbilities Abilities;
            public bool AttackReady;
            public bool AttackBlockedByMissingFormula;
            public string DeferredAttackReason;
            public string AttackEvidence;
            public IOriginalVerifiedBasicAttackTargetFilter BasicTargetFilter;
        }

        private readonly CharacterUnitCatalog units;
        private readonly CharacterSkillCatalog profiles;
        private readonly UnitRoster roster;
        private readonly GameSession game = new GameSession();
        private readonly CombatScheduler scheduler = new CombatScheduler();
        private readonly CombatSkillTriggerEngine triggers;
        private readonly CombatResolution resolution;
        private readonly CombatAttackPipeline attackPipeline;
        private readonly CharacterSkillEffectExecutor effectExecutor;
        private readonly Dictionary<long, CharacterInstance> characters =
            new Dictionary<long, CharacterInstance>();
        private readonly Dictionary<string, SpatialCombatEnemy> enemies =
            new Dictionary<string, SpatialCombatEnemy>(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatStatusEngine> statuses =
            new Dictionary<string, CombatStatusEngine>(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatStatusEngine> unitStatuses =
            new Dictionary<string, CombatStatusEngine>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> armorBreakStacks =
            new Dictionary<string, int>(StringComparer.Ordinal);
        // Bdq(A04M) 원본 함수와 연결된 기술로 발생했음이 확인된 스택만 기록.
        private readonly Dictionary<string, int> provenA04MStacks =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> provenA04PTotals =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private Ordr323BonneyDefenseController bonneyArmor;
        private IOriginalBonneyFormEvidence bonneyFormEvidence;
        private readonly List<CharacterCombatEvent> events = new List<CharacterCombatEvent>();
        private readonly CombatAuraSystem auras = new CombatAuraSystem();
        private readonly List<CharacterCombatEvent> pendingCommandEvents =
            new List<CharacterCombatEvent>();
        private bool processingStep;
        private double pendingStepSeconds;
        private IOriginalStoryDispatchPort storyDispatch;
        private IOriginalVerifiedUnitAttackProvider verifiedAttackProvider;
        private OriginalMapCommonAttackPreview commonAttackPreview;
        private IOriginalSpecialSkillTriggerMapping specialTriggerMapping;
        private IOriginalSpecialSkillEventRules specialEventRules;
        private IOriginalCombatTargetSelectionRules targetSelectionRules;
        private ISpecialGradeLawCastService lawCastService;
        private EconomyWallet otamaRewardWallet;
        private readonly Dictionary<long, double> lawReadyAt = new Dictionary<long, double>();
        private readonly MoriaParticipationLedger moriaParticipation = new MoriaParticipationLedger();
        public long MoriaZombieMaterials { get; private set; }
        public MobileCombatAutomation MobileAutomation { get; } = new MobileCombatAutomation();

        // 실제 경제/조합과 공유한 지갑만 주입한다. 별도 임시 지갑을 만들지 않는다.
        public void ConfigureOtamaWoodWallet(EconomyWallet sharedWallet)
        {
            if (sharedWallet == null) throw new ArgumentNullException(nameof(sharedWallet));
            if (otamaRewardWallet != null && !ReferenceEquals(otamaRewardWallet, sharedWallet))
                throw new InvalidOperationException("세션 중 공유 지갑 변경은 불가능합니다.");
            otamaRewardWallet = sharedWallet;
        }

        public bool TryUseOtamaDango(long instanceId)
        {
            CharacterInstance unit;
            if (game.Phase != SessionPhase.Running || otamaRewardWallet == null ||
                !characters.TryGetValue(instanceId, out unit) ||
                unit.Catalog.Id != SpecialGradeMobilePolicy.Otama) return false;
            bool paid;
            try
            {
                paid = otamaRewardWallet.TryGrantOnce(
                    SpecialGradeMobilePolicy.OtamaRewardId(instanceId),
                    new ResourceBundle().Add("wood", SpecialGradeMobilePolicy.OtamaWoodReward));
            }
            catch (OverflowException)
            {
                // 지급에 실패했으면 1회 사용권도 소모되지 않는다.
                return false;
            }
            if (!paid) return false;
            var evt = new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(instanceId), null, "경단먹이기",
                "wood +16", SpecialGradeMobilePolicy.OtamaWoodReward);
            if (processingStep) events.Add(evt); else pendingCommandEvents.Add(evt);
            return true;
        }

        public void ConfigureSpecialGradeLawCasting(ISpecialGradeLawCastService service)
        {
            lawCastService = service ?? throw new ArgumentNullException(nameof(service));
        }

        // 시전 가능한 시점마다 현재 전장의 전체 후보를 다시 평가한다.
        // 실제 가속 효과·자원/중복 검증 서비스 없이는 시전 성공으로 처리하지 않는다.
        public bool TryRunLawAcceleration(long lawId)
        {
            CharacterInstance law;
            if (game.Phase != SessionPhase.Running || lawCastService == null ||
                !characters.TryGetValue(lawId, out law) ||
                law.Catalog.Id != SpecialGradeMobilePolicy.Law) return false;
            double ready;
            if (lawReadyAt.TryGetValue(lawId, out ready) && Time < ready) return false;
            if (!lawCastService.CanCastNow(lawId, Time)) return false;
            bool inStory = IsStoryDeployed(lawId);
            var candidates = new List<LawDpsCandidate>();
            foreach (var target in MobileUnitSnapshot())
            {
                if (IsStoryDeployed(target.InstanceId) != inStory) continue;
                double dps;
                if (!lawCastService.TryEvaluateTarget(lawId, target.InstanceId, Time, out dps))
                    continue;
                candidates.Add(new LawDpsCandidate(target.InstanceId, inStory, true, dps));
            }
            long? chosen = SpecialGradeMobilePolicy.ChooseLawTarget(inStory, candidates);
            if (!chosen.HasValue || !characters.ContainsKey(chosen.Value) ||
                IsStoryDeployed(lawId) != IsStoryDeployed(chosen.Value)) return false;
            if (!lawCastService.TryCast(lawId, chosen.Value, Time)) return false;
            lawReadyAt[lawId] = Time + 50;
            return true;
        }

        // 평타의 보스/선두 선택 정책을 동일하게 사용한다. 별도 스킬 타깃 AI 없음.
        public string CurrentAttackTargetOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit) || !unit.AttackReady ||
                unit.AttackBlockedByMissingFormula || unit.Mapping == null) return null;
            var candidates = BuildBasicAttackCandidates(unit);
            return ChooseApprovedBossTarget(unit, candidates) ??
                attackPipeline.SelectDefaultTarget(InstanceKey(instanceId), candidates,
                    TargetPriority.Nearest);
        }

        private void OnActualDamage(string actor, string enemyId, long actualDamage, bool killed)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy)) return;
            long actorId;
            CharacterInstance unit;
            bool isMoria = long.TryParse(actor, NumberStyles.None, CultureInfo.InvariantCulture,
                out actorId) && characters.TryGetValue(actorId, out unit) &&
                unit.Catalog.Id == SpecialGradeMobilePolicy.Moria;
            moriaParticipation.Record(enemyId, isMoria, actualDamage, enemy.Kind);
            if (!killed || !moriaParticipation.Complete(enemyId)) return;
            MoriaZombieMaterials = checked(MoriaZombieMaterials + 1);
            var earned = new CharacterCombatEvent(CharacterCombatEventType.ZombieMaterialEarned,
                actor, enemyId, null, "모리아 처치 참여", 1);
            if (processingStep) events.Add(earned); else pendingCommandEvents.Add(earned);
        }

        private bool original323DefenseLinksConfigured;

        public void ConfigureOriginal323DefenseSkillLinks()
        {
            if (original323DefenseLinksConfigured || enemies.Count != 0 ||
                characters.Count != 0 || game.Phase == SessionPhase.Running)
                throw new InvalidOperationException(
                    "원본 방깎·마방깎 연결은 게임 시작 전에 한 번만 설정할 수 있습니다.");
            effectExecutor.ConfigureOriginal323DefenseSkillRouter(
                new Ordr323VerifiedDefenseEffectRouter(armorBreakStacks,
                    provenA04MStacks, statuses, VerifiedCasterRawcode));
            bonneyArmor = new Ordr323BonneyDefenseController(
                statuses, provenA04PTotals);
            attackPipeline.ConfigureVerifiedAttackDeclared(ApplyBonneyDeclaredAttack);
            original323DefenseLinksConfigured = true;
        }

        // Never infer an original W3X unit type from the catalog ID or tooltip.
        // Only a separately approved attack binding can attest the rawcode.
        private string VerifiedCasterRawcode(string casterId)
        {
            long id;
            CharacterInstance unit;
            if (!long.TryParse(casterId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out unit) ||
                !unit.AttackReady || unit.Mapping == null)
                return null;
            string rawcode = unit.Mapping.AttackSpec.UnitRawCode;
            return rawcode != null && rawcode.Length == 4 ? rawcode : null;
        }

        // Current original form has to come from an independent state/transform
        // tracker. We NEVER guess H0BX vs H0BY from the tooltips.
        public void ConfigureVerifiedBonneyFormEvidence(IOriginalBonneyFormEvidence provider)
        {
            if (!original323DefenseLinksConfigured)
                throw new InvalidOperationException("원본 2.323[C] 연결부터 구성하세요.");
            bonneyFormEvidence = provider ??
                throw new ArgumentNullException(nameof(provider));
        }

        public bool IsVerified323BonneyArmorTotal(string enemyId, int total)
        {
            int value;
            return !string.IsNullOrWhiteSpace(enemyId) && total > 0 &&
                provenA04PTotals.TryGetValue(enemyId, out value) &&
                value == total;
        }

        // Only a source-validated game combat event should call this for
        // conditional bonuses (+20/+2). RNG is not invented here.
        public bool TryApplyVerifiedBonneyArmorEvent(long casterId, string enemyId,
            Ordr323BonneyArmorEvent kind, out string reason,
            string areaCenterEnemyId = null)
        {
            reason = null;
            if (game.Phase != SessionPhase.Running ||
                bonneyArmor == null || bonneyFormEvidence == null)
            {
                reason = "원본 보니 전투/변신 상태 공급자 미연결";
                return false;
            }
            CharacterInstance caster;
            SpatialCombatEnemy target;
            if (!characters.TryGetValue(casterId, out caster) ||
                caster.Catalog.Id != "TR-21" ||
                !enemies.TryGetValue(enemyId, out target) ||
                !target.Enemy.IsAlive || !CanAttackInThisLane(caster, target))
            {
                reason = "보니 캐릭터/대상/전장 검증 실패";
                return false;
            }
            if (kind == Ordr323BonneyArmorEvent.AreaProc)
            {
                SpatialCombatEnemy center;
                if (string.IsNullOrWhiteSpace(areaCenterEnemyId) ||
                    !enemies.TryGetValue(areaCenterEnemyId, out center) ||
                    !CanAttackInThisLane(caster, center) ||
                    !Ordr323BonneyDefenseController.IsWithinVerifiedAreaProc(
                        center.X, center.Y, target.X, target.Y))
                {
                    reason = "원본 B39/B4B 320범위 대상 확인 실패";
                    return false;
                }
            }
            Ordr323BonneyForm form;
            if (!bonneyFormEvidence.TryGetVerifiedForm(casterId, out form))
            {
                reason = "보니 원본 형태 미확인";
                return false;
            }
            if (!bonneyArmor.TryApply(casterId, form, kind, target.Id, out reason))
                return false;
            var result = new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied,
                InstanceKey(casterId), target.Id, "보니 A04P",
                "jass:A04P:" + kind.ToString(), 0);
            if (processingStep) events.Add(result);
            else pendingCommandEvents.Add(result);
            return true;
        }

        private void ApplyBonneyDeclaredAttack(string attackerId, string enemyId)
        {
            if (bonneyFormEvidence == null) return;
            long casterId;
            if (!long.TryParse(attackerId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out casterId))
                return;
            CharacterInstance caster;
            if (!characters.TryGetValue(casterId, out caster) ||
                caster.Catalog.Id != "TR-21") return;
            string reason;
            if (!TryApplyVerifiedBonneyArmorEvent(casterId, enemyId,
                Ordr323BonneyArmorEvent.AttackDeclared, out reason))
            {
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred,
                    attackerId, enemyId, "보니 A04P",
                    reason ?? "원본 보니 능력 적용 보류", 0));
            }
        }

        public bool IsVerified323ArmorBreakStack(string enemyId, int stacks)
        {
            int verified;
            return !string.IsNullOrWhiteSpace(enemyId) && stacks >= 0 &&
                provenA04MStacks.TryGetValue(enemyId, out verified) &&
                verified == stacks;
        }

        public void ConfigureVerifiedSpecialEffects(
            IOriginalSpecialCombatEffectHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            effectExecutor.ConfigureVerifiedSpecialEffects(handler);
        }

        public void ConfigureVerifiedUnitAttackModifiers(IOriginalUnitAttackModifiers rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            attackPipeline.ConfigureVerifiedModifiers(rules, id =>
            {
                CombatStatusEngine state;
                if (!unitStatuses.TryGetValue(id, out state))
                    throw new KeyNotFoundException("공격자 상태 정보가 없습니다.");
                return state.Snapshot();
            });
        }

        public CombatStatusEngine UnitStatusOf(long id)
        {
            CombatStatusEngine value;
            if (!unitStatuses.TryGetValue(InstanceKey(id), out value))
                throw new KeyNotFoundException("유닛 효과 정보가 없습니다.");
            return value;
        }

        public void ApplyVerifiedUnitEffect(long id, CombatEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            UnitStatusOf(id).Apply(effect);
        }

        // 기본 공격을 등록하기 전에 특수 이벤트 매핑을 제공한다.
        // 조건 미확인 스킬은 자동으로 일반 평타 스킬로 바꾸지 않는다.
        public void ConfigureVerifiedSpecialTriggers(
            IOriginalSpecialSkillTriggerMapping mapping,
            IOriginalSpecialSkillEventRules eventRules)
        {
            if (characters.Count != 0)
                throw new InvalidOperationException(
                    "기존 캐릭터 등록 전에 특수 스킬 규칙을 설정하세요.");
            specialTriggerMapping = mapping ??
                throw new ArgumentNullException(nameof(mapping));
            specialEventRules = eventRules ??
                throw new ArgumentNullException(nameof(eventRules));
        }

        public void ConfigureVerifiedTargetSelection(
            IOriginalCombatTargetSelectionRules verifiedRules)
        {
            targetSelectionRules = verifiedRules ??
                throw new ArgumentNullException(nameof(verifiedRules));
        }

        // 검증된 오라의 원작 반경/효과만 등록한다. 대상별 효과는 매번 재계산된다.
        public void AddVerifiedEnemyAura(CombatAuraEmitter source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!characters.ContainsKey(source.UnitInstanceId))
                throw new KeyNotFoundException("오라를 사용하는 캐릭터가 없습니다.");
            auras.Add(source);
            RefreshEnemyAuras();
        }

        public void RemoveVerifiedEnemyAura(long unitInstanceId, string auraId)
        {
            auras.Remove(unitInstanceId, auraId);
            RefreshEnemyAuras();
        }

        private void RefreshEnemyAuras()
        {
            auras.Refresh(statuses, enemies.Values,
                id => characters.ContainsKey(id)
                    ? (UnitState?)roster.Get(id) : null);
        }

        // 다중 타격/지속 피해 실행 시각은 원작 출처를 가진 공급자만 연결한다.
        public void ConfigureVerifiedSkillHitTiming(IOriginalSkillHitTimingProvider rules)
        {
            effectExecutor.ConfigureVerifiedHitTiming(rules);
        }

        public void ConfigureStoryDeployment(IOriginalStoryDispatchPort verifiedStory)
        {
            storyDispatch = verifiedStory;
        }

        // Supports ALL 271 catalogue types, including the 71 without a detailed
        // skill profile. A provider must supply original-verified basic stats;
        // an unsupported unit remains spawnable but unable to attack.
        public void ConfigureVerifiedUnitAttackProvider(
            IOriginalVerifiedUnitAttackProvider originalProvider)
        {
            verifiedAttackProvider = originalProvider;
        }

        // Map object timings are useful for animation-less mobile attack cues,
        // but MUST NOT be interpreted as verified final damage or skill procs.
        public void ConfigureOriginalMapCommonAttackPreview(OriginalMapBaseStatCatalog map)
        {
            if (commonAttackPreview != null)
                throw new InvalidOperationException("흔함 공격 미리보기는 한 번만 연결할 수 있습니다.");
            commonAttackPreview = new OriginalMapCommonAttackPreview(map);
            foreach (var unit in characters.Values)
                if (!unit.AttackReady)
                    commonAttackPreview.TryTrack(unit.Id, unit.Catalog.Id, scheduler.Time);
        }

        public int BindExistingVerifiedAttacks()
        {
            if (verifiedAttackProvider == null) return 0;
            int bound = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance instance = characters[id];
                if (instance.AttackReady) continue;
                OriginalVerifiedAttackBinding found;
                if (!verifiedAttackProvider.TryResolve(instance.Catalog,
                    instance.Profile, out found)) continue;
                if (found == null)
                    throw new InvalidOperationException(
                        "원본 공격 스탯 공급자가 비어 있는 성공 결과를 반환했습니다.");
                BindVerifiedBasicAttack(id, found.Stats, found.FirstAttackDelaySeconds,
                    found.OriginalUnitRawcode);
                instance.AttackEvidence = found.SourceEvidence;
                instance.BasicTargetFilter = verifiedAttackProvider as
                    IOriginalVerifiedBasicAttackTargetFilter;
                bound++;
            }
            return bound;
        }

        public bool IsStoryDeployed(long instanceId)
        {
            return storyDispatch != null && storyDispatch.IsDeployed(instanceId);
        }

        public GameSession Game { get { return game; } }
        public UnitRoster Roster { get { return roster; } }
        public int CharacterCount { get { return characters.Count; } }
        public int EnemyCount { get { return enemies.Count; } }
        public int ReadyAttackers
        {
            get
            {
                int count = 0;
                foreach (var unit in characters.Values)
                    if (unit.AttackReady && !unit.AttackBlockedByMissingFormula) count++;
                return count;
            }
        }
        public int PreviewAttackers
        {
            get { return commonAttackPreview != null ? commonAttackPreview.Count : 0; }
        }
        public double Time { get { return scheduler.Time; } }
        public double PendingSimulationSeconds { get { return pendingStepSeconds; } }
        public IReadOnlyList<CharacterCombatEvent> LastEvents { get { return events.AsReadOnly(); } }

        // 프레임·HUD·모바일 행동 정책은 유닛과 적의 현재 상태만 조회할 수 있다.
        public IReadOnlyList<MobileUnitSnapshot> MobileUnitSnapshot()
        {
            var result = new List<MobileUnitSnapshot>();
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance unit = characters[id];
                result.Add(new MobileUnitSnapshot(id, unit.Catalog, unit.Profile, roster.Get(id)));
            }
            return result.AsReadOnly();
        }

        public IReadOnlyList<MobileEnemySnapshot> MobileEnemySnapshot()
        {
            var result = new List<MobileEnemySnapshot>();
            foreach (var enemy in enemies.Values)
                if (enemy.Enemy.IsAlive)
                    result.Add(new MobileEnemySnapshot(enemy.Id, enemy.Kind,
                        enemy.Enemy.Health, enemy.Enemy.MaxHealth, enemy.X, enemy.Y));
            result.Sort((a,b) => string.CompareOrdinal(a.Id, b.Id));
            return result.AsReadOnly();
        }

        public CharacterCombatRuntime(CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver verifiedBasicDamage,
            IVerifiedCharacterSkillDamageResolver verifiedSkillDamage,
            ICombatProcRandom random)
            : this(units, profiles, verifiedBasicDamage, verifiedSkillDamage, random,
                new UnitRoster()) { }

        public CharacterCombatRuntime(CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver verifiedBasicDamage,
            IVerifiedCharacterSkillDamageResolver verifiedSkillDamage,
            ICombatProcRandom random, UnitRoster sharedRoster)
        {
            roster = sharedRoster ?? throw new ArgumentNullException(nameof(sharedRoster));
            this.units = units ?? throw new ArgumentNullException(nameof(units));
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            if (random == null) throw new ArgumentNullException(nameof(random));
            triggers = new CombatSkillTriggerEngine(random);
            resolution = new CombatResolution(game, scheduler);
            resolution.DamageApplied += OnActualDamage;
            attackPipeline = new CombatAttackPipeline(scheduler, resolution, triggers,
                verifiedBasicDamage ?? throw new ArgumentNullException(nameof(verifiedBasicDamage)));
            // 유닛 재배치가 어려운 모바일 플레이 기준: 같은 전장 안의 평타 사거리 무제한.
            attackPipeline.ConfigureReachMode(
                CombatAttackReachMode.UnlimitedWithinAssignedBattlefield);
            effectExecutor = new CharacterSkillEffectExecutor(resolution,
                verifiedSkillDamage ?? throw new ArgumentNullException(nameof(verifiedSkillDamage)),
                statuses, armorBreakStacks, events);
            foreach (var profile in profiles.All)
                if (units.Find(profile.UnitId) == null)
                    throw new FormatException("스킬 프로필에 존재하지 않는 유닛 ID: " + profile.UnitId);
        }

        public void Start() { game.Start(); }

        public void BeginWave(int waveNumber) { game.BeginWave(waveNumber); }

        public long SpawnCharacter(string catalogUnitId, double x, double y)
        {
            CatalogUnit catalogUnit = units.Require(catalogUnitId);
            long id = roster.Add(catalogUnitId, x, y);
            var profile = profiles.Find(catalogUnitId);
            characters.Add(id, new CharacterInstance {
                Id = id, Catalog = catalogUnit, Profile = profile,
                Abilities = new CharacterActiveAbilities(profile)
            });
            unitStatuses.Add(InstanceKey(id), new CombatStatusEngine());
            bool placed;
            try
            {
                placed = MobileAutomation.TryPlace(catalogUnit, roster, id);
                if (verifiedAttackProvider != null)
                {
                    OriginalVerifiedAttackBinding original;
                    if (verifiedAttackProvider.TryResolve(catalogUnit, profile, out original))
                    {
                        if (original == null)
                            throw new InvalidOperationException(
                                "원본 공격 스탯 공급자가 null로 등록 승인을 반환했습니다.");
                        BindVerifiedBasicAttack(id, original.Stats,
                            original.FirstAttackDelaySeconds, original.OriginalUnitRawcode);
                        characters[id].AttackEvidence = original.SourceEvidence;
                        characters[id].BasicTargetFilter = verifiedAttackProvider as
                            IOriginalVerifiedBasicAttackTargetFilter;
                    }
                }
                if (!characters[id].AttackReady && commonAttackPreview != null)
                    commonAttackPreview.TryTrack(id, catalogUnitId, scheduler.Time);
            }
            catch
            {
                // Incomplete spawns never remain in the roster or skill engines.
                if (characters[id].AttackReady)
                    attackPipeline.RemoveUnit(InstanceKey(id));
                if (commonAttackPreview != null) commonAttackPreview.Remove(id);
                characters.Remove(id);
                unitStatuses.Remove(InstanceKey(id));
                roster.Remove(id);
                throw;
            }
            pendingCommandEvents.Add(new CharacterCombatEvent(CharacterCombatEventType.UnitSpawned,
                InstanceKey(id), null, null, catalogUnit.Name, 0));
            if (placed)
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.UnitPlaced, InstanceKey(id), null,
                    null, "automatic-verified-position", 0));
            return id;
        }

        public bool HasVerifiedAttack(long instanceId)
        {
            CharacterInstance instance;
            return characters.TryGetValue(instanceId, out instance) && instance.AttackReady;
        }

        // Read-only visual hint for the Unity presenter. Map weapon types alone
        // cannot establish a physical/magical damage school.
        public bool TryGetVerifiedAttackSchool(long instanceId, out DamageSchool school)
        {
            CharacterInstance instance;
            if (characters.TryGetValue(instanceId, out instance) &&
                instance.AttackReady && instance.Mapping != null)
            {
                school = instance.Mapping.AttackSpec.BasicAttack.School;
                return true;
            }
            school = DamageSchool.ScriptDefined;
            return false;
        }

        // RNG/공격 쿨타임을 움직이지 않는 읽기 전용 진단.
        public BasicAttackReadiness InspectBasicAttack(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            string id = unit.Catalog.Id;
            if (!unit.AttackReady || unit.Mapping == null)
            {
                var diagnostics = verifiedAttackProvider as IOriginalBasicAttackEvidenceDiagnostics;
                string detail = verifiedAttackProvider == null
                    ? "원본 평타 승인 공급자 미연결"
                    : diagnostics == null ? "원본 평타 공급자가 미승인"
                    : diagnostics.ExplainUnapprovedUnit(unit.Catalog);
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.UnverifiedSource, detail, null);
            }
            if (unit.AttackBlockedByMissingFormula)
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.FormulaDeferred,
                    unit.DeferredAttackReason ?? "최종 피해 공식 미확인",
                    attackPipeline.NextAttackAt(InstanceKey(instanceId)));
            var visible = BuildTargetCandidates(unit);
            if (visible.Count == 0)
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.NoBattlefieldTarget,
                    "현재 소속 전장에 살아 있는 적 없음", null);
            var eligible = BuildBasicAttackCandidates(unit);
            if (eligible.Count == 0)
            {
                var diagnostics = unit.BasicTargetFilter as IOriginalBasicAttackEvidenceDiagnostics;
                string detail = diagnostics == null
                    ? "원본 대상/방어 검증 보류"
                    : diagnostics.ExplainRejectedTarget(unit.Catalog, enemies[visible[0].Id].Enemy);
                return new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.UnverifiedTarget, detail,
                    attackPipeline.NextAttackAt(InstanceKey(instanceId)));
            }
            double? next = attackPipeline.NextAttackAt(InstanceKey(instanceId));
            return next.HasValue && !CombatScheduler.HasReached(scheduler.Time, next.Value)
                ? new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.Cooldown, "검증된 평타 공격 시점 대기", next)
                : new BasicAttackReadiness(instanceId, id,
                    BasicAttackReadinessKind.Ready, "평타 사전 검증을 통과한 대상 존재", next);
        }

        public IReadOnlyList<BasicAttackReadiness> CaptureBasicAttackDiagnostics()
        {
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            var report = new List<BasicAttackReadiness>();
            foreach (long id in ids) report.Add(InspectBasicAttack(id));
            return report.AsReadOnly();
        }

        public string AttackEvidenceOf(long instanceId)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 유닛 인스턴스.");
            return instance.AttackEvidence;
        }

        // 모바일에서는 평타의 사거리 수치가 없어도 등록할 수 있다.
        // 공격력·공속·피해계열/공식은 여전히 원작 근거가 필요하다.
        // 미등록 유닛은 선택·배치만 가능하고 공격하지 않는다.
        public IReadOnlyList<string> BindVerifiedBasicAttack(long instanceId,
            BasicAttackSpec verifiedStats, double firstAttackDelay)
        {
            return BindVerifiedBasicAttack(instanceId, verifiedStats,
                firstAttackDelay, null);
        }

        public IReadOnlyList<string> BindVerifiedBasicAttack(long instanceId,
            BasicAttackSpec verifiedStats, double firstAttackDelay,
            string originalUnitRawcode)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            if (instance.AttackReady)
                throw new InvalidOperationException("공격 스펙이 이미 등록되어 있습니다.");
            var mapping = CharacterCombatMapping.Build(instance.Catalog.Id,
                instance.Profile, verifiedStats, originalUnitRawcode,
                specialTriggerMapping);
            attackPipeline.RegisterUnit(InstanceKey(instanceId), mapping.AttackSpec,
                firstAttackDelay);
            if (commonAttackPreview != null) commonAttackPreview.Remove(instanceId);
            instance.Mapping = mapping;
            instance.AttackReady = true;
            return mapping.DeferredReasons;
        }

        // 개별 스펙 수집이 끝나면 전체 캐릭터 데이터를 건드리지 않고 자동 등록할 수 있다.
        // JSON에 basicAttackVerificationStatus == "original-confirmed"가 없으면 적용하지 않는다.
        public bool TryBindCatalogVerifiedAttack(long instanceId, double firstAttackDelay)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            var profile = unit.Profile;
            if (profile == null || !profile.HasVerifiedBasicAttack) return false;
            object rawPower = profile.BasicAttack["nominalAttackPower"];
            object rawInterval = profile.BasicAttack["intervalSeconds"];
            object rawRange;
            double rangeValue = 0; // 모바일 무제한 사거리 내부 표식, 실제 원작 사거리 아님
            if (profile.BasicAttack.TryGetValue("range", out rawRange) &&
                rawRange != null)
            {
                if (!(rawRange is double)) return false;
                rangeValue = (double)rawRange;
            }
            else if (attackPipeline.ReachMode == CombatAttackReachMode.OriginalUnitRange)
                return false;
            object rawSchool = profile.BasicAttack["damageSchool"];
            double power = (double)rawPower;
            if (power < 0 || power > long.MaxValue || Math.Floor(power) != power)
                throw new FormatException("원본 공격력은 0 이상의 정수여야 합니다.");
            DamageSchool school;
            if (!Enum.TryParse((string)rawSchool, true, out school) ||
                !Enum.IsDefined(typeof(DamageSchool), school))
                throw new FormatException("검증된 기본 피해 계열 값 오류.");
            BindVerifiedBasicAttack(instanceId,
                new BasicAttackSpec((long)power, (double)rawInterval, rangeValue,
                    school, DamageTraits.None),
                firstAttackDelay);
            return true;
        }

        // 모바일 UI가 캐릭터 상세 스킬·현재 적 체력을 읽는 조회 경로.
        // 객체를 조회해도 공격 스펙이 자동으로 활성화되지는 않는다.
        public CharacterSkillProfile ProfileOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            return unit.Profile;
        }

        public CombatEnemy EnemyOf(string enemyId)
        {
            return string.IsNullOrWhiteSpace(enemyId) ? null : resolution.GetEnemy(enemyId);
        }

        // Concrete source-backed helper-cow damage/stun sink. This uses the
        // existing CombatResolution for HP, kills and participation.
        // A07T remains blocked pending correct original A04R behavior.
        // The external world MUST resolve CHAOS/UNIVERSAL defense, ability
        // range, mana cost and immunity before passing prepared final hits.
        public bool TryApplyOriginalHelperCowAreaEffects(string originalAbilityId,
            int level, IReadOnlyList<OriginalHelperCowResolvedHit> hits)
        {
            if (game.Phase != SessionPhase.Running || hits == null ||
                hits.Count == 0) return false;
            // Reject unverified abilities/levels; do not treat every Q/W/R
            // as a helper-cow area spell.
            OriginalHelperCowAreaEvidence.JassDamage(originalAbilityId, level);
            // CJg/CJh call Bd8 -> A04R ability-level increase, NOT a
            // 5/8-second stun. Refuse A07T until that persistent ability
            // mutation and damage-reduction behavior are verified.
            if (OriginalHelperCowAreaEvidence.JassA04RLevelIncrease(
                originalAbilityId, level) != 0)
                return false;

            var distinct = new HashSet<string>(StringComparer.Ordinal);
            // Preflight the entire batch before mutating HP or statuses.
            foreach (var hit in hits)
            {
                if (hit == null || !distinct.Add(hit.EnemyId))
                    return false;
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(hit.EnemyId, out enemy) ||
                    !enemy.Enemy.IsAlive || resolution.GetEnemy(hit.EnemyId) == null ||
                    (enemy.Kind != CharacterEnemyKind.Line &&
                     enemy.Kind != CharacterEnemyKind.Boss &&
                     enemy.Kind != CharacterEnemyKind.Enraged))
                    return false;
            }

            const string actorId = "helper:h08A";
            var activated = new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, actorId, null,
                originalAbilityId, "원본 도움소 광역 시전", 0);
            if (processingStep) events.Add(activated);
            else pendingCommandEvents.Add(activated);

            foreach (var hit in hits)
            {
                long actualDamage;
                // The target is still present in CombatResolution until its
                // current hit kills it; never bypass the canonical kill count.
                if (!resolution.TryApplySkillDamage(actorId, hit.EnemyId,
                    hit.FinalDamage, out actualDamage))
                    throw new InvalidOperationException(
                        "도움소 사전 검증된 적이 타격 직전 사라졌습니다.");

                var dealt = new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, actorId, hit.EnemyId,
                    originalAbilityId, "원본 최종 피해량", actualDamage);
                if (processingStep) events.Add(dealt);
                else pendingCommandEvents.Add(dealt);

                if (resolution.GetEnemy(hit.EnemyId) == null)
                {
                    var killed = new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated, actorId, hit.EnemyId,
                        originalAbilityId, "도움소 타격으로 처치", actualDamage);
                    if (processingStep) events.Add(killed);
                    else pendingCommandEvents.Add(killed);
                }
            }

            RemoveDefeatedEnemies();
            return true;
        }

        public void ConfigureCharacterResources(long instanceId, CharacterResources verifiedResources)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            unit.Abilities.ConfigureResources(verifiedResources);
        }

        // UI의 스킬 버튼 또는 원본 조건 검증을 마친 자동 마나 스킬 호출 경로.
        // 소비 자원·쿨타임·효과 종류가 확인되지 않은 스킬은 시전 실패로 반환한다.
        public AbilityCastResult TryCastActiveSkill(long instanceId, string skillId,
            string primaryEnemyId)
        {
            if (game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("게임 진행 중에만 시전할 수 있습니다.");
            CharacterInstance caster;
            if (!characters.TryGetValue(instanceId, out caster))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");

            SpatialCombatEnemy enemy = null;
            bool hasTarget = !string.IsNullOrWhiteSpace(primaryEnemyId) &&
                enemies.TryGetValue(primaryEnemyId, out enemy) &&
                enemy.Enemy.IsAlive && CanAttackInThisLane(caster, enemy);
            // 자원을 소모하기 전에 등록된 피해 계산기의 지원 여부를 확인한다.
            if (caster.Profile != null)
                foreach (var candidate in caster.Profile.Skills)
                    if (candidate.Id == skillId &&
                        !effectExecutor.CanAttemptWithConfiguredFormula(
                            InstanceKey(instanceId), caster.Profile, candidate))
                        return AbilityCastResult.UnsupportedEffect;

            ImportedCombatSkill skill;
            bool verifiedTiming = false;
            ImportedCombatSkill selectedSkill = null;
            if (caster.Profile != null)
                foreach (var candidate in caster.Profile.Skills)
                    if (candidate.Id == skillId)
                    {
                        selectedSkill = candidate;
                        verifiedTiming = effectExecutor.HasVerifiedTimings(
                            caster.Profile, candidate);
                        break;
                    }
            AbilityCastResult result = caster.Abilities.TryCast(
                skillId, hasTarget, verifiedTiming,
                c => effectExecutor.CanHandleSpecialComponent(
                    caster.Profile, selectedSkill, c), out skill);
            if (result != AbilityCastResult.Cast) return result;

            UnitState position = roster.Get(instanceId);
            int before = events.Count;
            effectExecutor.Execute(InstanceKey(instanceId), caster.Profile, skill,
                enemy, position.X, position.Y, EligibleSkillTargets(caster));
            if (!processingStep)
                for (int index = before; index < events.Count; index++)
                    pendingCommandEvents.Add(events[index]);
            RemoveDefeatedEnemies();
            return result;
        }

        // 게임 이벤트(스택/변신/피격 등)가 원본 조건을 충족했다고 승인한 때만
        // 해당 캐릭터의 이벤트형 스킬을 실행한다.
        public bool TryActivateVerifiedSpecialEvent(long instanceId,
            string skillId, string primaryEnemyId)
        {
            if (game.Phase != SessionPhase.Running || specialEventRules == null)
                return false;
            if (string.IsNullOrWhiteSpace(skillId) ||
                string.IsNullOrWhiteSpace(primaryEnemyId)) return false;
            CharacterInstance unit;
            SpatialCombatEnemy enemy;
            if (!characters.TryGetValue(instanceId, out unit) ||
                !enemies.TryGetValue(primaryEnemyId, out enemy) ||
                !enemy.Enemy.IsAlive || unit.Mapping == null ||
                unit.Profile == null || !CanAttackInThisLane(unit, enemy))
                return false;
            ImportedCombatSkill skill;
            if (!unit.Mapping.ActivatedSkills.TryGetValue(skillId, out skill))
                return false;
            if (attackPipeline.NextAttackAt(InstanceKey(instanceId)) == null ||
                !effectExecutor.CanAttemptWithConfiguredFormula(
                    InstanceKey(instanceId), unit.Profile, skill))
                return false;
            var position = roster.Get(instanceId);
            double dx = enemy.X - position.X;
            double dy = enemy.Y - position.Y;
            double distanceSquared = dx * dx + dy * dy;
            if (!attackPipeline.CanReach(InstanceKey(unit.Id), distanceSquared) ||
                !specialEventRules.CanActivate(unit.Catalog, skill, enemy.Kind,
                    scheduler.Time))
                return false;
            if (!attackPipeline.TryActivateVerifiedCondition(
                InstanceKey(instanceId), skillId))
                return false;
            int before = events.Count;
            effectExecutor.Execute(InstanceKey(instanceId), unit.Profile,
                skill, enemy, position.X, position.Y, EligibleSkillTargets(unit));
            if (!processingStep)
                for (int i = before; i < events.Count; i++)
                    pendingCommandEvents.Add(events[i]);
            RemoveDefeatedEnemies();
            return true;
        }

        public CharacterResources ResourcesOf(long instanceId)
        {
            CharacterInstance caster;
            if (!characters.TryGetValue(instanceId, out caster))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            return caster.Abilities.Resources;
        }

        public void RemoveCharacter(long instanceId)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            if (instance.AttackReady) attackPipeline.RemoveUnit(InstanceKey(instanceId));
            if (commonAttackPreview != null) commonAttackPreview.Remove(instanceId);
            effectExecutor.CancelCaster(InstanceKey(instanceId));
            lawReadyAt.Remove(instanceId);
            auras.RemoveAll(instanceId);
            characters.Remove(instanceId);
            unitStatuses.Remove(InstanceKey(instanceId));
            RefreshEnemyAuras();
            roster.Remove(instanceId);
        }

        // Used only by an uncommitted Wisp transaction. A failed reward must
        // not leave a phantom Spawned/Placed cue in the next mobile frame.
        public void RollbackUncommittedSpawn(long instanceId)
        {
            string key = InstanceKey(instanceId);
            RemoveCharacter(instanceId);
            pendingCommandEvents.RemoveAll(evt => evt.ActorId == key &&
                (evt.Type == CharacterCombatEventType.UnitSpawned ||
                 evt.Type == CharacterCombatEventType.UnitPlaced));
            events.RemoveAll(evt => evt.ActorId == key &&
                (evt.Type == CharacterCombatEventType.UnitSpawned ||
                 evt.Type == CharacterCombatEventType.UnitPlaced));
        }

        public void MoveCharacter(long instanceId, double x, double y)
        {
            if (!characters.ContainsKey(instanceId))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            roster.Move(instanceId, x, y);
        }

        public void SpawnEnemy(string enemyId, long health, CharacterEnemyKind kind,
            double x, double y, double pathProgress)
        {
            SpawnEnemyWithDefenses(enemyId, health, kind, x, y, pathProgress,
                EnemyDefenseProfile.UnverifiedKind(kind));
        }

        // 2.323[C] 원본에서 확인한 A04Q/Def5 마법 저항값으로 생성.
        // LK[JG]와 NJ는 아이템/이벤트로 바뀔 수 있어 현재 레벨을 필수로 받는다.
        // 검증된 레벨 5..41 외에는 적을 아예 만들지 않는다.
        public void SpawnEnemyWithVerified323MagicDefense(string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            double? preAbilityArmor, string armorType,
            int currentA04QLevel, string evidence)
        {
            var defenses = Ordr323VerifiedDefenseAbilities
                .FromVerifiedMagicAbilityLevel(kind, preAbilityArmor,
                    armorType, currentA04QLevel, evidence);
            SpawnEnemyWithDefenses(enemyId, health, kind, x, y, pathProgress,
                defenses);
        }

        public void SpawnEnemyWithDefenses(string enemyId, long health,
            CharacterEnemyKind kind, double x, double y, double pathProgress,
            EnemyDefenseProfile verifiedDefenses)
        {
            if (string.IsNullOrWhiteSpace(enemyId)) throw new ArgumentException("적 ID가 필요합니다.");
            if (enemies.ContainsKey(enemyId)) throw new InvalidOperationException("이미 존재하는 적 ID입니다.");
            if (verifiedDefenses == null) throw new ArgumentNullException(nameof(verifiedDefenses));
            if (verifiedDefenses.Kind.HasValue && verifiedDefenses.Kind.Value != kind)
                throw new ArgumentException("적 분류와 방어 프로필 분류가 다릅니다.");
            var enemy = new CombatEnemy(enemyId, health, verifiedDefenses);
            var spatial = new SpatialCombatEnemy(enemy, kind, x, y, pathProgress);
            resolution.Spawn(enemy);
            enemies.Add(enemyId, spatial);
            statuses.Add(enemyId, enemy.Status);
            armorBreakStacks.Add(enemyId, 0);
            provenA04MStacks.Add(enemyId, 0);
            provenA04PTotals.Add(enemyId, 0);
            RefreshEnemyAuras();
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EnemySpawned, enemyId, enemyId,
                null, kind.ToString(), health));
        }

        // 실제 게임의 적 이동 루프에서 사용. 스턴은 이동을 막고,
        // 이감 수치는 아직 원작 이동속도 공식이 없어 외부 경로 이동기가 확인해야 한다.
        public bool TryMoveEnemy(string enemyId, double x, double y, double progress)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            if (statuses[enemyId].IsStunned()) return false;
            enemy.Move(x, y, progress);
            RefreshEnemyAuras();
            return true;
        }

        // 스턴 무시 강제 이동(원본 맵에 확인된 넉백/이동만 호출해야 한다).
        public void ForceMoveEnemy(string enemyId, double x, double y, double progress)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            enemy.Move(x, y, progress);
            RefreshEnemyAuras();
        }

        public void ResumeAfterVerifiedFormulaUpdate(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 캐릭터.");
            unit.AttackBlockedByMissingFormula = false;
            unit.DeferredAttackReason = null; // 재승인 후 이전의 실패 사유를 남기지 않는다.
        }

        public CombatStatusEngine StatusOf(string enemyId)
        {
            CombatStatusEngine value;
            return statuses.TryGetValue(enemyId, out value) ? value : null;
        }

        public int ArmorBreakStacksOf(string enemyId)
        {
            int count;
            return armorBreakStacks.TryGetValue(enemyId, out count) ? count : 0;
        }

        public bool LeakEnemy(string enemyId)
        {
            bool leaked = resolution.TryLeak(enemyId);
            if (leaked) RemoveEnemyRuntimeState(enemyId);
            return leaked;
        }

        // 공격·지속피해·상태효과의 시간을 같은 시계로 진행한다.
        // 큰 delta에서도 지나간 공격 시점을 순서대로 실행하고 누락하지 않는다.
        private void AdvanceCombatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            effectExecutor.Advance(seconds, elapsed =>
            {
                attackPipeline.Advance(elapsed);
                foreach (var unit in characters.Values)
                    unit.Abilities.Advance(elapsed);
                foreach (var status in statuses.Values)
                    status.Advance(elapsed);
                foreach (var status in unitStatuses.Values)
                    status.Advance(elapsed);
            });
        }

        private bool CanAttackInThisLane(CharacterInstance unit,
            SpatialCombatEnemy enemy)
        {
            // 크립/해왕류/도전은 별도 출전 허가가 없는 한 일반 전장에 섞이지 않는다.
            if (enemy.Kind == CharacterEnemyKind.Creep ||
                enemy.Kind == CharacterEnemyKind.SeaKing ||
                enemy.Kind == CharacterEnemyKind.Challenge)
                return false;
            bool dispatched = IsStoryDeployed(unit.Id);
            return dispatched
                ? enemy.Kind == CharacterEnemyKind.Story
                : enemy.Kind == CharacterEnemyKind.Line ||
                  enemy.Kind == CharacterEnemyKind.Boss ||
                  enemy.Kind == CharacterEnemyKind.Enraged;
        }

        // 범위 기술과 상태 효과도 기본 공격과 같은 전장의 적만 대상으로.
        private List<SpatialCombatEnemy> EligibleSkillTargets(CharacterInstance unit)
        {
            var eligible = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
                if (enemy.Enemy.IsAlive && CanAttackInThisLane(unit, enemy))
                    eligible.Add(enemy);
            return eligible;
        }

        private List<TargetCandidate> BuildTargetCandidates(CharacterInstance unit)
        {
            var position = roster.Get(unit.Id);
            var candidates = new List<TargetCandidate>();
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(unit, enemy)) continue;
                double dx = enemy.X - position.X;
                double dy = enemy.Y - position.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (double.IsInfinity(distanceSquared)) continue;
                candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                    enemy.PathProgress, true));
            }
            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return candidates;
        }

        private List<TargetCandidate> BuildBasicAttackCandidates(CharacterInstance unit)
        {
            var candidates = BuildTargetCandidates(unit);
            if (unit.BasicTargetFilter != null)
                candidates.RemoveAll(candidate => !unit.BasicTargetFilter
                    .CanAttemptBasicAttack(unit.Catalog, enemies[candidate.Id].Enemy));
            return candidates;
        }

        // 현재 표적을 가진 유닛 중 가장 빠른 공격 시점을 찾는다.
        private double? NextEligibleAttackAt()
        {
            double? next = null;
            foreach (var unit in characters.Values)
            {
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null) continue;
                double? at = attackPipeline.NextAttackAt(InstanceKey(unit.Id));
                var candidates = BuildBasicAttackCandidates(unit);
                if (attackPipeline.SelectDefaultTarget(InstanceKey(unit.Id),
                    candidates, TargetPriority.Nearest) != null &&
                    at.HasValue && (!next.HasValue || at.Value < next.Value))
                    next = at;
                // A separately verified periodic skill may target an enemy
                // whose native basic-attack armor has not been established.
                if (BuildTargetCandidates(unit).Count == 0) continue;
                foreach (var entry in unit.Mapping.ActivatedSkills)
                {
                    if (entry.Value.Trigger != "periodic") continue;
                    double? scheduled = attackPipeline.NextPeriodicSkillAt(
                        InstanceKey(unit.Id), entry.Key);
                    if (scheduled.HasValue &&
                        (!next.HasValue || scheduled.Value < next.Value))
                        next = scheduled;
                }
            }
            if (commonAttackPreview != null)
                foreach (var unit in characters.Values)
                {
                    if (unit.AttackReady) continue;
                    double? scheduled = commonAttackPreview.NextAt(unit.Id);
                    if (!scheduled.HasValue || BuildTargetCandidates(unit).Count == 0)
                        continue;
                    if (!next.HasValue || scheduled.Value < next.Value)
                        next = scheduled;
                }
            return next;
        }

        private int ExecuteReadyCommonAttackPreviews(int budget)
        {
            if (budget <= 0 || commonAttackPreview == null) return 0;
            int shown = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (shown >= budget) break;
                var unit = characters[id];
                if (unit.AttackReady) continue;
                double? scheduled = commonAttackPreview.NextAt(id);
                if (!scheduled.HasValue ||
                    !CombatScheduler.HasReached(scheduler.Time, scheduled.Value)) continue;
                var candidates = BuildTargetCandidates(unit);
                if (candidates.Count == 0) continue;
                string selected = ChoosePreviewTarget(unit, candidates);
                if (selected == null) continue;

                OriginalMapCharacterBasics evidence;
                if (!commonAttackPreview.TryCommit(id, scheduler.Time, out evidence))
                    continue;
                shown++;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.AttackPreview,
                    InstanceKey(id), selected, null,
                    evidence.OriginalRawcode + " map-base " +
                    evidence.Attack1.MinimumBaseDamage.Value + "-" +
                    evidence.Attack1.MaximumBaseDamage.Value +
                    " (미검증 최종피해: 체력 감소 없음)", 0));
            }
            return shown;
        }

        private string ChoosePreviewTarget(CharacterInstance unit,
            List<TargetCandidate> candidates)
        {
            if (targetSelectionRules != null)
            {
                var contacts = new List<VerifiedTargetContact>();
                foreach (var candidate in candidates)
                    contacts.Add(new VerifiedTargetContact(candidate,
                        enemies[candidate.Id].Kind));
                string approved;
                if (targetSelectionRules.TryChoose(unit.Catalog,
                    contacts.AsReadOnly(), double.MaxValue, out approved))
                {
                    foreach (var candidate in candidates)
                        if (candidate.Id == approved) return approved;
                    throw new InvalidOperationException(
                        "미리보기 대상 정책이 다른 전장이나 존재하지 않는 적을 선택했습니다.");
                }
            }
            var bossRules = MobileAutomation.BossRules;
            if (bossRules != null)
            {
                foreach (var candidate in candidates)
                {
                    var enemy = enemies[candidate.Id];
                    if (enemy.Kind == CharacterEnemyKind.Boss &&
                        bossRules.CanPreferBoss(unit.Catalog, enemy.Enemy,
                            candidate.DistanceSquared, double.MaxValue))
                        return candidate.Id;
                }
            }
            return CombatTargeting.Select(candidates, 0,
                TargetPriority.Nearest, true);
        }

        private int ExecuteReadyAttacks(int budget)
        {
            int fired = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (fired >= budget) break;
                CharacterInstance unit = characters[id];
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null) continue;
                var at = attackPipeline.NextAttackAt(InstanceKey(id));
                if (!at.HasValue ||
                    !CombatScheduler.HasReached(scheduler.Time, at.Value)) continue;

                var candidates = BuildBasicAttackCandidates(unit);
                string approvedBoss = ChooseApprovedBossTarget(unit, candidates);
                BasicAttackResult hit;
                try
                {
                    if (!attackPipeline.TryBasicAttack(InstanceKey(id), candidates,
                        approvedBoss, out hit))
                        continue;
                }
                catch (NotSupportedException ex)
                {
                    unit.AttackBlockedByMissingFormula = true;
                    unit.DeferredAttackReason = "평타 피해 공식 미연결: " + ex.Message;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred,
                        InstanceKey(id), null, null,
                        "평타 피해 공식 미연결: " + ex.Message, 0));
                    continue;
                }
                fired++;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    InstanceKey(id), hit.TargetId, null, "basic", hit.ActualDamage));
                SpatialCombatEnemy primary;
                if (!enemies.TryGetValue(hit.TargetId, out primary))
                    throw new InvalidOperationException("타격 대상 정보 소실");
                if (!primary.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated,
                        InstanceKey(id), primary.Id, null, "basic", 0));
                if (unit.Profile == null || unit.Mapping == null) continue;
                var position = roster.Get(id);
                foreach (string skillId in hit.ActivatedSkillRawCodes)
                {
                    ImportedCombatSkill skill;
                    if (!unit.Mapping.ActivatedSkills.TryGetValue(skillId, out skill))
                        continue;
                    effectExecutor.Execute(InstanceKey(id), unit.Profile,
                        skill, primary, position.X, position.Y, EligibleSkillTargets(unit));
                }
            }
            RemoveDefeatedEnemies();
            return fired;
        }

        private int ExecuteReadyPeriodicSkills(int budget)
        {
            int processed = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (processed >= budget) break;
                var unit = characters[id];
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null || unit.Profile == null) continue;
                var candidates = BuildTargetCandidates(unit);
                var primaryId = attackPipeline.SelectDefaultTarget(
                    InstanceKey(unit.Id), candidates, TargetPriority.Nearest);
                if (primaryId == null) continue;
                var skills = new List<string>(unit.Mapping.ActivatedSkills.Keys);
                skills.Sort(StringComparer.Ordinal);
                foreach (string skillId in skills)
                {
                    if (processed >= budget) break;
                    ImportedCombatSkill skill = unit.Mapping.ActivatedSkills[skillId];
                    if (skill.Trigger != "periodic") continue;
                    double? due = attackPipeline.NextPeriodicSkillAt(
                        InstanceKey(id), skillId);
                    if (!due.HasValue ||
                        !CombatScheduler.HasReached(scheduler.Time, due.Value)) continue;
                    // 확률 실패도 한 번의 예정 발동으로 처리해야 같은 시각에 무한 재시도하지 않는다.
                    bool activated = attackPipeline.TryActivatePeriodicSkill(
                        InstanceKey(id), skillId);
                    processed++;
                    if (!activated) continue;
                    SpatialCombatEnemy primary;
                    if (!enemies.TryGetValue(primaryId, out primary) ||
                        !primary.Enemy.IsAlive) break;
                    var position = roster.Get(id);
                    effectExecutor.Execute(InstanceKey(id), unit.Profile,
                        skill, primary, position.X, position.Y, EligibleSkillTargets(unit));
                    if (!primary.Enemy.IsAlive)
                    {
                        primaryId = attackPipeline.SelectDefaultTarget(
                            InstanceKey(unit.Id), BuildTargetCandidates(unit),
                            TargetPriority.Nearest);
                        if (primaryId == null) break;
                    }
                }
            }
            RemoveDefeatedEnemies();
            return processed;
        }

        // API에 전달한 시간은 모두 처리하거나 다음 Step으로 이월한다.
        // 과도한 프레임 지연 시 전투 시뮬레이션을 조용히 건너뛰지 않는다.
        public IReadOnlyList<CharacterCombatEvent> Step(double deltaSeconds)
        {
            if (game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("게임이 진행 중이 아닙니다.");
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0 || double.IsInfinity(pendingStepSeconds + deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            pendingStepSeconds += deltaSeconds;
            events.Clear();
            if (pendingCommandEvents.Count != 0)
            {
                events.AddRange(pendingCommandEvents);
                pendingCommandEvents.Clear();
            }
            processingStep = true;
            try
            {
                RefreshEnemyAuras();
                const int maxAttacksPerStep = 8192; // 무한 연산 방지, 잔여 시간은 이월
                int completed = 0;
                bool checkCurrentTime = true;
                while ((checkCurrentTime || pendingStepSeconds > 0) &&
                       completed < maxAttacksPerStep)
                {
                    checkCurrentTime = false;
                    double? next = NextEligibleAttackAt();
                    if (!next.HasValue || !CombatScheduler.HasReached(
                        scheduler.Time + pendingStepSeconds, next.Value))
                    {
                        AdvanceCombatTime(pendingStepSeconds);
                        pendingStepSeconds = 0;
                        MobileAutomation.RunAutomaticAbilities(this);
                        break;
                    }
                    // Never deduct a rounding residual larger than pending time.
                    double wait = Math.Min(pendingStepSeconds,
                        Math.Max(0, next.Value - scheduler.Time));
                    if (wait > 0)
                    {
                        AdvanceCombatTime(wait);
                        pendingStepSeconds -= wait;
                    }
                    MobileAutomation.RunAutomaticAbilities(this);
                    int fired = ExecuteReadyAttacks(maxAttacksPerStep - completed);
                    completed += fired;
                    int previews = ExecuteReadyCommonAttackPreviews(
                        maxAttacksPerStep - completed);
                    completed += previews;
                    int periodic = ExecuteReadyPeriodicSkills(
                        maxAttacksPerStep - completed);
                    completed += periodic;
                    if (fired == 0 && previews == 0 && periodic == 0)
                    {
                        // 자동 시전으로 적이 사라지는 경우 등 다음 시간으로 진행.
                        AdvanceCombatTime(pendingStepSeconds);
                        pendingStepSeconds = 0;
                        break;
                    }
                    if (pendingStepSeconds == 0) break;
                }
                RemoveDefeatedEnemies();
                return events.AsReadOnly();
            }
            finally
            {
                processingStep = false;
            }
        }

        private string ChooseApprovedBossTarget(CharacterInstance unit,
            List<TargetCandidate> candidates)
        {
            if (targetSelectionRules != null)
            {
                var contacts = new List<VerifiedTargetContact>();
                foreach (var candidate in candidates)
                {
                    SpatialCombatEnemy enemy;
                    if (!enemies.TryGetValue(candidate.Id, out enemy) ||
                        !enemy.Enemy.IsAlive) continue;
                    contacts.Add(new VerifiedTargetContact(candidate, enemy.Kind));
                }
                string chosen;
                if (targetSelectionRules.TryChoose(unit.Catalog,
                    contacts.AsReadOnly(),
                    attackPipeline.ReachForRuleEvaluation(InstanceKey(unit.Id)),
                    out chosen))
                {
                    if (string.IsNullOrWhiteSpace(chosen))
                        throw new InvalidOperationException("검증된 타깃 정책이 빈 ID를 반환했습니다.");
                    bool eligible = false;
                    foreach (var contact in contacts)
                        if (contact.Candidate.Id == chosen &&
                            attackPipeline.CanReach(InstanceKey(unit.Id),
                                contact.Candidate.DistanceSquared))
                        { eligible = true; break; }
                    if (!eligible)
                        throw new InvalidOperationException(
                            "타깃 정책이 다른 전장 또는 공격 불가능한 적을 반환했습니다.");
                    return chosen;
                }
            }
            var rules = MobileAutomation.BossRules;
            if (rules == null || unit.Mapping == null) return null;
            string preferred = null;
            double nearestBossDistance = double.MaxValue;
            double effectiveReach = attackPipeline.ReachForRuleEvaluation(
                InstanceKey(unit.Id));
            foreach (var candidate in candidates)
            {
                if (!candidate.Alive ||
                    !attackPipeline.CanReach(InstanceKey(unit.Id),
                        candidate.DistanceSquared)) continue;
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(candidate.Id, out enemy) ||
                    enemy.Kind != CharacterEnemyKind.Boss || !enemy.Enemy.IsAlive) continue;
                if (!rules.CanPreferBoss(unit.Catalog, enemy.Enemy,
                    candidate.DistanceSquared, effectiveReach)) continue;
                if (candidate.DistanceSquared < nearestBossDistance ||
                    (candidate.DistanceSquared == nearestBossDistance &&
                     (preferred == null || string.CompareOrdinal(candidate.Id, preferred) < 0)))
                {
                    nearestBossDistance = candidate.DistanceSquared;
                    preferred = candidate.Id;
                }
            }
            return preferred;
        }

        private void RemoveDefeatedEnemies()
        {
            var removed = new List<string>();
            foreach (var pair in enemies)
                if (!pair.Value.Enemy.IsAlive || resolution.GetEnemy(pair.Key) == null)
                    removed.Add(pair.Key);
            foreach (string id in removed) RemoveEnemyRuntimeState(id);
        }

        private void RemoveEnemyRuntimeState(string id)
        {
            moriaParticipation.Forget(id);
            effectExecutor.CancelTarget(id);
            enemies.Remove(id);
            statuses.Remove(id);
            armorBreakStacks.Remove(id);
            provenA04MStacks.Remove(id);
            provenA04PTotals.Remove(id);
        }

        private static string InstanceKey(long id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}

