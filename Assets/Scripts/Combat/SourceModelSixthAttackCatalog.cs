using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Additional original 2.323[C] w5 callbacks with exact, explicit JASS
    // damage operands. Only the reproduced hits enter the practice model.
    // Auxiliary Warcraft dummy casts, object passives and map-specific
    // resistance remain independent and must never be silently fabricated.
    public static class SourceModelSixthAttackCatalog
    {
        private static SourceScriptHit Direct(double damage,
            Ordr323AttackType attack = Ordr323AttackType.Spells,
            double bossCurrentHealthPercent = 0,
            double enragedMaxHealthPercent = 0) =>
            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                damage, 1, 1, attack,
                bossCurrentHealthPercent, enragedMaxHealthPercent);

        private static SourceScriptHit Circle(double radius, double damage,
            double minimum = 1, double maximum = 1) =>
            new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                damage, minimum, maximum, Ordr323AttackType.Spells);

        private static readonly SourceScriptAttackSpec[] Entries = {
            // CCE: 11% -> normal 35000; boss 0.9% current HP;
            // enraged(A903) 4.5% maximum HP. A902/A911 are boss markers.
            new SourceScriptAttackSpec("X-01", "h05H", "CCE", 100, 11,
                Array.Empty<SourceScriptHit>(),
                new[] { Direct(35000, Ordr323AttackType.Chaos, .9, 4.5) },
                null),
            // BpC: every attacked event, with a 0.42 s target-location AoE.
            new SourceScriptAttackSpec("R-15", "h01T", "BpC", 1, 1,
                Array.Empty<SourceScriptHit>(),
                new[] { Circle(400, 1500) }, null, .42),
            // BpZ: independent rolls. 10% native Blizzard occurs FIRST;
            // the 12% direct 10000 damage is the separately modelled hit.
            new SourceScriptAttackSpec("R-27", "h01P", "BpZ", 100, 12,
                Array.Empty<SourceScriptHit>(),
                new[] { Direct(10000) }, null),
            // Bp1: 10% target-location AoE with BJ random damage x1..1.5.
            new SourceScriptAttackSpec("R-40", "h02J", "Bp1", 100, 10,
                Array.Empty<SourceScriptHit>(),
                new[] { Circle(400, 6000, 1, 1.5) }, null),
            // Bq9: boss and unbuffed A903/enraged targets enter Bq8
            // before any 16% roll. After B05G, enraged attacks use this
            // 16% branch and add 18% max HP once in the runtime.
            // Story(A904) adds an additional direct 375000.
            new SourceScriptAttackSpec("H-17", "h03Z", "Bq9", 100, 16,
                Array.Empty<SourceScriptHit>(),
                new[] {
                    Direct(250000),
                    Circle(500, 100000),
                    new SourceScriptHit(SourceScriptHitShape.Direct,
                        0, 0, 375000, 1, 1, Ordr323AttackType.Spells,
                        storyOnly: true)
                }, null)
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode =
            Build();

        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var rows = new Dictionary<string, SourceScriptAttackSpec>(
                StringComparer.Ordinal);
            foreach (var spec in Entries)
            {
                if (spec.UnitRawcode.Length != 4 ||
                    spec.RollSides <= 0 || spec.RollSuccess <= 0 ||
                    spec.RollSuccess > spec.RollSides ||
                    spec.Always.Count + spec.OnProc.Count == 0)
                    throw new InvalidOperationException(
                        "원본 공격 콜백 등록 오류: " + spec.CatalogId);
                rows.Add(spec.UnitRawcode, spec);
            }
            return rows;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All =>
            Array.AsReadOnly(Entries);

        public static bool TryGet(string rawcode,
            out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
