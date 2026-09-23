using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinderPass.Environment
{
    /// <summary>
    /// Feeds the single-pass terrain shader (CinderPass/Terrain). The terrain layers' textures are copied on
    /// the GPU into three texture arrays (albedo, normal, mask) - no extra assets and no CPU copies - and bound
    /// globally together with per-layer tiling/tint parameters. Each terrain also receives its second splat
    /// control map, so all eight layers are shaded, height-blended and lit once instead of in two passes.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TerrainTextureArrays : MonoBehaviour
    {
        [SerializeField] TerrainLayer[] layers = new TerrainLayer[0];
        [Tooltip("Per-layer bias added to the mask-map height for height-based blending (rock and stones sit proud of dust and grass).")]
        [SerializeField] float[] heightBias = new float[0];
        [SerializeField] Terrain[] terrains = new Terrain[0];
        [Tooltip("Tileable noise (R channel) for large-scale colour variation.")]
        [SerializeField] Texture2D macroNoise;

        const int MaxLayers = 8;
        static readonly int AlbedoId = Shader.PropertyToID("_CP_TerrainAlbedo");
        static readonly int NormalId = Shader.PropertyToID("_CP_TerrainNormal");
        static readonly int MaskId = Shader.PropertyToID("_CP_TerrainMask");
        static readonly int MacroId = Shader.PropertyToID("_CP_TerrainMacro");
        static readonly int LayerStId = Shader.PropertyToID("_CP_TerrainLayerST");
        static readonly int LayerTintId = Shader.PropertyToID("_CP_TerrainLayerTint");
        static readonly int Control1Id = Shader.PropertyToID("_Control1");

        Texture2DArray albedo, normal, mask;
        MaterialPropertyBlock block;
        readonly Vector4[] layerSt = new Vector4[MaxLayers];
        readonly Vector4[] layerTint = new Vector4[MaxLayers];

        public void Configure(TerrainLayer[] terrainLayers, float[] heightBiases, Terrain[] targets, Texture2D noise)
        {
            layers = terrainLayers;
            heightBias = heightBiases;
            terrains = targets;
            macroNoise = noise;
            if (isActiveAndEnabled) Apply();
        }

        void OnEnable()
        {
            Apply();
            RenderPipelineManager.beginContextRendering += OnBeginRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            Release();
        }

        /// <summary>Rebuilds the arrays and rebinds everything (call after editing layers or repainting).</summary>
        public void Apply()
        {
            Release();
            if (layers == null || layers.Length == 0) return;
            albedo = BuildArray("CP_TerrainAlbedo", l => l.diffuseTexture, false);
            normal = BuildArray("CP_TerrainNormal", l => l.normalMapTexture, true);
            mask = BuildArray("CP_TerrainMask", l => l.maskMapTexture, true);
            Shader.SetGlobalTexture(AlbedoId, albedo);
            Shader.SetGlobalTexture(NormalId, normal);
            Shader.SetGlobalTexture(MaskId, mask);
            Shader.SetGlobalTexture(MacroId, macroNoise != null ? macroNoise : Texture2D.grayTexture);

            for (int i = 0; i < MaxLayers; i++)
            {
                var l = i < layers.Length ? layers[i] : null;
                if (l == null) { layerSt[i] = new Vector4(0.1f, 1f, 1f, 0f); layerTint[i] = Vector4.one; continue; }
                float bias = heightBias != null && i < heightBias.Length ? heightBias[i] : 0f;
                layerSt[i] = new Vector4(1f / Mathf.Max(0.1f, l.tileSize.x), l.normalScale, l.maskMapRemapMax.w, bias);
                layerTint[i] = l.diffuseRemapMax;
            }
            Shader.SetGlobalVectorArray(LayerStId, layerSt);
            Shader.SetGlobalVectorArray(LayerTintId, layerTint);

            BindControlMaps();
        }

        // Reloading or saving a TerrainData recreates its alphamap textures and resets the terrain's splat property
        // block, so the second control map is re-bound before every render (a handful of cheap native calls).
        void OnBeginRendering(ScriptableRenderContext context, List<Camera> cameras) => BindControlMaps();

        void BindControlMaps()
        {
            block ??= new MaterialPropertyBlock();
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var td = t.terrainData;
                t.GetSplatMaterialPropertyBlock(block);
                block.SetTexture(Control1Id, td.alphamapTextureCount > 1 ? td.GetAlphamapTexture(1) : Texture2D.blackTexture);
                t.SetSplatMaterialPropertyBlock(block);
            }
        }

        Texture2DArray BuildArray(string label, System.Func<TerrainLayer, Texture2D> pick, bool linear)
        {
            Texture2D first = null;
            foreach (var l in layers) if (l != null && pick(l) != null) { first = pick(l); break; }
            if (first == null) return null;

            var array = new Texture2DArray(first.width, first.height, MaxLayers, first.format, first.mipmapCount, linear)
            {
                name = label,
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            array.Apply(false, true); // allocate on the GPU only; slices are filled by GPU copies below
            for (int i = 0; i < layers.Length && i < MaxLayers; i++)
            {
                var src = layers[i] != null ? pick(layers[i]) : null;
                if (src == null) continue;
                if (src.width != first.width || src.height != first.height || src.format != first.format || src.mipmapCount != first.mipmapCount)
                {
                    Debug.LogWarning($"[TerrainTextureArrays] {src.name} does not match {first.name} (size/format/mips); layer {i} skipped in {label}.", this);
                    continue;
                }
                Graphics.CopyTexture(src, 0, array, i);
            }
            return array;
        }

        void Release()
        {
            DestroySafe(albedo);
            DestroySafe(normal);
            DestroySafe(mask);
            albedo = normal = mask = null;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
