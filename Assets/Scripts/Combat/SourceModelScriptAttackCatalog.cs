using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Offline practice ONLY. Each row is transcribed from the pinned w5 attack
    // callbacks in original-2323C-attack-skill-batch.json. These are source
    // instructions, NOT permission to claim Warcraft native spell parity.
    public enum SourceScriptHitShape { Direct, Circle, RankyakuLine, AttackerCircle, ProjectileLine, ForkedLightningCone }

    // Attack type and JASS damage type are separate Warcraft parameters.
    // Retain the old model behavior for skills without a pinned damage type.
    public enum SourceScriptDamageType { ModelDefault, Universal }

    public sealed class SourceScriptHit
    {
        public SourceScriptHitShape Shape { get; private set; }
        public double Radius { get; private set; }
        // AUcs DataD: travelling wave's ending half-width.
        public double EndRadius { get; private set; }
        // Native ANfl's target cap and unit-target cast range.
        public int MaxTargets { get; private set; }
        public double CastRange { get; private set; }
        public double Length { get; private set; }
        public double Damage { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public Ordr323AttackType Attack { get; private set; }
        public SourceScriptDamageType DamageType { get; private set; }
        public bool UsesArmorRating => DamageType != SourceScriptDamageType.Universal;
        // JASS Bcb uses target HP at damage time for some attacks. These
        // alternative branches are source-callback inputs, not extra hits.
        public double BossCurrentHealthPercent { get; private set; }
        public double EnragedMaxHealthPercent { get; private set; }
        public bool StoryOnly { get; private set; }
        // Original A904 story marker can replace (not add to) the
        // normal hit's damage operand; keep one hit/defense application.
        public double StoryDamage { get; private set; }
        // BFi(5000,100000): exact inclusive integer source roll;
        // zero bounds mean the ordinary fixed/ranged damage path.
        public int RandomIntegerMinimum { get; private set; }
        public int RandomIntegerMaximum { get; private set; }
        // C03 callbacks whose damage is proportional to the confirmed basic
        // hit. Their native damage-listener armor inversion is not yet exact.
        public double BasicHitDamageMultiplier { get; private set; }
        public double TargetMaxHealthMultiplier { get; private set; }
        public double TargetCurrentHealthMultiplier { get; private set; }
        public double LineMaxHealthMultiplier { get; private set; }
        public double LineCurrentHealthMultiplier { get; private set; }
        public Ordr323AttackType? LineAttackOverride { get; private set; }

        internal SourceScriptHit(SourceScriptHitShape shape, double radius,
            double length, double damage, double minimum, double maximum,
            Ordr323AttackType attack,
            double bossCurrentHealthPercent = 0,
            double enragedMaxHealthPercent = 0,
            bool storyOnly = false, double storyDamage = 0,
            int randomIntegerMinimum = 0, int randomIntegerMaximum = 0,
            double basicHitDamageMultiplier = 0,
            double targetMaxHealthMultiplier = 0,
            double targetCurrentHealthMultiplier = 0,
            double lineMaxHealthMultiplier = 0,
            double lineCurrentHealthMultiplier = 0,
            Ordr323AttackType? lineAttackOverride = null,
            double endRadius = -1, int maxTargets = 0, double castRange = 0,
            SourceScriptDamageType damageType = SourceScriptDamageType.ModelDefault)
        {
            if (!Enum.IsDefined(typeof(SourceScriptDamageType), damageType))
                throw new ArgumentOutOfRangeException(nameof(damageType));
            if (shape == SourceScriptHitShape.ForkedLightningCone &&
                (maxTargets <= 0 || castRange <= 0 || length <= 0 ||
                 radius < 0 || endRadius < 0 || damage <= 0))
                throw new ArgumentOutOfRangeException(nameof(maxTargets),
                    "원본 갈래 번개 범위·피해·대상 수가 올바르지 않습니다.");
            Shape = shape; Radius = radius; EndRadius = endRadius >= 0 ? endRadius : radius;
            MaxTargets = maxTargets; CastRange = castRange;
            Length = length; Damage = damage;
            Minimum = minimum; Maximum = maximum; Attack = attack;
            DamageType = damageType;
            BossCurrentHealthPercent = bossCurrentHealthPercent;
            EnragedMaxHealthPercent = enragedMaxHealthPercent;
            StoryOnly = storyOnly; StoryDamage = storyDamage;
            RandomIntegerMinimum = randomIntegerMinimum;
            RandomIntegerMaximum = randomIntegerMaximum;
            BasicHitDamageMultiplier = basicHitDamageMultiplier;
            TargetMaxHealthMultiplier = targetMaxHealthMultiplier;
            TargetCurrentHealthMultiplier = targetCurrentHealthMultiplier;
            LineMaxHealthMultiplier = lineMaxHealthMultiplier;
            LineCurrentHealthMultiplier = lineCurrentHealthMultiplier;
            LineAttackOverride = lineAttackOverride;
        }
    }

    // Shared authored tapered-cone geometry: the caster is the origin;
    // Area1 is the initial half-width and DataD1 the final half-width.
    // The heading is supplied by the individual native ability's order.
    public static class SourceModelTaperedConeGeometry
    {
        public static bool Contains(SourceScriptHit wave, double originX,
            double originY, double facingDegrees, double targetX, double targetY)
        {
            if (wave == null ||
                (wave.Shape != SourceScriptHitShape.RankyakuLine &&
                 wave.Shape != SourceScriptHitShape.ForkedLightningCone) ||
                wave.Length <= 0 || wave.Radius < 0 || wave.EndRadius < 0 ||
                double.IsNaN(facingDegrees) || double.IsInfinity(facingDegrees))
                return false;
            double angle = facingDegrees * Math.PI / 180;
            double fx = Math.Cos(angle), fy = Math.Sin(angle);
            double x = targetX - originX, y = targetY - originY;
            double along = x * fx + y * fy;
            if (along < 0 || along > wave.Length) return false;
            double across = Math.Abs(x * fy - y * fx);
            double width = wave.Radius +
                (wave.EndRadius - wave.Radius) * along / wave.Length;
            return across <= width;
        }
    }

    // Keep the existing Rankyaku interface stable for its callers/tests.
    public static class SourceModelRankyakuGeometry
    {
        public static bool Contains(SourceScriptHit wave, double originX,
            double originY, double facingDegrees, double targetX, double targetY)
        {
            return wave != null && wave.Shape == SourceScriptHitShape.RankyakuLine &&
                SourceModelTaperedConeGeometry.Contains(wave, originX,
                    originY, facingDegrees, targetX, targetY);
        }
    }

    public sealed class SourceScriptAttackSpec
    {
        public string CatalogId { get; private set; }
        public string UnitRawcode { get; private set; }
        public string JassFunction { get; private set; }
        public int RollSides { get; private set; }
        public int RollSuccess { get; private set; }
        // Cracker's direct Bcb occurs on EVERY attack, BEFORE its 8% branch.
        public IReadOnlyList<SourceScriptHit> Always { get; private set; }
        public IReadOnlyList<SourceScriptHit> OnProc { get; private set; }
        public string DeferredNativeEffect { get; private set; }
        public double DelaySeconds { get; private set; }

        internal SourceScriptAttackSpec(string id, string raw, string function,
            int sides, int success, SourceScriptHit[] always,
            SourceScriptHit[] triggered, string deferred, double delaySeconds = 0)
        {
            CatalogId = id; UnitRawcode = raw; JassFunction = function;
            RollSides = sides; RollSuccess = success;
            Always = Array.AsReadOnly(always ?? Array.Empty<SourceScriptHit>());
            OnProc = Array.AsReadOnly(triggered ?? Array.Empty<SourceScriptHit>());
            DeferredNativeEffect = deferred;
            DelaySeconds = delaySeconds;
        }
    }

    public static class SourceModelScriptAttackCatalog
    {
        public const string SourceJassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";

        private static SourceScriptHit Direct(double damage, double min = 1,
            double max = 1, Ordr323AttackType attack = Ordr323AttackType.Spells)
        { return new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
            damage, min, max, attack); }

        private static SourceScriptHit Circle(double radius, double damage,
            Ordr323AttackType attack = Ordr323AttackType.Spells)
        { return new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
            damage, 1, 1, attack); }

        // 2.323[C] JASS Bnt calls UnitDamagePointLoc with
        // ATTACK_TYPE_NORMAL + DAMAGE_TYPE_UNIVERSAL. The former maps to
        // Ordr323AttackType.Spells; the latter bypasses numeric armor.
        private static SourceScriptHit NamiUniversalCircle()
        {
            return new SourceScriptHit(SourceScriptHitShape.Circle,
                400, 0, 800, 1, 1, Ordr323AttackType.Spells,
                damageType: SourceScriptDamageType.Universal);
        }

        private static SourceScriptHit Rankyaku()
        {
            // Pinned AbilityData.slk A0VS/AUcs: Area=200, Rng=1700
            // (CAST reach, NOT travel), DataA=150, DataC=800 (travel),
            // DataD=250 (ending half-width). Bnm/Bnp create e0KD at
            // GetUnitLoc(attacker), facing GetUnitFacing(attacker), and
            // issue carrionswarm at that very same location.
            return new SourceScriptHit(SourceScriptHitShape.RankyakuLine,
                200, 800, 150, 1, 1, Ordr323AttackType.Spells,
                endRadius: 250);
        }

        private static SourceScriptAttackSpec Row(string id, string raw,
            string callback, int sides, int success, SourceScriptHit[] hits,
            string native = null, SourceScriptHit[] always = null)
        { return new SourceScriptAttackSpec(id, raw, callback, sides, success,
            always, hits, native); }

        private static readonly SourceScriptAttackSpec[] Entries = {
            Row("U-04", "h00M", "Bnm", 100, 10,
                new[] { Rankyaku() }, "A0VS 동일점 시전의 원본 엔진 진행방향·비행시각/면역 미검증"),
            Row("U-13", "h00A", "Bnp", 100, 10,
                new[] { Rankyaku() }, "A0VS 동일점 시전의 원본 엔진 진행방향·비행시각/면역 미검증"),
            // Original JASS Bnt handles the 10% / 800 Universal packet.
            // Its e00K/A089 helper is implemented by the legacy follow-up;
            // aftershock duration 3s is from the S-03 skill description.
            Row("S-03", "h00P", "Bnt", 100, 10,
                new[] { NamiUniversalCircle() }),
            Row("S-13", "h013", "Bn6", 100, 20,
                new[] { Direct(500) }, "e00P 보조 소환의 네이티브 효과"),
            Row("R-11", "h04H", "Bo4", 100, 10,
                new[] { Direct(12500, 1, 2) }, "e01E 보조 소환의 네이티브 효과"),
            Row("R-12", "h01W", "Bo6", 100, 10,
                new[] { Circle(375, 6000) }, "e01F 보조 소환의 네이티브 효과"),
            Row("R-13", "h01V", "Bo9", 100, 10,
                new[] { Circle(425, 12000) }, "e01G/A14X 인페르노 보스 저항/타격시점 미검증"),
            Row("R-21", "h02I", "BpM", 100, 20,
                new[] { Direct(5000, .8, 1.5) }),
            Row("R-23", "h028", "BpQ", 100, 30,
                new[] { Circle(260, 5000, Ordr323AttackType.Chaos) }),
            Row("R-26", "h02B", "BpX", 100, 10,
                new[] { Circle(405, 9000) }, "h0C5/A14Y 인페르노 보스 저항/타격시점 미검증"),
            Row("R-30", "h02C", "Bpd", 100, 10,
                new[] { Circle(485, 7500) }, "e01P/A150 인페르노 보스 저항/타격시점 미검증"),
            Row("R-31", "h02A", "Bpf", 100, 7,
                new[] { Circle(500, 13000) }, "e01Q/A151 인페르노 보스 저항/타격시점 미검증"),
            Row("R-35", "h02K", "Bps", 100, 12,
                new[] { Circle(375, 35000, Ordr323AttackType.Siege),
                    Direct(17500, 1, 1, Ordr323AttackType.Siege) },
                "e01X 보조 소환의 네이티브 효과"),
            Row("R-38", "h02H", "Bpx", 1000, 75,
                new[] { Direct(17500, 1, 1.5) }, "e01Z 보조 소환의 네이티브 효과"),
            Row("H-11", "h03O", "Bre", 100, 12,
                new[] { Circle(500, 85000) }, "e03H inferno 주문"),
            Row("H-16", "h041", "Bq3", 100, 10,
                new[] { Direct(330000), Circle(415, 120000) },
                "e031/A156 인페르노 보스 저항/타격시점 미검증"),
            Row("H-18", "h03Y", "Bqh", 100, 11,
                new[] { Circle(500, 100000) }, "e02V/e013 보조 소환"),
            Row("L-29", "h03K", "BsF", 100, 15,
                new[] { Direct(150000, 1, 3) }, "e03S 보조 소환"),
            Row("L-33", "h03H", "Btd", 100, 8,
                // e04X has only Aloc/Avul: a VISUAL helper. Btd's Bcb
                // 20000 on every attack and 250000 AoE on 8% are complete
                // explicit JASS damage; no extra native combat effect.
                new[] { Circle(550, 250000) }, null,
                new[] { Direct(20000) }),
            Row("RN-04", "h06X", "CCg", 100, 17,
                new[] { Circle(375, 1500000, Ordr323AttackType.Chaos) },
                "Bd8(A04R)+15의 네이티브 isr2 피해 환산과 A10I faeriefire 시전/면역 미검증")
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode = Build();
        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var result = new Dictionary<string, SourceScriptAttackSpec>(StringComparer.Ordinal);
            foreach (var row in Entries)
            {
                if (row.RollSides < 1 || row.RollSuccess < 1 ||
                    row.RollSuccess > row.RollSides || row.UnitRawcode.Length != 4)
                    throw new InvalidOperationException("원본 공격 스킬 등록 오류");
                result.Add(row.UnitRawcode, row);
            }
            if (result.Count != 20) throw new InvalidOperationException("20종 스킬 소스 누락");
            return result;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
