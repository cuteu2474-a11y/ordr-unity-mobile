using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Remaining C03 JASS attack callbacks. A registration is NOT a claim
    // that the Warcraft native spell itself is reproduced: callbacks that
    // only cast a native dummy remain explicitly deferred.
    internal sealed class SourceRemainingCallback
    {
        internal readonly string CatalogId;
        internal readonly string Rawcode;
        internal readonly string Jass;
        internal readonly int Sides;
        internal readonly int Successes;
        internal readonly string NativeGap;

        internal SourceRemainingCallback(string id, string raw, string jass,
            int sides, int successes, string nativeGap)
        {
            CatalogId = id; Rawcode = raw; Jass = jass;
            Sides = sides; Successes = successes; NativeGap = nativeGap;
        }
    }

    internal static class SourceRemainingCallbackCatalog
    {
        internal const string JassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";

        private static readonly SourceRemainingCallback[] Entries = {
            new SourceRemainingCallback("S-06", "h00R", "Bnw", 100, 10,
                "e0KE carrionswarm 원본 피해/타격 대상 미추출"),
            new SourceRemainingCallback("S-07", "h00U", "Bny", 100, 16,
                "B00F가 없을 때 e0KD bloodlust의 지속/능력치 미추출"),
            new SourceRemainingCallback("R-08", "h022", "Bou", 100, 10,
                "e00U fanofknives 네이티브 피해/범위 미추출"),
            new SourceRemainingCallback("R-09", "h01Z", "Bow", 100, 10,
                "e01C 보조 소환의 지속 피해/폭발 조건 미추출"),
            new SourceRemainingCallback("R-22", "h029", "BpO", 100, 10,
                "e01J carrionswarm 및 세 보조 개체의 타격 자료 미추출"),
            new SourceRemainingCallback("R-24", "h027", "BpT", 100, 10,
                "h06O selfdestruct 대상·변이/소환 종료 미모델"),
            new SourceRemainingCallback("R-29", "h024", "Bpb", 1000, 125,
                "e0KE forkedlightning의 네이티브 대상/피해 미추출"),
            new SourceRemainingCallback("R-32", "h02G", "Bph", 100, 10,
                "e0KE bloodlust 본체 강화의 버프 지속/중첩 미추출"),
            new SourceRemainingCallback("R-36", "h023", "Bpv", 100, 13,
                "e0KH carrionswarm 타격/피해 미추출; 기존 A0F6 15%와 별도"),
            new SourceRemainingCallback("R-39", "h01R", "Bpz", 100, 15,
                "e0KG chainlightning 타격 대상/피해 미추출"),
            new SourceRemainingCallback("L-02", "h02P", "BsU", 1000, 88,
                "Bd8(XL) 중첩 및 적 자격·면역 미검증"),
            new SourceRemainingCallback("L-05", "h02Q", "Bsf/Bse", 100, 27,
                "GetUnitPointValue(target) 미공급: 3000배 직접 피해 보류"),
            new SourceRemainingCallback("L-12", "h02N", "BsL", 1, 1,
                "B01N buff 게이트, frostarmor/bloodlust, h05D 소환 미연결"),
            new SourceRemainingCallback("L-36", "h032", "BtV", 100, 10,
                "B01A buff gate와 BtT 양성피해 리스너, 8% A096 강화 미검증"),
            new SourceRemainingCallback("D-06", "h09V", "CD0/CDz", 1, 1,
                "B07T 분기 및 원본 방해 주문/애니메이션 미검증"),
            new SourceRemainingCallback("TR-01", "H090", "Bz1", 1, 1,
                "A0HS 특성 단계·힘 스탯 스케일·위협수준 64와 보조 효과 미검증"),
            new SourceRemainingCallback("MY-01", "h09M", "CGd", 1, 1,
                "B07E 변형/메타모포시스와 원본 주문 시전 구간 미검증")
        };

        private static readonly Dictionary<string, SourceRemainingCallback> ByRaw = Build();

        private static Dictionary<string, SourceRemainingCallback> Build()
        {
            var map = new Dictionary<string, SourceRemainingCallback>(StringComparer.Ordinal);
            foreach (var row in Entries)
            {
                if (row.Rawcode.Length != 4 || row.Sides <= 0 ||
                    row.Successes < 1 || row.Successes > row.Sides)
                    throw new InvalidOperationException("원본 잔여 콜백 등록 오류");
                map.Add(row.Rawcode, row);
            }
            if (map.Count != 17)
                throw new InvalidOperationException("원본 17종 콜백 명세 수 오류");
            return map;
        }

        internal static bool TryGet(string rawcode, out SourceRemainingCallback spec) =>
            ByRaw.TryGetValue(rawcode ?? "", out spec);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, int> remainingLaboonPositiveHits =
            new Dictionary<long, int>();
        private long remainingCallbackSerial;

        private void ForgetSourceRemainingCallbacks(long actorId)
        {
            remainingLaboonPositiveHits.Remove(actorId);
        }

        private static SourceScriptAttackSpec RemainingHitSpec(string id,
            string raw, string function, params SourceScriptHit[] hits) =>
            new SourceScriptAttackSpec(id, raw, function, 1, 1,
                Array.Empty<SourceScriptHit>(), hits, null);

        private static SourceScriptHit RemainingCircle(double radius,
            double amount, double minimum = 1, double maximum = 1,
            Ordr323AttackType attack = Ordr323AttackType.Spells) =>
            new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                amount, minimum, maximum, attack);

        private static SourceScriptHit RemainingDirect(double amount,
            Ordr323AttackType attack = Ordr323AttackType.Spells,
            double lineMaxHealthMultiplier = 0,
            double lineCurrentHealthMultiplier = 0,
            Ordr323AttackType? lineAttackOverride = null) =>
            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0, amount, 1, 1,
                attack, lineMaxHealthMultiplier: lineMaxHealthMultiplier,
                lineCurrentHealthMultiplier: lineCurrentHealthMultiplier,
                lineAttackOverride: lineAttackOverride);

        private void RemainingDeferred(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            string detail = null)
        {
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, InstanceKey(actor.Id),
                target.Id, actor.Catalog.Name + " 원본 공격 기술",
                "MODEL_ONLY " + spec.Jass + " / " + (detail ?? spec.NativeGap), 0));
        }

        private void RemainingProc(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            string description)
        {
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                target.Id, actor.Catalog.Name + " 공격 기술",
                "MODEL_ONLY " + spec.Jass + " / " + description, 0));
        }

        private void RemainingHits(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            params SourceScriptHit[] hits)
        {
            var s = RemainingHitSpec(spec.CatalogId, spec.Rawcode, spec.Jass, hits);
            ApplySourceScriptHits(actor, target, s, s.OnProc);
        }

        private void RemainingDelayedHits(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            double delay, params SourceScriptHit[] hits)
        {
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            double x = target.X, y = target.Y;
            if (remainingCallbackSerial == long.MaxValue)
                throw new OverflowException("원본 콜백 예약 순번 범위 초과");
            long serial = ++remainingCallbackSerial;
            var script = RemainingHitSpec(spec.CatalogId, spec.Rawcode, spec.Jass, hits);
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                "remaining-jass:" + actor.Id + ":" + serial,
                delay, () => {
                    if (CanFinishSourceModelDelayedAttack(actor.Id, lane, revision))
                        ApplySourceScriptHits(actor, target, script, script.OnProc, x, y);
                });
        }

        private void OnSourceRemainingDeclared(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec)
        {
            // Ten callbacks have only an externally-defined native dummy
            // order. Their exact source proc rolls are executable, but do
            // not replace undocumented spell damage with invented values.
            if (spec.CatalogId != "L-02" && spec.CatalogId != "L-12" &&
                spec.CatalogId != "L-36" && spec.CatalogId != "TR-01" &&
                spec.CatalogId != "MY-01" && spec.CatalogId != "L-05" &&
                spec.CatalogId != "D-06")
            {
                if (sourceModelRules.NextScriptRoll(spec.Sides) < spec.Successes)
                {
                    RemainingProc(actor, target, spec,
                        spec.Successes + "/" + spec.Sides + " 보조 주문 선언");
                    RemainingDeferred(actor, target, spec);
                }
                return;
            }

            if (spec.CatalogId == "L-02")
            {
                // BsU: source SLK HP35 and independent 88/1000 draw.
                // The old tooltip 8.8% packet is suppressed by the caller.
                var hp = actor.Abilities.Resources;
                if (hp == null ||
                    !sourceModelManagedResourceRows.ContainsKey(actor.Id) ||
                    Math.Abs(hp.MaximumHealth - 35) > .0001)
                {
                    RemainingDeferred(actor, target, spec, "HP35 원본 자원 없음");
                    return;
                }
                bool threshold = Math.Abs(hp.Health - 35) < .0001;
                hp.SetHealth(threshold ? 1 : Math.Min(35, hp.Health + 1));
                if (threshold)
                {
                    RemainingProc(actor, target, spec, "HP35 크리마텍트");
                    RemainingHits(actor, target, spec,
                        RemainingCircle(575, 225000, 1, 1.5));
                    RemainingDeferred(actor, target, spec);
                }
                if (sourceModelRules.NextScriptRoll(1000) < 88)
                {
                    RemainingProc(actor, target, spec, "독립 88/1000 크리마텍트");
                    RemainingHits(actor, target, spec,
                        RemainingCircle(575, 225000, 1, 1.5));
                    RemainingDeferred(actor, target, spec);
                }
                return;
            }

            if (spec.CatalogId == "L-12")
            {
                // BsL explicitly deals one hit per attack when B01N is
                // absent. Native buff tracking is not yet available; this
                // source-model branch uses the unbuffed result and flags it.
                RemainingProc(actor, target, spec, "비강화 공격/그림자 흡수");
                RemainingHits(actor, target, spec,
                    RemainingDirect(800000,
                        lineMaxHealthMultiplier: 2,
                        lineAttackOverride: Ordr323AttackType.Chaos));
                RemainingDeferred(actor, target, spec);
                return;
            }

            if (spec.CatalogId == "L-36")
            {
                // BtV uses B01A buff to select the guaranteed BtT branch;
                // absent a proven native buff provider use the unbuffed 10%
                // BtU and keep the alternative as deferred, not a fake proc.
                if (sourceModelRules.NextScriptRoll(100) < 10)
                {
                    RemainingProc(actor, target, spec, "BtU 10% 매료 타격");
                    var hanCockHits = new List<SourceScriptHit> {
                        RemainingCircle(415, 150000, 1, 1.5),
                        new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                            150000, 1, 1.5, Ordr323AttackType.Spells)
                    };
                    if (target.Kind == CharacterEnemyKind.Line)
                        hanCockHits.Add(RemainingDirect(0,
                            Ordr323AttackType.Hero,
                            lineMaxHealthMultiplier: .05));
                    RemainingDelayedHits(actor, target, spec, .36,
                        hanCockHits.ToArray());
                }
                if (sourceModelRules.NextScriptRoll(100) < 8)
                {
                    RemainingProc(actor, target, spec, "A096 독립 8% 자기 강화");
                    RemainingDeferred(actor, target, spec);
                }
                return;
            }

            if (spec.CatalogId == "TR-01")
            {
                // Exact JASS roll structure: 85 HP threshold, then an
                // independent 175/1000 (225/1000 at A0HS level2), 10% and
                // 4%. Source hero strength and target ability flags are not
                // in this model. The base damage is kept, not invented STR.
                var hp = actor.Abilities.Resources;
                if (hp != null &&
                    sourceModelManagedResourceRows.ContainsKey(actor.Id) &&
                    Math.Abs(hp.MaximumHealth - 85) < .0001)
                {
                    if (hp.Health >= 85 - .0001)
                    {
                        hp.SetHealth(1);
                        RemainingProc(actor, target, spec, "HP85 지진 폭발");
                        RemainingHits(actor, target, spec,
                            new SourceScriptHit(SourceScriptHitShape.AttackerCircle,
                                925, 0, 1750000, 1, 1,
                                Ordr323AttackType.Spells,
                                targetCurrentHealthMultiplier: .03));
                        RemainingDeferred(actor, target, spec, "힘 60000배 스케일 미공급");
                    }
                    else hp.SetHealth(Math.Min(85, hp.Health + 1));
                }
                else RemainingDeferred(actor, target, spec,
                    "HP85 원본 자원 미공급");
                if (sourceModelRules.NextScriptRoll(1000) < 175)
                {
                    RemainingProc(actor, target, spec, "175/1000 연계 공격");
                    RemainingDelayedHits(actor, target, spec, .01,
                        RemainingDirect(3000000));
                }
                if (sourceModelRules.NextScriptRoll(100) < 10)
                {
                    int alternate = sourceModelRules.NextScriptRoll(100);
                    if (alternate < 17)
                    {
                        RemainingProc(actor, target, spec, "10% → 17% 광역 연계");
                        RemainingHits(actor, target, spec,
                            new SourceScriptHit(SourceScriptHitShape.AttackerCircle,
                                700, 0, 650000, 1, 1,
                                Ordr323AttackType.Spells));
                        RemainingDeferred(actor, target, spec, "힘 5000배 스케일 미공급");
                    }
                    else
                    {
                        RemainingProc(actor, target, spec, "10% → 83% 진동 공격");
                        RemainingHits(actor, target, spec,
                            new SourceScriptHit(SourceScriptHitShape.AttackerCircle,
                                650, 0, 400000, 1, 1,
                                Ordr323AttackType.Spells));
                    }
                }
                if (sourceModelRules.NextScriptRoll(100) < 4)
                {
                    RemainingProc(actor, target, spec, "독립 4% 연쇄 타격");
                    RemainingDelayedHits(actor, target, spec, .06,
                        RemainingCircle(415, 625000));
                    RemainingDeferred(actor, target, spec, "힘 5000배 및 이동 투사체 미모델");
                }
                return;
            }

            if (spec.CatalogId == "MY-01")
            {
                var mp = actor.Abilities.Resources;
                if (mp != null &&
                    sourceModelManagedResourceRows.ContainsKey(actor.Id) &&
                    Math.Abs(mp.MaximumMana - 185) < .0001)
                {
                    if (mp.Mana >= 185 - .0001)
                    {
                        mp.SetMana(0);
                        RemainingProc(actor, target, spec, "MP185 변신 조건");
                        RemainingDeferred(actor, target, spec,
                            "원본 metamorphosis/영역과 상태 잠금 별도 엔진 필요");
                    }
                    else mp.SetMana(Math.Min(185, mp.Mana + 1));
                }
                else RemainingDeferred(actor, target, spec,
                    "MP185 원본 자원 미공급");
                // Both CGd rolls are 10% in pinned JASS (the 5% tooltip
                // is conflicting evidence). The old tooltip double roll is
                // deliberately suppressed by the bulk-auto handler.
                if (sourceModelRules.NextScriptRoll(100) < 10)
                {
                    RemainingProc(actor, target, spec, "CGZ 10% 순전 창");
                    RemainingDelayedHits(actor, target, spec, .9,
                        RemainingCircle(600, 300000));
                    for (int i = 1; i <= 20; i++)
                        RemainingDelayedHits(actor, target, spec, .93 + i * .1,
                            RemainingCircle(600, 150000));
                }
                if (sourceModelRules.NextScriptRoll(100) < 10)
                {
                    RemainingProc(actor, target, spec, "CGc 10% 무라사키");
                    RemainingDelayedHits(actor, target, spec, 1.16,
                        new SourceScriptHit(SourceScriptHitShape.Circle,
                            800, 0, 4500000, 1, 1,
                            Ordr323AttackType.Spells,
                            targetMaxHealthMultiplier: .04));
                    RemainingDeferred(actor, target, spec,
                        "B07E 버프 시 CGa 대신 CGc로 처리하는 모바일 분기");
                }
            }
        }

        private void OnSourceRemainingAfterPositiveBasicHit(
            string attackerId, string rawcode, string targetId, long actual)
        {
            if (actual <= 0 || !UsesSourceModelBasicAttacks ||
                sourceModelRules == null || !processingStep ||
                game.Phase != SessionPhase.Running) return;
            SourceRemainingCallback spec;
            if (!SourceRemainingCallbackCatalog.TryGet(rawcode, out spec) ||
                (spec.CatalogId != "L-05" && spec.CatalogId != "D-06")) return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy target;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Catalog.Id != spec.CatalogId ||
                actor.Mapping == null ||
                (actor.Mapping.AttackSpec.UnitRawCode != rawcode &&
                 sourceModelRules.SourceFormRawcodeOf(attackerId) != rawcode) ||
                !enemies.TryGetValue(targetId, out target) ||
                !CanAttackInThisLane(actor, target)) return;

            if (spec.CatalogId == "L-05")
            {
                int count;
                remainingLaboonPositiveHits.TryGetValue(actor.Id, out count);
                if (count == int.MaxValue) count = 0;
                remainingLaboonPositiveHits[actor.Id] = ++count;
                bool everySeventh = count >= 7;
                // Bse's short-circuit rolls 27% ONLY if the counter is not 7.
                if (!everySeventh && sourceModelRules.NextScriptRoll(100) >= 27)
                    return;
                remainingLaboonPositiveHits[actor.Id] = 0;
                RemainingProc(actor, target, spec,
                    everySeventh ? "7번째 적중 보장" : "적중 뒤 27% 박치기");
                // The source also deals 3000 * GetUnitPointValue(target).
                // That W3X object integer is absent from the enemy runtime;
                // model only its separate explicit 175000 radius575 packet.
                RemainingHits(actor, target, spec,
                    RemainingCircle(575, 175000));
                RemainingDeferred(actor, target, spec);
                return;
            }

            // CDz is a 1.2s positive-damage listener, then an attack-order
            // animation. No arbitrary proc roll; the original B07T buff
            // changes the path and remains separately deferred here.
            RemainingProc(actor, target, spec, "양성 피해 확인 후 파동 엘보");
            RemainingDelayedHits(actor, target, spec, .69,
                RemainingCircle(425, 360000),
                RemainingDirect(720000, lineCurrentHealthMultiplier: .36,
                    lineAttackOverride: Ordr323AttackType.Siege));
            RemainingDeferred(actor, target, spec);
        }
    }
}
