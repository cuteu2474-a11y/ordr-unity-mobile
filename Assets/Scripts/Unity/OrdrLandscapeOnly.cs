using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Enforce landscape on both iPad/iPhone and Android before the game scene
    // draws; both landscape directions are allowed, neither portrait is.
    public static class OrdrLandscapeOnly
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureScreenOrientation()
        {
#if UNITY_IOS || UNITY_ANDROID
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        }
    }
}
