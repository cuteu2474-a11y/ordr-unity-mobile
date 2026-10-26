using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // Keep regular, source-verified JASS and Otherworld Mystic recipes in one
    // player-facing catalog. ONLY the source that owns an audited recipe may
    // perform its irreversible original-world transaction.
    public sealed class CombinedOriginalFusionPort :
        IOriginalFusionPort, IOriginalFusionCommitCapability,
        IOriginalFusionRecipeCommitCapability
    {
        private readonly IReadOnlyList<IOriginalFusionPort> sources;
        private readonly Dictionary<string, IOriginalFusionPort> owners =
            new Dictionary<string, IOriginalFusionPort>(StringComparer.Ordinal);

        public CombinedOriginalFusionPort(AssistedFusionService regularRecipes,
            OtherworldMysticFusionPreviewPort mysticRecipes)
            : this(new IOriginalFusionPort[] { regularRecipes, mysticRecipes })
        { }

        // At least two independent sources. This also allows the verified
        // original JASS port to coexist with the two familiar mobile catalogs.
        public CombinedOriginalFusionPort(params IOriginalFusionPort[] recipeSources)
        {
            if (recipeSources == null || recipeSources.Length < 2)
                throw new ArgumentException("원본 조합 목록은 둘 이상 연결해야 합니다.");
            var unique = new HashSet<IOriginalFusionPort>();
            var copies = new List<IOriginalFusionPort>();
            foreach (var source in recipeSources)
            {
                if (source == null || !unique.Add(source))
                    throw new ArgumentException("조합 제공자가 누락되거나 중복됐습니다.");
                copies.Add(source);
            }
            sources = copies.AsReadOnly();
        }

        public bool CanCommit
        {
            get
            {
                foreach (var source in sources)
                    if (SourceCanCommit(source)) return true;
                return false;
            }
        }

        public bool CanCommitRecipe(string recipeId)
        {
            IOriginalFusionPort source;
            return !string.IsNullOrWhiteSpace(recipeId) &&
                owners.TryGetValue(recipeId, out source) &&
                SourceCanCommit(source) &&
                SourceCanCommitRecipe(source, recipeId);
        }

        private static bool SourceCanCommitRecipe(IOriginalFusionPort source,
            string recipeId)
        {
            var scoped = source as IOriginalFusionRecipeCommitCapability;
            return scoped == null || scoped.CanCommitRecipe(recipeId);
        }

        private static bool SourceCanCommit(IOriginalFusionPort source)
        {
            var capability = source as IOriginalFusionCommitCapability;
            // Legacy externally supplied original ports remain supported.
            return capability == null || capability.CanCommit;
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits)
        {
            if (ownedUnits == null)
                throw new ArgumentNullException(nameof(ownedUnits));
            var nextOwners =
                new Dictionary<string, IOriginalFusionPort>(StringComparer.Ordinal);
            var previews = new List<MobileFusionPreview>();
            foreach (var source in sources)
                Add(previews, nextOwners, source, source.AvailableRecipes(ownedUnits));
            previews.Sort((a, b) =>
                string.CompareOrdinal(a.RecipeId, b.RecipeId));
            // Don't retain stale partial registration if one provider fails.
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
            if (!SourceCanCommit(owner) ||
                !SourceCanCommitRecipe(owner, recipeId))
                return ManualActionResult.Unavailable(
                    "선택한 조합식의 원작 제작 거래");
            // Only an original-world atomic commit may debit the wallet,
            // consume material instances and create the result unit.
            return owner.ConfirmFusion(recipeId, selectedMaterialInstances) ??
                new ManualActionResult(ManualActionState.Rejected,
                    "원본 조합 실행 결과를 확인하지 못했습니다.");
        }

        private static void Add(List<MobileFusionPreview> combined,
            Dictionary<string, IOriginalFusionPort> lookup,
            IOriginalFusionPort source,
            IReadOnlyList<MobileFusionPreview> offers)
        {
            if (offers == null)
                throw new InvalidOperationException("원본 조합 목록을 읽지 못했습니다.");
            foreach (var offer in offers)
            {
                if (offer == null || lookup.ContainsKey(offer.RecipeId))
                    throw new InvalidOperationException(
                        "원본 조합식 ID가 누락되거나 중복됐습니다.");
                lookup.Add(offer.RecipeId, source);
                combined.Add(offer);
            }
        }
    }
}
