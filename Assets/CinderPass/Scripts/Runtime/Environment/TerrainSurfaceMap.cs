using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Answers "what surface is at this point?" for a terrain. The dominant splat layer per alphamap texel
    /// is precomputed once into a byte grid, so wheel queries are O(1) with no allocations.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain))]
    public sealed class TerrainSurfaceMap : MonoBehaviour
    {
        [SerializeField] SurfaceLibrary library;

        Terrain terrain;
        byte[] dominant;
        SurfaceInfo[] layerSurfaces;
        int resolution;
        Vector3 origin;
        Vector3 size;

        public SurfaceLibrary Library => library;
        public Terrain Terrain => terrain;

        void Awake() => Build();

        public void Build()
        {
            terrain = GetComponent<Terrain>();
            var data = terrain.terrainData;
            origin = terrain.GetPosition();
            size = data.size;
            resolution = data.alphamapResolution;
            var layers = data.terrainLayers;
            layerSurfaces = new SurfaceInfo[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                layerSurfaces[i] = library != null ? library.Find(layers[i]) : null;

            int layerCount = data.alphamapLayers;
            dominant = new byte[resolution * resolution];
            if (layerCount == 0) return;
            float[,,] maps = data.GetAlphamaps(0, 0, resolution, resolution);
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int best = 0;
                float bestW = -1f;
                for (int l = 0; l < layerCount; l++)
                {
                    float w = maps[y, x, l];
                    if (w > bestW) { bestW = w; best = l; }
                }
                dominant[y * resolution + x] = (byte)best;
            }
        }

        public SurfaceInfo Sample(Vector3 worldPosition)
        {
            if (dominant == null || layerSurfaces == null || layerSurfaces.Length == 0)
                return library != null ? library.Fallback : null;
            float u = (worldPosition.x - origin.x) / size.x;
            float v = (worldPosition.z - origin.z) / size.z;
            int x = Mathf.Clamp((int)(u * resolution), 0, resolution - 1);
            int y = Mathf.Clamp((int)(v * resolution), 0, resolution - 1);
            var s = layerSurfaces[dominant[y * resolution + x]];
            return s ?? library?.Fallback;
        }
    }
}
