using UnityEditor;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Import rules for the surface textures in Resources/Surfaces (CC0 scans, see docs/THIRD_PARTY_ASSETS.md):
    /// "*_nor" files are tangent-space normal maps (OpenGL convention, as Unity expects), everything is at most
    /// 1024 px with mipmaps, repeating and anisotropic filtering.
    /// </summary>
    public sealed class FreeVeloSurfaceImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Surfaces/")) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_nor.jpg") || assetPath.EndsWith("_nor.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
        }
    }
}
