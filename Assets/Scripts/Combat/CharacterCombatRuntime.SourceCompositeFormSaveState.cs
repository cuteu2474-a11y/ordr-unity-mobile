using System;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // All of these forms stay on the same roster instance. An exact
        // finite profile lifetime is required; H0BY instead ends at zero MP.
        private bool CanRebindRestoredCompositeForm(CharacterInstance actor,
            string form)
        {
            if (actor == null || actor.Mapping == null ||
                actor.Profile == null || sourceModelRules == null ||
                actor.Abilities.Resources == null) return false;
            switch (form)
            {
                case "awakening":
                    return SourceModelTenthFormProfile(actor, "TR-03", "H09E") &&
                        SourceModelTenthCompleteForm(
                            FindSourceSkill(actor, "각성"), "TR-03");
                case "forest":
                    return SourceModelTenthFormProfile(actor, "TR-06", "H0BL") &&
                        SourceModelTenthCompleteForm(
                            FindSourceSkill(actor, "숲인간 변신"), "TR-06");
                case "giant":
                    return SourceModelTenthFormProfile(actor, "E-01", "H0BK") &&
                        SourceModelEleventhGiant(FindSourceSkill(actor, "거대화"));
                case "executioner":
                    return SourceModelTenthFormProfile(actor, "LI-10", "h0AI") &&
                        SourceModelEleventhKing(FindSourceSkill(actor, "처형자"));
                case "frankyGeneral":
                    return SourceModelTenthFormProfile(actor, "TR-28", "H08Y") &&
                        SourceModelTenthCompleteFrankyUpgrade(actor);
                case "nika":
                    return SourceModelTenthFormProfile(actor, "TR-21", "H0BX") &&
                        SourceModelEleventhBonney(
                            FindSourceSkill(actor, "니카같은 미래"));
                default: return false;
            }
        }

        private static bool ValidateRestorableCompositeForm(
            SourceCombatFormCheckpoint form, SourceCombatActorCheckpoint actor,
            double time)
        {
            if (form == null || actor == null || form.OwnerId <= 0)
                return false;
            if (form.Form == "nika")
                return actor.CatalogId == "TR-21" &&
                    actor.SourceManagedResources && form.ExpiresAt == -1;
            string sourceCatalog;
            double originalDuration;
            switch (form.Form)
            {
                case "awakening":
                    sourceCatalog = "TR-03"; originalDuration = 4.5; break;
                case "forest":
                    sourceCatalog = "TR-06"; originalDuration = 7; break;
                case "giant":
                    sourceCatalog = "E-01"; originalDuration = 4.25; break;
                case "executioner":
                    sourceCatalog = "LI-10"; originalDuration = 1.25; break;
                case "frankyGeneral":
                    sourceCatalog = "TR-28"; originalDuration = 6; break;
                default: return false;
            }
            return actor.CatalogId == sourceCatalog &&
                actor.SourceManagedResources &&
                !double.IsNaN(form.ExpiresAt) &&
                !double.IsInfinity(form.ExpiresAt) &&
                form.ExpiresAt > time &&
                form.ExpiresAt - time <= originalDuration + .00000001;
        }
    }
}
