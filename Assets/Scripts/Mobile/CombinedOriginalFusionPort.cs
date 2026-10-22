using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // The fixed Nightmare + Otherworld mode shows regular and Mystic recipes
    // together. This dispatcher NEVER executes recipes on its own.
    public sealed class CombinedOriginalFusionPort : IOriginalFusionPort
    {
        private readonly AssistedFusionService regular;
        private readonly OtherworldMysticFusionPreviewPort mystic;
        private readonly Dictionary<string, IOriginalFusionPort> owners =
            new Dictionary<string, IOriginalFusionPort>(StringComparer.Ordinal);

        public bool CanCommit { get { return regular.CanCommit || mystic.CanCommit; } }

        public CombinedOriginalFusionPort(AssistedFusionService regularRecipes,
            OtherworldMysticFusionPreviewPort mysticRecipes)
        {
            regular = regularRecipes ?? throw new ArgumentNullException(nameof(regularRecipes));
            mystic = mysticRecipes ?? throw new ArgumentNullException(nameof(mysticRecipes));
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits)
        {
            if (ownedUnits == null) throw new ArgumentNullException(nameof(ownedUnits));
            var nextOwners = new Dictionary<string, IOriginalFusionPort>(StringComparer.Ordinal);
            var previews = new List<MobileFusionPreview>();
            Add(previews, nextOwners, regular, regular.AvailableRecipes(ownedUnits));
            Add(previews, nextOwners, mystic, mystic.AvailableRecipes(ownedUnits));
            previews.Sort((a, b) => string.CompareOrdinal(a.RecipeId, b.RecipeId));
            owners.Clear();
            foreach (var pair in nextOwners) owners.Add(pair.Key, pair.Value);
            return previews.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> selectedMaterialInstances)
        {
            IOriginalFusionPort owner;
            if (string.IsNullOrWhiteSpace(recipeId) ||
                !owners.TryGetValue(recipeId, out owner))
                return new ManualActionResult(ManualActionState.Rejected,
                    "조합식을 새로고침하여 원본 재료를 확인해 주세요.");
            // The underlying port revalidates ownership, materials and original
            // world authorization at the exact confirmation moment.
            return owner.ConfirmFusion(recipeId, selectedMaterialInstances);
        }

        private static void Add(List<MobileFusionPreview> combined,
            Dictionary<string, IOriginalFusionPort> lookup, IOriginalFusionPort source,
            IReadOnlyList<MobileFusionPreview> offers)
        {
            if (offers == null)
                throw new InvalidOperationException("원본 조합 목록을 읽지 못했습니다.");
            foreach (var offer in offers)
            {
                if (offer == null || lookup.ContainsKey(offer.RecipeId))
                    throw new InvalidOperationException("원본 조합식 ID가 누락되거나 중복됐습니다.");
                lookup.Add(offer.RecipeId, source);
                combined.Add(offer);
            }
        }
    }
}
