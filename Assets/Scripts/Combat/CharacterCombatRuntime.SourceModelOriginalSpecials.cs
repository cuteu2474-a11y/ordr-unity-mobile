using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private bool IsSourceSpecialActor(CharacterInstance actor)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                actor == null || actor.Mapping == null) return false;
            string expectedId;
            return SourceModelOriginalSpecialCallbackRules.TryGetUnit(
                actor.Mapping.AttackSpec.UnitRawCode, out expectedId) &&
                actor.Catalog.Id == expectedId;
        }

        private static readonly SourceScriptAttackSpec RyumaJass =
            new SourceScriptAttackSpec("R-07", "h025", "Bos", 1, 1,
                new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 2000, 1, 1, Ordr323AttackType.Chaos) },
                new[] { new SourceScriptHit(SourceScriptHitShape.Circle,
                    375, 0, 4500, 1, 1, Ordr323AttackType.Spells) },
                "JD/A903/A904 원본 능력 레벨·면역 확인 전 체력10% 추가피해 보류");

        private static readonly SourceScriptAttackSpec BlackbeardJass =
            new SourceScriptAttackSpec("R-03", "h021", "Boa/BoZ", 1, 1,
                null, new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                    0, 0, 15000, 1, 1, Ordr323AttackType.Spells) },
                "원작 강제 이동 BcY 타이밍·Bdi/A903/A904 네이티브 면역 미인증");

        private static readonly SourceScriptAttackSpec ZoroJass =
            new SourceScriptAttackSpec("L-27", "h02S", "Bsk/Bsj", 1, 1,
                null, new[] {
                    new SourceScriptHit(SourceScriptHitShape.Direct,
                        0, 0, 900000, 1, 1.5, Ordr323AttackType.Spells),
                    new SourceScriptHit(SourceScriptHitShape.Circle,
                        385, 0, 150000, 1, 1, Ordr323AttackType.Spells)
                }, "Bdl/A902/A911/A903/A904 네이티브 조건·투사체 연출 미인증",
                .04);

        private static readonly SourceScriptAttackSpec KumaJassAdditional =
            new SourceScriptAttackSpec("L-32", "h030", "BtL", 1, 1,
                null, new[] { new SourceScriptHit(SourceScriptHitShape.Circle,
                    500, 0, 200000, 1, 1, Ordr323AttackType.Spells) },
                "원본 이펙트 객체 및 보스 저항 미확인");

        private int SourceRoll1To(int inclusiveMaximum) =>
            sourceModelRules.NextScriptRoll(inclusiveMaximum) + 1;

        private void RecordSourceSpecial(CharacterInstance actor,
            SpatialCombatEnemy target, string skill, string note)
        {
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                target == null ? null : target.Id, skill,
                "MODEL_ONLY 원본 war3map.j " + note, 0));
        }

        private void ExecuteSourceModelOriginalSpecial(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (!IsSourceSpecialActor(actor)) return;
            switch (actor.Catalog.Id)
            {
                case "L-19": ExecuteSourceSugar(actor, primary); break;
                case "R-03": ExecuteSourceBlackbeard(actor, primary); break;
                case "R-07": ExecuteSourceRyuma(actor, primary); break;
                case "L-27": ExecuteSourceZoro(actor, primary); break;
                case "L-32": ExecuteSourceKuma(actor, primary); break;
            }
        }

        private void ExecuteSourceSugar(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            // Btq uses GetUnitLoc(GetAttacker()), never the attacked target.
            var casterPosition = roster.Get(actor.Id);
            // Btq draws 1..1000 first. If successful, one separate 1..100
            // roll determines the A0CY ability. The suicide branch is an
            // ELSE IF, not a second independent proc on the same attack.
            if (SourceModelOriginalSpecialCallbackRules.SugarToyBranch(
                    SourceRoll1To(1000)))
            {
                bool hasA0CY = SourceRoll1To(100) <=
                    SourceModelOriginalSpecialCallbackRules.SugarLootAbilityRollSuccess;
                SpawnSourceTemporaryActor(actor, "장난감 No.31",
                    "장난감 No.31", 4.5, "h06N", hasA0CY,
                    casterPosition.X, casterPosition.Y);
                RecordSourceSpecial(actor, primary, "장난감 No.31",
                    "Btq: h06N 생성 / 4.5초 / 독립 객체 평타 / A0CY 능력 " +
                    (hasA0CY ? "부여, 실제 아이템 보상 미인증" : "미부여"));
                // No invented sellable loot: A0CY is an ability granted on
                // creation, not an unconditional wallet payout at expiry.
                return;
            }
            if (SourceModelOriginalSpecialCallbackRules.SugarSuicideBranch(
                    SourceRoll1To(100)))
            {
                SpawnSourceTemporaryActor(actor, "하비하비열매",
                    "자폭 장난감", 3, "h06P", false,
                    casterPosition.X, casterPosition.Y);
                RecordSourceSpecial(actor, primary, "하비하비열매",
                    "Btq: 공격자 위치에서 h06P 생성 / 3초 / 피격 대상에게 selfdestruct 명령");
                var profile = FindSourceSkill(actor, "하비하비열매");
                if (profile != null && profile.Components.Count == 1 &&
                    profile.Components[0].Type == "summon" &&
                    profile.Components[0].Text("explosionDamageSchool") == "physical" &&
                    profile.Components[0].Number("explosionDamage") == 500000 &&
                    profile.Components[0].Number("explosionRadius") == 500)
                    ApplySourceModelAreaPacket(InstanceKey(actor.Id),
                        IsStoryDeployed(actor.Id), primary, 500, 500000,
                        Ordr323AttackType.Spells, "하비하비열매 자폭");
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                    primary.Id, "하비하비열매",
                    "TOOLTIP_MODEL_ONLY 자폭 피해 즉시 정산은 근사: 원본 AEsd 발동·피해 타이밍 미검증", 0));
            }
        }

        private void ExecuteSourceRyuma(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            // Bos always deals 2000 Chaos first; a single subsequent 10%
            // BFi chooses the 4500/r375 attack and optional health-relative
            // bonus. JD ability level and Bdi/A903/A904 target flags are not
            // sufficient in the current practice enemy data.
            ApplySourceScriptHits(actor, primary, RyumaJass, RyumaJass.Always);
            if (SourceRoll1To(100) > 10) return;
            RecordSourceSpecial(actor, primary, "흑도 슈스이",
                "Bos: 10% / 반경375 / 4500 기본 광역피해");
            ApplySourceScriptHits(actor, primary, RyumaJass, RyumaJass.OnProc);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                primary.Id, "흑도 슈스이",
                "원본 Bdi/JD/A903/A904 미확인: 현재체력 10% 추가피해 보류", 0));
        }

        private void ExecuteSourceBlackbeard(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (SourceRoll1To(100) > 12) return;
            RecordSourceSpecial(actor, primary, "검은수염 어둠",
                "Boa: 12% / BoZ 지연 0.01+0.13초");
            string actorKey = InstanceKey(actor.Id);
            string enemyId = primary.Id;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            // Original BcY displacement is conditional on Bdl(). No
            // guessed immunity state for bosses/stories: model only a line.
            if (primary.Kind == CharacterEnemyKind.Line)
            {
                var pos = roster.Get(actor.Id);
                double dx = primary.X - pos.X, dy = primary.Y - pos.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0)
                {
                    double pullX = pos.X + dx / length * 82.0;
                    double pullY = pos.Y + dy / length * 82.0;
                    effectExecutor.SchedulePracticeSourceAttack(
                        actorKey, enemyId, .01, () => {
                            SpatialCombatEnemy living;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(enemyId, out living) ||
                                !living.Enemy.IsAlive ||
                                living.Kind != CharacterEnemyKind.Line) return;
                            ForceMoveEnemy(enemyId, pullX, pullY, living.PathProgress);
                            sourceSpecialPullHeldUntil[enemyId] = effectExecutor.Time + .12;
                        });
                }
            }
            effectExecutor.SchedulePracticeSourceAttack(actorKey, enemyId, .14,
                () => {
                    SpatialCombatEnemy living;
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                        !enemies.TryGetValue(enemyId, out living) ||
                        !living.Enemy.IsAlive) return;
                    ApplySourceScriptHits(actor, living,
                        BlackbeardJass, BlackbeardJass.OnProc);
                });
        }

        private readonly System.Collections.Generic.Dictionary<string, double>
            sourceSpecialPullHeldUntil =
                new System.Collections.Generic.Dictionary<string, double>(
                    StringComparer.Ordinal);

        public bool IsSourceModelPullHeld(string enemyId)
        {
            double until;
            return UsesSourceModelBasicAttacks && enemyId != null &&
                (SourceModelTwelfthBonneyRooted(enemyId) ||
                 (sourceSpecialPullHeldUntil.TryGetValue(enemyId, out until) &&
                  effectExecutor.Time < until));
        }

        private void ExecuteSourceZoro(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            // Bdl is !A902&&!A911&&!A903&&!A904. In the source practice
            // ONLY ordinary line enemies are modeled as eligible. No native
            // ability-state certification or boss execution.
            bool sourceLineProxy = primary.Kind == CharacterEnemyKind.Line;
            bool threshold = SourceModelOriginalSpecialCallbackRules
                .ZoroSourceExecuteCondition(sourceLineProxy,
                    primary.Enemy.Health, primary.Enemy.MaxHealth);
            if (!threshold && SourceRoll1To(100) > 10) return;
            RecordSourceSpecial(actor, primary, "사자의노래",
                "Bsk/Bsj: " + (threshold ? "연습 일반라인 15% 체력 조건" : "10% 분기") +
                " / .04초 후 900000×[1..1.5], 150000/r385");
            string actorKey = InstanceKey(actor.Id), victim = primary.Id;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            effectExecutor.SchedulePracticeSourceAttack(actorKey, victim, .04,
                () => {
                    SpatialCombatEnemy living;
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                        !enemies.TryGetValue(victim, out living) ||
                        !living.Enemy.IsAlive) return;
                    ApplySourceScriptHits(actor, living, ZoroJass, ZoroJass.OnProc);
                    // Original Bsj's maxHP*2 hit runs after the AoE and only
                    // if Bdl still holds AND the target remains under 15%.
                    if (living.Kind != CharacterEnemyKind.Line ||
                        !living.Enemy.IsAlive ||
                        !SourceModelOriginalSpecialCallbackRules
                            .ZoroSourceExecuteCondition(true,
                                living.Enemy.Health, living.Enemy.MaxHealth))
                        return;
                    double planned = living.Enemy.MaxHealth * 2.0;
                    if (planned >= long.MaxValue) return;
                    ApplySourceScriptHits(actor, living, ZoroJass,
                        new[] { new SourceScriptHit(SourceScriptHitShape.Direct,
                            0, 0, planned, 1, 1, Ordr323AttackType.Chaos) });
                });
        }

        private void ExecuteSourceKuma(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var resource = actor.Abilities.Resources;
            if (resource != null)
            {
                // BtL: EXACT mana==115 fires, otherwise +1 per attacked
                // event. The previous independent auto-cast path double-paid
                // at a different event phase and is intentionally disabled.
                if (resource.MaximumMana == 115 && resource.Mana == 115)
                {
                    resource.SetMana(0);
                    ImportedCombatSkill skill = FindSourceSkill(actor, "우르스스 쇼크");
                    if (skill != null && skill.Number("manaCost") == 115 &&
                        skill.Components.Count == 2)
                    {
                        var targets = SourceScriptTargets(actor, primary,
                            new SourceScriptHit(SourceScriptHitShape.Circle,
                                600, 0, 0, 1, 1, Ordr323AttackType.Spells));
                        RecordSourceSpecial(actor, primary, "우르스스 쇼크",
                            "BtL: 115 마나 공격 선언 시 사용 / 반경600, 2450000+잃은HP4%");
                        ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
                    }
                }
                else if (resource.MaximumMana == 115 &&
                    resource.Mana < 115)
                    resource.SetMana(resource.Mana + 1);
            }
            if (SourceRoll1To(100) <= 10)
            {
                RecordSourceSpecial(actor, primary, "발바닥발바닥 열매",
                    "BtL: 10% / 반경500 / 200000 단일 광역 호출");
                ApplySourceScriptHits(actor, primary, KumaJassAdditional,
                    KumaJassAdditional.OnProc);
            }
        }
    }
}
