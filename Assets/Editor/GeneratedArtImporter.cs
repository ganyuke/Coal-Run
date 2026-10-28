using UnityEditor;
using UnityEngine;

namespace CoalRun.Editor
{
    // Everything under Assets/Art/Generated is a sprite at 64 pixels a unit unless its name says otherwise
    // ("name@128.png" is 128 a unit), smooth-filtered and uncompressed, so the low-poly edges stay clean. A name
    // ending "_tile" gets a full-rect mesh, so a SpriteRenderer can draw it in Tiled mode (girders, chain).
    public class GeneratedArtImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Art/Generated/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            var ppu = 64f;
            var at = assetPath.LastIndexOf('@');
            if (at >= 0)
            {
                var digits = System.IO.Path.GetFileNameWithoutExtension(assetPath)[(System.IO.Path.GetFileNameWithoutExtension(assetPath).LastIndexOf('@') + 1)..];
                if (float.TryParse(digits, out var parsed)) ppu = parsed;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            if (!System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith("_tile")) return;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}
