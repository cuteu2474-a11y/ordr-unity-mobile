using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

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
            public bool AttackReady;
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

        public GameSession Game { get { return game; } }
        public UnitRoster Roster { get { return roster; } }
        public int CharacterCount { get { return characters.Count; } }
        public int EnemyCount { get { return enemies.Count; } }
        public double Time { get { return scheduler.Time; } }
        public IReadOnlyList<CharacterCombatEvent> LastEvents { get { return events.AsReadOnly(); } }

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
            characters.Add(id, new CharacterInstance {
                Id = id, Catalog = catalogUnit, Profile = profiles.Find(catalogUnitId)
            });
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.UnitSpawned,
                InstanceKey(id), null, null, catalogUnit.Name, 0));
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
            if (string.IsNullOrWhiteSpace(enemyId)) throw new ArgumentException("적 ID가 필요합니다.");
            if (enemies.ContainsKey(enemyId)) throw new InvalidOperationException("이미 존재하는 적 ID입니다.");
            var enemy = new CombatEnemy(enemyId, health);
            var spatial = new SpatialCombatEnemy(enemy, kind, x, y, pathProgress);
            resolution.Spawn(enemy);
            enemies.Add(enemyId, spatial);
            statuses.Add(enemyId, new CombatStatusEngine());
            armorBreakStacks.Add(enemyId, 0);
        }

        public void MoveEnemy(string enemyId, double x, double y, double progress)
        {
            SpatialCombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy))
                throw new KeyNotFoundException("존재하지 않는 적 ID.");
            enemy.Move(x, y, progress);
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
            attackPipeline.Advance(deltaSeconds);
            foreach (var status in statuses.Values) status.Advance(deltaSeconds);

            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance unit = characters[id];
                if (!unit.AttackReady) continue;
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
                BasicAttackResult hit;
                if (!attackPipeline.TryBasicAttack(InstanceKey(id), candidates, out hit))
                    continue;
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
