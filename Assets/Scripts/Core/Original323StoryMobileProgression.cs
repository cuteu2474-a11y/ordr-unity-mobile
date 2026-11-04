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
        public const string GreenBloodUseResourceId = "story:green-blood:A13A";
        private readonly HashSet<string> applied =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> research =
            new HashSet<string>(StringComparer.Ordinal);
        private IOriginal323StoryNativeRewardPort externalEffects;
        private IOriginal323StoryNativeRewardPort storyMaterialEffects;
        private IOriginal323StoryTraitSelection traitSelection;
        private readonly EconomyWallet sharedWallet;
        private bool traitPointsAlreadySettled;
        public int TraitPoints { get; private set; }

        // The parameterless model remains usable for source-only audits.
        // Live solo sessions bind the same wallet used by missions and units.
        public Original323StoryMobileProgression() { }
        public Original323StoryMobileProgression(EconomyWallet wallet)
        {
            sharedWallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }
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

        // The built-in stage-nine support actor is a separate native effect.
        // An optional external replay adapter cannot create a second copy.
        public void ConnectStoryMaterialEffects(IOriginal323StoryNativeRewardPort port)
        {
            if (storyMaterialEffects != null || port == null || ReferenceEquals(port, this))
                throw new InvalidOperationException("스토리 재료 월드를 중복/잘못 연결했습니다.");
            storyMaterialEffects = port;
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
            if (award.Kind == Original323StoryNativeRewardKind.AncientShip &&
                storyMaterialEffects != null)
            {
                if (!storyMaterialEffects.TryApplySourceNativeReward(award)) return false;
                applied.Add(award.Receipt);
                return true;
            }
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
                if (sharedWallet != null)
                {
                    // The native obligation is settled only once the actual
                    // spendable balance and durable receipt have been committed.
                    // A post-commit observer may throw: its receipt still wins
                    // so replay never grants another set of points.
                    try
                    {
                        if (!sharedWallet.TryGrantOnce(award.Receipt,
                            new ResourceBundle().Add(
                                OriginalMissionWalletRewards.TraitPointsId,
                                originalPoints)) &&
                            !sharedWallet.HasCompletedTransaction(award.Receipt))
                            return false;
                    }
                    catch
                    {
                        if (!sharedWallet.HasCompletedTransaction(award.Receipt))
                            throw;
                    }
                }
                TraitPoints = checked(TraitPoints + originalPoints);
                traitPointsAlreadySettled = true;
                applied.Add(award.Receipt);
                return true;
            }
            if (award.Kind == Original323StoryNativeRewardKind.GreenBloodSkill &&
                award.Stage == 14 && award.OriginalRawcode == "A13A" &&
                award.Amount == 1)
            {
                // If a native actor already applied the real one-use spell,
                // do not mint a second mobile casting entitlement.
                if (externalEffects != null &&
                    externalEffects.TryApplySourceNativeReward(award))
                {
                    applied.Add(award.Receipt);
                    return true;
                }
                if (sharedWallet == null) return false;
                // Mobile result model for the exact three CKe replacement
                // branches without a CEN hook. This token is consumed as
                // GREN in the same wallet exchange as the target-unit swap.
                try
                {
                    if (!sharedWallet.TryGrantOnce(award.Receipt,
                        new ResourceBundle().Add(GreenBloodUseResourceId, 1)) &&
                        !sharedWallet.HasCompletedTransaction(award.Receipt))
                        return false;
                }
                catch
                {
                    if (!sharedWallet.HasCompletedTransaction(award.Receipt))
                        throw;
                }
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
