using System;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>Physical/visual properties of a ground surface.</summary>
    [Serializable]
    public sealed class SurfaceInfo
    {
        public string name = "Default";
        [Tooltip("Terrain layer this surface is painted with (null for the fallback entry).")]
        public TerrainLayer terrainLayer;
        [Tooltip("Tyre grip multiplier (1 = dry packed dirt).")]
        [Range(0.2f, 1.5f)] public float grip = 1f;
        [Tooltip("Extra rolling resistance torque per wheel (Nm).")]
        [Min(0f)] public float rollingResistance = 0f;
        public Color dustColor = new Color(0.55f, 0.48f, 0.4f, 0.6f);
        [Tooltip("Relative amount of dust/debris thrown by the tyres.")]
        [Range(0f, 2f)] public float dustAmount = 1f;
        [Tooltip("Volume of the tyre-on-surface audio loop.")]
        [Range(0f, 1f)] public float rollNoise = 0.6f;
    }

    /// <summary>Data asset mapping terrain layers to driving surfaces.</summary>
    [CreateAssetMenu(menuName = "Cinder Pass/Surface Library", fileName = "SurfaceLibrary")]
    public sealed class SurfaceLibrary : ScriptableObject
    {
        [SerializeField] SurfaceInfo fallback = new SurfaceInfo { name = "Rock", grip = 1.05f, dustAmount = 0.2f };
        [SerializeField] SurfaceInfo[] surfaces = Array.Empty<SurfaceInfo>();

        public SurfaceInfo Fallback => fallback;
        public SurfaceInfo[] Surfaces => surfaces;

        public SurfaceInfo Find(TerrainLayer layer)
        {
            if (layer != null)
                foreach (var s in surfaces)
                    if (s.terrainLayer == layer) return s;
            return fallback;
        }

        public void SetSurfaces(SurfaceInfo fallbackSurface, SurfaceInfo[] entries)
        {
            fallback = fallbackSurface;
            surfaces = entries;
        }
    }
}
