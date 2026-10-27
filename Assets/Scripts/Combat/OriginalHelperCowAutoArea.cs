using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Source: ORDR S2 2.323[C] war3map.j SHA-256
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // CJb / A0BZ: location-targeted area damage, 450 radius.
    // CJi / A07T: location-targeted area damage, 625 radius.
    // CJg/CJh call Bd8, raising XL=A04R ability levels by 5/8.
    // A04R is an isr2 damage-reduction field, NOT a timed stun.
    // Ability cast RANGE, mana, cooldown and immunity are NOT given by these
    // callbacks; only an original-verified live world may supply/execute them.
    public static class OriginalHelperCowAreaEvidence
    {
        public const string PointDamageAbility = "A0BZ";
        public const string EarthquakeAbility = "A07T";
        public const string A04RDamageReductionAbility = "A04R";
        public const double PointDamageRadius = 450.0;
        public const double EarthquakeRadius = 625.0;

        public static double VerifiedRadius(string abilityRawcode)
        {
            switch (abilityRawcode)
            {
                case PointDamageAbility: return PointDamageRadius;
                case EarthquakeAbility: return EarthquakeRadius;
                default: throw new ArgumentException(
                    "원본 JASS 위치형 도움소 능력으로 확인되지 않았습니다.", nameof(abilityRawcode));
            }
        }

        public static long JassDamage(string abilityRawcode, int abilityLevel)
        {
            if (abilityLevel != 1 && abilityLevel != 2)
                throw new ArgumentOutOfRangeException(nameof(abilityLevel));
            switch (abilityRawcode)
            {
                // CJb calls UnitDamagePointLoc with CHAOS/UNIVERSAL.
                case PointDamageAbility: return abilityLevel == 2 ? 10000000L : 7000000L;
                // CJg/CJh call Bcb with CHAOS/UNIVERSAL.
                case EarthquakeAbility: return abilityLevel == 2 ? 2000000L : 1000000L;
                default: throw new ArgumentException("확인되지 않은 능력", nameof(abilityRawcode));
            }
        }

        public static int JassA04RLevelIncrease(string abilityRawcode, int abilityLevel)
        {
            if (abilityLevel != 1 && abilityLevel != 2)
                throw new ArgumentOutOfRangeException(nameof(abilityLevel));
            if (abilityRawcode == PointDamageAbility) return 0;
            if (abilityRawcode == EarthquakeAbility) return abilityLevel == 2 ? 8 : 5;
            throw new ArgumentException("확인되지 않은 능력", nameof(abilityRawcode));
        }
    }

    // CastRange is sourced from a verified ability level or live Warcraft
    // object-data adapter; NEVER substitute the 450/625 effect radius for it.
    public sealed class OriginalHelperCowReadyAreaAbility
    {
        public string AbilityRawcode { get; private set; }
        public int Level { get; private set; }
        public double OriginalCastRange { get; private set; }
        public double EffectRadius { get; private set; }

        public OriginalHelperCowReadyAreaAbility(
            string abilityRawcode, int level, double verifiedCastRange)
        {
            EffectRadius = OriginalHelperCowAreaEvidence.VerifiedRadius(abilityRawcode);
            if (level < 1 || level > 2 ||
                double.IsNaN(verifiedCastRange) ||
                double.IsInfinity(verifiedCastRange) || verifiedCastRange <= 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedCastRange),
                    "원본 능력 레벨과 시전 사거리의 검증이 필요합니다.");
            AbilityRawcode = abilityRawcode;
            Level = level;
            OriginalCastRange = verifiedCastRange;
        }
    }

    public sealed class OriginalHelperCowAreaCaster
    {
        public string Rawcode { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool IsAlive { get; private set; }

        public OriginalHelperCowAreaCaster(string rawcode, double x, double y, bool alive)
        {
            if (string.IsNullOrWhiteSpace(rawcode) ||
                double.IsNaN(x) || double.IsNaN(y) ||
                double.IsInfinity(x) || double.IsInfinity(y))
                throw new ArgumentException("실제 도움소 위치 또는 ID가 올바르지 않습니다.");
            Rawcode = rawcode; X = x; Y = y; IsAlive = alive;
        }
    }

    // Combat/world port must handle the real original mana debit, cooldown,
    // target ownership/immunity, damage and the separate A04R ability-level
    // increase (which must be implemented before A07T may be cast).
    // The target selector only computes a cast POSITION, never applies damage.
    public interface IOriginalHelperCowAreaSpellWorld
    {
        bool TryGetLiveCaster(out OriginalHelperCowAreaCaster caster);
        IReadOnlyList<OriginalHelperCowReadyAreaAbility> AvailableReadyAbilities();
        bool IsVerifiedEligibleTarget(string abilityRawcode, MobileEnemySnapshot enemy);
        bool IsVerifiedCastPointAllowed(string abilityRawcode, double x, double y);
        // Revalidate mana/range/cooldown/ability immediately before committing.
        bool TryCommitCast(string abilityRawcode, double x, double y);
    }

    public sealed class OriginalHelperCowAreaTarget
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public int HitCount { get; private set; }
        public int BossCount { get; private set; }
        public IReadOnlyList<string> EnemyIds { get; private set; }

        internal OriginalHelperCowAreaTarget(double x, double y,
            List<string> ids, int bosses)
        {
            X = x; Y = y; HitCount = ids.Count; BossCount = bosses;
            EnemyIds = ids.AsReadOnly();
        }
    }

    // Maximizes hits in the ORIGINAL effect radius with UNLIMITED mobile
    // cast reach. Candidate centers include pairwise effect-circle intersections,
    // so we can hit two enemies even if neither enemy's exact position is a
    // good cast center. Geometry does not change the original AoE or DPS.
    public static class OriginalHelperCowAreaTargeting
    {
        private const double Epsilon = 0.00001;

        public static OriginalHelperCowAreaTarget Choose(
            double casterX, double casterY, double originalCastRange,
            double verifiedEffectRadius, IReadOnlyList<MobileEnemySnapshot> enemies,
            Func<MobileEnemySnapshot, bool> originalEligibility,
            Func<double, double, bool> originalPointAllowed)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (originalEligibility == null) throw new ArgumentNullException(nameof(originalEligibility));
            if (originalPointAllowed == null) throw new ArgumentNullException(nameof(originalPointAllowed));
            if (!Finite(casterX) || !Finite(casterY) ||
                !Finite(originalCastRange) || originalCastRange <= 0 ||
                !Finite(verifiedEffectRadius) || verifiedEffectRadius <= 0)
                throw new ArgumentException("원본 스킬 거리와 시전자 좌표가 필요합니다.");

            var accepted = new List<MobileEnemySnapshot>();
            foreach (var enemy in enemies)
            {
                if (enemy != null && enemy.Health > 0 &&
                    Finite(enemy.X) && Finite(enemy.Y) && originalEligibility(enemy))
                    accepted.Add(enemy);
            }
            accepted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (accepted.Count == 0) return null;

            // originalCastRange remains verified evidence; it is not a mobile reach cap.
            double area2 = verifiedEffectRadius * verifiedEffectRadius;
            OriginalHelperCowAreaTarget best = null;
            double bestDistance2 = double.PositiveInfinity;

            Action<double, double> consider = (x, y) =>
            {
                if (!Finite(x) || !Finite(y)) return;
                double dx = x - casterX, dy = y - casterY;
                double distance2 = dx * dx + dy * dy;
                if (!originalPointAllowed(x, y)) return;

                var ids = new List<string>();
                int bosses = 0;
                foreach (var enemy in accepted)
                {
                    double ex = enemy.X - x, ey = enemy.Y - y;
                    if (ex * ex + ey * ey <= area2 + Epsilon)
                    {
                        ids.Add(enemy.Id);
                        if (enemy.Kind == CharacterEnemyKind.Boss ||
                            enemy.Kind == CharacterEnemyKind.Enraged)
                            bosses++;
                    }
                }
                if (ids.Count == 0) return;
                bool better = best == null || ids.Count > best.HitCount ||
                    (ids.Count == best.HitCount && bosses > best.BossCount) ||
                    (ids.Count == best.HitCount && bosses == best.BossCount &&
                     distance2 < bestDistance2 - Epsilon) ||
                    (ids.Count == best.HitCount && bosses == best.BossCount &&
                     Math.Abs(distance2 - bestDistance2) <= Epsilon &&
                     (x < best.X - Epsilon ||
                      (Math.Abs(x - best.X) <= Epsilon && y < best.Y - Epsilon)));
                if (!better) return;
                best = new OriginalHelperCowAreaTarget(x, y, ids, bosses);
                bestDistance2 = distance2;
            };

            consider(casterX, casterY);
            for (int i = 0; i < accepted.Count; i++)
            {
                var a = accepted[i];
                consider(a.X, a.Y);
                // No cast-range boundary exists in the mobile targeting geometry.
                for (int j = i + 1; j < accepted.Count; j++)
                {
                    var b = accepted[j];
                    Intersections(a.X, a.Y, verifiedEffectRadius,
                        b.X, b.Y, verifiedEffectRadius, consider);
                }
            }
            return best;
        }

        private static void Intersections(double ax, double ay, double ar,
            double bx, double by, double br, Action<double, double> visitor)
        {
            double dx = bx - ax, dy = by - ay;
            double d2 = dx * dx + dy * dy;
            if (d2 <= Epsilon * Epsilon) return;
            double distance = Math.Sqrt(d2);
            if (distance > ar + br + Epsilon ||
                distance + Math.Min(ar, br) < Math.Max(ar, br) - Epsilon) return;
            double along = (ar * ar - br * br + d2) / (2.0 * distance);
            double offset2 = ar * ar - along * along;
            if (offset2 < -Epsilon) return;
            double normal = Math.Sqrt(Math.Max(0.0, offset2));
            double mx = ax + dx * along / distance;
            double my = ay + dy * along / distance;
            double px = -dy * normal / distance;
            double py = dx * normal / distance;
            visitor(mx + px, my + py);
            if (normal > Epsilon) visitor(mx - px, my - py);
        }

        private static bool Finite(double v) => !double.IsInfinity(v) && !double.IsNaN(v);
    }

    // Called on Unity gameplay ticks after the battle has advanced. The
    // verified port is the sole authority to cast or debit mana/cooldown;
    // no mock effects, targets or ability ranges are supplied by this class.
    public sealed class OriginalHelperCowAutoAreaCasting
    {
        private readonly GameSession game;
        private readonly Func<double> simulationSeconds;
        private readonly Func<IReadOnlyList<MobileEnemySnapshot>> enemySnapshot;
        private readonly IOriginalHelperCowAreaSpellWorld world;
        private double nextScan;

        public OriginalHelperCowAutoAreaCasting(GameSession verifiedGame,
            Func<double> seconds,
            Func<IReadOnlyList<MobileEnemySnapshot>> currentEnemies,
            IOriginalHelperCowAreaSpellWorld originalWorld)
        {
            game = verifiedGame ?? throw new ArgumentNullException(nameof(verifiedGame));
            simulationSeconds = seconds ?? throw new ArgumentNullException(nameof(seconds));
            enemySnapshot = currentEnemies ?? throw new ArgumentNullException(nameof(currentEnemies));
            world = originalWorld ?? throw new ArgumentNullException(nameof(originalWorld));
        }

        public bool Tick()
        {
            if (game.Phase != SessionPhase.Running) return false;
            double now = simulationSeconds();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                now < nextScan) return false;
            // A scan interval is only a CPU bound, NOT a spell cooldown.
            nextScan = now + 0.15;
            OriginalHelperCowAreaCaster caster;
            if (!world.TryGetLiveCaster(out caster) || caster == null ||
                !caster.IsAlive || caster.Rawcode != OriginalHelperCowRules.HelperRawcode)
                return false;

            var ready = world.AvailableReadyAbilities();
            if (ready == null || ready.Count == 0) return false;
            var spells = new List<OriginalHelperCowReadyAreaAbility>();
            foreach (var item in ready)
                if (item != null) spells.Add(item);
            spells.Sort((a,b) => string.CompareOrdinal(a.AbilityRawcode, b.AbilityRawcode));
            var enemies = enemySnapshot();
            if (enemies == null || enemies.Count == 0) return false;

            foreach (var skill in spells)
            {
                var target = OriginalHelperCowAreaTargeting.Choose(
                    caster.X, caster.Y, skill.OriginalCastRange, skill.EffectRadius,
                    enemies,
                    enemy => world.IsVerifiedEligibleTarget(skill.AbilityRawcode, enemy),
                    (x,y) => world.IsVerifiedCastPointAllowed(skill.AbilityRawcode, x, y));
                // Revalidate original ability/mana/cooldown in TryCommitCast.
                if (target != null &&
                    world.TryCommitCast(skill.AbilityRawcode, target.X, target.Y))
                    return true; // at most one auto cast per scanning step
            }
            return false;
        }
    }
}
