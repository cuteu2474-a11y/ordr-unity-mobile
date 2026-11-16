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
            double damage, double radius = 0, double length = 0,
            SourceScriptDamageType damageType = SourceScriptDamageType.ModelDefault)
        {
            return new SourceScriptHit(shape, radius, length, damage, 1, 1,
                Ordr323AttackType.Spells, damageType: damageType);
        }

        // Bn4 adds native A08O/Apxf to a short-lived h0C5 proxy.
        // The proxy is rendered as timed attacks, never as a persistent unit.
        public static SourceScriptHit BonClayPhoenixFireTick()
        {
            return new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                250, 1, 1, Ordr323AttackType.Spells,
                damageType: SourceScriptDamageType.Magical);
        }

        // Build the mobile cone from the pinned W3X e0KD/A086 (ANfl) facts,
        // rather than treating a native multi-target order as one direct hit.
        private static SourceScriptHit EnelForkedLightning()
        {
            Original323AttackDummySpellCommand order;
            Original323NativeDummyAbilityFacts ability;
            if (!Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                    "h00Q", out order) ||
                !Original323NativeDummyAbilityCatalog.TryGet(order, out ability) ||
                ability.AbilityRawcode != "A086" ||
                ability.ParentWarcraftAbility != "ANfl" ||
                !ability.MaxNativeTargets.HasValue ||
                ability.MaxNativeTargets.Value != ability.DataB1 ||
                ability.WarcraftTargetMask != "air,ground,enemy,neutral,organic")
                throw new InvalidOperationException("에넬 갈래 번개 원본 능력 연결 불일치");
            return new SourceScriptHit(SourceScriptHitShape.ForkedLightningCone,
                ability.Area1, ability.DataC1, ability.DataA1, 1, 1,
                Ordr323AttackType.Spells,
                endRadius: ability.DataD1,
                maxTargets: ability.MaxNativeTargets.Value,
                castRange: ability.CastRange1,
                 damageType: SourceScriptDamageType.Magical);
        }

        // Source W3X: Bn0/e0KD/A08L (AEfk). DataA1=1500,
        // Area1=1150, DataC1=7 maximum distinct enemies.
        private static SourceScriptHit MarcoFanOfKnives()
        {
            return new SourceScriptHit(SourceScriptHitShape.AttackerCircle,
                1150, 0, 1500, 1, 1, Ordr323AttackType.Spells,
                maxTargets: 7);
        }

        private static readonly SourceScriptAttackSpec[] Entries = {
            // Bn0: 10% cast at ATTACKER, maximum seven targets.
            new SourceScriptAttackSpec("S-08", "h00T", "Bn0", 100, 10,
                null, new[] { MarcoFanOfKnives() },
                "A08L 최대7명 가까운 대상 우선 모델 / 원본 대상 순서·면역·미사일 미검증"),
            // Bn4: h0C5 adds A08O (Apxf), lives 1.2s; no direct JASS hit.
            new SourceScriptAttackSpec("S-12", "h014", "Bn4", 100, 12,
                null, Array.Empty<SourceScriptHit>(),
                "A08O phoenixfire 250/0.2초, 800범위·1.2초 생존: 첫 발사·대상/면역 미검증"),
            // BoC executes BoB; in state 1 after .29s, Bn9 applies Bcb.
            new SourceScriptAttackSpec("S-15", "h012", "BoC", 100, 16,
                null, new[] { Hit(SourceScriptHitShape.Circle, 3000, 450) },
                "BoB 대상 A904 보유 시 추가 5000·0.45초 복구/공격 일시정지 미검증", .29),
            // Bnr: 10% native e0KD/A086 (ANfl), caster-origin tapered
            // cone, four total eligible targets, 1500 magic per target.
            new SourceScriptAttackSpec("S-18", "h00Q", "Bnr", 100, 10,
                null, new[] { EnelForkedLightning() },
                "A086 원본 범위·4명 모바일 모델 / 네이티브 대상 열거·면역·최종 HP 별도 미인증"),
            // BoE casts e0KG/A0ES (AUcs) toward attacked unit.
            new SourceScriptAttackSpec("S-19", "h01J", "BoE", 100, 14,
                null, new[] { Hit(SourceScriptHitShape.ProjectileLine, 850, 215, 1700,
                    SourceScriptDamageType.Magical) },
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
