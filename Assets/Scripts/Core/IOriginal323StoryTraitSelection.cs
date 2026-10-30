using System;

namespace Ordr.Mobile.Core
{
    // Actual 2.323[C] game trait choice kW[player] determines whether BdL
    // adds +1 EXTRA point. This must be read from an owned, verified player
    // selection; it cannot be inferred from Nightmare difficulty or hero grade.
    public interface IOriginal323StoryTraitSelection
    {
        bool HasDoublePointStoryTrait();
    }
}
