using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Additional damage packets transcribed from the pinned 2.323[C] JASS.
    // Native summons, buffs and dependent trigger chains remain deferred.
    public static class SourceModelFifthAttackCatalog
    {
        private static SourceScriptHit Direct(double amount, double min = 1, double max = 1)
        {
            return new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                amount, min, max, Ordr323AttackType.Spells);
        }

        private static SourceScriptHit Circle(double radius, double amount,
            double min = 1, double max = 1)
        {
            return new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                amount, min, max, Ordr323AttackType.Spells);
        }

        private static readonly SourceScriptAttackSpec[] Entries = {
            // BrI: a single 14% roll precedes Bcc(315,100000).
            // Conditional target-class extra Bcb is not yet executable.
            new SourceScriptAttackSpec("H-13", "h055", "BrI", 100, 14,
                null, new[] { Circle(315, 100000) },
                "BrI: 25% current-life vs 200000 conditional Bcb, dummy summons deferred"),
            // BzH: 10% roll; Bcc(400,400000,.8,1.5) and
            // Bcb(200000,.8,1.5) are in the SAME branch.
            // The independent 8% aY trigger is not collapsed into this roll.
            new SourceScriptAttackSpec("TR-28", "H08Y", "BzH", 100, 10,
                null, new[] { Circle(400, 400000, .8, 1.5),
                    Direct(200000, .8, 1.5) },
                "BzH: mana-150 transform/trigger and independent 8% aY deferred")
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode = Build();
        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var result = new Dictionary<string, SourceScriptAttackSpec>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry.RollSides != 100 || entry.RollSuccess <= 0 ||
                    entry.RollSuccess > 100 || entry.UnitRawcode.Length != 4 ||
                    entry.OnProc.Count == 0)
                    throw new InvalidOperationException("Fifth source attack model contract mismatch");
                result.Add(entry.UnitRawcode, entry);
            }
            if (result.Count != 2)
                throw new InvalidOperationException("Fifth source attack model coverage mismatch");
            return result;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
