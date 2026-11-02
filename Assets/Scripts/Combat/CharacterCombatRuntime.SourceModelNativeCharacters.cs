using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Source-backed offline 2.323[C] progression and object-data basic attacks.
    // Native splash, original form spells and full combat parity stay unverified.
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, string> sourceNativeForms =
            new Dictionary<long, string>();
        private readonly Dictionary<long, int> sourceKaidoLifeCounters =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> sourceKaidoAttackCounters =
            new Dictionary<long, int>();
        private readonly List<SourceModelVegapunkSatellite> sourceSatellites =
            new List<SourceModelVegapunkSatellite>();
        private long sourceNextSatelliteId;

        private static readonly SourceScriptAttackSpec VegapunkSourceAttack =
            new SourceScriptAttackSpec("TR-30", "H0AA", "B4l/B4e", 1, 1,
                null, new[] {
                    new SourceScriptHit(SourceScriptHitShape.Circle, 575, 0,
                        4250000, 1, 1, Ordr323AttackType.Chaos),
                    new SourceScriptHit(SourceScriptHitShape.Circle, 575, 0,
                        4250000, 1, 1, Ordr323AttackType.Chaos)
                },
                "B4e 두 UnitDamagePoint의 서로 다른 DAMAGE_TYPE: 실전 방어/면역 미검증");

        public string PracticeNativeFormOf(long instanceId)
        {
            string label;
            return UsesSourceModelBasicAttacks && characters.ContainsKey(instanceId) &&
                sourceNativeForms.TryGetValue(instanceId, out label) ? label : null;
        }

        public int PracticeKaidoAttackCounterOf(long instanceId)
        {
            int count;
            return UsesSourceModelBasicAttacks && characters.ContainsKey(instanceId) &&
                sourceKaidoAttackCounters.TryGetValue(instanceId, out count) ? count : 0;
        }

        public IReadOnlyList<SourceModelVegapunkSatellite> PracticeSatellites()
        {
            return sourceSatellites.AsReadOnly();
        }

        public bool HasPracticeSatellite(string rawcode)
        {
            foreach (var state in sourceSatellites)
                if (state.OriginalRawcode == rawcode) return true;
            return false;
        }

        public int PracticeSatelliteCorePurchases()
        {
            int count = 0;
            foreach (var state in sourceSatellites)
                if (state.OriginalRawcode == "H0AB" ||
                    state.OriginalRawcode == "H0AE") count++;
            return count;
        }

        // Original B4a one-ability-per-PLAYER checkout. The Android shared
        // wallet accepts source-confirmed story BdL and personal mission points;
        // it remains unavailable until those entitlements are actually earned.
        // Purchased satellites have their own identity, separate from the
        // character fusion roster, and cannot yield invented combat/rewards.
        public bool TryAcquirePracticeSatellite(long creatorId, string rawcode,
            EconomyWallet sharedWallet)
        {
            if (sharedWallet == null) throw new ArgumentNullException(nameof(sharedWallet));
            if (!ReferenceEquals(sharedWallet, otamaRewardWallet) ||
                !UsesSourceModelBasicAttacks ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 ||
                sourceSatellites.Count >= 3 || HasPracticeSatellite(rawcode))
                return false;
            CharacterInstance actor;
            if (!characters.TryGetValue(creatorId, out actor) ||
                actor.Catalog.Id != SourceModelNativeCharacterRules.Vegapunk ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "H0AA") return false;
            long points, wood;
            if (!SourceModelNativeCharacterRules.TryPlanSatelliteCost(rawcode,
                PracticeSatelliteCorePurchases(), out points, out wood))
                return false;
            if (sourceModelRules == null ||
                sourceModelRules.SourceObjectAttack(rawcode) == null) return false;
            if (sourceNextSatelliteId == long.MaxValue)
                throw new OverflowException("위성 ID 저장 용량 초과");
            var costs = new ResourceBundle()
                .Add(SourceModelNativeCharacterRules.TraitPointsResourceId, points);
            if (wood > 0)
                costs.Add(SourceModelNativeCharacterRules.WoodResourceId, wood);
            string receipt = SourceModelNativeCharacterRules.SatelliteActionId(rawcode);
            if (!sharedWallet.TryExchangeOnce(receipt, costs, new ResourceBundle()))
                return false;
            var position = roster.Get(creatorId);
            var state = new SourceModelVegapunkSatellite(
                ++sourceNextSatelliteId, creatorId, rawcode, scheduler.Time);
            sourceSatellites.Add(state);
            RegisterSourceIndependentAttack(SatelliteAttackKey(state.Id), rawcode,
                IsStoryDeployed(creatorId), position.X, position.Y, null,
                actor.Profile);
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(creatorId),
                InstanceKey(creatorId), "베가펑크 · " + state.Name,
                "MODEL_ONLY B4a: 원본 특성포인트 " + points +
                "/목재 " + wood + " 소비 / 원본 객체 평타 모델 가동 (네이티브 스킬 미검증)", 0));
            return true;
        }

        private void ExecuteNativeSourceCharacterAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (actor.Catalog.Id == SourceModelNativeCharacterRules.Chopper)
                ExecuteChopperSourceAttack(actor, primary);
            else if (actor.Catalog.Id == SourceModelNativeCharacterRules.Vegapunk)
                ExecuteVegapunkSourceAttack(actor, primary);
            else if (actor.Catalog.Id == SourceModelNativeCharacterRules.Kaido)
                ExecuteKaidoSourceAttack(actor, primary);
        }

        private void ExecuteChopperSourceAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (sourceNativeForms.ContainsKey(actor.Id)) return;
            var resource = actor.Abilities.Resources;
            if (resource == null || resource.MaximumMana != 150) return;
            if (resource.Mana == 150)
            {
                // Byb issues 'metamorphosis' at full mana. It DOES NOT
                // explicitly spend mana; native ability costs are unknown.
                // We track the requested native identity H0B6, rather than
                // inventing a four-second buff or a new basic attack spec.
                sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "H0B6");
                sourceNativeForms[actor.Id] = "쵸파 폭주형 · H0B6 (객체 평타 모델)";
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    primary.Id, "쵸파 럼블볼 폭주",
                    "MODEL_ONLY Byb: MP150 → H0B6 기본 공격 285001~285003 / 1.2초; 원본 변신 비용·스플래시 미검증", 0));
            }
            else
                resource.SetMana(Math.Min(150, resource.Mana + 1));
        }

        private void ExecuteVegapunkSourceAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var resource = actor.Abilities.Resources;
            if (resource == null || resource.MaximumMana != 200) return;
            if (resource.Mana < 200)
            {
                resource.SetMana(Math.Min(200, resource.Mana + 1));
                return;
            }
            resource.SetMana(0);
            string actorKey = InstanceKey(actor.Id), enemyId = primary.Id;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, actorKey, enemyId,
                "베가펑크 마나 공격",
                "MODEL_ONLY B4l: MP200→0 / B4e 1.23초 지연 후 2×4250000 반경575", 0));
            effectExecutor.SchedulePracticeSourceAttack(actorKey, enemyId,
                SourceModelNativeCharacterRules.VegapunkMainDelaySeconds, () => {
                    SpatialCombatEnemy live;
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                        !enemies.TryGetValue(enemyId, out live) ||
                        !live.Enemy.IsAlive || !CanAttackInThisLane(actor, live)) return;
                    ApplySourceScriptHits(actor, live, VegapunkSourceAttack,
                        VegapunkSourceAttack.OnProc);
                });
        }

        private void ExecuteKaidoSourceAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            int life;
            if (!sourceKaidoLifeCounters.TryGetValue(actor.Id, out life))
                life = 100; // Original UnitBalance.slk HP=100 at spawn.
            if (life >= 100)
            {
                // CBL/CBM call B_m on exactly 100 current HP: the source
                // counter resets to 1 and starts a large native attack.
                sourceKaidoLifeCounters[actor.Id] = 1;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                    primary.Id, "카이도 100 체력 공격",
                    "MODEL_ONLY CBL/CBM: B_m→Jc/Jh / native 피해 콜백 정산 대기", 0));
                return;
            }
            sourceKaidoLifeCounters[actor.Id] = Math.Min(100, life + 1);
            if (SourceRoll1To(100) <= 14)
            {
                string former;
                sourceNativeForms.TryGetValue(actor.Id, out former);
                bool dragon = former != null && former.StartsWith("카이도 용형", StringComparison.Ordinal);
                if (dragon) sourceModelRules.ClearSourceForm(InstanceKey(actor.Id));
                else sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "h0AD");
                sourceNativeForms[actor.Id] = dragon ?
                    "카이도 인간형 · h07M (기본 평타 복귀)" :
                    "카이도 용형 · h0AD (객체 평타 모델)";
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    primary.Id, "카이도 변신",
                    "MODEL_ONLY CBL/CBM: 14% 변신 / h0AD 기본 공격 250001·1.25초 (용형 시); B05F·A0PS·스플래시 미검증", 0));
                // Original branch stops other procs on this attacked event.
                return;
            }
            int previous;
            sourceKaidoAttackCounters.TryGetValue(actor.Id, out previous);
            int current = previous >= 10 ? 1 : previous + 1;
            sourceKaidoAttackCounters[actor.Id] = current;
            if (current == 10)
                ExecuteKaidoTenthModelPacket(actor, primary);
        }

        private void ExecuteKaidoTenthModelPacket(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            string current;
            bool dragon = sourceNativeForms.TryGetValue(actor.Id, out current) &&
                current.StartsWith("카이도 용형", StringComparison.Ordinal);
            string name = dragon ? "용형 열 번째 공격" : "인간형 열 번째 공격";
            var skill = FindSourceSkill(actor, name);
            double flat = dragon ? 3600000 : 2000000;
            double percent = dragon ? 300 : 400;
            if (skill == null || skill.Trigger != "everyNthAttack" ||
                skill.Number("requiredAttacks") != 10 || skill.Radius != 500 ||
                skill.Components.Count != 3 || skill.Components[0].Type != "physical" ||
                skill.Components[0].Number("amount") != flat ||
                skill.Components[0].Number("plusBasicAttackDamagePercent") != percent ||
                skill.Components[2].Type != "stun" ||
                skill.Components[2].Number("durationSeconds") != 2.5)
            {
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                    InstanceKey(actor.Id), primary.Id, name,
                    "10회 공격 도감 패킷 불완전: 피해 보류", 0));
                return;
            }
            string attacker = InstanceKey(actor.Id);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                attacker, primary.Id, name,
                "TOOLTIP_MODEL_ONLY 10번째 공격: 확정 고정 물리 부분만 정산", 0));
            ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id),
                primary, 500, flat, Ordr323AttackType.Spells, name);
            foreach (var target in enemies.Values)
            {
                double dx = target.X - primary.X, dy = target.Y - primary.Y;
                if (dx * dx + dy * dy <= 500 * 500 &&
                    CanAttackInThisLane(actor, target))
                    ApplySourceModelLineStun(attacker, target, name, 2.5);
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, primary.Id, name,
                "원본 JASS 판정·평타 비례 추가피해·고정 비율 보너스·저항 무시 미검증", 0));
        }

        private void ForgetNativeSourceCharacter(long id)
        {
            if (sourceModelRules != null) sourceModelRules.ClearSourceForm(InstanceKey(id));
            sourceNativeForms.Remove(id);
            sourceKaidoLifeCounters.Remove(id);
            sourceKaidoAttackCounters.Remove(id);
            // B4a satellites are independent player-owned units; removing
            // the creator does not delete them or refund spent resources.
        }
    }
}
