using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Pinned W3X native spell OBJECT inputs, not observed Warcraft outcomes.
    // AUin/A14Y and the four cast dummy abilities use DataA1=0 (no extra
    // native damage), DataB1=.01, and summon only inert h0C5 helpers.
    public sealed class SourceModelInfernoFollowup
    {
        public string CatalogId { get; private set; }
        public string AbilityRawcode { get; private set; }
        public double Radius { get; private set; }
        public double NormalStunSeconds { get; private set; }
        public double HeroStunSeconds { get; private set; }
        public double ImpactDelaySeconds => 0.01;

        internal SourceModelInfernoFollowup(string id, string ability,
            double radius, double duration, double heroDuration)
        {
            CatalogId = id;
            AbilityRawcode = ability;
            Radius = radius;
            NormalStunSeconds = duration;
            HeroStunSeconds = heroDuration;
        }
    }

    public static class SourceModelLegacyNativeFollowupCatalog
    {
        public const string SourceJassSha256 = SourceModelScriptAttackCatalog.SourceJassSha256;
        public const string NamiAuraAbility = "A089";
        public const double NamiMovementSlowPercent = 5;
        public const double NamiZoneRadius = 450;
        public const string SentinelFaerieAbility = "A10I";
        public const double SentinelArmorReduction = 50;
        public const double SentinelFaerieDuration = 60;
        public const int SentinelA04RLevelIncrease = 15;

        private static readonly SourceModelInfernoFollowup[] Entries = {
            new SourceModelInfernoFollowup("R-13", "A14X", 425, .9, .18),
            // BpX casts A14Y on h0C5. e01L/A12Y is only CREATED (not cast).
            new SourceModelInfernoFollowup("R-26", "A14Y", 405, .95, .19),
            new SourceModelInfernoFollowup("R-30", "A150", 485, 1.15, .23),
            new SourceModelInfernoFollowup("R-31", "A151", 500, 1.2, .24),
            new SourceModelInfernoFollowup("H-16", "A156", 415, 1.7, .34)
        };

        private static readonly Dictionary<string, SourceModelInfernoFollowup> ById = Build();
        private static Dictionary<string, SourceModelInfernoFollowup> Build()
        {
            var result = new Dictionary<string, SourceModelInfernoFollowup>(StringComparer.Ordinal);
            foreach (var spec in Entries)
            {
                if (spec.Radius <= 0 || spec.NormalStunSeconds <= 0 ||
                    spec.HeroStunSeconds <= 0 || spec.HeroStunSeconds >= spec.NormalStunSeconds)
                    throw new InvalidOperationException("원본 인페르노 객체 모델 데이터 오류");
                result.Add(spec.CatalogId, spec);
            }
            if (result.Count != 5) throw new InvalidOperationException("원본 인페르노 5종 누락");
            return result;
        }

        public static IReadOnlyList<SourceModelInfernoFollowup> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string catalogId, out SourceModelInfernoFollowup spec) =>
            ById.TryGetValue(catalogId ?? "", out spec);
    }
}
