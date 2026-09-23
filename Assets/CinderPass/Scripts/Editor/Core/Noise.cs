using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>Deterministic gradient / cellular noise used by all generators (optionally tileable).</summary>
    public sealed class Noise
    {
        readonly int[] perm = new int[512];
        static readonly Vector2[] Grad =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, 0.7071f), new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, -0.7071f)
        };

        public Noise(int seed)
        {
            var rng = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        int Hash(int x, int y) => perm[(perm[x & 255] + y) & 511];

        /// <summary>Gradient noise in [-1, 1]. If period &gt; 0 the noise tiles with that integer period.</summary>
        public float Perlin(float x, float y, int period = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            int x0 = xi, y0 = yi, x1 = xi + 1, y1 = yi + 1;
            if (period > 0)
            {
                x0 = ((x0 % period) + period) % period; x1 = ((x1 % period) + period) % period;
                y0 = ((y0 % period) + period) % period; y1 = ((y1 % period) + period) % period;
            }
            float n00 = Vector2.Dot(Grad[Hash(x0, y0) & 7], new Vector2(xf, yf));
            float n10 = Vector2.Dot(Grad[Hash(x1, y0) & 7], new Vector2(xf - 1, yf));
            float n01 = Vector2.Dot(Grad[Hash(x0, y1) & 7], new Vector2(xf, yf - 1));
            float n11 = Vector2.Dot(Grad[Hash(x1, y1) & 7], new Vector2(xf - 1, yf - 1));
            float u = Fade(xf), v = Fade(yf);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.4142f;
        }

        /// <summary>Fractal Brownian motion in roughly [-1, 1].</summary>
        public float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f, int period = 0)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int p = period;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x, y, p) * amp;
                norm += amp;
                amp *= gain;
                x *= lacunarity; y *= lacunarity;
                if (p > 0) p = Mathf.RoundToInt(p * lacunarity);
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in [0, 1] - sharp crests, good for mountain ridges and gullies.</summary>
        public float Ridged(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f, int period = 0)
        {
            float sum = 0f, amp = 1f, norm = 0f, weight = 1f;
            int p = period;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Perlin(x, y, p));
                n *= n;
                n *= weight;
                weight = Mathf.Clamp01(n * 2f);
                sum += n * amp;
                norm += amp;
                amp *= gain;
                x *= lacunarity; y *= lacunarity;
                if (p > 0) p = Mathf.RoundToInt(p * lacunarity);
            }
            return sum / norm;
        }

        /// <summary>Cellular noise: F1 and F2 distances (in cell units). Tileable when period &gt; 0.</summary>
        public void Worley(float x, float y, int period, out float f1, out float f2)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            f1 = f2 = 10f;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx, cy = yi + dy;
                int hx = cx, hy = cy;
                if (period > 0) { hx = ((cx % period) + period) % period; hy = ((cy % period) + period) % period; }
                int h = Hash(hx, hy);
                float px = cx + (perm[h] / 255f) * 0.9f + 0.05f;
                float py = cy + (perm[(h + 71) & 511] / 255f) * 0.9f + 0.05f;
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }
        }

        /// <summary>Hash of integer coords to [0,1) - for deterministic per-cell randomness.</summary>
        public static float Hash01(int x, int y, int seed = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
