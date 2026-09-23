using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Single authority for every material and terrain layer in the game. Materials are shared assets
    /// (never per-instance copies) so the SRP Batcher / GPU Resident Drawer can batch everything.
    /// </summary>
    public static class MaterialLibrary
    {
        const string LitShaderName = "Universal Render Pipeline/Lit";

        public static string MatPath(string folder, string name) => $"{CinderPaths.Materials}/{folder}/{name}.mat";

        // ------------------------------------------------------------------ generic URP Lit

        public sealed class LitSpec
        {
            public Color color = Color.white;
            public Texture albedo, normal, mask;
            public float smoothness = 0.3f, metallic = 0f, normalScale = 1f, occlusion = 1f;
            public Vector2 tiling = Vector2.one;
            public bool alphaClip;
            public float cutoff = 0.5f;
            public Color emission = Color.black;
            public bool doubleSided;
        }

        public static Material Lit(string folder, string name, LitSpec s)
        {
            var m = AssetUtil.Material(MatPath(folder, name), AssetUtil.FindShader(LitShaderName));
            m.SetColor("_BaseColor", s.color);
            m.SetTexture("_BaseMap", s.albedo);
            m.SetTextureScale("_BaseMap", s.tiling);
            m.SetTexture("_BumpMap", s.normal);
            m.SetFloat("_BumpScale", s.normalScale);
            m.SetTexture("_MetallicGlossMap", s.mask);
            m.SetTexture("_OcclusionMap", s.mask);
            m.SetFloat("_OcclusionStrength", s.mask != null ? s.occlusion : 0f);
            m.SetFloat("_Smoothness", s.mask != null ? Mathf.Max(s.smoothness, 0.01f) : s.smoothness);
            m.SetFloat("_Metallic", s.metallic);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_AlphaClip", s.alphaClip ? 1f : 0f);
            m.SetFloat("_Cutoff", s.cutoff);
            m.SetFloat("_Cull", s.doubleSided ? 0f : 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            bool emissive = s.emission.maxColorComponent > 0.001f;
            m.SetColor("_EmissionColor", s.emission);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (emissive) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ terrain

        public sealed class LayerDef
        {
            public string id;
            public string assetName;
            public float tileSize;
            public Color tint = Color.white;
            public float normalScale = 1f;
            public float smoothnessScale = 1f;
            /// <summary>Added to the mask-map height when layers meet (positive = sits on top).</summary>
            public float heightBias;
        }

        /// <summary>Terrain layers in painting order (index = splat channel).</summary>
        public static readonly LayerDef[] TerrainLayerDefs =
        {
            new LayerDef { id = "sparse_grass",        assetName = "TL_Grass",       tileSize = 6f,   tint = new Color(0.86f, 0.97f, 0.74f) },
            new LayerDef { id = "brown_mud_leaves_01", assetName = "TL_ForestFloor", tileSize = 5f,   tint = new Color(0.78f, 0.77f, 0.7f) },
            new LayerDef { id = "rocky_trail",         assetName = "TL_Trail",       tileSize = 4.5f, tint = new Color(0.7f, 0.64f, 0.56f), heightBias = 0.06f },
            new LayerDef { id = "forest_ground_04",    assetName = "TL_Dirt",        tileSize = 5f,   tint = new Color(0.82f, 0.77f, 0.7f) },
            new LayerDef { id = "aerial_rocks_02",     assetName = "TL_Rock",        tileSize = 17f,  tint = new Color(0.56f, 0.56f, 0.55f), normalScale = 1.4f, heightBias = 0.18f },
            new LayerDef { id = "burned_ground_01",    assetName = "TL_Scorched",    tileSize = 6f,   tint = new Color(0.58f, 0.56f, 0.54f) },
            new LayerDef { id = "ground_grey",         assetName = "TL_Ash",         tileSize = 7f,   tint = new Color(0.34f, 0.33f, 0.33f), heightBias = -0.06f },
            new LayerDef { id = "dark_rock_02",        assetName = "TL_Basalt",      tileSize = 11f,  tint = new Color(0.6f, 0.58f, 0.58f), normalScale = 1.3f, heightBias = 0.12f },
        };

        public const int LayerGrass = 0, LayerForest = 1, LayerTrail = 2, LayerDirt = 3, LayerRock = 4, LayerScorched = 5, LayerAsh = 6, LayerBasalt = 7;

        public static TerrainLayer[] BuildTerrainLayers()
        {
            var result = new TerrainLayer[TerrainLayerDefs.Length];
            for (int i = 0; i < TerrainLayerDefs.Length; i++)
            {
                var d = TerrainLayerDefs[i];
                string dir = $"{CinderPaths.GroundTextures}/{d.id}";
                string path = $"{CinderPaths.TerrainLayers}/{d.assetName}.terrainlayer";
                AssetUtil.EnsureFolder(CinderPaths.TerrainLayers);
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, path);
                }
                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.id}_diff.jpg");
                layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.id}_nor.jpg");
                layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.GroundMaskPath(d.id));
                layer.tileSize = new Vector2(d.tileSize, d.tileSize);
                layer.normalScale = d.normalScale;
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = new Vector4(d.tint.r, d.tint.g, d.tint.b, 1f);
                layer.maskMapRemapMin = Vector4.zero;
                layer.maskMapRemapMax = new Vector4(1f, 1f, 1f, d.smoothnessScale * 0.85f);
                layer.smoothness = 0f;
                layer.metallic = 0f;
                EditorUtility.SetDirty(layer);
                result[i] = layer;
            }
            return result;
        }

        /// <summary>Single-pass 8-layer terrain material (CinderPass/Terrain); layer textures are bound at runtime by TerrainTextureArrays.</summary>
        public static Material TerrainMaterial()
        {
            var m = AssetUtil.Material(MatPath("Environment", "M_Terrain"), AssetUtil.FindShader("CinderPass/Terrain"));
            m.shaderKeywords = new string[0];
            m.SetFloat("_HeightTransition", 0.4f);
            m.SetFloat("_MacroStrength", 0.18f);
            m.SetFloat("_MacroScale", 150f);
            m.SetFloat("_FarBlend", 0.55f);
            m.SetFloat("_FarStart", 18f);
            m.SetFloat("_FarRange", 60f);
            m.SetFloat("_TriplanarSharpness", 0.3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static float[] TerrainHeightBiases()
        {
            var result = new float[TerrainLayerDefs.Length];
            for (int i = 0; i < result.Length; i++) result[i] = TerrainLayerDefs[i].heightBias;
            return result;
        }

        // ------------------------------------------------------------------ Poly Haven models

        public static Material ForModel(string id, Color tint, float smoothnessBias = 1f, string suffix = "")
        {
            string dir = $"{CinderPaths.Models}/{id}/textures";
            Texture2D diff = null, nrm = null;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (f.EndsWith(".meta")) continue;
                string n = Path.GetFileName(f);
                string p = f.Replace('\\', '/');
                if (n.Contains("_diff_")) diff = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                else if (n.Contains("_nor_gl_")) nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.ModelMaskPath(id)) ?? TextureBaker.BakeModelMask(id, dir);
            return Lit("Models", $"M_{id}{suffix}", new LitSpec
            {
                color = tint, albedo = diff, normal = nrm, mask = mask, smoothness = smoothnessBias, occlusion = 1f
            });
        }

        // ------------------------------------------------------------------ foliage

        public static Material Foliage(string name, Texture albedo, Texture normal, Color tint, float cutoff, float windWeight, bool upNormal, Color translucency, float smoothness = 0.15f)
        {
            var m = AssetUtil.Material(MatPath("Foliage", name), AssetUtil.FindShader("CinderPass/Foliage"));
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", normal);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_WindWeight", windWeight);
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_Translucency", translucency);
            m.SetFloat("_UpNormal", upNormal ? 1f : 0f);
            if (upNormal) m.EnableKeyword("_FOLIAGE_UPNORMAL"); else m.DisableKeyword("_FOLIAGE_UPNORMAL");
            m.SetFloat("_AlphaClip", 1f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.enableInstancing = true;
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ particles

        public enum Blend { Alpha, Additive, Premultiplied }

        public static Material Particle(string name, Texture tex, Color tint, Blend blend, float emission, float lighting, float softDepth = 1.5f)
        {
            var m = AssetUtil.Material(MatPath("VFX", name), AssetUtil.FindShader("CinderPass/ParticleSoft"));
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", tint);
            m.SetFloat("_Emission", emission);
            m.SetFloat("_Lighting", lighting);
            m.SetFloat("_SoftDepth", softDepth);
            switch (blend)
            {
                case Blend.Additive:
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.One);
                    break;
                case Blend.Premultiplied:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    break;
                default:
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    break;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ lava

        public static Material Lava(string name, bool channel, float coverage, float activity, float flowSpeed, float crustTiling, float noiseTiling)
        {
            var m = AssetUtil.Material(MatPath("Volcanic", name), AssetUtil.FindShader("CinderPass/Lava"));
            string rock = $"{CinderPaths.GroundTextures}/dark_rock";
            m.SetTexture("_CrustAlbedo", AssetDatabase.LoadAssetAtPath<Texture2D>($"{rock}/dark_rock_diff.jpg"));
            m.SetTexture("_CrustNormal", AssetDatabase.LoadAssetAtPath<Texture2D>($"{rock}/dark_rock_nor.jpg"));
            m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.LavaNoisePath));
            m.SetFloat("_UVFlow", channel ? 1f : 0f);
            if (channel) m.EnableKeyword("_LAVA_UV_FLOW"); else m.DisableKeyword("_LAVA_UV_FLOW");
            m.SetFloat("_CrustCoverage", coverage);
            m.SetFloat("_Activity", activity);
            m.SetFloat("_FlowSpeed", flowSpeed);
            m.SetFloat("_CrustTiling", crustTiling);
            m.SetFloat("_NoiseTiling", noiseTiling);
            m.SetColor("_MoltenColor", new Color(2.1f, 0.4f, 0.04f));
            m.SetColor("_CoreColor", new Color(3.8f, 1.25f, 0.18f));
            m.SetColor("_CrackColor", new Color(2.2f, 0.34f, 0.03f));
            m.SetColor("_CrustColor", new Color(0.15f, 0.13f, 0.125f));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ sky

        public static Material Sky(float rotation, float exposure)
        {
            var m = AssetUtil.Material(MatPath("Environment", "M_Sky"), AssetUtil.FindShader("Skybox/Cubemap"));
            m.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(CinderPaths.SkyHdri));
            m.SetFloat("_Rotation", rotation);
            m.SetFloat("_Exposure", exposure);
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
