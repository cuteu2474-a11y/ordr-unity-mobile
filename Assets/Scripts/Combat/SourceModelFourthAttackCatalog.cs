using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Explicit Bcb/Bcc packets in the pinned 2.323[C] JASS. This catalog
    // intentionally excludes native casts, conditional defense branches and
    // variable-dependent effects until their dependencies are executable.
    public static class SourceModelFourthAttackCatalog
    {
        private static SourceScriptHit Direct(double damage, double min = 1,
            double max = 1)
        {
            return new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                damage, min, max, Ordr323AttackType.Spells);
        }

        private static SourceScriptHit Circle(double radius, double damage,
            double min = 1, double max = 1)
        {
            return new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                damage, min, max, Ordr323AttackType.Spells);
        }

        private static readonly SourceScriptAttackSpec[] Entries = {
            // Btb: unconditional 40,000 normal Bcb(.8,1.5), then
            // conditional Bdl percentage damage, then 57/400 Bcc.
            new SourceScriptAttackSpec("L-16", "h034", "Btb", 400, 57,
                new[] { Direct(40000, .8, 1.5) },
                new[] { Circle(315, 100000, .8, 1.5) },
                "Btb: Bdl target-specific current-life 3.5% damage deferred"),
            // Bt2: 15/100 normal 400,000 Bcb. Bdi/Bdl max-life
            // branches and the independent 8/100 summon roll are deferred.
            new SourceScriptAttackSpec("L-21", "h042", "Bt2", 100, 15,
                Array.Empty<SourceScriptHit>(),
                new[] { Direct(400000) },
                "Bt2: Bdi/Bdl max-life branches and independent 8% summon deferred")
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode = Build();
        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var result = new Dictionary<string, SourceScriptAttackSpec>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry.RollSides < 1 || entry.RollSuccess < 1 ||
                    entry.RollSuccess > entry.RollSides ||
                    entry.UnitRawcode.Length != 4)
                    throw new InvalidOperationException("Fourth source attack catalog invalid");
                result.Add(entry.UnitRawcode, entry);
            }
            if (result.Count != 2)
                throw new InvalidOperationException("Fourth source attack coverage mismatch");
            return result;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
