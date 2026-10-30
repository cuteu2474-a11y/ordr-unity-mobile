using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Core
{
    // Real local campaign flags for source-confirmed story research and base
    // trait points. Other Warcraft native effects are never marked as applied
    // until their actual actor/hero subsystem acknowledges the action.
    public sealed class Original323StoryMobileProgression :
        IOriginal323StoryNativeRewardPort
    {
        private readonly HashSet<string> applied =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> research =
            new HashSet<string>(StringComparer.Ordinal);
        private IOriginal323StoryNativeRewardPort externalEffects;
        private IOriginal323StoryTraitSelection traitSelection;
        private bool traitPointsAlreadySettled;
        public int TraitPoints { get; private set; }
        public void ConfigureOriginalStoryTraitSelection(
            IOriginal323StoryTraitSelection source)
        {
            if (traitPointsAlreadySettled)
                throw new InvalidOperationException(
                    "기본 스토리 특성포인트 정산 후에는 선택 상태를 바꿀 수 없습니다.");
            traitSelection = source ??
                throw new ArgumentNullException(nameof(source));
        }
        public IReadOnlyCollection<string> Researches => research;
        public bool HasResearch(string originalRawcode) =>
            !string.IsNullOrEmpty(originalRawcode) &&
            research.Contains(originalRawcode);

        public void ConnectAdditionalNativeEffects(
            IOriginal323StoryNativeRewardPort nativeWorld)
        {
            externalEffects = nativeWorld ??
                throw new ArgumentNullException(nameof(nativeWorld));
            if (ReferenceEquals(nativeWorld, this))
                throw new ArgumentException("자기 자신을 보상 처리기에 연결할 수 없습니다.");
        }

        private static bool KnownResearch(int stage, string rawcode) =>
            (stage == 5 && rawcode == "R01N") ||
            (stage == 8 && rawcode == "R01O") ||
            (stage == 9 && rawcode == "R01P") ||
            (stage == 10 && rawcode == "R01Q") ||
            (stage == 12 && rawcode == "R01S");

        public bool TryApplySourceNativeReward(Original323StoryNativeReward award)
        {
            if (award == null || string.IsNullOrWhiteSpace(award.Receipt))
                return false;
            if (applied.Contains(award.Receipt)) return true;
            if (award.Kind == Original323StoryNativeRewardKind.Research &&
                award.Amount == 1 &&
                KnownResearch(award.Stage, award.OriginalRawcode))
            {
                research.Add(award.OriginalRawcode);
                applied.Add(award.Receipt);
                return true;
            }
            // Original BdL grants 1 base point; a further +1 depends on
            // kW[player] == ka and MUST NOT be inferred from story completion.
            if (award.Kind == Original323StoryNativeRewardKind.TraitPoints &&
                award.Stage >= 8 && award.Stage <= 13 &&
                award.Amount == 1 && award.OriginalRawcode == "BdL")
            {
                int originalPoints = award.Amount;
                if (traitSelection != null && traitSelection.HasDoublePointStoryTrait())
                    originalPoints = checked(originalPoints + 1);
                TraitPoints = checked(TraitPoints + originalPoints);
                traitPointsAlreadySettled = true;
                applied.Add(award.Receipt);
                return true;
            }
            if (externalEffects != null &&
                externalEffects.TryApplySourceNativeReward(award))
            {
                applied.Add(award.Receipt);
                return true;
            }
            return false;
        }
    }
}
