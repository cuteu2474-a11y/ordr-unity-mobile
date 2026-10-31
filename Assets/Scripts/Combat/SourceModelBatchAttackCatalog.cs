using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Pinned original S2 2.323[C] JASS w5 registration + direct damage
    // callbacks. This is MODEL_ONLY, not evidence of WC3 native parity.
    // Stages preserve separate BFi rolls and the source's hit order.
    public sealed class SourceModelBatchAttackStage
    {
        public string JassPath { get; private set; }
        public int RollSides { get; private set; }
        public int RollSuccess { get; private set; }
        public double DelaySeconds { get; private set; }
        public IReadOnlyList<SourceScriptHit> Hits { get; private set; }
        public string Deferred { get; private set; }

        internal SourceModelBatchAttackStage(string path, int sides, int success,
            double delay, SourceScriptHit[] hits, string deferred = null)
        {
            if (string.IsNullOrWhiteSpace(path) || delay < 0 ||
                (sides == 0 ? success != 0 :
                 sides < 0 || success < 1 || success > sides))
                throw new ArgumentException("Invalid source attack stage");
            JassPath = path; RollSides = sides; RollSuccess = success;
            DelaySeconds = delay;
            Hits = Array.AsReadOnly(hits ?? Array.Empty<SourceScriptHit>());
            Deferred = deferred ?? "";
        }
    }

    public sealed class SourceModelBatchAttackSpec
    {
        public string CatalogId { get; private set; }
        public string Rawcode { get; private set; }
        public string JassFunction { get; private set; }
        public IReadOnlyList<SourceModelBatchAttackStage> Stages { get; private set; }
        public string PendingNative { get; private set; }
        // Reuse the existing damage settlement and source-labeled events.
        public SourceScriptAttackSpec DamageLabel { get; private set; }

        internal SourceModelBatchAttackSpec(string id, string raw, string function,
            string pending, params SourceModelBatchAttackStage[] stages)
        {
            CatalogId = id; Rawcode = raw; JassFunction = function;
            PendingNative = pending;
            Stages = Array.AsReadOnly(stages ?? Array.Empty<SourceModelBatchAttackStage>());
            DamageLabel = new SourceScriptAttackSpec(id, raw, function, 1, 1,
                null, Array.Empty<SourceScriptHit>(), pending);
        }
    }

    public static class SourceModelBatchAttackCatalog
    {
        public const string SourceJassSha256 = SourceModelScriptAttackCatalog.SourceJassSha256;

        private static SourceScriptHit Direct(double amount, double minimum = 1,
            double maximum = 1, Ordr323AttackType type = Ordr323AttackType.Spells)
        {
            return new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                amount, minimum, maximum, type);
        }

        private static SourceScriptHit Circle(double radius, double amount,
            double minimum = 1, double maximum = 1,
            Ordr323AttackType type = Ordr323AttackType.Spells)
        {
            return new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                amount, minimum, maximum, type);
        }

        private static SourceModelBatchAttackStage Stage(string path,
            int denominator, int numerator, SourceScriptHit[] hits,
            string pending = null, double delay = 0)
        {
            return new SourceModelBatchAttackStage(path, denominator,
                numerator, delay, hits, pending);
        }

        private static readonly SourceModelBatchAttackSpec[] Entries = {
            // Boi -> Bog/Boh: enemy enumeration at attacked location, 400.
            new SourceModelBatchAttackSpec("R-05", "h02L", "Boi",
                "e011/e012 auxiliary units and Warcraft enum/RNG parity",
                Stage("Boi:7/100 -> Boh", 100, 7,
                    new[] { Circle(400, 10000, .5, 2) })),
            // Br8 -> Br7: 14% target-region damage; mana=150 Br6 deferred.
            new SourceModelBatchAttackSpec("H-09", "h03X", "Br8",
                "Br6: mana 150, radius 800, 2150000 and Bd5/BeC deferred",
                Stage("Br8:14/100 -> Br7", 100, 14,
                    new[] { Circle(600, 200000) },
                    "Br7 native slow order omitted")),
            // Buo: two independent BFi(1,1000) rolls; ratio HP damage
            // and native purge are separate outputs, not extra flat packets.
            new SourceModelBatchAttackSpec("L-08", "h03C", "Buo",
                "Target-life AoE, purge and 15% special-target direct hit deferred",
                Stage("Buo:45/1000", 1000, 45,
                    new[] { Circle(525, 285000) }),
                Stage("Buo:125/1000", 1000, 125,
                    new[] { Circle(525, 175000) })),
            // Bsk is NOT unconditional: <=15% life AND Bdl triggers
            // one Bsj, otherwise there is one fallback 10% BFi roll.
            // The Bdl target ability predicate is not available to the
            // practice model; low-health targets are explicitly deferred.
            // Bsj state 1 happens after BJ3(.04), with target point pinned.
            new SourceModelBatchAttackSpec("L-27", "h02S", "Bsk/Bsj",
                "Bsk <=15% health requires Bdl(A902/A911/A903/A904) class; exact low-health branch deferred",
                Stage("Bsk:10/100 fallback Bsj", 100, 10,
                    new[] { Direct(900000, 1, 1.5), Circle(385, 150000) },
                    null, .04)),
            // BtL has an independently gated 115-mana radius-600 branch.
            new SourceModelBatchAttackSpec("L-32", "h030", "BtL",
                "115-mana BtK missing-life + 2500000 AoE branch deferred",
                Stage("BtL:10/100", 100, 10,
                    new[] { Circle(500, 200000) })),
            new SourceModelBatchAttackSpec("L-23", "h039", "Bt8",
                "Bt6 Bdq(1) and second 5% Bt7 Bdq(2) and summoned attacker deferred",
                Stage("Bt8:10/100 -> Bt6", 100, 10,
                    new[] { Circle(525, 145000) }),
                Stage("Bt8:5/100 -> Bt7", 100, 5,
                    Array.Empty<SourceScriptHit>(), "Bt7 native Bdq(2) not modeled")),
            new SourceModelBatchAttackSpec("L-38", "h03B", "BuT",
                "Life=115 or boss trigger BuR and native slow on BuS deferred",
                Stage("BuT:11/100 -> BuS", 100, 11,
                    new[] { Circle(600, 200000) })),
            // B1O runs under B1P's 18% branch, not its 12.5% branch.
            new SourceModelBatchAttackSpec("TR-03", "H09E", "B1P",
                "B1P first 125/1000 damage depends on hero STR; 10% bZ and metamorphosis deferred",
                Stage("B1P:125/1000 STR packet", 1000, 125,
                    Array.Empty<SourceScriptHit>(), "750000 + 10000x STR not derived"),
                Stage("B1P:18/100 -> B1O", 100, 18,
                    new[] { Circle(525, 205000) },
                    "B1O native slow order omitted"),
                Stage("B1P:10/100 bZ", 100, 10,
                    Array.Empty<SourceScriptHit>(), "bZ delayed native trigger deferred")),
            // B0e rolls 10/100 unconditionally after its conditional boss
            // trigger and mana 135 paths. Those paths remain deferred.
            new SourceModelBatchAttackSpec("TR-17", "H095", "B0e",
                "Boss A903 conditional a7/a8 and mana=135 a9 are deferred",
                Stage("B0e:10/100", 100, 10,
                    new[] { Circle(450, 425000) },
                    "Firebolt auxiliary damage/target immunity omitted")),
            // B19 uses independent 17%, 10%, 25% rolls. IG proxy is a
            // distinct owner in source; our model labels attribution.
            new SourceModelBatchAttackSpec("TR-27", "H05N", "B19",
                "Mana=135 bk, bl/bm native triggers and IG proxy attribution deferred",
                Stage("B19:17/100 bl", 100, 17,
                    Array.Empty<SourceScriptHit>(), "bl native branch deferred"),
                Stage("B19:10/100 bm", 100, 10,
                    Array.Empty<SourceScriptHit>(), "bm native branch deferred"),
                Stage("B19:25/100 Bcb from IG proxy", 100, 25,
                    new[] { Direct(500000) },
                    "Source IG dummy owner/target order not reproduced")),
            // B59 always performs its final independent 10% B52 branch.
            new SourceModelBatchAttackSpec("E-06", "h057", "B59/B52",
                "Mana-150 B5t and I6/I8 trigger paths and dP tier conditional deferred",
                Stage("B59:10/100 -> B52", 100, 10,
                    new[] { Circle(450, 350000, 1, 2) })),
            new SourceModelBatchAttackSpec("I-09", "h04B", "B93/B92",
                "B93 mana/ability level and four independent summon trigger paths deferred",
                Stage("B93:10/100 -> B92", 100, 10,
                    new[] { Circle(600, 400000) },
                    "Bdq(1) and native helper lifetime omitted"),
                Stage("B93:75/1000 ed", 1000, 75,
                    Array.Empty<SourceScriptHit>(), "ed delayed native trigger deferred"))
        };

        private static readonly Dictionary<string, SourceModelBatchAttackSpec> ByRawcode = Build();

        private static Dictionary<string, SourceModelBatchAttackSpec> Build()
        {
            var result = new Dictionary<string, SourceModelBatchAttackSpec>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Entries)
            {
                if (row.Rawcode.Length != 4 || string.IsNullOrEmpty(row.JassFunction) ||
                    row.Stages.Count == 0 || !ids.Add(row.CatalogId) ||
                    result.ContainsKey(row.Rawcode))
                    throw new InvalidOperationException("Next-30 source batch catalog invalid");
                result.Add(row.Rawcode, row);
            }
            if (result.Count != 12)
                throw new InvalidOperationException("Next-30 source batch expected 12 mapped callbacks");
            return result;
        }

        public static IReadOnlyList<SourceModelBatchAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceModelBatchAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
