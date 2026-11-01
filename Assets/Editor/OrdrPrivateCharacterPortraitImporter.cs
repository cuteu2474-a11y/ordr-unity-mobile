#if UNITY_EDITOR
using UnityEditor;

namespace Ordr.Mobile.Editor
{
    // Unity's default PNG import may be Texture, which Resources.Load<Sprite>
    // cannot use. Configure only the explicitly private portrait directory.
    internal sealed class OrdrPrivateCharacterPortraitImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/CharacterPortraits/",
                    System.StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 512;
        }
    }
}
#endif
