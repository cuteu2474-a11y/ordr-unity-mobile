using System.Runtime.CompilerServices;

// EditMode tests exercise source-model transform/lifecycle contracts via
// internal helpers without widening the Android player's public API.
// The assembly name is pinned to Assets/Tests/EditMode/Ordr.Mobile.EditModeTests.asmdef.
[assembly: InternalsVisibleTo("Ordr.Mobile.EditModeTests")]
