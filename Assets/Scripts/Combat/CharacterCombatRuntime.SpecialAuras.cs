using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private IOriginal323AuraCatalog originalSpecialAuras;
        private IOriginal323SpecialAuraTargetRules originalSpecialAuraTargets;
        private readonly HashSet<string> specialAuraKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<long, string> previousAllyAuraSignatures = new Dictionary<long, string>();
        private string previousEnemyAuraSignature;
        private readonly Dictionary<string, HashSet<CombatEffect>> provenSpecialEnemyAuras =
            new Dictionary<string, HashSet<CombatEffect>>(StringComparer.Ordinal);

        public bool IsVerifiedOriginalSpecialAuraEffect(string enemyId, CombatEffect effect)
        {
            HashSet<CombatEffect> active;
            return enemyId != null && effect != null &&
                provenSpecialEnemyAuras.TryGetValue(enemyId, out active) && active.Contains(effect);
        }

        public void ConfigureOriginalSpecialAuras(Original323SpecialAuraCatalog catalog,
            IOriginal323SpecialAuraTargetRules verifiedTargets = null)
        {
            ConfigureOriginalAuras(catalog, verifiedTargets);
        }

        public void ConfigureOriginalAuras(IOriginal323AuraCatalog catalog,
            IOriginal323SpecialAuraTargetRules verifiedTargets = null)
        {
            if (characters.Count != 0 || originalSpecialAuras != null)
                throw new InvalidOperationException("원본 오라는 첫 소환 전에 한 번만 연결하세요.");
            originalSpecialAuras = catalog ?? throw new ArgumentNullException(nameof(catalog));
            originalSpecialAuraTargets = verifiedTargets;
        }

        public Original323SpecialAura OriginalSpecialAuraOf(long instanceId)
        {
            var auras = OriginalAurasOf(instanceId);
            return auras.Count == 0 ? null : auras[0];
        }

        public IReadOnlyList<Original323SpecialAura> OriginalAurasOf(long instanceId)
        {
            CharacterInstance unit;
            if (!characters.TryGetValue(instanceId, out unit))
                throw new KeyNotFoundException("존재하지 않는 유닛");
            return originalSpecialAuras?.FindAll(unit.Catalog.Id) ??
                Array.AsReadOnly(new Original323SpecialAura[0]);
        }

        private bool IsVerifiedCurrentNativeAuraRank(long casterId,
            Original323SpecialAura aura)
        {
            var definitions = originalSpecialAuras.FindAll(
                characters[casterId].Catalog.Id);
            bool multiRank = false;
            foreach (var other in definitions)
                if (other.AbilityRawcode == aura.AbilityRawcode &&
                    other.Level != aura.Level)
                {
                    multiRank = true;
                    break;
                }
            if (!multiRank) return true;
            var observer = originalSpecialAuraTargets as
                IOriginal323NativeAuraRankWitness;
            if (observer == null) return false;
            int current;
            string proof;
            return observer.TryObserveCurrentRank(casterId,
                       aura.AbilityRawcode, Time, out current, out proof) &&
                   !string.IsNullOrWhiteSpace(proof) &&
                   current == aura.Level;
        }

        private void RefreshOriginalSpecialAuras()
        {
            foreach (var status in statuses.Values)
                foreach (string key in specialAuraKeys) status.RemoveSource(key);
            foreach (var status in unitStatuses.Values)
                foreach (string key in specialAuraKeys) status.RemoveSource(key);
            specialAuraKeys.Clear();
            provenSpecialEnemyAuras.Clear();
            if (originalSpecialAuras == null || originalSpecialAuraTargets == null) return;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            var enemyIds = new List<string>(enemies.Keys);
            enemyIds.Sort(StringComparer.Ordinal);
            var allySignatures = new Dictionary<long, string>();
            var enemySignature = new System.Text.StringBuilder();
            foreach (long id in ids)
            foreach (var aura in originalSpecialAuras.FindAll(characters[id].Catalog.Id))
            {
                // Multi-level source rows are candidates, not simultaneous
                // active buffs. Require the CURRENT Warcraft rank.
                if (!IsVerifiedCurrentNativeAuraRank(id, aura)) continue;
                var caster = roster.Get(id);
                if (aura.Type == CombatEffectType.ArmorReduction)
                {
                    var flatRules = originalSpecialAuraTargets as IOriginal323FlatArmorAuraRules;
                    string flatEvidence;
                    if (flatRules == null || !flatRules.IsVerifiedFlatArmor(aura, caster,
                        Time, out flatEvidence) || string.IsNullOrWhiteSpace(flatEvidence)) continue;
                }
                string key = aura.SourceKey(id);
                specialAuraKeys.Add(key);
                if (aura.TargetsAllies)
                {
                    foreach (long targetId in ids)
                    {
                        if (IsStoryDeployed(id) != IsStoryDeployed(targetId) ||
                            !aura.AllowsAllyRole(id == targetId)) continue;
                        var target = roster.Get(targetId);
                        double dx = target.X - caster.X, dy = target.Y - caster.Y;
                        string proof;
                        if (dx * dx + dy * dy > aura.Radius * aura.Radius ||
                            !originalSpecialAuraTargets.CanAffectAlly(aura, caster,
                                target, Time, out proof) || string.IsNullOrWhiteSpace(proof)) continue;
                        unitStatuses[InstanceKey(targetId)].Apply(new CombatEffect(key,
                            aura.Type, CombatEffectOrigin.Aura, aura.Amount, 0));
                        string signature;
                        allySignatures.TryGetValue(targetId, out signature);
                        allySignatures[targetId] = signature + "|" + key;
                    }
                }
                else
                {
                    foreach (string targetId in enemyIds)
                    {
                        var target = enemies[targetId];
                        if (!target.Enemy.IsAlive || !CanAttackInThisLane(characters[id], target)) continue;
                        double dx = target.X - caster.X, dy = target.Y - caster.Y;
                        string proof;
                        if (dx * dx + dy * dy > aura.Radius * aura.Radius ||
                            !originalSpecialAuraTargets.CanAffectEnemy(aura, caster,
                                target, Time, out proof) || string.IsNullOrWhiteSpace(proof)) continue;
                        var effect = new CombatEffect(key, aura.Type,
                            CombatEffectOrigin.Aura, aura.Amount, 0);
                        statuses[targetId].Apply(effect);
                        HashSet<CombatEffect> verified;
                        if (!provenSpecialEnemyAuras.TryGetValue(targetId, out verified))
                        {
                            verified = new HashSet<CombatEffect>();
                            provenSpecialEnemyAuras.Add(targetId, verified);
                        }
                        verified.Add(effect);
                        enemySignature.Append(targetId.Length).Append(':').Append(targetId)
                            .Append('|').Append(key).Append(';');
                    }
                }
            }
            // A missing buff formula may have blocked a native attack. Retry
            // only when this unit's actual aura membership changes; removing
            // the buff must restore its otherwise approved vanilla attacks.
            string newEnemySignature = enemySignature.ToString();
            foreach (long id in ids)
            {
                string before, after;
                previousAllyAuraSignatures.TryGetValue(id, out before);
                allySignatures.TryGetValue(id, out after);
                if (before != after || previousEnemyAuraSignature != newEnemySignature)
                    ResumeAfterVerifiedFormulaUpdate(id);
            }
            previousAllyAuraSignatures.Clear();
            foreach (var pair in allySignatures) previousAllyAuraSignatures.Add(pair.Key, pair.Value);
            previousEnemyAuraSignature = newEnemySignature;
        }
    }
}
