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
        private readonly Dictionary<string, int> armorBreakStacks =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<CharacterCombatEvent> events = new List<CharacterCombatEvent>();
        private readonly List<CharacterCombatEvent> pendingCommandEvents =
            new List<CharacterCombatEvent>();
        private bool processingStep;
        private IOriginalStoryDispatchPort storyDispatch;
        public MobileCombatAutomation MobileAutomation { get; } = new MobileCombatAutomation();

        public void ConfigureStoryDeployment(IOriginalStoryDispatchPort verifiedStory)
        {
            storyDispatch = verifiedStory;
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
            bool placed;
            try
            {
                placed = MobileAutomation.TryPlace(catalogUnit, roster, id);
            }
            catch
            {
                // 원작 배치 서비스가 실패한 생성은 소환 성공으로 기록하지 않는다.
                characters.Remove(id);
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

        // 검증된 공격력·공속·사거리만 호출자가 제공한다.
        // 이를 호출하지 않은 271종 유닛도 선택·배치할 수 있으나 실제 공격하지 않는다.
        public IReadOnlyList<string> BindVerifiedBasicAttack(long instanceId,
            BasicAttackSpec verifiedStats, double firstAttackDelay)
        {
            CharacterInstance instance;
            if (!characters.TryGetValue(instanceId, out instance))
                throw new KeyNotFoundException("존재하지 않는 캐릭터 인스턴스.");
            if (instance.AttackReady)
                throw new InvalidOperationException("공격 스펙이 이미 등록되어 있습니다.");
            var mapping = CharacterCombatMapping.Build(instance.Catalog.Id, instance.Profile,
                verifiedStats);
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
            object rawRange = profile.BasicAttack["range"];
            object rawSchool = profile.BasicAttack["damageSchool"];
            double power = (double)rawPower;
            if (power < 0 || power > long.MaxValue || Math.Floor(power) != power)
                throw new FormatException("원본 공격력은 0 이상의 정수여야 합니다.");
            DamageSchool school;
            if (!Enum.TryParse((string)rawSchool, true, out school) ||
                !Enum.IsDefined(typeof(DamageSchool), school))
                throw new FormatException("검증된 기본 피해 계열 값 오류.");
            BindVerifiedBasicAttack(instanceId,
                new BasicAttackSpec((long)power, (double)rawInterval, (double)rawRange,
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
                        !effectExecutor.CanAttemptWithConfiguredFormula(candidate))
                        return AbilityCastResult.UnsupportedEffect;

            ImportedCombatSkill skill;
            AbilityCastResult result = caster.Abilities.TryCast(skillId, hasTarget, out skill);
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
            characters.Remove(instanceId);
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
            return true;
        }

        // 스턴 무시 강제 이동(원본 맵에 확인된 넉백/이동만 호출해야 한다).
        public void ForceMoveEnemy(string enemyId, double x, double y, double progress)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            enemy.Move(x, y, progress);
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

        // 엔진 시간을 진행하고, 현재 공격할 수 있는 유닛에만 1회씩 자동 공격을 시도한다.
        // 한 Tick에서 지나간 공격 횟수 추적·원작 공속 가속 공식은 별도 구현 대상이다.
        public IReadOnlyList<CharacterCombatEvent> Step(double deltaSeconds)
        {
            if (game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("게임이 진행 중이 아닙니다.");
            events.Clear();
            if (pendingCommandEvents.Count != 0)
            {
                events.AddRange(pendingCommandEvents);
                pendingCommandEvents.Clear();
            }
            attackPipeline.Advance(deltaSeconds);
            foreach (var unit in characters.Values)
                unit.Abilities.Advance(deltaSeconds);
            foreach (var status in statuses.Values) status.Advance(deltaSeconds);
            processingStep = true;
            try
            {
                // 원작 규칙 승인 후 자동 Q/W/R. 조건 미확인 시 자원·쿨타임을 소비하지 않는다.
                MobileAutomation.RunAutomaticAbilities(this);

            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance unit = characters[id];
                if (!unit.AttackReady || unit.AttackBlockedByMissingFormula ||
                    IsStoryDeployed(id)) continue;
                UnitState position = roster.Get(id);
                var candidates = new List<TargetCandidate>();
                foreach (var enemy in enemies.Values)
                {
                    if (!enemy.Enemy.IsAlive) continue;
                    double dx = enemy.X - position.X;
                    double dy = enemy.Y - position.Y;
                    double distanceSquared = dx * dx + dy * dy;
                    if (double.IsInfinity(distanceSquared)) continue;
                    candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                        enemy.PathProgress, true));
                }
                string originalApprovedBoss = ChooseApprovedBossTarget(unit, candidates);
                BasicAttackResult hit;
                try
                {
                    if (!attackPipeline.TryBasicAttack(InstanceKey(id), candidates,
                        originalApprovedBoss, out hit))
                        continue;
                }
                catch (NotSupportedException ex)
                {
                    unit.AttackBlockedByMissingFormula = true;
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                        InstanceKey(id), null, null,
                        "평타 원본 피해 공식 미연결: " + ex.Message, 0));
                    continue;
                }
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    InstanceKey(id), hit.TargetId, null, "basic", hit.ActualDamage));
                SpatialCombatEnemy primary;
                if (!enemies.TryGetValue(hit.TargetId, out primary))
                    throw new InvalidOperationException("타격 직후 대상 좌표가 소실되었습니다.");
                if (!primary.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        InstanceKey(id), primary.Id, null, "basic", 0));

                if (unit.Profile == null || unit.Mapping == null) continue;
                foreach (string skillId in hit.ActivatedSkillRawCodes)
                {
                    ImportedCombatSkill skill;
                    if (!unit.Mapping.ActivatedSkills.TryGetValue(skillId, out skill))
                        continue;
                    effectExecutor.Execute(InstanceKey(id), unit.Profile, skill,
                        primary, position.X, position.Y, enemies.Values);
                }
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
            var rules = MobileAutomation.BossRules;
            if (rules == null || unit.Mapping == null) return null;
            string preferred = null;
            double nearestBossDistance = double.MaxValue;
            double range = unit.Mapping.AttackSpec.BasicAttack.Range;
            double maximumSquared = range * range;
            foreach (var candidate in candidates)
            {
                if (!candidate.Alive || candidate.DistanceSquared > maximumSquared) continue;
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(candidate.Id, out enemy) ||
                    enemy.Kind != CharacterEnemyKind.Boss || !enemy.Enemy.IsAlive) continue;
                if (!rules.CanPreferBoss(unit.Catalog, enemy.Enemy,
                    candidate.DistanceSquared, range)) continue;
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
