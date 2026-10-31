using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Offline practice ONLY. Each row is transcribed from the pinned w5 attack
    // callbacks in original-2323C-attack-skill-batch.json. These are source
    // instructions, NOT permission to claim Warcraft native spell parity.
    public enum SourceScriptHitShape { Direct, Circle, RankyakuLine, AttackerCircle, ProjectileLine }

    public sealed class SourceScriptHit
    {
        public SourceScriptHitShape Shape { get; private set; }
        public double Radius { get; private set; }
        public double Length { get; private set; }
        public double Damage { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public Ordr323AttackType Attack { get; private set; }

        internal SourceScriptHit(SourceScriptHitShape shape, double radius,
            double length, double damage, double minimum, double maximum,
            Ordr323AttackType attack)
        {
            Shape = shape; Radius = radius; Length = length; Damage = damage;
            Minimum = minimum; Maximum = maximum; Attack = attack;
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

        private static SourceScriptHit Rankyaku()
        {
            // A0VS/AUcs: area=200, range=1700, damage=150. Bnm/Bnp
            // actually order carrionswarm at the ATTACKER'S OWN location.
            // The mobile aim toward the current target is a model choice:
            // no Warcraft facing/path witness is available.
            return new SourceScriptHit(SourceScriptHitShape.RankyakuLine,
                200, 1700, 150, 1, 1, Ordr323AttackType.Spells);
        }

        private static SourceScriptAttackSpec Row(string id, string raw,
            string callback, int sides, int success, SourceScriptHit[] hits,
            string native = null, SourceScriptHit[] always = null)
        { return new SourceScriptAttackSpec(id, raw, callback, sides, success,
            always, hits, native); }

        private static readonly SourceScriptAttackSpec[] Entries = {
            Row("U-04", "h00M", "Bnm", 100, 10,
                new[] { Rankyaku() }, "A0VS 원본 투사체/방향·면역 미검증"),
            Row("U-13", "h00A", "Bnp", 100, 10,
                new[] { Rankyaku() }, "A0VS 원본 투사체/방향·면역 미검증"),
            Row("S-03", "h00P", "Bnt", 100, 10,
                new[] { Circle(400, 800) }, "e00K/A089 이동속도 -5% 오라의 원본 지속/중첩·면역 미검증"),
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
