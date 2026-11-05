using System;

namespace Ordr.Mobile.Core
{
    // Legacy adapter name. The actual kW[player]==ka choice is CASINO:
    // BdL adds +1 EXTRA OTHERWORLD GAMBLING CHARGE, never a trait point.
    // This must come from the actual selected navigation, not hero grade.
    public interface IOriginal323StoryTraitSelection
    {
        bool HasDoublePointStoryTrait();
    }
}
