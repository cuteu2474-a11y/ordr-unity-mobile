using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // C03 attack callbacks reconciled against war3map.j and the original
    // Units/AbilityData.slk + Units/UnitAbilities.slk inside the pinned W3X.
    // Native orders are executed as explicitly labeled mobile approximations;
    // order geometry, buff immunity and Warcraft target selection remain open.
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
                "A08D/사거리800/폭200/피해600 확인; 원본 자기위치 주문 방향 미검증"),
            new SourceRemainingCallback("S-07", "h00U", "Bny", 100, 16,
                "A08I/B00F 1.75초/공격속도 +360% 확인; 원본 중첩·시전 지연 미검증"),
            new SourceRemainingCallback("R-08", "h022", "Bou", 100, 10,
                "A0FC 피해10000/반경1150/최대10명 확인; 실제 투사체 판정 미검증"),
            new SourceRemainingCallback("R-09", "h01Z", "Bow", 100, 10,
                "A0FE 피해1250/간격.09/사거리1000 확인; 소환체 종료조건 미검증"),
            new SourceRemainingCallback("R-22", "h029", "BpO", 100, 10,
                "A0TP 피해6000/길이1050/폭250; 추가 3소환은 주문명령 부재"),
            new SourceRemainingCallback("R-24", "h027", "BpT", 100, 10,
                "A03A 폭발 수치500·10000 확인; 접근시간·피해영역 미검증"),
            new SourceRemainingCallback("R-29", "h024", "Bpb", 1000, 125,
                "A03O 피해7000/최대9명/사거리1750 확인; 원본 부채꼴 판정 미검증"),
            new SourceRemainingCallback("R-32", "h02G", "Bph", 100, 10,
                "A0F1/B02H 3초/공격속도 +400% 확인; 원본 중첩 미검증"),
            new SourceRemainingCallback("R-36", "h023", "Bpv", 100, 13,
                "A07K 피해8000/길이1700/폭200; 기존 A0F6 15%와 별도"),
            new SourceRemainingCallback("R-39", "h01R", "Bpz", 100, 15,
                "A054 피해6000/최대8명/도약1250/감쇠-0.1 확인; 도약 순서 미검증"),
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
        private readonly Dictionary<long, double> remainingHancockBuffUntil =
            new Dictionary<long, double>();
        private readonly Dictionary<long, string> remainingHancockEnhancedTarget =
            new Dictionary<long, string>();
        private long remainingCallbackSerial;

        private void ForgetSourceRemainingCallbacks(long actorId)
        {
            ForgetSourceAkainuBoss(actorId);
            remainingLaboonPositiveHits.Remove(actorId);
            remainingHancockBuffUntil.Remove(actorId);
            remainingHancockEnhancedTarget.Remove(actorId);
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

        private void RemainingHitsAfterBasic(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            long confirmedBasicDamage, params SourceScriptHit[] hits)
        {
            var s = RemainingHitSpec(spec.CatalogId, spec.Rawcode, spec.Jass, hits);
            ApplySourceScriptHits(actor, target, s, s.OnProc,
                sourceBasicDamage: confirmedBasicDamage);
        }

        // A source-order radius is centered on the native dummy (attacker),
        // not on the damaged enemy. Limit AEfk/ANfl to original max targets.
        // A directed ANfl order must strike the explicitly ordered primary
        // before choosing additional eligible fork targets. In the mobile
        // unlimited-range policy, 1750 is not a reason to drop the target.
        private List<SpatialCombatEnemy> RemainingOrderedNativeTargets(
            CharacterInstance actor, SpatialCombatEnemy primary,
            double x, double y, double secondaryRadius, int maximum)
        {
            var result = new List<SpatialCombatEnemy>();
            if (maximum < 1 || primary == null || !primary.Enemy.IsAlive ||
                !CanAttackInThisLane(actor, primary)) return result;
            result.Add(primary);
            var excluded = new HashSet<string>(StringComparer.Ordinal) {
                primary.Id
            };
            result.AddRange(RemainingNativeTargets(actor, x, y,
                secondaryRadius, maximum - 1, excluded));
            return result;
        }

        private List<SpatialCombatEnemy> RemainingNativeTargets(
            CharacterInstance actor, double cx, double cy, double radius,
            int maximum, HashSet<string> excluded = null)
        {
            var candidates = new List<SpatialCombatEnemy>();
            double radiusSquared = radius * radius;
            foreach (var candidate in enemies.Values)
            {
                if (!candidate.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, candidate) ||
                    (excluded != null && excluded.Contains(candidate.Id))) continue;
                double x = candidate.X - cx, y = candidate.Y - cy;
                if (x * x + y * y <= radiusSquared) candidates.Add(candidate);
            }
            candidates.Sort((a, b) => {
                double da = (a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy);
                double db = (b.X - cx) * (b.X - cx) + (b.Y - cy) * (b.Y - cy);
                int d = da.CompareTo(db);
                return d == 0 ? StringComparer.Ordinal.Compare(a.Id, b.Id) : d;
            });
            if (candidates.Count > maximum)
                candidates.RemoveRange(maximum, candidates.Count - maximum);
            return candidates;
        }

        // The original frostarmor buffs B01N/B07E do not have a source-verified
        // armor amount. Record the permanent buff *identity* in the shared
        // status lifecycle, with zero numeric armor, so a future dispel can
        // remove it without a separate permanent, undispellable hash gate.
        private static string RemainingFrostArmorSource(long actorId, string ability) =>
            "practice-native:frostarmor:" + ability + ":" + actorId;

        private bool RemainingHasFrostArmor(CharacterInstance actor, string ability)
        {
            string source = RemainingFrostArmorSource(actor.Id, ability);
            foreach (var effect in unitStatuses[InstanceKey(actor.Id)].Snapshot())
                if (effect.SourceId == source &&
                    effect.Type == CombatEffectType.ArmorBuff) return true;
            return false;
        }

        // Keep verified native buff identities in the shared status engine,
        // where existing dispels, expiry and caster removal all apply.
        // An indefinite W3X armor buff has duration zero; the explicitly
        // timed Burgess B07T follows its positive-hit damage-dependent timer.
        private void ApplySourceNativeFrostArmorIdentity(
            CharacterInstance actor, string ability, string buff,
            double duration = 0)
        {
            string owner = InstanceKey(actor.Id);
            string source = RemainingFrostArmorSource(actor.Id, ability);
            unitStatuses[owner].RemoveSource(source);
            unitStatuses[owner].Apply(new CombatEffect(source,
                CombatEffectType.ArmorBuff,
                duration > 0 ? CombatEffectOrigin.TriggeredSkill :
                    CombatEffectOrigin.Aura, 0, duration));
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, owner, owner,
                actor.Catalog.Name + " · " + buff,
                "MODEL_ONLY " + ability + "/" + buff +
                " 강화 상태 등록 / 수치 미확인·해제 가능" +
                (duration > 0 ? " / " +
                 duration.ToString(CultureInfo.InvariantCulture) + "초" : ""),
                0));
        }

        private void RemainingGrantFrostArmor(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            string ability, string buff)
        {
            ApplySourceNativeFrostArmorIdentity(actor, ability, buff);
        }

        private bool RemainingHasNativeBuff(CharacterInstance actor, string buff)
        {
            string id = "practice-native:source-remaining-bloodlust:" + buff + ":" +
                InstanceKey(actor.Id);
            foreach (var effect in unitStatuses[InstanceKey(actor.Id)].Snapshot())
                if (effect.SourceId == id &&
                    effect.Type == CombatEffectType.AttackSpeedBuff) return true;
            return false;
        }

        private void RemainingNativeBloodlust(CharacterInstance actor,
            SourceRemainingCallback spec, SpatialCombatEnemy target,
            string abilityId, string buffId, double duration, double speedPercent)
        {
            string owner = InstanceKey(actor.Id);
            string id = "practice-native:source-remaining-bloodlust:" + buffId + ":" + owner;
            var status = unitStatuses[owner];
            status.RemoveSource(id);
            status.Apply(new CombatEffect(id, CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.TriggeredSkill, speedPercent, duration));
            RemainingProc(actor, target, spec,
                abilityId + "/" + buffId + " +" + speedPercent + "% / " +
                duration.ToString(CultureInfo.InvariantCulture) + "초");
        }

        private void ExecuteRemainingNative(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec,
            double magellanSpawnRadians)
        {
            var position = roster.Get(actor.Id);
            switch (spec.CatalogId)
            {
                case "S-06": // e0KE/A08D AUcs, point order at caster location.
                    RemainingHits(actor, target, spec,
                        new SourceScriptHit(SourceScriptHitShape.ProjectileLine,
                            200, 800, 600, 1, 1, Ordr323AttackType.Spells));
                    break;
                case "S-07": // e0KD/A08I Ablo, B00F refresh gate.
                    if (!RemainingHasNativeBuff(actor, "B00F"))
                        RemainingNativeBloodlust(actor, spec, target,
                            "A08I", "B00F", 1.75, 360);
                    break;
                case "R-08": // e00U/A0FC AEfk: ten knives at most.
                    foreach (var victim in RemainingNativeTargets(actor,
                        position.X, position.Y, 1150, 10))
                        RemainingHits(actor, victim, spec, RemainingDirect(10000));
                    break;
                case "R-09": // Bow: random 150-offset dummy, A0FE/Apxf.
                    // BFj(0,360) is evaluated before the 10% JASS branch.
                    // The dummy has NO source-verified lifetime, so only
                    // the first 1250 Phoenix Fire object packet is modeled.
                    double poisonX = position.X + 150 * Math.Cos(magellanSpawnRadians);
                    double poisonY = position.Y + 150 * Math.Sin(magellanSpawnRadians);
                    var nearby = RemainingNativeTargets(actor,
                        poisonX, poisonY, 1000, 1);
                    RemainingProc(actor, target, spec,
                        "e01C 죽음 애니메이션 / 시전자 반경 150 무작위 위치");
                    if (nearby.Count > 0)
                        RemainingHits(actor, nearby[0], spec, RemainingDirect(1250));
                    break;
                case "R-22": // e01J/A0TP AUcs; three unordered FX dummies.
                    RemainingHits(actor, target, spec,
                        new SourceScriptHit(SourceScriptHitShape.ProjectileLine,
                            250, 1050, 6000, 1, 1, Ordr323AttackType.Spells));
                    break;
                case "R-24": // h06O/A03A Asds, 3.5s source timed life.
                    // The native order is selfdestruct on the chosen victim.
                    // We expose the object as a temporary non-roster actor;
                    // the .35s point impact is still a labeled approximation.
                    var doll = SpawnSourceTemporaryActor(actor,
                        "BpT · 장난감 자폭", "슈가 장난감 자폭", 3.5, "h06O",
                        false, position.X, position.Y);
                    RemainingProc(actor, target, spec,
                        "h06O 장난감 생성 / 3.5초 수명 / target selfdestruct");
                    bool dollLane = IsStoryDeployed(actor.Id);
                    long dollRevision = SourceModelLaneRevision(actor.Id);
                    double impactX = target.X, impactY = target.Y;
                    var dollImpact = RemainingHitSpec(spec.CatalogId,
                        spec.Rawcode, spec.Jass, RemainingDirect(500));
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                        "remaining-doll:" + actor.Id + ":" + doll.Id,
                        .35, () => {
                            if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                    dollLane, dollRevision) ||
                                !RetireSourceTemporaryActor(doll.Id)) return;
                            ApplySourceScriptHits(actor, target, dollImpact,
                                dollImpact.OnProc, impactX, impactY);
                        });
                    break;
                case "R-29": // e0KE/A03O ANfl, ordered target + up to 8 forks.
                    foreach (var victim in RemainingOrderedNativeTargets(
                        actor, target, position.X, position.Y, 1750, 9))
                        RemainingHits(actor, victim, spec, RemainingDirect(7000));
                    break;
                case "R-32": // e0KE/A0F1 Ablo, B02H.
                    RemainingNativeBloodlust(actor, spec, target,
                        "A0F1", "B02H", 3, 400);
                    break;
                case "R-36": // e0KH/A07K AUcs, target-oriented point order.
                    RemainingHits(actor, target, spec,
                        new SourceScriptHit(SourceScriptHitShape.ProjectileLine,
                            200, 1700, 8000, 1, 1, Ordr323AttackType.Spells));
                    break;
                case "R-39": // e0KG/A054 AOcl: eight jumps, 1250 between.
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var current = target;
                    double damage = 6000;
                    for (int bounce = 0; bounce < 8 && current != null; bounce++)
                    {
                        // A054 1600 cast range is removed by mobile's
                        // unlimited-reach rule; 1250 bounce radius and 8
                        // unique targets remain exactly bounded.
                        seen.Add(current.Id);
                        RemainingHits(actor, current, spec, RemainingDirect(damage));
                        var others = RemainingNativeTargets(actor, current.X,
                            current.Y, 1250, 1, seen);
                        current = others.Count > 0 ? others[0] : null;
                        damage *= 1.1; // A054 DataC=-0.1 (negative decay).
                    }
                    break;
            }
            RemainingDeferred(actor, target, spec);
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

        // Bd8 is not a generic slow: XL in the pinned JASS is A04R.
        // Only wave enemies registered with an object-level witness can
        // gain an additional native magic-defense level here.
        private void RemainingNamiA04R(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceRemainingCallback spec)
        {
            foreach (var enemy in RemainingNativeTargets(actor,
                primary.X, primary.Y, 575, int.MaxValue))
            {
                int oldLevel, nextLevel;
                if (!sourcePracticeA04RLevels.TryGetValue(enemy.Id, out oldLevel) ||
                    !Ordr323OriginalA04R.TryPlanIncrease(oldLevel, 1, out nextLevel))
                {
                    RemainingDeferred(actor, enemy, spec,
                        "Bd8/A04R: 대상 원본 레벨 또는 상한 미검증");
                    continue;
                }
                sourcePracticeA04RLevels[enemy.Id] = nextLevel;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                    enemy.Id, "나미 · 마법 방어 단계 변화",
                    "MODEL_ONLY Bd8: A04R " + oldLevel + "→" + nextLevel, 0));
            }
        }

        private void OnSourceRemainingDeclared(CharacterInstance actor,
            SpatialCombatEnemy target, SourceRemainingCallback spec)
        {
            // The ten native casts resolve source-object numeric impacts
            // through the same damage/status engine as scripted callbacks.
            if (spec.CatalogId != "L-02" && spec.CatalogId != "L-12" &&
                spec.CatalogId != "L-36" && spec.CatalogId != "TR-01" &&
                spec.CatalogId != "MY-01" && spec.CatalogId != "L-05" &&
                spec.CatalogId != "D-06")
            {
                // Bow computes its random dummy position BEFORE BFi's
                // attack-proc draw. Preserve this independent draw even
                // when Magellan's 10% proc does not activate.
                double magellanSpawnRadians = spec.CatalogId == "R-09"
                    ? sourceModelRules.NextScriptRoll(1000000) *
                        (2 * Math.PI / 1000000.0) : 0;
                if (sourceModelRules.NextScriptRoll(spec.Sides) < spec.Successes)
                {
                    RemainingProc(actor, target, spec,
                        spec.Successes + "/" + spec.Sides + " 원본 보조 주문");
                    ExecuteRemainingNative(actor, target, spec,
                        magellanSpawnRadians);
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
                    RemainingNamiA04R(actor, target, spec);
                }
                if (sourceModelRules.NextScriptRoll(1000) < 88)
                {
                    RemainingProc(actor, target, spec, "독립 88/1000 크리마텍트");
                    RemainingHits(actor, target, spec,
                        RemainingCircle(575, 225000, 1, 1.5));
                    RemainingNamiA04R(actor, target, spec);
                }
                return;
            }

            if (spec.CatalogId == "L-12")
            {
                // BsL casts e03T/A0AV (frostarmor, B01N) on the first
                // attack. Later attacks are gated while that buff remains;
                // the W3X object gives duration zero (indefinite).
                if (RemainingHasFrostArmor(actor, "A0AV")) return;
                RemainingGrantFrostArmor(actor, target, spec, "A0AV", "B01N");
                RemainingNativeBloodlust(actor, spec, target,
                    "A0AW", "B01O", 3, 35);
                RemainingProc(actor, target, spec, "비강화 공격/그림자 흡수");
                RemainingHits(actor, target, spec,
                    RemainingDirect(800000,
                        lineMaxHealthMultiplier: 2,
                        lineAttackOverride: Ordr323AttackType.Chaos));
                // BsL places h05D 300 units behind the damaged enemy,
                // relative to the attacking Moria. Native life is 18s.
                var moriaPosition = roster.Get(actor.Id);
                double direction = Math.Atan2(
                    target.Y - moriaPosition.Y, target.X - moriaPosition.X);
                double shadowX = target.X - 300 * Math.Cos(direction);
                double shadowY = target.Y - 300 * Math.Sin(direction);
                bool hasShadowAttack = sourceModelRules != null &&
                    sourceModelRules.SourceObjectAttack("h05D") != null;
                var shadow = SpawnSourceTemporaryActor(actor,
                    "BsL · 그림자 소환", "모리아 그림자", 18, "h05D", false,
                    shadowX, shadowY, hasShadowAttack);
                RemainingProc(actor, target, spec,
                    "h05D 그림자 " + shadow.Id + " 소환 / 18초 / " +
                    (hasShadowAttack ? "객체 평타 연결" : "원본 공격 주사위 미공급"));
                return;
            }

            if (spec.CatalogId == "L-36")
            {
                // A096 Ablo lasts exactly .4s and applies B01A. Its next
                // attack during that window enters BtT with no 10% roll.
                remainingHancockEnhancedTarget.Remove(actor.Id);
                double until;
                if (remainingHancockBuffUntil.TryGetValue(actor.Id, out until) &&
                    until > effectExecutor.Time)
                {
                    remainingHancockEnhancedTarget[actor.Id] = target.Id;
                    RemainingProc(actor, target, spec,
                        "B01A 0.4초 내 BtT 확정 · 실제 적중 피해 대기");
                    return;
                }
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
                    remainingHancockBuffUntil[actor.Id] = effectExecutor.Time + .4;
                    RemainingNativeBloodlust(actor, spec, target,
                        "A096", "B01A", .4, 45);
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
                        // CGd pauses the owner, then CGY executes 50 hits
                        // starting at .74s; the final unpause is at 6.2s.
                        // Reuse the established form-cast gate to suppress
                        // ordinary basic attacks during the native realm.
                        BeginSourceAttackPause(actor, target,
                            "고죠 · 무량공처", 6.2);
                        for (int pulse = 0; pulse < 50; pulse++)
                            RemainingDelayedHits(actor, target, spec,
                                .74 + pulse * .1,
                                new SourceScriptHit(
                                    SourceScriptHitShape.AttackerCircle,
                                    1600, 0, 450000, 1, 1,
                                    Ordr323AttackType.Chaos,
                                    targetMaxHealthMultiplier: .0045));
                        RemainingProc(actor, target, spec,
                            "CGY 50회 영역 피해 / 6.2초 일반 공격 일시정지");
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
                    // CGc issues h04V/A10C frostarmor on the caster:
                    // permanent B07E until dispelled. Later rolls take
                    // the CGa 1.5m path, not the CGc 4.5m + 4% path.
                    bool frostArmor = RemainingHasFrostArmor(actor, "A10C");
                    RemainingProc(actor, target, spec,
                        frostArmor ? "CGa 10% 아오" : "CGc 10% 무라사키");
                    if (frostArmor)
                        RemainingDelayedHits(actor, target, spec, 1.43,
                            RemainingCircle(800, 1500000));
                    else
                    {
                        RemainingGrantFrostArmor(actor, target, spec,
                            "A10C", "B07E");
                        RemainingDelayedHits(actor, target, spec, 1.13,
                            new SourceScriptHit(SourceScriptHitShape.Circle,
                                800, 0, 4500000, 1, 1,
                                Ordr323AttackType.Spells,
                                targetMaxHealthMultiplier: .04));
                    }
                    RemainingDeferred(actor, target, spec,
                        "A10C/B07E 강화 상태 기록 / WC3 시각효과·실제 디스펠 이벤트 미연결");
                }
            }
        }

        // BsM registers BsL for h05D shadows as well as h02N Moria.
        // A shadow cannot re-cast its owner's gated frostarmor/summon,
        // but BsL always opens the BsK damage-listener; its positive native
        // hit therefore splashes half the confirmed receipt too.
        private void OnSourceMoriaShadowPositiveHit(long ownerId,
            SpatialCombatEnemy target, long confirmedDamage)
        {
            CharacterInstance moria;
            SourceRemainingCallback spec;
            if (confirmedDamage <= 0 || !characters.TryGetValue(ownerId, out moria) ||
                moria.Catalog.Id != "L-12" ||
                !SourceRemainingCallbackCatalog.TryGet("h02N", out spec) ||
                !CanAttackInThisLane(moria, target)) return;
            RemainingHitsAfterBasic(moria, target, spec, confirmedDamage,
                new SourceScriptHit(SourceScriptHitShape.Circle, 375, 0,
                    0, 1, 1, Ordr323AttackType.Chaos,
                    basicHitDamageMultiplier: .5));
        }

        private void OnSourceRemainingAfterPositiveBasicHit(
            string attackerId, string rawcode, string targetId, long actual)
        {
            if (actual <= 0 || !UsesSourceModelBasicAttacks ||
                sourceModelRules == null || !processingStep ||
                game.Phase != SessionPhase.Running) return;
            SourceRemainingCallback spec;
            if (!SourceRemainingCallbackCatalog.TryGet(rawcode, out spec) ||
                (spec.CatalogId != "L-05" && spec.CatalogId != "D-06" &&
                 spec.CatalogId != "L-12" && spec.CatalogId != "L-36")) return;
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
                // Source UnitData.slk gives wave points 100/200. Only
                // registered exact original spawn rawcodes may use them.
                int? points = PracticeSourcePointValue(target.Id);
                if (points.HasValue)
                    RemainingHits(actor, target, spec,
                        RemainingDirect(3000 * points.Value,
                            Ordr323AttackType.Siege));
                else
                    RemainingDeferred(actor, target, spec,
                        "대상은 65R 원본 포인트 등록 대상이 아니므로 직접 피해 보류");
                RemainingHits(actor, target, spec,
                    RemainingCircle(575, 175000));
                // Bse pauses Laboon after the confirmed positive-hit proc,
                // then restores its queued attack order after .65s.
                BeginSourceAttackPause(actor, target, "라분 · 박치기", .65);
                return;
            }

            if (spec.CatalogId == "L-36")
            {
                string expected;
                if (!remainingHancockEnhancedTarget.TryGetValue(actor.Id,
                        out expected) || expected != target.Id) return;
                remainingHancockEnhancedTarget.Remove(actor.Id);
                RemainingProc(actor, target, spec, "BtT · 양성 적중 확인 후 확정 매료");
                RemainingHitsAfterBasic(actor, target, spec, actual,
                    new SourceScriptHit(SourceScriptHitShape.Circle, 345, 0,
                        85000, 1, 1, Ordr323AttackType.Spells,
                        basicHitDamageMultiplier: 1.25));
                RemainingHits(actor, target, spec,
                    RemainingDirect(150000,
                        lineMaxHealthMultiplier: .07,
                        lineAttackOverride: Ordr323AttackType.Hero));
                return;
            }

            if (spec.CatalogId == "L-12")
            {
                // BsK observes a positive native hit and splashes 50% of
                // that receipt to enemies around the original target.
                RemainingHitsAfterBasic(actor, target, spec, actual,
                    new SourceScriptHit(SourceScriptHitShape.Circle, 375, 0,
                        0, 1, 1, Ordr323AttackType.Chaos,
                        basicHitDamageMultiplier: .5));
                if (sourceModelRules.NextScriptRoll(100) < 10)
                {
                    RemainingProc(actor, target, spec, "BsK 10% 그림자 추격");
                    RemainingDelayedHits(actor, target, spec, .03,
                        RemainingCircle(400, 250000));
                    RemainingDelayedHits(actor, target, spec, .08,
                        RemainingDirect(200000));
                    RemainingDelayedHits(actor, target, spec, 1.08,
                        RemainingDirect(200000));
                }
                return;
            }

            // CDz: A133 frostarmor applies B07T. A subsequent positive
            // hit while the native B07T status remains takes the .03s path.
            // B07T is dispellable and may expire before another hit.
            bool armored = RemainingHasFrostArmor(actor, "A133");
            if (armored)
            {
                RemainingProc(actor, target, spec,
                    "B07T · .03초 후 빠른 파동 엘보");
                RemainingDelayedHits(actor, target, spec, .03,
                    RemainingCircle(425, 100000),
                    RemainingDirect(200000, lineCurrentHealthMultiplier: .10,
                        lineAttackOverride: Ordr323AttackType.Siege));
            }
            else
            {
                // CDz schedules removal after .315+.375+.35 windup/recovery,
                // then 12-min(confirmedBasicDamage/10000, 9) seconds.
                // Using the original positive basic-hit receipt prevents a
                // fixed 12-second approximation and permits real dispels.
                double armorSeconds = 1.04 +
                    12 - Math.Min(actual / 10000.0, 9);
                ApplySourceNativeFrostArmorIdentity(actor,
                    "A133", "B07T", armorSeconds);
                RemainingProc(actor, target, spec,
                    "B07T 획득 · .69초 후 파동 엘보");
                // CDz: .315 + .375 windup, .35 recovery; release at 1.04s.
                BeginSourceAttackPause(actor, target,
                    "바제스 · 파동 엘보", 1.04);
                RemainingDelayedHits(actor, target, spec, .69,
                    RemainingCircle(425, 360000),
                    RemainingDirect(720000, lineCurrentHealthMultiplier: .36,
                        lineAttackOverride: Ordr323AttackType.Siege));
            }
        }
    }
}
