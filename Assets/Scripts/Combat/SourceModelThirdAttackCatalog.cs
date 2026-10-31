using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Original JASS Boa/Bos/Bri: source-pinned attack declaration model.
    // Only the unconditional Bcb packets below are executed. The native
    // TriggerExecute, percent-health, summoned-unit and ability effects
    // remain deferred until their complete original call graphs are audited.
    public static class SourceModelThirdAttackCatalog
    {
        public const string SourceJassSha256 = SourceModelScriptAttackCatalog.SourceJassSha256;

        private static readonly SourceScriptAttackSpec[] Entries = {
            // Bos calls Bcb(2000 CHAOS) before the 10% branch.
            new SourceScriptAttackSpec("R-07", "h025", "Bos", 100, 10,
                new[] { new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                    2000, 1, 1, Ordr323AttackType.Chaos) },
                Array.Empty<SourceScriptHit>(),
                "Bos: BiZ(375), current-life percentage, target abilities and native dummy deferred"),
            // Bri calls Bcb(11500 CHAOS) before the 14% branch.
            new SourceScriptAttackSpec("H-05", "h044", "Bri", 100, 14,
                new[] { new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                    11500, 1, 1, Ordr323AttackType.Chaos) },
                Array.Empty<SourceScriptHit>(),
                "Bri: Bcc(350,80000), conditional percentage/current-life and native dummy deferred")
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode = Build();
        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var result = new Dictionary<string, SourceScriptAttackSpec>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry.RollSides != 100 || entry.RollSuccess <= 0 ||
                    entry.RollSuccess > 100 || entry.UnitRawcode.Length != 4 ||
                    entry.Always.Count == 0 || entry.OnProc.Count != 0)
                    throw new InvalidOperationException("Source third attack model contract mismatch");
                result.Add(entry.UnitRawcode, entry);
            }
            if (result.Count != 2) throw new InvalidOperationException("Third attack source coverage mismatch");
            return result;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
