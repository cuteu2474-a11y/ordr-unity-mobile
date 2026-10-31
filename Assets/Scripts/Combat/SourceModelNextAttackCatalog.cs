using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Six additional w5 callbacks extracted from the pinned 2.323[C] JASS.
    // Warcraft-native spell outputs below are playable MOBILE approximations,
    // not a claim of engine-identical missiles, damage immunities or RNG.
    public static class SourceModelNextAttackCatalog
    {
        public const string SourceJassSha256 = SourceModelScriptAttackCatalog.SourceJassSha256;

        private static SourceScriptHit Hit(SourceScriptHitShape shape,
            double damage, double radius = 0, double length = 0)
        {
            return new SourceScriptHit(shape, radius, length, damage, 1, 1,
                Ordr323AttackType.Spells);
        }

        private static readonly SourceScriptAttackSpec[] Entries = {
            // Bn0: native e0KD/A08L (AEfk) at the ATTACKER, not the target.
            new SourceScriptAttackSpec("S-08", "h00T", "Bn0", 100, 10,
                null, new[] { Hit(SourceScriptHitShape.AttackerCircle, 1500, 1150) },
                "A08L fanofknives 원본 최대 대상·면역·미사일 미검증"),
            // Bn4: h0C5 adds A08O (Apxf), lives 1.2s; no direct JASS hit.
            new SourceScriptAttackSpec("S-12", "h014", "Bn4", 100, 12,
                null, Array.Empty<SourceScriptHit>(),
                "A08O phoenixfire 250/0.2초, 800범위·1.2초 생존: 첫 발사·대상/면역 미검증"),
            // BoC executes BoB; in state 1 after .29s, Bn9 applies Bcb.
            new SourceScriptAttackSpec("S-15", "h012", "BoC", 100, 16,
                null, new[] { Hit(SourceScriptHitShape.Circle, 3000, 450) },
                "BoB 대상 A904 보유 시 추가 5000·0.45초 복구/공격 일시정지 미검증", .29),
            // Bnr issues a target order to e0KD/A086 (ANfl).
            new SourceScriptAttackSpec("S-18", "h00Q", "Bnr", 100, 10,
                null, new[] { Hit(SourceScriptHitShape.Direct, 1500) },
                "A086 forkedlightning 4갈래 후속 대상·거리·면역 미검증"),
            // BoE casts e0KG/A0ES (AUcs) toward attacked unit.
            new SourceScriptAttackSpec("S-19", "h01J", "BoE", 100, 14,
                null, new[] { Hit(SourceScriptHitShape.ProjectileLine, 850, 215, 1700) },
                "A0ES carrionswarm 투사체 비행·너비·피격 수·면역 미검증"),
            // BoI: two cosmetic e00T, and synchronous Bcb at the target.
            new SourceScriptAttackSpec("S-22", "h00W", "BoI", 100, 15,
                null, new[] { Hit(SourceScriptHitShape.Circle, 1000, 450) },
                "BoG 적 진영 열거·원본 피해 면역/대상 순서 미검증")
        };

        private static readonly Dictionary<string, SourceScriptAttackSpec> ByRawcode = Build();

        private static Dictionary<string, SourceScriptAttackSpec> Build()
        {
            var map = new Dictionary<string, SourceScriptAttackSpec>(StringComparer.Ordinal);
            foreach (var row in Entries)
            {
                if (row.RollSides != 100 || row.RollSuccess <= 0 ||
                    row.RollSuccess > row.RollSides || row.UnitRawcode.Length != 4)
                    throw new InvalidOperationException("추가 w5 소스 모델 카탈로그 오류");
                map.Add(row.UnitRawcode, row);
            }
            if (map.Count != 6) throw new InvalidOperationException("추가 w5 6종 누락");
            return map;
        }

        public static IReadOnlyList<SourceScriptAttackSpec> All => Array.AsReadOnly(Entries);
        public static bool TryGet(string rawcode, out SourceScriptAttackSpec spec) =>
            ByRawcode.TryGetValue(rawcode ?? "", out spec);
    }
}
