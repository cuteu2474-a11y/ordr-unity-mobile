using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private SourceModelUnitResourceCatalog sourceModelResourceCatalog;
        private readonly Dictionary<long, SourceModelUnitResourceSpec>
            sourceModelManagedResourceRows =
                new Dictionary<long, SourceModelUnitResourceSpec>();

        public void ConfigureSourceModelUnitResources(SourceModelUnitResourceCatalog source)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceModelResourceCatalog != null)
                throw new InvalidOperationException(
                    "원본 기반 연습 자원은 전투 모델에 한 번만 연결합니다.");
            sourceModelResourceCatalog = source ??
                throw new ArgumentNullException(nameof(source));
            foreach (var actor in characters.Values)
                ProvisionSourceModelResources(actor);
        }

        private bool CanProvisionSourceModelResources(CharacterInstance actor)
        {
            if (!UsesSourceModelBasicAttacks || actor == null ||
                actor.Profile == null || actor.Mapping == null ||
                actor.Abilities.Resources != null) return false;
            // Existing form skill also consumes source HP; its runtime
            // already validates the complete transform packet.
            if ((actor.Catalog.Id == "TR-18" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0BD") ||
                (actor.Catalog.Id == "TR-03" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H09E") ||
                (actor.Catalog.Id == "TR-06" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0BL") ||
                (actor.Catalog.Id == "TR-28" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H08Y") ||
                (actor.Catalog.Id == "E-01" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0BK") ||
                (actor.Catalog.Id == "TR-21" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0BX") ||
                (actor.Catalog.Id == "LI-10" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "h0AI") ||
                (actor.Catalog.Id == "LI-03" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "h08O") ||
                (actor.Catalog.Id == "TR-13" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H08Z") ||
                (actor.Catalog.Id == "L-32" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "h030") ||
                (actor.Catalog.Id == "TR-23" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H091") ||
                (actor.Catalog.Id == "TR-30" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0AA") ||
                (actor.Catalog.Id == "I-11" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "h07M") ||
                // B7U spends one HP per declared attack and B6t grants
                // mana. Those native events require h067's pinned SLK pool
                // even if the separate tooltip active is not auto-castable.
                (actor.Catalog.Id == "E-09" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "h067"))
                return true;
            // TR-26 has a documented mana-gated on-attack packet rather
            // than an activeMana skill; its original SLK pool is still needed.
            if (actor.Catalog.Id == "TR-26" &&
                actor.Mapping.AttackSpec.UnitRawCode == "H0B5")
                foreach (var skill in actor.Profile.Skills)
                    if (skill.Id == "TR-26:skill:0" &&
                        SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill))
                        return true;
            // Aokiji's 12.5% Ice Age is an on-attack *mana-gated* cast.
            // Reuse the original SLK pool; no guessed starting MP.
            if (actor.Catalog.Id == "TR-16" &&
                actor.Mapping.AttackSpec.UnitRawCode == "H097")
                foreach (var skill in actor.Profile.Skills)
                    if (skill.Id == "TR-16:skill:0" &&
                        SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill))
                        return true;
            if ((actor.Catalog.Id == "TR-05" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H096") ||
                (actor.Catalog.Id == "TR-25" &&
                 actor.Mapping.AttackSpec.UnitRawCode == "H0B4"))
                return SourceModelTwentyFifthSkillCatalog.HasExactOwner(
                    actor.Profile, actor.Catalog.Id);
            // LI-08 spends HP on an on-attack proc, not an activeHealth cast.
            // Provision only from its pinned SLK object and exact profile;
            // without that source pool the proc must not run at all.
            if (actor.Catalog.Id == "LI-08" &&
                actor.Mapping.AttackSpec.UnitRawCode == "h07I")
                return SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(
                    actor.Profile);
            SourceModelNextThirtyUnit entry;
            SourceModelUnconnectedSkillRow extra;
            IReadOnlyList<string> activeNames;
            if (SourceModelNextThirtySkillCatalog.TryGet(
                    actor.Mapping.AttackSpec.UnitRawCode, out entry) &&
                entry.CatalogId == actor.Catalog.Id)
                activeNames = entry.PracticeActiveSkills;
            else if (SourceModelUnconnectedSkillCatalog.TryGet(
                    actor.Mapping.AttackSpec.UnitRawCode, out extra) &&
                extra.CatalogId == actor.Catalog.Id)
                activeNames = extra.PracticeActiveSkills;
            else return false;
            if (activeNames.Count == 0) return false;
            foreach (var skill in actor.Profile.Skills)
                foreach (var approved in activeNames)
                    if (skill.Name == approved &&
                        SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill))
                        return true;
            return false;
        }

        private void ProvisionSourceModelResources(CharacterInstance actor)
        {
            if (sourceModelResourceCatalog == null ||
                !CanProvisionSourceModelResources(actor)) return;
            var stats = sourceModelResourceCatalog.Find(actor.Catalog.Id);
            if (stats == null ||
                stats.OriginalRawcode != actor.Mapping.AttackSpec.UnitRawCode) return;
            actor.Abilities.ConfigureResources(stats.CreateStartingPool());
            sourceModelManagedResourceRows.Add(actor.Id, stats);
        }

        private void AdvanceSourceModelResources(double seconds)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelManagedResourceRows.Count == 0)
                return;
            foreach (var actor in characters.Values)
            {
                SourceModelUnitResourceSpec source;
                if (sourceModelManagedResourceRows.TryGetValue(actor.Id, out source))
                {
                    // Native IntRegenBonus = 0.08 mana/sec for every additional
                    // integer INT point earned after the starting hero level.
                    var growth = original323HeroLevelWorld == null ? null :
                        original323HeroLevelWorld.LevelStateOf(actor.Id);
                    double levelManaRegen = growth == null ? 0 :
                        growth.AddedIntelligence * .08;
                    actor.Abilities.Resources.RegenerateSourceModel(
                        source.HealthRegeneration,
                        source.ManaRegeneration + levelManaRegen, seconds);
                }
            }
        }

        // Explicitly allow-listed complete tooltip/model actives
        // are auto-cast in practice using original W3X resource baselines.
        // No guessed spell targets, cooldown or source RNG:
        // reuse the same target policy and source script skill executor.
        // All casts settle after the combat Step; never mutate during attacks.
        public int AutoCastApprovedSourceModelActives(int maximumPerFrame = 32)
        {
            if (maximumPerFrame < 1 || maximumPerFrame > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumPerFrame));
            if (!UsesSourceModelBasicAttacks || game.Phase != Ordr.Mobile.Core.SessionPhase.Running ||
                processingStep || pendingStepSeconds != 0) return 0;
            // A unit with independently verified external CharacterResources
            // may auto-cast an explicitly approved source skill even when
            // no global source-object resource catalogue was installed.
            // Individual actors without resource states still remain blocked.

            var ids = new List<long>(characters.Keys);
            ids.Sort();
            int completed = 0;
            foreach (long id in ids)
            {
                if (completed >= maximumPerFrame) break;
                CharacterInstance actor;
                if (!characters.TryGetValue(id, out actor) ||
                    actor.Profile == null || actor.Mapping == null ||
                    !actor.AttackReady || IsOriginalUnitCasting(id) ||
                    SourceFormIsCasting(id) || actor.Abilities.Resources == null)
                    continue;
                if (TryAutoCastSourceModelTenthForm(actor))
                {
                    completed++;
                    continue;
                }
                SourceModelNextThirtyUnit row;
                SourceModelUnconnectedSkillRow extra;
                IReadOnlyList<string> activeNames;
                if (SourceModelNextThirtySkillCatalog.TryGet(
                        actor.Mapping.AttackSpec.UnitRawCode, out row) &&
                    row.CatalogId == actor.Catalog.Id)
                    activeNames = row.PracticeActiveSkills;
                else if (SourceModelUnconnectedSkillCatalog.TryGet(
                        actor.Mapping.AttackSpec.UnitRawCode, out extra) &&
                    extra.CatalogId == actor.Catalog.Id)
                    activeNames = extra.PracticeActiveSkills;
                else continue;
                if (activeNames.Count == 0) continue;
                var targets = BuildTargetCandidates(actor);
                if (targets.Count == 0) continue;
                string primary = ChoosePreviewTarget(actor, targets);
                if (primary == null) continue;
                // Explicit approval order is the mobile auto-skill priority.
                // A profile may list a newly connected HP active ahead of an
                // existing MP active; connecting it must not silently change
                // the previously selected primary automatic skill.
                bool actorCast = false;
                foreach (string approvedName in activeNames)
                {
                    foreach (var skill in actor.Profile.Skills)
                    {
                        if (skill.Name != approvedName ||
                            !SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill))
                            continue;
                        double hp = skill.Number("healthCost") ?? 0;
                        double mp = skill.Number("manaCost") ?? 0;
                        if (!actor.Abilities.Resources.CanSpend(hp, mp)) continue;
                        if (TryCastActiveSkill(id, skill.Id, primary) ==
                            AbilityCastResult.Cast)
                        {
                            completed++;
                            actorCast = true;
                            break;
                        }
                    }
                    // At most one auto-cast per actor in a frame.
                    if (actorCast) break;
                }
            }
            return completed;
        }
    }
}
