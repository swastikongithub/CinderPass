using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Import conventions for third-party source art, applied automatically on (re)import:
    /// normal maps flagged, data maps linear, sensible size caps and compression, meshes readable
    /// for the LOD/decimation tooling, no imported materials (the MaterialLibrary owns materials).
    /// </summary>
    public sealed class ImportRules : AssetPostprocessor
    {
        static bool IsThirdParty(string path) => path.StartsWith(CinderPaths.ThirdParty);

        void OnPreprocessTexture()
        {
            if (!IsThirdParty(assetPath)) return;
            var imp = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

            bool normal = file.Contains("_nor");
            bool data = file.Contains("_arm") || file.Contains("_rough") || file.Contains("_metal") || file.Contains("_disp")
                        || file.Contains("_ao") || file.Contains("_alpha") || file.Contains("_mask");
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal && !data;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.isReadable = false;

            if (assetPath.EndsWith(".hdr"))
            {
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                imp.maxTextureSize = 4096;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.sRGBTexture = false;
                return;
            }
            // Source textures that only feed the generators (packed into mask maps) stay small in memory.
            imp.maxTextureSize = assetPath.Contains("/Ground/") ? 2048 : 1024;
            if (assetPath.Contains("/Foliage/")) imp.maxTextureSize = 2048;
        }

        void OnPreprocessModel()
        {
            if (!IsThirdParty(assetPath)) return;
            var imp = (ModelImporter)assetImporter;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importAnimation = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.isReadable = true;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.addCollider = false;
        }
    }
}
