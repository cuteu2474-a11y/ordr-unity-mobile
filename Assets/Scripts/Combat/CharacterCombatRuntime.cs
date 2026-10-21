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
            public string AttackEvidence;
        }

        private readonly CharacterUnitCatalog units;
        private readonly CharacterSkillCatalog profiles;
        private readonly UnitRoster roster = new UnitRoster();
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
        private readonly List<CharacterCombatEvent> events = new List<CharacterCombatEvent>();
        private readonly CombatAuraSystem auras = new CombatAuraSystem();
        private readonly List<CharacterCombatEvent> pendingCommandEvents =
            new List<CharacterCombatEvent>();
        private bool processingStep;
        private double pendingStepSeconds;
        private IOriginalStoryDispatchPort storyDispatch;
        private IOriginalVerifiedUnitAttackProvider verifiedAttackProvider;
        private IOriginalSpecialSkillTriggerMapping specialTriggerMapping;
        private IOriginalSpecialSkillEventRules specialEventRules;
        private IOriginalCombatTargetSelectionRules targetSelectionRules;
        public MobileCombatAutomation MobileAutomation { get; } = new MobileCombatAutomation();

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
                        enemy.Enemy.Health, enemy.X, enemy.Y));
            result.Sort((a,b) => string.CompareOrdinal(a.Id, b.Id));
            return result.AsReadOnly();
        }

        public CharacterCombatRuntime(CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver verifiedBasicDamage,
            IVerifiedCharacterSkillDamageResolver verifiedSkillDamage,
            ICombatProcRandom random)
        {
            this.units = units ?? throw new ArgumentNullException(nameof(units));
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            if (random == null) throw new ArgumentNullException(nameof(random));
            triggers = new CombatSkillTriggerEngine(random);
            resolution = new CombatResolution(game, scheduler);
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
                    }
                }
            }
            catch
            {
                // Incomplete spawns never remain in the roster or skill engines.
                if (characters[id].AttackReady)
                    attackPipeline.RemoveUnit(InstanceKey(id));
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
                enemy.Enemy.IsAlive;
            // 자원을 소모하기 전에 등록된 피해 계산기의 지원 여부를 확인한다.
            if (caster.Profile != null)
                foreach (var candidate in caster.Profile.Skills)
                    if (candidate.Id == skillId &&
                        !effectExecutor.CanAttemptWithConfiguredFormula(
                            caster.Profile, candidate))
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
                enemy, position.X, position.Y, enemies.Values);
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
                !effectExecutor.CanAttemptWithConfiguredFormula(unit.Profile, skill))
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
                skill, enemy, position.X, position.Y, enemies.Values);
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
            effectExecutor.CancelCaster(InstanceKey(instanceId));
            auras.RemoveAll(instanceId);
            characters.Remove(instanceId);
            unitStatuses.Remove(InstanceKey(instanceId));
            RefreshEnemyAuras();
            roster.Remove(instanceId);
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

        // 현재 표적을 가진 유닛 중 가장 빠른 공격 시점을 찾는다.
        private double? NextEligibleAttackAt()
        {
            double? next = null;
            foreach (var unit in characters.Values)
            {
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    unit.Mapping == null) continue;
                double? at = attackPipeline.NextAttackAt(InstanceKey(unit.Id));
                var candidates = BuildTargetCandidates(unit);
                if (attackPipeline.SelectDefaultTarget(InstanceKey(unit.Id),
                    candidates, TargetPriority.Nearest) == null) continue;
                if (at.HasValue && (!next.HasValue || at.Value < next.Value))
                    next = at;
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
            return next;
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
                if (!at.HasValue || at.Value > scheduler.Time) continue;

                var candidates = BuildTargetCandidates(unit);
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
                        skill, primary, position.X, position.Y, enemies.Values);
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
                    if (!due.HasValue || due.Value > scheduler.Time) continue;
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
                        skill, primary, position.X, position.Y, enemies.Values);
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
                    if (!next.HasValue || next.Value > scheduler.Time + pendingStepSeconds)
                    {
                        AdvanceCombatTime(pendingStepSeconds);
                        pendingStepSeconds = 0;
                        MobileAutomation.RunAutomaticAbilities(this);
                        break;
                    }
                    double wait = Math.Max(0, next.Value - scheduler.Time);
                    if (wait > 0)
                    {
                        AdvanceCombatTime(wait);
                        pendingStepSeconds -= wait;
                    }
                    MobileAutomation.RunAutomaticAbilities(this);
                    int fired = ExecuteReadyAttacks(maxAttacksPerStep - completed);
                    completed += fired;
                    int periodic = ExecuteReadyPeriodicSkills(
                        maxAttacksPerStep - completed);
                    completed += periodic;
                    if (fired == 0 && periodic == 0)
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
            effectExecutor.CancelTarget(id);
            enemies.Remove(id);
            statuses.Remove(id);
            armorBreakStacks.Remove(id);
        }

        private static string InstanceKey(long id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
