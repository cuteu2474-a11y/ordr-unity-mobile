using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Positive basic-hit stage of original w5 callbacks. These units are
    // deliberately separate from the ATTACK_DECLARED script catalogs.
    // Native dummy orders, projectile contact and resistance stay deferred.
    public sealed class SourceAfterHitAttackUnit
    {
        public string CatalogId { get; private set; }
        public string UnitRawcode { get; private set; }
        public bool ExclusiveAlternatives { get; private set; }
        public IReadOnlyList<SourceScriptAttackSpec> Branches { get; private set; }

        internal SourceAfterHitAttackUnit(string catalogId, string rawcode,
            bool exclusiveAlternatives, params SourceScriptAttackSpec[] branches)
        {
            CatalogId = catalogId; UnitRawcode = rawcode;
            ExclusiveAlternatives = exclusiveAlternatives;
            Branches = Array.AsReadOnly(branches);
        }
    }

    public static class SourceModelAfterHitAttackCatalog
    {
        // OnProc is applied only after a confirmed >0 basic attack.
        private static SourceScriptHit Direct(double amount,
            double minimum = 1, double maximum = 1) =>
            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                amount, minimum, maximum, Ordr323AttackType.Spells);

        private static SourceScriptHit RandomIntegerDirect(int minimum, int maximum) =>
            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                0, 1, 1, Ordr323AttackType.Spells,
                randomIntegerMinimum: minimum, randomIntegerMaximum: maximum);

        private static SourceScriptHit Circle(double radius, double amount) =>
            new SourceScriptHit(SourceScriptHitShape.Circle, radius, 0,
                amount, 1, 1, Ordr323AttackType.Spells);

        private static SourceScriptAttackSpec Branch(string catalogId, string rawcode,
            string jass, int successes, SourceScriptHit[] hits,
            string nativeEffect = null, double delay = 0) =>
            new SourceScriptAttackSpec(catalogId, rawcode, jass, 100, successes,
                Array.Empty<SourceScriptHit>(), hits, nativeEffect, delay);

        private static readonly SourceAfterHitAttackUnit[] Entries = {
            // R-10 / Bo2 -> Bo1: the 15% declaration branch listens for
            // attacker-sourced positive damage, then resolves one inclusive
            // BFi(5000,100000) direct hit and radius 375 / 7000 AoE.
            new SourceAfterHitAttackUnit("R-10", "h01Y", false,
                Branch("R-10", "h01Y", "Bo2/Bo1", 15,
                    new[] { RandomIntegerDirect(5000, 100000),
                        Circle(375, 7000) },
                    "e01D thunderbolt stun and order not modeled")),

            // R-33 / Bpm: 8% Bpk OR (on failure) another independent
            // 8% Bpl. First is conditioned on positive attack damage.
            // Second is actually declared-stage in JASS: the practice
            // basic-hit callback intentionally omits second-branch procs
            // on zero-damage attacks, until that lifecycle is modelled.
            new SourceAfterHitAttackUnit("R-33", "h02F", true,
                Branch("R-33", "h02F", "Bpm/Bpk", 8,
                    new[] { Circle(450, 10000) }),
                Branch("R-33", "h02F", "Bpm/Bpl", 8,
                    new[] { Circle(375, 16000), Direct(50000) },
                    "e01T thunderbolt stun/order not modeled", .45)),

            // H-01 / BrW: BOTH 15% rolls are independent. Each BrU/BrV
            // registers a positive-damage listener; the first applies
            // radius 600 / 200000, the second direct 252500 x1..1.5.
            new SourceAfterHitAttackUnit("H-01", "h043", false,
                Branch("H-01", "h043", "BrW/BrU", 15,
                    new[] { Circle(600, 200000) },
                    "e03E carrionswarm native projectile not modeled"),
                Branch("H-01", "h043", "BrW/BrV", 15,
                    new[] { Direct(252500, 1, 1.5) }))
        };

        private static readonly Dictionary<string, SourceAfterHitAttackUnit>
            ByRawcode = Build();

        private static Dictionary<string, SourceAfterHitAttackUnit> Build()
        {
            var result = new Dictionary<string, SourceAfterHitAttackUnit>(
                StringComparer.Ordinal);
            foreach (var unit in Entries)
            {
                if (unit.UnitRawcode.Length != 4 || unit.Branches.Count == 0)
                    throw new InvalidOperationException(
                        "적중 후 기술 등록 오류: " + unit.CatalogId);
                foreach (var branch in unit.Branches)
                    if (branch.UnitRawcode != unit.UnitRawcode ||
                        branch.CatalogId != unit.CatalogId ||
                        branch.RollSides <= 0 ||
                        branch.RollSuccess <= 0 ||
                        branch.RollSuccess > branch.RollSides ||
                        branch.OnProc.Count == 0)
                        throw new InvalidOperationException(
                            "적중 후 기술 발동 조건 오류: " + unit.CatalogId);
                result.Add(unit.UnitRawcode, unit);
            }
            return result;
        }

        public static IReadOnlyList<SourceAfterHitAttackUnit> All =>
            Array.AsReadOnly(Entries);

        public static bool TryGet(string rawcode,
            out SourceAfterHitAttackUnit unit) =>
            ByRawcode.TryGetValue(rawcode ?? "", out unit);
    }
}
