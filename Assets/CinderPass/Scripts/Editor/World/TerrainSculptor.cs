using System.Collections.Generic;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>Per-cell data produced by sculpting and consumed by painting/scattering.</summary>
    public sealed class TerrainGrid
    {
        public int res;
        public float cell;
        public float[] height;     // metres, row-major [z * res + x]
        public float[] roadDist;   // horizontal distance to the route centre line (large if far)
        public float[] roadHeight; // road surface height of the nearest route sample
        public float[] canyon;     // canyon weight of the nearest route sample
        public float[] lavaDist;   // distance to the nearest lava edge (negative inside lava)

        public int Index(int x, int z) => z * res + x;

        public float SampleBilinear(float[] arr, float wx, float wz)
        {
            float fx = Mathf.Clamp(wx / cell, 0, res - 1.001f), fz = Mathf.Clamp(wz / cell, 0, res - 1.001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float a = arr[Index(x0, z0)], b = arr[Index(x0 + 1, z0)], c = arr[Index(x0, z0 + 1)], d = arr[Index(x0 + 1, z0 + 1)];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public float Height(float wx, float wz) => SampleBilinear(height, wx, wz);
        public float RoadDistance(float wx, float wz) => SampleBilinear(roadDist, wx, wz);
        public float LavaDistance(float wx, float wz) => SampleBilinear(lavaDist, wx, wz);

        public float SlopeDegrees(int x, int z)
        {
            int xl = Mathf.Max(0, x - 1), xr = Mathf.Min(res - 1, x + 1), zl = Mathf.Max(0, z - 1), zr = Mathf.Min(res - 1, z + 1);
            float dx = (height[Index(xr, z)] - height[Index(xl, z)]) / ((xr - xl) * cell);
            float dz = (height[Index(x, zr)] - height[Index(x, zl)]) / ((zr - zl) * cell);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }

        public Vector3 Normal(float wx, float wz)
        {
            float e = cell;
            float hl = Height(wx - e, wz), hr = Height(wx + e, wz), hd = Height(wx, wz - e), hu = Height(wx, wz + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }
    }

    /// <summary>Lava surface geometry decided during sculpting (so meshes match the carved terrain exactly).</summary>
    public sealed class LavaLayout
    {
        public sealed class Pool
        {
            public string label;
            public Vector2 centre;
            public float radius;
            public float level;
            public float activity;
            public float[] outline; // radius multiplier per angular step
        }

        public readonly List<Pool> pools = new List<Pool>();
        public Vector3[] river;     // x, lava surface height, z (upstream first)
        public float riverHalfWidth;
        public Pool crater;

        /// <summary>Metres over which the river widens from its source vent to full width.</summary>
        public const float SourceTaperLength = 16f;

        /// <summary>Channel half-width at river sample <paramref name="i"/> (samples are ~1 m apart): the flow
        /// emerges narrow from its vent and widens downstream instead of starting as a square-cut ribbon.</summary>
        public float RiverHalfWidthAt(int i) => riverHalfWidth * Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 1f, i / SourceTaperLength));

        public static float OutlineAt(Pool p, float angle)
        {
            int n = p.outline.Length;
            float f = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * n;
            int i0 = (int)f % n, i1 = (i0 + 1) % n;
            return Mathf.Lerp(p.outline[i0], p.outline[i1], f - Mathf.Floor(f));
        }
    }

    /// <summary>
    /// Builds the playable heightfield: macro landforms, route-aware conformance, hydraulic + thermal
    /// erosion, exact road carving (crown, ditches, slope-aware embankments, steep canyon walls),
    /// then lava pools and the lava river channel.
    /// </summary>
    public static class TerrainSculptor
    {
        public static TerrainGrid Sculpt(WorldField field, RouteSamples route, out LavaLayout lava)
        {
            var d = field.D;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            void T(string stage) => Debug.Log($"[TerrainSculptor] {stage}: {sw.Elapsed.TotalSeconds:0.0}s");
            int res = d.heightmapResolution;
            float cell = d.size / (res - 1);
            var g = new TerrainGrid
            {
                res = res, cell = cell,
                height = new float[res * res], roadDist = new float[res * res], roadHeight = new float[res * res],
                canyon = new float[res * res], lavaDist = new float[res * res]
            };
            for (int i = 0; i < g.lavaDist.Length; i++) g.lavaDist[i] = 1e4f;

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                g.height[g.Index(x, z)] = field.MacroHeight(x * cell, z * cell);

            T("macro heights");
            StampRoute(g, route, 75f);
            T("route stamp");

            // Broad conformance: the land around the route already sits near road level, so the final carve
            // only needs gentle embankments - except in the canyon, where the ridge must stay high.
            for (int i = 0; i < g.height.Length; i++)
            {
                float dist = g.roadDist[i];
                if (dist > 75f) continue;
                float w = (1f - Mathf.SmoothStep(0f, 1f, dist / 75f)) * 0.8f * (1f - g.canyon[i]);
                g.height[i] = Mathf.Lerp(g.height[i], g.roadHeight[i], w);
            }

            var before = (float[])g.height.Clone();
            HydraulicErosion(g, d.seed, 380000);
            T("hydraulic erosion");
            ThermalErosion(g, 5, 36f);
            T("thermal erosion");
            // Fade erosion out near the tile border so the backdrop tiles (macro only) join seamlessly.
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float e = Mathf.Min(Mathf.Min(x, z), Mathf.Min(res - 1 - x, res - 1 - z)) * cell;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 60f, e));
                int i = g.Index(x, z);
                g.height[i] = Mathf.Lerp(before[i], g.height[i], k);
            }

            CarveRoad(g, d.roadHalfWidth);
            lava = CarveLava(g, field);
            T("road + lava carve");

            for (int i = 0; i < g.height.Length; i++) g.height[i] = Mathf.Clamp(g.height[i], 0.5f, d.heightScale - 0.5f);
            return g;
        }

        /// <summary>For every cell near the route: nearest distance, road height and canyon weight.</summary>
        static void StampRoute(TerrainGrid g, RouteSamples route, float radius)
        {
            for (int i = 0; i < g.roadDist.Length; i++) g.roadDist[i] = 1e4f;
            int rc = Mathf.CeilToInt(radius / g.cell);
            var pts = route.points;
            for (int s = 0; s < pts.Length; s += 2)
            {
                Vector3 p = pts[s];
                int cx = Mathf.RoundToInt(p.x / g.cell), cz = Mathf.RoundToInt(p.z / g.cell);
                for (int z = Mathf.Max(0, cz - rc); z <= Mathf.Min(g.res - 1, cz + rc); z++)
                for (int x = Mathf.Max(0, cx - rc); x <= Mathf.Min(g.res - 1, cx + rc); x++)
                {
                    float dx = x * g.cell - p.x, dz = z * g.cell - p.z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    int i = g.Index(x, z);
                    if (dist < g.roadDist[i])
                    {
                        g.roadDist[i] = dist;
                        g.roadHeight[i] = p.y;
                        g.canyon[i] = route.canyon[s];
                    }
                }
            }
        }

        static void CarveRoad(TerrainGrid g, float halfWidth)
        {
            for (int i = 0; i < g.height.Length; i++)
            {
                float dist = g.roadDist[i];
                if (dist > 75f) continue;
                float natural = g.height[i];
                float road = g.roadHeight[i];
                float canyon = g.canyon[i];
                float delta = Mathf.Abs(natural - road);
                float target;
                if (dist <= halfWidth)
                {
                    target = road - 0.035f * (dist / halfWidth) * (dist / halfWidth);
                }
                else
                {
                    float falloffOpen = Mathf.Max(10f, delta * 1.7f);
                    float falloffCanyon = 3.5f + delta * 0.3f;
                    float falloff = Mathf.Lerp(falloffOpen, falloffCanyon, canyon);
                    float t = Mathf.Clamp01((dist - halfWidth) / falloff);
                    float smooth = t * t * (3f - 2f * t);
                    float ditch = dist < halfWidth + 2.6f ? -0.22f * Mathf.Sin((dist - halfWidth) / 2.6f * Mathf.PI) * (1f - canyon) : 0f;
                    target = Mathf.Lerp(road + ditch, natural, smooth);
                }
                g.height[i] = target;
            }
        }

        // ---------------------------------------------------------------- lava

        static LavaLayout CarveLava(TerrainGrid g, WorldField field)
        {
            var d = field.D;
            var layout = new LavaLayout { riverHalfWidth = d.lavaRiverHalfWidth };
            var rng = new System.Random(d.seed + 7);
            var noise = new Noise(d.seed + 55);

            foreach (var p in d.lavaPools)
            {
                var pool = new LavaLayout.Pool { label = p.label, centre = p.centre, radius = p.radius, activity = p.activity, outline = new float[48] };
                float phase = (float)rng.NextDouble() * 10f;
                for (int k = 0; k < pool.outline.Length; k++)
                {
                    float a = k / (float)pool.outline.Length * Mathf.PI * 2f;
                    pool.outline[k] = 1f + 0.11f * noise.Perlin(Mathf.Cos(a) * 1.6f + phase, Mathf.Sin(a) * 1.6f + phase);
                }
                // Level: sunk below the lowest ground around the rim so the lava never floats above terrain.
                float ground = float.MaxValue;
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.PI * 2f;
                    ground = Mathf.Min(ground, g.Height(p.centre.x + Mathf.Cos(a) * p.radius, p.centre.y + Mathf.Sin(a) * p.radius));
                }
                pool.level = ground - p.sunken;
                layout.pools.Add(pool);
            }

            // River: surface follows the ground downstream, never rising, and meets the Cauldron's level.
            var riverPts = Densify(d.lavaRiver, 1f);
            var surf = new float[riverPts.Count];
            for (int i = 0; i < surf.Length; i++) surf[i] = g.Height(riverPts[i].x, riverPts[i].y) - 1.3f;
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < surf.Length - 1; i++) surf[i] = (surf[i - 1] + surf[i] + surf[i + 1]) / 3f;
            for (int i = 1; i < surf.Length; i++) surf[i] = Mathf.Min(surf[i], surf[i - 1] - 0.02f);
            var cauldron = layout.pools.Count > 0 ? layout.pools[0] : null;
            if (cauldron != null)
            {
                int blend = Mathf.Min(14, surf.Length - 1);
                for (int i = 0; i < blend; i++)
                {
                    int idx = surf.Length - 1 - i;
                    float t = 1f - i / (float)blend;
                    surf[idx] = Mathf.Lerp(surf[idx], cauldron.level + 0.05f, t * t);
                }
            }
            layout.river = new Vector3[riverPts.Count];
            for (int i = 0; i < riverPts.Count; i++) layout.river[i] = new Vector3(riverPts[i].x, surf[i], riverPts[i].y);

            CarveRiver(g, layout);
            foreach (var pool in layout.pools) CarvePool(g, pool);

            // Summit crater lava (visual landmark; the crater floor is part of the macro field).
            float craterFloor = field.MacroHeight(d.volcano.x, d.volcano.y);
            layout.crater = new LavaLayout.Pool
            {
                label = "Summit crater", centre = d.volcano, radius = d.craterRadius * 0.7f, level = craterFloor + 3f, activity = 1.6f,
                outline = new float[48]
            };
            for (int k = 0; k < 48; k++) layout.crater.outline[k] = 1f + 0.08f * noise.Perlin(k * 0.3f, 4.2f);
            return layout;
        }

        static List<Vector2> Densify(Vector2[] path, float step)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                // Catmull-Rom through neighbours for a natural meander.
                Vector2 p0 = path[Mathf.Max(0, i - 1)], p3 = path[Mathf.Min(path.Length - 1, i + 2)];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n;
                    result.Add(0.5f * (2f * a + (-p0 + b) * t + (2f * p0 - 5f * a + 4f * b - p3) * t * t + (-p0 + 3f * a - 3f * b + p3) * t * t * t));
                }
            }
            result.Add(path[path.Length - 1]);
            return result;
        }

        static void CarvePool(TerrainGrid g, LavaLayout.Pool p)
        {
            float rim = 0.7f + p.radius * 0.05f;
            float depth = 1.2f + 0.6f * p.activity;
            float reach = p.radius * 1.25f + 14f;
            int cx = Mathf.RoundToInt(p.centre.x / g.cell), cz = Mathf.RoundToInt(p.centre.y / g.cell);
            int rc = Mathf.CeilToInt(reach / g.cell);
            for (int z = Mathf.Max(0, cz - rc); z <= Mathf.Min(g.res - 1, cz + rc); z++)
            for (int x = Mathf.Max(0, cx - rc); x <= Mathf.Min(g.res - 1, cx + rc); x++)
            {
                float dx = x * g.cell - p.centre.x, dz = z * g.cell - p.centre.y;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float ro = p.radius * LavaLayout.OutlineAt(p, Mathf.Atan2(dz, dx));
                int i = g.Index(x, z);
                float natural = g.height[i];
                float target;
                if (r < ro)
                {
                    float q = r / ro;
                    target = p.level - 0.3f - depth * (1f - q * q);
                }
                else if (r < ro + 3f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, (r - ro) / 3f);
                    target = Mathf.Lerp(p.level - 0.3f, Mathf.Max(p.level + rim, natural * 0.3f + (p.level + rim) * 0.7f), t);
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, (r - ro - 3f) / 11f);
                    target = Mathf.Lerp(Mathf.Max(p.level + rim, natural * 0.3f + (p.level + rim) * 0.7f), natural, t);
                }
                g.height[i] = target;
                g.lavaDist[i] = Mathf.Min(g.lavaDist[i], r - ro);
            }
        }

        static void CarveRiver(TerrainGrid g, LavaLayout lava)
        {
            float reach = lava.riverHalfWidth + 13f;
            int rc = Mathf.CeilToInt(reach / g.cell);
            // Per cell, the river sample whose channel edge is closest (the channel narrows toward the source).
            var best = new Dictionary<int, (float edge, float dist, float surf, float hw)>();
            for (int k = 0; k < lava.river.Length; k++)
            {
                var p = lava.river[k];
                float phw = lava.RiverHalfWidthAt(k);
                int cx = Mathf.RoundToInt(p.x / g.cell), cz = Mathf.RoundToInt(p.z / g.cell);
                for (int z = Mathf.Max(0, cz - rc); z <= Mathf.Min(g.res - 1, cz + rc); z++)
                for (int x = Mathf.Max(0, cx - rc); x <= Mathf.Min(g.res - 1, cx + rc); x++)
                {
                    float dx = x * g.cell - p.x, dz = z * g.cell - p.z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > reach) continue;
                    int i = g.Index(x, z);
                    float edge = dist - phw;
                    if (!best.TryGetValue(i, out var b) || edge < b.edge) best[i] = (edge, dist, p.y, phw);
                }
            }
            foreach (var kv in best)
            {
                int i = kv.Key;
                float dist = kv.Value.dist, s = kv.Value.surf, hw = kv.Value.hw;
                float natural = g.height[i];
                float levee = s + 0.9f;
                float target;
                if (dist < hw) target = s - 0.3f - 1.1f * (1f - (dist / hw) * (dist / hw));
                else if (dist < hw + 2.5f) target = Mathf.Lerp(s - 0.3f, Mathf.Max(levee, natural * 0.4f + levee * 0.6f), Mathf.SmoothStep(0f, 1f, (dist - hw) / 2.5f));
                else target = Mathf.Lerp(Mathf.Max(levee, natural * 0.4f + levee * 0.6f), natural, Mathf.SmoothStep(0f, 1f, (dist - hw - 2.5f) / 10.5f));
                g.height[i] = target;
                g.lavaDist[i] = Mathf.Min(g.lavaDist[i], dist - hw);
            }
        }

        // ---------------------------------------------------------------- erosion

        /// <summary>Droplet-based hydraulic erosion (carves gullies, deposits fans at slope feet).</summary>
        static void HydraulicErosion(TerrainGrid g, int seed, int droplets)
        {
            const float scale = 40f; // work in scaled height units so parameters behave like a 0..1 map
            int res = g.res;
            var map = new float[g.height.Length];
            for (int i = 0; i < map.Length; i++) map[i] = g.height[i] / scale;

            const float inertia = 0.06f, capacityFactor = 4f, minCapacity = 0.01f, erodeSpeed = 0.3f, depositSpeed = 0.3f,
                evaporate = 0.015f, gravity = 4f, maxErodePerStep = 0.012f;
            const int maxSteps = 34, radius = 2;
            var brushOffsets = new List<(int dx, int dz, float w)>();
            float wsum = 0f;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > radius) continue;
                float w = 1f - dist / radius;
                brushOffsets.Add((dx, dz, w));
                wsum += w;
            }
            var brush = brushOffsets.ToArray();
            var rng = new System.Random(seed);
            for (int n = 0; n < droplets; n++)
            {
                float px = (float)rng.NextDouble() * (res - 3) + 1, pz = (float)rng.NextDouble() * (res - 3) + 1;
                float dirX = 0f, dirZ = 0f, speed = 1f, water = 1f, sediment = 0f;
                for (int step = 0; step < maxSteps; step++)
                {
                    int nx = (int)px, nz = (int)pz;
                    float ox = px - nx, oz = pz - nz;
                    HeightGradient(map, res, px, pz, out float h, out float gx, out float gz);
                    dirX = dirX * inertia - gx * (1f - inertia);
                    dirZ = dirZ * inertia - gz * (1f - inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len < 1e-6f) break;
                    dirX /= len; dirZ /= len;
                    px += dirX; pz += dirZ;
                    if (px < 1 || pz < 1 || px >= res - 2 || pz >= res - 2) break;
                    HeightGradient(map, res, px, pz, out float nh, out _, out _);
                    float dh = nh - h;
                    float capacity = Mathf.Max(-dh * speed * water * capacityFactor, minCapacity);
                    if (sediment > capacity || dh > 0f)
                    {
                        float deposit = dh > 0f ? Mathf.Min(dh, sediment) : (sediment - capacity) * depositSpeed;
                        sediment -= deposit;
                        int i0 = nz * res + nx;
                        map[i0] += deposit * (1 - ox) * (1 - oz);
                        map[i0 + 1] += deposit * ox * (1 - oz);
                        map[i0 + res] += deposit * (1 - ox) * oz;
                        map[i0 + res + 1] += deposit * ox * oz;
                    }
                    else
                    {
                        float erode = Mathf.Min(Mathf.Min((capacity - sediment) * erodeSpeed, -dh), maxErodePerStep);
                        foreach (var (bx, bz, w) in brush)
                        {
                            int ex = nx + bx, ez = nz + bz;
                            if (ex < 0 || ez < 0 || ex >= res || ez >= res) continue;
                            int ei = ez * res + ex;
                            float amount = erode * w / wsum;
                            float taken = Mathf.Min(map[ei], amount);
                            map[ei] -= taken;
                            sediment += taken;
                        }
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + dh * gravity));
                    water *= 1f - evaporate;
                }
            }
            for (int i = 0; i < map.Length; i++) g.height[i] = map[i] * scale;
        }

        static void HeightGradient(float[] map, int res, float px, float pz, out float h, out float gx, out float gz)
        {
            int x = (int)px, z = (int)pz;
            float u = px - x, v = pz - z;
            int i = z * res + x;
            float h00 = map[i], h10 = map[i + 1], h01 = map[i + res], h11 = map[i + res + 1];
            gx = (h10 - h00) * (1 - v) + (h11 - h01) * v;
            gz = (h01 - h00) * (1 - u) + (h11 - h10) * u;
            h = h00 * (1 - u) * (1 - v) + h10 * u * (1 - v) + h01 * (1 - u) * v + h11 * u * v;
        }

        /// <summary>Slumps slopes steeper than the talus angle (natural scree slopes).</summary>
        static void ThermalErosion(TerrainGrid g, int iterations, float talusDegrees)
        {
            int res = g.res;
            float talus = Mathf.Tan(talusDegrees * Mathf.Deg2Rad) * g.cell;
            var h = g.height;
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 1; z < res - 1; z++)
                for (int x = 1; x < res - 1; x++)
                {
                    int i = z * res + x;
                    float hi = h[i];
                    int lowest = -1;
                    float maxDiff = talus;
                    for (int n = 0; n < 4; n++)
                    {
                        int j = n == 0 ? i - 1 : n == 1 ? i + 1 : n == 2 ? i - res : i + res;
                        float diff = hi - h[j];
                        if (diff > maxDiff) { maxDiff = diff; lowest = j; }
                    }
                    if (lowest < 0) continue;
                    float move = (maxDiff - talus) * 0.25f;
                    h[i] -= move;
                    h[lowest] += move;
                }
            }
        }
    }
}
