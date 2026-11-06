using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Source: original ORDR S2 2.323[C] war3map.j, verified SHA256
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d.
    // Only explicit JASS damage paths enter the practice model.
    // Warcraft object dummy spells, stun, and final native resistance
    // have not been asserted equivalent to the original engine.
    public static class SourceModelSeventhAttackCatalog
    {
        private static SourceScriptHit Direct(double damage, double storyDamage = 0) =>
            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                damage, 1, 1, Ordr323AttackType.Spells,
                storyDamage: storyDamage);

        private static SourceScriptHit Circle(double radius, double damage) =>
            new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                damage, 1, 1, Ordr323AttackType.Spells);

        private static readonly SourceScriptAttackSpec[] Entries = {
            // R-04 / Boe -> Boc/Bod: 10% centered radius 350,
            // 6000 normal/universal damage to each enemy.
            new SourceScriptAttackSpec("R-04", "h01L", "Boe", 100, 10,
                Array.Empty<SourceScriptHit>(),
                new[] { Circle(350, 6000) }, null),

            // R-06 / Boo -> Bon: every attack opens a 15% immediate
            // staged branch, with the hits at ~0.32s. The other branch
            // only watches for a damage event and has no JASS hit to add.
            new SourceScriptAttackSpec("R-06", "h01X", "Boo", 100, 15,
                Array.Empty<SourceScriptHit>(),
                new[] { Direct(30000), Circle(500, 22500) },
                "e013 thunderbolt dummy: native stun/order not reproduced",
                .32),

            // R-34 / Bpp -> Bpo: 11% staged branch; after approximately
            // 0.625s, AoE 10000 (radius 600), then direct 35000.
            // Source ability A904 changes the SAME direct hit to 70000.
            // The practice 'Story' target kind represents this A904
            // branch; a second +35000 hit would double-apply armor.
            new SourceScriptAttackSpec("R-34", "h02E", "Bpp", 100, 11,
                Array.Empty<SourceScriptHit>(),
                new[] { Circle(600, 10000), Direct(35000, 70000) },
                "e058 inferno dummy: native impact/order not reproduced",
                .625)
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
