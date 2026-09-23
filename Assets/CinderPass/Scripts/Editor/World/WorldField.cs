using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Continuous description of the landscape: macro height (valid anywhere, so the surrounding
    /// backdrop tiles continue seamlessly) and the biome masks used for painting and scattering.
    /// </summary>
    public sealed class WorldField
    {
        public readonly WorldDesign D;
        readonly Noise n;
        readonly Noise n2;

        public WorldField(WorldDesign design)
        {
            D = design;
            n = new Noise(design.seed);
            n2 = new Noise(design.seed + 101);
        }

        // ---------------------------------------------------------------- height

        public float MacroHeight(float x, float z)
        {
            float h = D.baseHeight;
            h += n.Fbm(x / 380f, z / 380f, 5) * 24f;
            float basinK = BasinMask(x, z);
            h += n.Ridged(x / 170f + 11f, z / 170f + 7f, 4) * 16f * (1f - basinK);

            // Great Ridge (crest height interpolated along the polyline, crest broken up by ridged noise).
            RidgeDistance(x, z, out float d, out float crest);
            float ridgeShape = Mathf.Exp(-(d / D.ridgeWidth) * (d / D.ridgeWidth));
            float crestNoise = 0.72f + 0.55f * n2.Ridged(x / 95f, z / 95f, 4);
            h += crest * ridgeShape * crestNoise;

            // Rim mountains enclose the playable area and continue into the backdrop tiles.
            float e = EdgeDistance(x, z);
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(95f, -140f, e));
            float rimShape = n2.Fbm(x / 420f + 5f, z / 420f - 3f, 4) * 0.5f + 0.5f;
            h += rim * (55f + rimShape * 70f + n2.Ridged(x / 300f, z / 300f, 4) * 35f);

            // Volcanic basin.
            h = Mathf.Lerp(h, D.basinFloor + n.Fbm(x / 90f, z / 90f, 3) * 3f, basinK * 0.92f);

            // Stratovolcano with radial gullies and a summit crater.
            h += VolcanoHeight(x, z);

            // Basecamp meadow: a gentle, open clearing.
            float mb = Vector2.Distance(new Vector2(x, z), D.basecamp);
            float meadow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(D.basecampRadius * 0.6f, D.basecampRadius * 1.8f, mb));
            h = Mathf.Lerp(h, 24f + n.Fbm(x / 60f, z / 60f, 2) * 1.2f, meadow);
            return h;
        }

        public float VolcanoHeight(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - D.volcano;
            float r = p.magnitude;
            float R = D.volcanoRadius;
            if (r >= R) return 0f;
            float rc = D.craterRadius;
            // Concave stratovolcano profile, truncated at the crater rim.
            float Cone(float rr) => D.volcanoHeight * Mathf.Pow(1f - Mathf.Min(rr, R) / R, 1.9f);
            float rimHeight = Cone(rc);
            if (r >= rc)
            {
                float cone = Cone(r) * (1f - 0.04f * n.Fbm(x / 40f, z / 40f, 3));
                cone -= VolcanoGullyDepth * VolcanoGully(x, z);
                return cone + 4f * Mathf.Exp(-((r - rc) / 8f) * ((r - rc) / 8f)); // crater lip
            }
            // Bowl below the rim: steep inner walls and a flat floor.
            float q = r / rc;
            return rimHeight + 4f - D.craterDepth * (1f - Mathf.Pow(q, 3f));
        }

        const float VolcanoGullyDepth = 9f;

        /// <summary>
        /// 0..1 radial gully field on the volcano's flanks: meandering erosion channels (old lava and debris
        /// flows), deepest mid-flank and fading out at the crater rim and the foot. Carved into the height and
        /// used to paint basalt along the channels.
        /// </summary>
        public float VolcanoGully(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - D.volcano;
            float r = p.magnitude;
            float R = D.volcanoRadius, rc = D.craterRadius;
            if (r <= rc || r >= R) return 0f;
            Vector2 dir = p / r;
            // Noise sampled on a circle (continuous all the way round), stretched radially so channels run downhill.
            float warp = n.Fbm(x / 70f, z / 70f, 2) * 0.35f;
            float ridged = n2.Ridged(dir.x * 5f + warp + r / 140f, dir.y * 5f - warp + r / 160f, 3);
            float flank = Mathf.Sin(Mathf.PI * Mathf.Clamp01((r - rc) / (R - rc)));
            return Mathf.Clamp01(ridged * Mathf.Pow(flank, 0.7f));
        }

        public void RidgeDistance(float x, float z, out float distance, out float crest)
        {
            distance = float.MaxValue;
            crest = 0f;
            var pts = D.ridge;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x, z) - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(new Vector2(x, z), a + ab * t);
                if (d < distance)
                {
                    distance = d;
                    float ha = i < D.ridgeHeights.Length ? D.ridgeHeights[i] : 90f;
                    float hb = i + 1 < D.ridgeHeights.Length ? D.ridgeHeights[i + 1] : 90f;
                    crest = Mathf.Lerp(ha, hb, Mathf.SmoothStep(0f, 1f, t));
                }
            }
        }

        /// <summary>Signed distance to the playable boundary (positive inside the map).</summary>
        public float EdgeDistance(float x, float z) => Mathf.Min(Mathf.Min(x, z), Mathf.Min(D.size - x, D.size - z));

        public float BasinMask(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), D.basin) + n.Fbm(x / 150f, z / 150f, 3) * 35f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(D.basinRadius * 0.45f, D.basinRadius, d));
        }

        // ---------------------------------------------------------------- biome masks

        /// <summary>1 inside the volcanic region (basin + volcano flanks), 0 outside, noisy boundary.</summary>
        public float Volcanic(float x, float z)
        {
            float rv = Vector2.Distance(new Vector2(x, z), D.volcano) + n.Fbm(x / 120f, z / 120f, 3) * 45f;
            float v1 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(270f, 345f, rv));
            float rb = Vector2.Distance(new Vector2(x, z), D.basin) + n2.Fbm(x / 110f, z / 110f, 3) * 40f;
            float v2 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(165f, 235f, rb));
            return Mathf.Max(v1, v2);
        }

        /// <summary>Scorched transition band around the volcanic region.</summary>
        public float ScorchedRing(float volcanic) => Mathf.Clamp01(4f * volcanic * (1f - volcanic) * 1.15f);

        /// <summary>Forest tendency (before slope/road/altitude exclusions): dense in the south-west, thinning north-east.</summary>
        public float Forest(float x, float z)
        {
            float region = Mathf.Clamp01(1.45f - (x * 0.8f + z) / 880f);
            float clumps = Mathf.Clamp01((n.Fbm(x / 210f + 3f, z / 210f + 9f, 4) * 0.5f + 0.5f - 0.3f) * 3.4f);
            float detail = n2.Fbm(x / 45f, z / 45f, 2) * 0.5f + 0.5f;
            return Mathf.Clamp01(region * clumps * Mathf.Lerp(0.75f, 1.15f, detail));
        }

        /// <summary>Dry grassland in the south-east lowlands between the basin and the plains.</summary>
        public float Dryness(float x, float z)
        {
            float east = Mathf.Clamp01((x - 560f) / 260f);
            float south = Mathf.Clamp01((640f - z) / 260f);
            return Mathf.Clamp01(east * south * 1.4f + n.Fbm(x / 140f, z / 140f, 3) * 0.25f);
        }

        public float Meadow(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), D.basecamp);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(D.basecampRadius * 0.7f, D.basecampRadius * 1.3f, d));
        }

        public float Patch(float x, float z, float scale, int offset) => n2.Fbm(x / scale + offset * 7.3f, z / scale - offset * 3.1f, 3) * 0.5f + 0.5f;
    }
}
