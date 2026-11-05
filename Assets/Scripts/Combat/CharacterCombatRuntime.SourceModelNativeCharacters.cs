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
        private sealed class SourceChopperBurst
        {
            public double ExpiresAt;
            public bool StoryLane;
            public long LaneRevision;
        }
        private readonly Dictionary<long, SourceChopperBurst> sourceChopperBursts =
            new Dictionary<long, SourceChopperBurst>();
        // The source Byb callback does not reveal the native metamorphosis
        // mana payment. A full pool must not create an endless 4s loop.
        // A witnessed fall below 150 is needed before the next model burst.
        private readonly HashSet<long> sourceChopperAwaitingManaReset =
            new HashSet<long>();
        private readonly Dictionary<long, int> sourceKaidoLifeCounters =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> sourceKaidoAttackCounters =
            new Dictionary<long, int>();
        private sealed class SourceKaidoArmorStacks
        {
            public int Single;
            public int Area;
        }
        private readonly Dictionary<long, Dictionary<string, SourceKaidoArmorStacks>>
            sourceKaidoArmorStacks =
                new Dictionary<long, Dictionary<string, SourceKaidoArmorStacks>>();
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
                IsStoryDeployed(creatorId), position.X, position.Y,
                creatorId, null, actor.Profile);
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
            if (sourceChopperBursts.ContainsKey(actor.Id)) return;
            var resource = actor.Abilities.Resources;
            if (resource == null || resource.MaximumMana != 150) return;
            if (resource.Mana < 150)
                sourceChopperAwaitingManaReset.Remove(actor.Id);
            if (resource.Mana == 150)
            {
                if (sourceChopperAwaitingManaReset.Contains(actor.Id) ||
                    !SourceChopperHasCompleteTooltip(actor)) return;
                // Byb requests metamorphosis at full mana; the imported
                // tooltip records 4 seconds. This is an explicitly labelled
                // tooltip-timed model, NOT a verified Warcraft native cost.
                sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "H0B6");
                sourceNativeForms[actor.Id] = "쵸파 폭주형 · H0B6 (4초 모델)";
                sourceChopperBursts[actor.Id] = new SourceChopperBurst {
                    ExpiresAt = effectExecutor.Time + 4,
                    StoryLane = IsStoryDeployed(actor.Id),
                    LaneRevision = SourceModelLaneRevision(actor.Id)
                };
                sourceChopperAwaitingManaReset.Add(actor.Id);
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    primary.Id, "쵸파 럼블볼 폭주",
                    "TOOLTIP_MODEL_ONLY Byb: MP150 → H0B6 / 4초 / 객체 공격 285001~285003·1.2초; 원본 변신 마나비용 미확인", 0));
            }
            else
                resource.SetMana(Math.Min(150, resource.Mana + 1));
        }

        private static bool SourceChopperHasCompleteTooltip(CharacterInstance actor)
        {
            if (actor.Catalog.Id != "TR-23" || actor.Profile == null ||
                actor.Profile.UnitId != "TR-23" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "H091") return false;
            var skill = FindSourceSkill(actor, "럼블볼 폭주");
            return skill != null && skill.Id == "TR-23:skill:0" &&
                skill.Trigger == "activeMana" && skill.Target == "self" &&
                skill.Number("manaCost") == 150 && skill.Components.Count == 4 &&
                skill.Components[0].Type == "transform" &&
                skill.Components[0].Text("form") == "폭주" &&
                skill.Components[0].Number("durationSeconds") == 4 &&
                skill.Components[1].Type == "basicAttackSplash" &&
                skill.Components[1].Number("radius") == 400 &&
                skill.Components[2].Type == "attackSpeedReduction" &&
                skill.Components[3].Type == "attackPowerIncrease";
        }

        private void EndSourceChopperBurst(long id, bool report)
        {
            if (!sourceChopperBursts.Remove(id)) return;
            if (sourceModelRules != null &&
                sourceModelRules.SourceFormRawcodeOf(InstanceKey(id)) == "H0B6")
                sourceModelRules.ClearSourceForm(InstanceKey(id));
            sourceNativeForms.Remove(id);
            if (report && characters.ContainsKey(id))
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied, InstanceKey(id),
                    InstanceKey(id), "쵸파 폭주 종료",
                    "TOOLTIP_MODEL_ONLY 4초 만료·원래 객체 평타 복귀; 재변신 마나 소모는 원본 미확인", 0));
        }

        private void AdvanceSourceChopperBursts(double seconds)
        {
            if (sourceChopperBursts.Count == 0) return;
            var ended = new List<long>();
            foreach (var pair in sourceChopperBursts)
            {
                CharacterInstance actor;
                if (characters.TryGetValue(pair.Key, out actor) &&
                    actor.Abilities.Resources != null &&
                    actor.Abilities.Resources.Mana < 150)
                    sourceChopperAwaitingManaReset.Remove(pair.Key);
                if (!characters.ContainsKey(pair.Key) ||
                    pair.Value.ExpiresAt <= effectExecutor.Time ||
                    pair.Value.StoryLane != IsStoryDeployed(pair.Key) ||
                    pair.Value.LaneRevision != SourceModelLaneRevision(pair.Key))
                    ended.Add(pair.Key);
            }
            foreach (long id in ended) EndSourceChopperBurst(id, true);
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
            var regenSkill = FindProfileSkill(actor.Profile, "베가펑크 마나 공격");
            if (regenSkill != null && regenSkill.Trigger == "activeMana" &&
                regenSkill.Number("manaCost") == 200 &&
                regenSkill.Components.Count == 4 &&
                regenSkill.Components[3].Type == "satelliteManaRegeneration" &&
                regenSkill.Components[3].Number("perSecond") == 25 &&
                regenSkill.Components[3].Number("durationSeconds") == 3)
                StartSourceSatelliteManaRegeneration(actor.Id, 25, 3);
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
                ExecuteKaidoHundredLifeModelPacket(actor, primary);
                return;
            }
            sourceKaidoLifeCounters[actor.Id] = Math.Min(100, life + 1);
            // CBL rolls 14% ONLY when A0PS is present; its h07M object
            // does not carry that ability. CBM h0AD does (unless B05F).
            if (SourceModelNativeCharacterRules.KaidoCanRollAutoMorph(
                    SourceKaidoIsDragon(actor.Id) ? "h0AD" : "h07M") &&
                SourceRoll1To(100) <= SourceModelNativeCharacterRules.KaidoTransformRollPercent)
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
            if (previous >= SourceModelNativeCharacterRules.KaidoSourceCounterThreshold)
            {
                sourceKaidoAttackCounters[actor.Id] = 0;
                ExecuteKaidoTenthModelPacket(actor, primary);
            }
            else
            {
                // CBL/CBM: first BFi<=8, ELSE-IF new BFi<=15,
                // otherwise CBE/CBI. These are independent ordered rolls.
                int branch = SourceRoll1To(100) <=
                    SourceModelNativeCharacterRules.KaidoFirstSpecialRollPercent ? 8 :
                    SourceRoll1To(100) <=
                    SourceModelNativeCharacterRules.KaidoSecondSpecialRollPercent ? 15 : 0;
                if (branch == 8) ExecuteKaidoEighthPercentModelPacket(actor, primary);
                else if (branch == 15) ExecuteKaidoSecondSpecialModelPacket(actor, primary);
                else ExecuteKaidoOrdinaryModelPacket(actor, primary);
                sourceKaidoAttackCounters[actor.Id] = Math.Min(
                    SourceModelNativeCharacterRules.KaidoSourceCounterThreshold,
                    previous + SourceModelNativeCharacterRules.KaidoUserDataIncrease(branch));
            }
        }

        private void ExecuteKaidoHundredLifeModelPacket(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            // B_m -> B_k/B_l: at +.38s B_j applies 2,850,000/r900,
            // then Bcc runs 1,111,111/r900 ten times at +.42s and .1s
            // intervals. Native Bcc packet flags/damage finalization are
            // not equivalent to this explicitly labelled offline model.
            string attacker = InstanceKey(actor.Id), enemyId = primary.Id;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                attacker, enemyId, "카이도 100 체력 공격",
                "MODEL_ONLY B_m→B_k/B_l: 0.38초 후 반경900 2850000 + Bcc 1111111×10", 0));
            for (int hit = 0; hit < 11; hit++)
            {
                int index = hit;
                effectExecutor.SchedulePracticeSourceAttack(attacker, enemyId,
                    index == 0 ? .38 : .42 + (index - 1) * .1, () => {
                        SpatialCombatEnemy live;
                        if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                            !enemies.TryGetValue(enemyId, out live) ||
                            !live.Enemy.IsAlive || !CanAttackInThisLane(actor, live)) return;
                        ApplySourceModelAreaPacket(attacker, lane, live, 900,
                            index == 0 ? 2850000 : 1111111,
                            index == 0 ? Ordr323AttackType.Spells : Ordr323AttackType.Chaos,
                            "카이도 100 체력 공격");
                    });
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, enemyId, "카이도 100 체력 공격",
                "Bcc 원작 공격·피해타입·지연 커맨드/면역 판정은 모델이며 네이티브 실행 미검증", 0));
        }

        private void ExecuteKaidoSecondSpecialModelPacket(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            bool dragon = SourceKaidoIsDragon(actor.Id);
            string name = dragon ? "용형 두 번째 타격" : "인간형 두 번째 타격";
            string attacker = InstanceKey(actor.Id);
            // B_5: 350000 + eventDamage*.3 in radius500,
            // 500000 extra primary; B_1: 1,200,000*random(1..2) and
            // eventDamage*.3 in radius500. EventDamage is not known here.
            double flat = dragon ? 1200000 *
                (1 + sourceModelRules.NextScriptRoll(1000000) / 1000000.0) : 350000;
            bool critical = SourceRoll1To(100) <= 40;
            if (critical) flat *= 1.5;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                attacker, primary.Id, name,
                "MODEL_ONLY B_5/B_1: CBL/CBM 두 번째 15% 분기 · 원본 고정량 부분", 0));
            ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id),
                primary, 500, flat, Ordr323AttackType.Chaos, name);
            if (!dragon) ApplySourceModelFixedPrimaryPacket(attacker, primary,
                critical ? 750000 : 500000, name);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, primary.Id, name,
                "원본 EventDamage 비례·40% 분기 난수·공격 면역·지연 판정 미검증", 0));
        }

        private void ExecuteKaidoOrdinaryModelPacket(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            bool dragon = SourceKaidoIsDragon(actor.Id);
            string name = dragon ? "용형 기본 콜백" : "인간형 기본 콜백";
            string attacker = InstanceKey(actor.Id);
            // CBI: GetEventDamage*.1 + 200000 in radius500, with two
            // delayed passes. CBE: GetEventDamage*.25 + 150000/r500,
            // plus a single-target 500000 chaos packet.
            ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id),
                primary, 500, dragon ? 200000 : 150000,
                Ordr323AttackType.Spells, name);
            if (dragon)
                ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id),
                    primary, 500, 200000, Ordr323AttackType.Spells, name);
            else
                ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id),
                    primary, 0, 500000, Ordr323AttackType.Chaos, name);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, primary.Id, name,
                "MODEL_ONLY CBE/CBI: 원본 EventDamage 비례·공격 유형·시간차 면역 미검증", 0));
        }

        private bool SourceKaidoIsDragon(long id)
        {
            string form;
            return sourceNativeForms.TryGetValue(id, out form) &&
                form.StartsWith("카이도 용형", StringComparison.Ordinal);
        }

        private void ExecuteKaidoEighthPercentModelPacket(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            bool dragon = SourceKaidoIsDragon(actor.Id);
            string name = dragon ? "용형 타격" : "인간형 타격";
            var skill = FindSourceSkill(actor, name);
            double flat = dragon ? 1200000 : 500000;
            if (skill == null || skill.Trigger != "onAttack" ||
                skill.ChancePercent != 8 || skill.Radius != 500 ||
                skill.Components.Count != (dragon ? 1 : 4) ||
                skill.Components[0].Type != "physical" ||
                skill.Components[0].Number("amount") != flat ||
                skill.Components[0].Number("plusBasicAttackDamagePercent") !=
                    (dragon ? 30 : 50)) return;
            bool critical = false;
            if (dragon)
            {
                // Tooltip multiplier 1..2; a uniform model draw is not
                // a claim about the original JASS random distribution.
                double fraction = sourceModelRules.NextScriptRoll(1000000) / 1000000.0;
                flat *= 1 + fraction;
            }
            else if (skill.Components[3].Type == "critical" &&
                skill.Components[3].Number("chancePercent") == 40 &&
                skill.Components[3].Number("multiplier") == 1.5 &&
                (critical = SourceRoll1To(100) <= 40))
                flat *= 1.5;
            string attacker = InstanceKey(actor.Id);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                attacker, primary.Id, name,
                "MODEL_ONLY CBL/CBM 8% 분기 (단일 판정) / 원본 고정량 부분", 0));
            ApplySourceModelAreaPacket(attacker, IsStoryDeployed(actor.Id), primary,
                500, flat, Ordr323AttackType.Chaos, name);
            if (!dragon)
            {
                if (skill.Components[1].Type == "fixed" &&
                    skill.Components[1].Number("amount") == 850000 &&
                    skill.Components[1].Text("scope") == "singlePrimaryTarget")
                    ApplySourceModelFixedPrimaryPacket(attacker, primary,
                        critical ? 1275000 : 850000, name);
                ApplySourceModelLineStun(attacker, primary, name, 1.5);
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, primary.Id, name,
                "평타 비례 추가피해·원본 치명/고정 피해 면역 계산 미검증", 0));
        }

        private void ApplySourceKaidoArmorStack(CharacterInstance actor,
            SpatialCombatEnemy target, bool area)
        {
            if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target)) return;
            Dictionary<string, SourceKaidoArmorStacks> perTarget;
            if (!sourceKaidoArmorStacks.TryGetValue(actor.Id, out perTarget))
            {
                perTarget = new Dictionary<string, SourceKaidoArmorStacks>(StringComparer.Ordinal);
                sourceKaidoArmorStacks.Add(actor.Id, perTarget);
            }
            SourceKaidoArmorStacks stacks;
            if (!perTarget.TryGetValue(target.Id, out stacks))
            {
                stacks = new SourceKaidoArmorStacks();
                perTarget.Add(target.Id, stacks);
            }
            bool dragon = SourceKaidoIsDragon(actor.Id);
            if (area) stacks.Area = Math.Min(10, stacks.Area + (dragon ? 2 : 1));
            else stacks.Single = Math.Min(10, stacks.Single + (dragon ? 1 : 2));
            // Explicit tooltip model: initial 70 on the first direct hit,
            // +5 for subsequent direct stacks, and +3 per area stack.
            // No original native stack-lifetime/interaction claim.
            double reduction = (stacks.Single > 0 ? 70 + 5 * (stacks.Single - 1) : 0) +
                3 * stacks.Area;
            string key = "practice-kaido:armor:" + actor.Id;
            target.Enemy.Status.RemoveSource(key);
            target.Enemy.Status.Apply(new CombatEffect(key,
                CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura,
                reduction, 0));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), target.Id, "약육강식",
                "TOOLTIP_MODEL_ONLY 방어 감소 " + reduction +
                " / 단일 " + stacks.Single + "/10 · 광역 " + stacks.Area + "/10", 0));
        }

        private void ApplySourceKaidoBasicArmor(CharacterInstance actor,
            SpatialCombatEnemy target)
        {
            if (actor.Catalog.Id != "I-11" || !target.Enemy.IsAlive) return;
            var skill = FindSourceSkill(actor, "약육강식");
            if (skill == null || skill.Trigger != "onAttackAndSkill" ||
                skill.Components.Count != 2 ||
                skill.Components[0].Type != "armorReduction" ||
                skill.Components[0].Number("amount") != 70 ||
                skill.Components[0].Number("additionalPerStack") != 5 ||
                skill.Components[0].Number("maxStacks") != 10 ||
                skill.Components[1].Type != "armorReduction" ||
                skill.Components[1].Number("amountPerStack") != 3 ||
                skill.Components[1].Number("maxStacks") != 10) return;
            ApplySourceKaidoArmorStack(actor, target, false);
        }

        private void ForgetSourceKaidoArmorEnemy(string enemyId)
        {
            foreach (var perTarget in sourceKaidoArmorStacks.Values)
                perTarget.Remove(enemyId);
        }

        private void RefreshSourceKaidoArmorLanes()
        {
            foreach (var owner in sourceKaidoArmorStacks)
            {
                CharacterInstance actor;
                if (!characters.TryGetValue(owner.Key, out actor)) continue;
                string key = "practice-kaido:armor:" + owner.Key;
                var expired = new List<string>();
                foreach (var entry in owner.Value)
                {
                    SpatialCombatEnemy target;
                    if (!enemies.TryGetValue(entry.Key, out target))
                    { expired.Add(entry.Key); continue; }
                    if (CanAttackInThisLane(actor, target)) continue;
                    target.Enemy.Status.RemoveSource(key);
                    expired.Add(entry.Key);
                }
                foreach (string enemyId in expired) owner.Value.Remove(enemyId);
            }
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
                {
                    ApplySourceModelLineStun(attacker, target, name, 2.5);
                    ApplySourceKaidoArmorStack(actor, target, true);
                }
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                attacker, primary.Id, name,
                "원본 JASS 판정·평타 비례 추가피해·고정 비율 보너스·저항 무시 미검증", 0));
        }

        private void ForgetNativeSourceCharacter(long id)
        {
            EndSourceChopperBurst(id, false);
            sourceChopperAwaitingManaReset.Remove(id);
            if (sourceModelRules != null) sourceModelRules.ClearSourceForm(InstanceKey(id));
            sourceNativeForms.Remove(id);
            sourceKaidoLifeCounters.Remove(id);
            sourceKaidoAttackCounters.Remove(id);
            sourceKaidoArmorStacks.Remove(id);
            string armorKey = "practice-kaido:armor:" + id;
            foreach (var enemy in enemies.Values)
                enemy.Enemy.Status.RemoveSource(armorKey);
            // B4a satellites are independent player-owned units; removing
            // the creator does not delete them or refund spent resources.
        }
    }
}
