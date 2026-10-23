using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // The real h08A helper cow must expose its position, ability levels and
    // casting readiness in addition to the SAME authoritative mana used by
    // OriginalHelperCowManaRecharge. No duplicate helper mana is created.
    public interface IOriginalHelperCowCombatActor : IOriginalHelperCowManaActor
    {
        double X { get; }
        double Y { get; }
        int GetAbilityLevel(string originalAbilityRawcode);
        bool IsOriginalCastReady(string originalAbilityRawcode);
    }

    // Ability object data must be separately reviewed: A0BZ/A07T are not
    // present in the currently extracted war3map.w3a modifier subset.
    // These values must NEVER be inferred from the 450/625 effect radius.
    public sealed class OriginalHelperCowVerifiedCastData
    {
        public string AbilityRawcode { get; private set; }
        public int Level { get; private set; }
        public double CastRange { get; private set; }
        public double ManaCost { get; private set; }
        public double CooldownSeconds { get; private set; }
        public string Evidence { get; private set; }

        public OriginalHelperCowVerifiedCastData(string rawcode, int level,
            double range, double manaCost, double cooldown, string evidence)
        {
            OriginalHelperCowAreaEvidence.VerifiedRadius(rawcode);
            if (level < 1 || level > 2 ||
                !Finite(range) || range <= 0 ||
                !Finite(manaCost) || manaCost < 0 ||
                !Finite(cooldown) || cooldown < 0 ||
                string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원본 능력 레벨/시전거리/마나/쿨다운/출처가 필요합니다.");
            AbilityRawcode = rawcode;
            Level = level; CastRange = range;
            ManaCost = manaCost; CooldownSeconds = cooldown;
            Evidence = evidence;
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    public interface IOriginalHelperCowVerifiedCastDataSource
    {
        // False while any field of this exact ability LEVEL is unverified.
        bool TryRead(string abilityRawcode, int level,
            out OriginalHelperCowVerifiedCastData source);
    }

    public interface IOriginalHelperCowVerifiedAreaDamage
    {
        // Warcraft's enemy/ally/structure/magic-immune targeting and any
        // lane-specific restriction must be checked by the verified adapter.
        bool CanHit(string abilityRawcode, MobileEnemySnapshot snapshot,
            CombatEnemy originalEnemy);
        bool CanCastPoint(string abilityRawcode, double x, double y);

        // Convert JASS CHAOS/UNIVERSAL base damage into a verified final long
        // after the actual target's defense, buffs and integer quantization.
        // False = missing evidence; do not charge mana or inflict partial harm.
        bool TryFinalDamage(string abilityRawcode, int level,
            CombatEnemy target, IReadOnlyList<CombatEffect> currentEffects,
            long originalDamage, out long verifiedDamage);
    }

    // CJb uses native UnitDamagePointLoc without CJf's enemy-only filter.
    // The combat runtime models enemy HP but does NOT yet model damage to
    // allied/neutral/structure units. A source-verified world must establish
    // that every unit the native could actually damage is represented in
    // the prepared hit list, or prove the omitted unit is unaffected.
    // A07T instead uses CJf/IsUnitEnemy and does not call this native.
    public interface IOriginalHelperCowPointDamageCoverage
    {
        bool CoversEveryOriginalPointDamageVictim(double centerX,
            double centerY, double radius,
            IReadOnlyList<string> modeledVictimIds);
    }

    // Immutable, preflighted hit command that the SAME combat world will
    // resolve through CombatResolution so wave kills and events stay coherent.
    public sealed class OriginalHelperCowResolvedHit
    {
        public string EnemyId { get; private set; }
        public long FinalDamage { get; private set; }

        public OriginalHelperCowResolvedHit(string enemyId, long finalDamage)
        {
            if (string.IsNullOrWhiteSpace(enemyId) || finalDamage < 0)
                throw new ArgumentException("검증된 적 ID와 음수가 아닌 최종 피해가 필요합니다.");
            EnemyId = enemyId; FinalDamage = finalDamage;
        }
    }

    // Concrete combat-world bridge. Once a genuine h08A actor, original
    // ability object values and verified damage adapter are connected, the
    // previously implemented auto-area selector can actually cast in-game.
    public sealed class OriginalHelperCowLiveCombatWorld :
        IOriginalHelperCowAreaSpellWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Func<IOriginalHelperCowCombatActor> actorSource;
        private readonly IOriginalHelperCowVerifiedCastDataSource castData;
        private readonly IOriginalHelperCowVerifiedAreaDamage damage;
        private readonly IOriginalA04REffectEngine a04rEffects;
        private readonly int ownerPlayerId;
        private readonly Dictionary<string, double> nextReady =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private bool committing;

        public OriginalHelperCowLiveCombatWorld(CharacterCombatRuntime runtime,
            Func<IOriginalHelperCowCombatActor> originalCow,
            IOriginalHelperCowVerifiedCastDataSource originalAbilityValues,
            IOriginalHelperCowVerifiedAreaDamage originalDamage,
            int verifiedOwnerPlayerId = 0,
            IOriginalA04REffectEngine originalA04RMechanics = null)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            actorSource = originalCow ?? throw new ArgumentNullException(nameof(originalCow));
            castData = originalAbilityValues ??
                throw new ArgumentNullException(nameof(originalAbilityValues));
            damage = originalDamage ?? throw new ArgumentNullException(nameof(originalDamage));
            a04rEffects = originalA04RMechanics;
            if (verifiedOwnerPlayerId < 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedOwnerPlayerId));
            ownerPlayerId = verifiedOwnerPlayerId;
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        private bool Valid(IOriginalHelperCowCombatActor cow)
        {
            if (cow == null || !cow.IsAlive ||
                cow.OriginalRawcode != OriginalHelperCowRules.HelperRawcode ||
                cow.OwnerPlayerId != ownerPlayerId || !Finite(cow.X) || !Finite(cow.Y))
                return false;
            return Finite(cow.CurrentMana) && Finite(cow.MaximumMana) &&
                cow.MaximumMana > 0 && cow.CurrentMana >= 0 &&
                cow.CurrentMana <= cow.MaximumMana;
        }

        public bool TryGetLiveCaster(out OriginalHelperCowAreaCaster result)
        {
            result = null;
            var cow = actorSource();
            if (!Valid(cow) || combat.Game.Phase != SessionPhase.Running) return false;
            result = new OriginalHelperCowAreaCaster(cow.OriginalRawcode,
                cow.X, cow.Y, cow.IsAlive);
            return true;
        }

        private bool TryReady(string ability, IOriginalHelperCowCombatActor cow,
            out OriginalHelperCowVerifiedCastData verified)
        {
            verified = null;
            if (!Valid(cow) || combat.Game.Phase != SessionPhase.Running ||
                !cow.IsOriginalCastReady(ability)) return false;
            // CJg/CJh increase A04R ability levels, not stun seconds.
            // The map's isr2 field alone does not verify Warcraft damage
            // reduction mechanics; the physical effect port is mandatory.
            if (ability == OriginalHelperCowAreaEvidence.EarthquakeAbility &&
                a04rEffects == null) return false;
            int level = cow.GetAbilityLevel(ability);
            if (level < 1 || level > 2 ||
                !castData.TryRead(ability, level, out verified) ||
                verified == null ||
                verified.AbilityRawcode != ability || verified.Level != level)
                return false;
            double until;
            if (nextReady.TryGetValue(ability, out until) && combat.Time < until)
                return false;
            return cow.CurrentMana >= verified.ManaCost;
        }

        public IReadOnlyList<OriginalHelperCowReadyAreaAbility> AvailableReadyAbilities()
        {
            var list = new List<OriginalHelperCowReadyAreaAbility>();
            var cow = actorSource();
            if (!Valid(cow) || committing) return list.AsReadOnly();
            foreach (string ability in new[] {
                OriginalHelperCowAreaEvidence.PointDamageAbility,
                OriginalHelperCowAreaEvidence.EarthquakeAbility })
            {
                OriginalHelperCowVerifiedCastData verified;
                if (TryReady(ability, cow, out verified))
                    list.Add(new OriginalHelperCowReadyAreaAbility(
                        ability, verified.Level, verified.CastRange));
            }
            return list.AsReadOnly();
        }

        private bool CanHit(string ability, MobileEnemySnapshot snapshot)
        {
            if (snapshot == null) return false;
            CombatEnemy live = combat.EnemyOf(snapshot.Id);
            // CJf (A07T) checks actual owning-player allegiance, not our
            // mobile lane enum. The verified adapter must inspect ownership,
            // invulnerability and other native damage eligibility.
            return live != null && live.IsAlive &&
                damage.CanHit(ability, snapshot, live);
        }

        public bool IsVerifiedEligibleTarget(string ability, MobileEnemySnapshot enemy)
        {
            OriginalHelperCowAreaEvidence.VerifiedRadius(ability);
            return CanHit(ability, enemy);
        }

        public bool IsVerifiedCastPointAllowed(string ability, double x, double y)
        {
            OriginalHelperCowAreaEvidence.VerifiedRadius(ability);
            var cow = actorSource();
            OriginalHelperCowVerifiedCastData verified;
            if (!Finite(x) || !Finite(y) ||
                !TryReady(ability, cow, out verified)) return false;
            double dx = x - cow.X, dy = y - cow.Y;
            if (dx * dx + dy * dy >
                verified.CastRange * verified.CastRange + 0.00001 ||
                !damage.CanCastPoint(ability, x, y)) return false;
            if (ability == OriginalHelperCowAreaEvidence.PointDamageAbility)
            {
                // Do not silently turn the original all-unit point-damage
                // native into an enemy-only spell.
                var coverage = damage as IOriginalHelperCowPointDamageCoverage;
                if (coverage == null) return false;
                double radius = OriginalHelperCowAreaEvidence.PointDamageRadius;
                var included = new List<string>();
                foreach (var enemy in combat.MobileEnemySnapshot())
                {
                    double vx = enemy.X - x, vy = enemy.Y - y;
                    if (vx * vx + vy * vy <= radius * radius + 0.00001 &&
                        CanHit(ability, enemy))
                        included.Add(enemy.Id);
                }
                if (!coverage.CoversEveryOriginalPointDamageVictim(
                    x, y, radius, included.AsReadOnly())) return false;
            }
            return true;
        }

        public bool TryCommitCast(string ability, double x, double y)
        {
            if (committing) return false;
            var cow = actorSource();
            OriginalHelperCowVerifiedCastData verified;
            if (!TryReady(ability, cow, out verified) ||
                !IsVerifiedCastPointAllowed(ability, x, y)) return false;

            // Fully resolve all current LIVE targets before any mana debit.
            // No damage is applied until the entire target set has evidence.
            double radius = OriginalHelperCowAreaEvidence.VerifiedRadius(ability);
            long originalDamage = OriginalHelperCowAreaEvidence.JassDamage(
                ability, verified.Level);
            var hits = new List<OriginalHelperCowResolvedHit>();
            var snapshots = combat.MobileEnemySnapshot();
            foreach (var enemy in snapshots)
            {
                if (!CanHit(ability, enemy)) continue;
                double dx = enemy.X - x, dy = enemy.Y - y;
                if (dx * dx + dy * dy > radius * radius + 0.00001) continue;
                var live = combat.EnemyOf(enemy.Id);
                if (live == null || !live.IsAlive) return false;
                long finalDamage;
                if (!damage.TryFinalDamage(ability, verified.Level,
                    live, live.Status.Snapshot(), originalDamage, out finalDamage) ||
                    finalDamage < 0) return false;
                hits.Add(new OriginalHelperCowResolvedHit(enemy.Id, finalDamage));
            }
            if (hits.Count == 0) return false;
            if (!Valid(cow) || !ReferenceEquals(cow, actorSource()) ||
                cow.CurrentMana < verified.ManaCost ||
                !cow.IsOriginalCastReady(ability)) return false;

            // Unity processes this on one simulation thread. Runtime
            // revalidates targets then resolves damage and A04R level changes
            // through the same CombatEnemy/CombatResolution/GameSession.
            committing = true;
            double previousMana = cow.CurrentMana;
            bool applied = false;
            try
            {
                cow.SetMana(previousMana - verified.ManaCost);
                if (!Valid(cow) ||
                    Math.Abs(cow.CurrentMana -
                        (previousMana - verified.ManaCost)) > 0.0001)
                    throw new InvalidOperationException("검증된 마나 소비값이 일치하지 않습니다.");
                if (!combat.TryApplyOriginalHelperCowAreaEffects(
                    ability, verified.Level, hits, a04rEffects))
                    return false;
                applied = true;
                nextReady[ability] = combat.Time + verified.CooldownSeconds;
                return true;
            }
            finally
            {
                // False/preflight failures must never spend mana. Combat hits
                // are irreversible once applied; callers must supply an
                // effect resolver that cannot throw after mutation.
                if (!applied && Valid(cow)) cow.SetMana(previousMana);
                committing = false;
            }
        }
    }
}
