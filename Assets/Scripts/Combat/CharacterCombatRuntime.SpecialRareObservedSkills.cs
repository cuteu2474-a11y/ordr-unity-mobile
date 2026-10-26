using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // A FINAL observed Warcraft hit, after all source-native target masks,
    // armor, resistance, buffs and integer HP conversion. No tooltip amount
    // or unverified object field can be used in this structure.
    public sealed class Original323SpecialRareObservedHit
    {
        public string EnemyId { get; private set; }
        public long FinalHpLoss { get; private set; }
        public string NativeHitEvidence { get; private set; }
        public double? VerifiedStunSeconds { get; private set; }
        public string NativeStunEvidence { get; private set; }

        public Original323SpecialRareObservedHit(string enemyId, long finalHpLoss,
            string nativeHitEvidence, double? verifiedStunSeconds = null,
            string nativeStunEvidence = null)
        {
            if (string.IsNullOrWhiteSpace(enemyId) || finalHpLoss < 0 ||
                (finalHpLoss == 0 && !verifiedStunSeconds.HasValue) ||
                string.IsNullOrWhiteSpace(nativeHitEvidence) ||
                (verifiedStunSeconds.HasValue &&
                    (verifiedStunSeconds.Value <= 0 ||
                     double.IsNaN(verifiedStunSeconds.Value) ||
                     double.IsInfinity(verifiedStunSeconds.Value) ||
                     string.IsNullOrWhiteSpace(nativeStunEvidence))) ||
                (!verifiedStunSeconds.HasValue && nativeStunEvidence != null))
                throw new ArgumentException("원본 타격의 최종 HP·기절 시간 및 독립 증거가 부족합니다.");
            EnemyId = enemyId;
            FinalHpLoss = finalHpLoss;
            NativeHitEvidence = nativeHitEvidence;
            VerifiedStunSeconds = verifiedStunSeconds;
            NativeStunEvidence = nativeStunEvidence;
        }
    }

    public sealed class Original323SpecialRareObservedImpact
    {
        public string SourceEventId { get; private set; }
        public long CasterInstanceId { get; private set; }
        public string CasterCatalogId { get; private set; }
        public string CasterOriginalRawcode { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string HumanSkillName { get; private set; }
        public string OriginalW3xSha256 { get; private set; }
        public double ObservedImpactTime { get; private set; }
        public string NativeCastAndTargetEvidence { get; private set; }
        public IReadOnlyList<Original323SpecialRareObservedHit> Hits { get; private set; }

        public Original323SpecialRareObservedImpact(string sourceEventId,
            long casterInstanceId, string casterCatalogId,
            string casterOriginalRawcode, string abilityRawcode,
            string humanSkillName, string sourceW3xSha256, double observedTime,
            string verifiedNativeCastAndTargetEvidence,
            IEnumerable<Original323SpecialRareObservedHit> observedHits)
        {
            if (string.IsNullOrWhiteSpace(sourceEventId) || casterInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(casterCatalogId) ||
                casterOriginalRawcode == null || casterOriginalRawcode.Length != 4 ||
                abilityRawcode == null || abilityRawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(humanSkillName) ||
                string.IsNullOrWhiteSpace(sourceW3xSha256) ||
                double.IsNaN(observedTime) || double.IsInfinity(observedTime) ||
                observedTime < 0 ||
                string.IsNullOrWhiteSpace(verifiedNativeCastAndTargetEvidence) ||
                observedHits == null)
                throw new ArgumentException("원본 특별함·희귀함 주문 사건의 실시간 근거가 누락됐습니다.");
            var copied = new List<Original323SpecialRareObservedHit>();
            foreach (var hit in observedHits)
            {
                if (hit == null) throw new ArgumentException("원본 타격 목록에 null 항목");
                copied.Add(hit);
                if (copied.Count > 128)
                    throw new ArgumentOutOfRangeException(nameof(observedHits),
                        "실제 원본 타격 128개를 초과하는 배치는 승인하지 않습니다.");
            }
            if (copied.Count == 0)
                throw new ArgumentException("원본 타격 또는 상태효과가 없는 사건");

            SourceEventId = sourceEventId;
            CasterInstanceId = casterInstanceId;
            CasterCatalogId = casterCatalogId;
            CasterOriginalRawcode = casterOriginalRawcode;
            AbilityRawcode = abilityRawcode;
            HumanSkillName = humanSkillName;
            OriginalW3xSha256 = sourceW3xSha256;
            ObservedImpactTime = observedTime;
            NativeCastAndTargetEvidence = verifiedNativeCastAndTargetEvidence;
            Hits = copied.AsReadOnly();
        }
    }

    // A standalone Native WC3/JASS impact observer. Implementations MUST
    // verify source spell activation, owner, target mask, collision, original
    // timing, all current immunities, and FINAL integer HP for each enemy.
    // Observe and VerifyCurrentCasterIdentity MUST be read-only and consume no
    // attack/proc RNG. This intentionally does not infer chance from tooltips.
    public interface IOriginal323SpecialRareImpactWitness
    {
        bool TryObserveNativeImpact(string sourceEventId,
            CharacterCombatRuntime currentWorld,
            out Original323SpecialRareObservedImpact completeImpact);

        bool TryVerifyCurrentCasterIdentity(long liveCasterInstanceId,
            string expectedOriginalRawcode, out string originalEvidence);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private OriginalMapBaseStatCatalog specialRareOriginalMap;
        private IOriginal323SpecialRareImpactWitness specialRareImpactWitness;
        private readonly HashSet<string> committedSpecialRareImpactIds =
            new HashSet<string>(StringComparer.Ordinal);

        // Original object ability membership alone cannot trigger damage.
        // An independently witnessed current native impact is also mandatory.
        public void ConfigureOriginal323SpecialRareSkillImpacts(
            OriginalMapBaseStatCatalog originalMap,
            IOriginal323SpecialRareImpactWitness originalNativeObserver)
        {
            if (characters.Count != 0 || specialRareImpactWitness != null)
                throw new InvalidOperationException(
                    "특별함·희귀함 스킬 증거는 첫 캐릭터 생성 전에만 연결 가능합니다.");
            if (originalMap == null || originalNativeObserver == null)
                throw new ArgumentNullException(nameof(originalMap));
            if (originalMap.SourceW3xSha256 !=
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256)
                throw new ArgumentException("원본 S2 2.323[C] 맵 데이터 지문이 일치하지 않습니다.");
            specialRareOriginalMap = originalMap;
            specialRareImpactWitness = originalNativeObserver;
        }

        private static bool IsUnsupportedGenericSourceAbility(string rawcode)
        {
            // Already handled by the separate script-ATTACKED native dummy
            // spell bridge, or known move/collect/sell/invulnerability actions.
            // Do not apply a second hit under a new generic skill event.
            return rawcode == "A086" || rawcode == "A0VS" ||
                rawcode == "A07Y" || rawcode == "A0JE" ||
                rawcode == "A0B8" || rawcode == "Avul" ||
                rawcode == "A029";
        }

        // Called at the source-observed native impact, not merely when a
        // tooltip cooldown or local mobile projectile says "ready".
        // On success normal CombatResolution owns HP, deaths, contribution
        // and all UI events; an unsupported original effect never runs.
        public bool TryApplyObservedOriginal323SpecialRareImpact(
            string sourceEventId)
        {
            return TryCommitOriginal323SpecialRareImpact(
                sourceEventId, null);
        }

        // Used by the verified cast lifecycle to stop the independent
        // native impact observer from swapping its receipt between
        // ordered preflight and canonical HP settlement.
        internal bool TryCommitOriginal323SpecialRareImpact(
            string sourceEventId, Original323SpecialRareObservedImpact expectedReceipt)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (game.Phase != SessionPhase.Running ||
                specialRareOriginalMap == null || specialRareImpactWitness == null ||
                string.IsNullOrWhiteSpace(sourceEventId) ||
                committedSpecialRareImpactIds.Contains(sourceEventId))
                return false;

            double observedClock = scheduler.Time;
            int observedUnits = characters.Count;
            int observedEnemies = enemies.Count;
            int observedKills = game.DefeatedEnemies;

            Original323SpecialRareObservedImpact impact;
            if (!specialRareImpactWitness.TryObserveNativeImpact(
                    sourceEventId, this, out impact) ||
                impact == null || impact.SourceEventId != sourceEventId ||
                (expectedReceipt != null &&
                 !Object.ReferenceEquals(expectedReceipt, impact)))
                return false;
            // A pure witness must not change the battle clock or roster.
            if (scheduler.Time != observedClock ||
                characters.Count != observedUnits ||
                enemies.Count != observedEnemies ||
                game.DefeatedEnemies != observedKills ||
                Math.Abs(impact.ObservedImpactTime - scheduler.Time) > 0.0000001 ||
                impact.OriginalW3xSha256 !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                IsUnsupportedGenericSourceAbility(impact.AbilityRawcode))
                return false;

            CharacterInstance caster;
            if (!characters.TryGetValue(impact.CasterInstanceId, out caster) ||
                caster.Catalog.Id != impact.CasterCatalogId ||
                (caster.Catalog.Grade != "특별함" &&
                 caster.Catalog.Grade != "희귀함"))
                return false;
            var original = specialRareOriginalMap.Find(impact.CasterCatalogId);
            if (original == null ||
                original.OriginalRawcode != impact.CasterOriginalRawcode)
                return false;
            bool ownedAbility = false;
            foreach (string ability in original.OriginalAbilityRawcodes)
                if (ability == impact.AbilityRawcode) ownedAbility = true;
            if (!ownedAbility) return false;
            // JASS crafting abilities are NEVER combat procs, even when
            // the owner also lists that ability in object metadata.
            foreach (string crafting in original.SourceJassFusionAbilityRawcodes)
                if (crafting == impact.AbilityRawcode) return false;

            string casterEvidence;
            if (!specialRareImpactWitness.TryVerifyCurrentCasterIdentity(
                    impact.CasterInstanceId, impact.CasterOriginalRawcode,
                    out casterEvidence) || string.IsNullOrWhiteSpace(casterEvidence))
                return false;

            var selected = new HashSet<string>(StringComparer.Ordinal);
            long totalFinalHp = 0;
            foreach (var hit in impact.Hits)
            {
                SpatialCombatEnemy enemy;
                if (!selected.Add(hit.EnemyId) ||
                    !enemies.TryGetValue(hit.EnemyId, out enemy) ||
                    !enemy.Enemy.IsAlive ||
                    !CanAttackInThisLane(caster, enemy) ||
                    resolution.GetEnemy(hit.EnemyId) == null ||
                    hit.FinalHpLoss > enemy.Enemy.Health)
                    return false;
                // Zero final damage is valid for an independently witnessed
                // native stun. The observer must certify that immunity was
                // checked for the ENTIRE source spell and each target.
                try { totalFinalHp = checked(totalFinalHp + hit.FinalHpLoss); }
                catch (OverflowException) { return false; }
            }
            if (scheduler.Time != observedClock ||
                characters.Count != observedUnits ||
                enemies.Count != observedEnemies ||
                game.DefeatedEnemies != observedKills)
                return false;

            // Replay is a world-level native event identity, not a
            // (attacker,skill) alias which could double-spend a single cast.
            if (committedSpecialRareImpactIds.Count >= 100000 ||
                !committedSpecialRareImpactIds.Add(sourceEventId))
                return false;

            string actor = impact.CasterInstanceId.ToString(
                CultureInfo.InvariantCulture);
            var eventSink = processingStep ? events : pendingCommandEvents;
            eventSink.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, actor, null,
                impact.HumanSkillName,
                impact.AbilityRawcode + " / witnessed final HP total " +
                totalFinalHp.ToString(CultureInfo.InvariantCulture) + " / " +
                impact.NativeCastAndTargetEvidence + " / " + casterEvidence, 0));

            foreach (var hit in impact.Hits)
            {
                long actual;
                if (!resolution.TryApplySkillDamage(actor, hit.EnemyId,
                        hit.FinalHpLoss, out actual))
                    throw new InvalidOperationException(
                        "원본 스킬 사전 검증 후 실시간 타깃이 사라졌습니다.");
                eventSink.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, actor, hit.EnemyId,
                    impact.HumanSkillName, hit.NativeHitEvidence, actual));
                SpatialCombatEnemy aliveEnemy;
                if (hit.VerifiedStunSeconds.HasValue &&
                    enemies.TryGetValue(hit.EnemyId, out aliveEnemy) &&
                    aliveEnemy.Enemy.IsAlive)
                {
                    statuses[hit.EnemyId].Apply(new CombatEffect(
                        "original:special-rare:" + sourceEventId + ":" + actor,
                        CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1,
                        hit.VerifiedStunSeconds.Value));
                    eventSink.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.StatusApplied, actor, hit.EnemyId,
                        impact.HumanSkillName, hit.NativeStunEvidence, 0));
                }
                if (!enemies.TryGetValue(hit.EnemyId, out aliveEnemy) ||
                    !aliveEnemy.Enemy.IsAlive)
                    eventSink.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated, actor, hit.EnemyId,
                        impact.HumanSkillName, impact.AbilityRawcode, actual));
            }
            RemoveDefeatedEnemies();
            return true;
        }
    }
}
