using System.Collections.Generic;
using CinderPass.Environment;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Creates the playable terrain and its surrounding backdrop tiles, paints splat maps from the biome
    /// masks, scatters grass details and plants trees.
    /// </summary>
    public static class TerrainBuilder
    {
        public const string MainDataPath = CinderPaths.GenTerrain + "/TD_Main.asset";

        public sealed class VegetationSet
        {
            public GameObject[] firs;   // 3 variants
            public GameObject[] snags;  // 2 variants
            public GameObject grassLush, grassDry;
        }

        // ---------------------------------------------------------------- terrain objects

        public static Terrain CreateMain(TerrainGrid g, WorldDesign d, TerrainLayer[] layers, Material material, Transform parent)
        {
            var td = NewData(MainDataPath, g.res, d, layers, d.alphamapResolution);
            td.SetDetailResolution(d.detailResolution, 32);
            td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var h = new float[g.res, g.res];
            for (int z = 0; z < g.res; z++)
            for (int x = 0; x < g.res; x++)
                h[z, x] = g.height[g.Index(x, z)] / d.heightScale;
            td.SetHeights(0, 0, h);

            var terrain = CreateObject("Terrain_Main", td, material, parent, Vector3.zero);
            terrain.heightmapPixelError = 3f;
            // The Cinder Pass terrain shader has no base-map fallback: it shades every distance itself.
            terrain.basemapDistance = 20000f;
            terrain.detailObjectDistance = 95f;
            terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 1400f;
            terrain.treeBillboardDistance = 1400f;
            terrain.treeCrossFadeLength = 20f;
            terrain.treeMaximumFullLODCount = 60;
            return terrain;
        }

        static TerrainData NewData(string path, int res, WorldDesign d, TerrainLayer[] layers, int alphaRes)
        {
            AssetUtil.EnsureFolder(CinderPaths.GenTerrain);
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null) AssetDatabase.DeleteAsset(path);
            // Create the asset FIRST, then configure it: splat/detail textures created afterwards become
            // persistent sub-assets. (Configured before CreateAsset, they are regenerated - blank - on save.)
            var td = new TerrainData();
            AssetDatabase.CreateAsset(td, path);
            td.heightmapResolution = res;
            td.size = new Vector3(d.size, d.heightScale, d.size);
            td.alphamapResolution = alphaRes;
            td.baseMapResolution = Mathf.Min(1024, alphaRes);
            td.terrainLayers = layers;
            EditorUtility.SetDirty(td);
            return td;
        }

        static Terrain CreateObject(string name, TerrainData td, Material material, Transform parent, Vector3 position)
        {
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = material;
            t.drawInstanced = true;
            // Backdrop tiles use a lower heightmap resolution, which Unity cannot stitch as neighbours;
            // their shared borders match exactly at every backdrop vertex instead (see CreateBackdrop).
            t.allowAutoConnect = false;
            t.groupingID = name.GetHashCode() & 0xFFFF;
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            t.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            return t;
        }

        /// <summary>Eight low-resolution tiles continuing the landscape to the horizon (macro field only).</summary>
        public static List<Terrain> CreateBackdrop(WorldField f, TerrainLayer[] layers, Material material, Transform parent, VegetationSet veg)
        {
            var d = f.D;
            var result = new List<Terrain>();
            int res = d.outerHeightmapResolution;
            for (int tz = -1; tz <= 1; tz++)
            for (int tx = -1; tx <= 1; tx++)
            {
                if (tx == 0 && tz == 0) continue;
                string path = $"{CinderPaths.GenTerrain}/TD_Backdrop_{tx + 1}{tz + 1}.asset";
                var td = NewData(path, res, d, layers, 256);
                Vector3 origin = new Vector3(tx * d.size, 0f, tz * d.size);
                float cell = d.size / (res - 1);
                var h = new float[res, res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = Mathf.Clamp01(f.MacroHeight(origin.x + x * cell, origin.z + z * cell) / d.heightScale);
                td.SetHeights(0, 0, h);
                PaintBackdrop(td, f, origin);
                PlantBackdropTrees(td, f, origin, veg);
                var t = CreateObject($"Terrain_Backdrop_{tx + 1}{tz + 1}", td, material, parent, origin);
                t.heightmapPixelError = 8f;
                t.basemapDistance = 20000f;
                t.treeDistance = 1400f;
                t.treeBillboardDistance = 1400f;
                t.detailObjectDistance = 0f;
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Backdrop is not drivable: no collider needed (the rim mountains are walls in the main tile).
                var col = t.GetComponent<TerrainCollider>();
                if (col != null) Object.DestroyImmediate(col);
                result.Add(t);
            }
            return result;
        }

        // ---------------------------------------------------------------- painting

        public static void PaintMain(TerrainData td, TerrainGrid g, WorldField f)
        {
            var d = f.D;
            int ares = td.alphamapResolution;
            int layers = td.alphamapLayers;
            var maps = new float[ares, ares, layers];
            float w = d.roadHalfWidth;
            var wts = new float[layers];
            for (int az = 0; az < ares; az++)
            for (int ax = 0; ax < ares; ax++)
            {
                float x = (ax + 0.5f) * d.size / ares, z = (az + 0.5f) * d.size / ares;
                int gx = Mathf.Clamp(Mathf.RoundToInt(x / g.cell), 0, g.res - 1), gz = Mathf.Clamp(Mathf.RoundToInt(z / g.cell), 0, g.res - 1);
                float h = g.height[g.Index(gx, gz)];
                float slope = g.SlopeDegrees(gx, gz);
                float roadD = g.roadDist[g.Index(gx, gz)];
                float lavaD = g.lavaDist[g.Index(gx, gz)];
                float volc = f.Volcanic(x, z);
                float ring = f.ScorchedRing(volc);
                float forest = f.Forest(x, z);
                float dry = f.Dryness(x, z);
                float p1 = f.Patch(x, z, 26f, 1), p2 = f.Patch(x, z, 9f, 2), p3 = f.Patch(x, z, 60f, 3);

                System.Array.Clear(wts, 0, layers);
                float green = 1f - volc;
                wts[MaterialLibrary.LayerGrass] = green * (1f - forest * 0.85f) * (1f - dry * 0.55f) * (0.7f + 0.3f * p3);
                wts[MaterialLibrary.LayerForest] = green * forest * 1.1f;
                wts[MaterialLibrary.LayerDirt] = green * (dry * 0.75f * p1 + Mathf.Pow(p2, 4f) * 0.9f + Mathf.Pow(p1, 5f) * 0.6f);
                float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(33f, 47f, slope));
                float alpine = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 210f, h));
                wts[MaterialLibrary.LayerRock] = Mathf.Max(steep, alpine * 0.8f) * (1f - volc) * 2.2f;
                wts[MaterialLibrary.LayerScorched] = ring * (0.9f + 0.4f * p1);
                // Volcano flanks: dark basalt runs down the erosion channels, pale ash drapes the ridges between.
                float gully = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.8f, f.VolcanoGully(x, z)));
                float onCone = Vector2.Distance(new Vector2(x, z), d.volcano) < d.volcanoRadius ? 1f : 0f;
                wts[MaterialLibrary.LayerAsh] = volc * (1f - steep) * (0.65f + 0.35f * p3) * (1f - gully * 0.85f);
                float nearLava = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 9f, lavaD));
                float patches = Mathf.Pow(p2, 3f) * 0.7f * (1f - onCone * 0.8f);
                wts[MaterialLibrary.LayerBasalt] = volc * (steep * 2.2f + nearLava * 2.5f + patches + alpine + gully * 2.4f);

                // Trail (with a slightly irregular edge) and worn dirt shoulders.
                float edge = w - 0.4f + (p2 - 0.5f) * 1.4f;
                float trail = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 0.9f, edge + 0.6f, roadD));
                float shoulder = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(w - 0.5f, w + 3.5f + p1 * 2f, roadD))) * (1f - trail);
                wts[MaterialLibrary.LayerDirt] += shoulder * (volc > 0.5f ? 0f : 1.2f);
                wts[MaterialLibrary.LayerAsh] += shoulder * (volc > 0.5f ? 1f : 0f);

                float sum = 0f;
                for (int l = 0; l < layers; l++) sum += wts[l];
                if (sum < 1e-4f) { wts[MaterialLibrary.LayerGrass] = 1f; sum = 1f; }
                for (int l = 0; l < layers; l++)
                    maps[az, ax, l] = wts[l] / sum * (1f - trail) + (l == MaterialLibrary.LayerTrail ? trail : 0f);
            }
            td.SetAlphamaps(0, 0, maps);
        }

        static void PaintBackdrop(TerrainData td, WorldField f, Vector3 origin)
        {
            var d = f.D;
            int ares = td.alphamapResolution;
            int layers = td.alphamapLayers;
            var maps = new float[ares, ares, layers];
            for (int az = 0; az < ares; az++)
            for (int ax = 0; ax < ares; ax++)
            {
                float lx = (ax + 0.5f) / ares, lz = (az + 0.5f) / ares;
                float x = origin.x + lx * d.size, z = origin.z + lz * d.size;
                float h = td.GetInterpolatedHeight(lx, lz);
                float slope = td.GetSteepness(lx, lz);
                float volc = f.Volcanic(x, z);
                float forest = f.Forest(x, z);
                float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(33f, 47f, slope));
                float alpine = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(140f, 210f, h));
                float rock = Mathf.Max(steep, alpine);
                float grass = (1f - rock) * (1f - volc) * (1f - forest);
                float floor = (1f - rock) * (1f - volc) * forest;
                float ash = (1f - rock) * volc;
                float sum = rock + grass + floor + ash + 1e-4f;
                maps[az, ax, volc > 0.5f ? MaterialLibrary.LayerBasalt : MaterialLibrary.LayerRock] = rock / sum;
                maps[az, ax, MaterialLibrary.LayerGrass] = grass / sum;
                maps[az, ax, MaterialLibrary.LayerForest] = floor / sum;
                maps[az, ax, MaterialLibrary.LayerAsh] += ash / sum;
            }
            td.SetAlphamaps(0, 0, maps);
        }

        // ---------------------------------------------------------------- details

        public static void ScatterGrass(TerrainData td, TerrainGrid g, WorldField f, VegetationSet veg)
        {
            var d = f.D;
            td.detailPrototypes = new[]
            {
                GrassPrototype(veg.grassLush, 0.9f, 1.5f, 0.55f, 1.05f),
                GrassPrototype(veg.grassDry, 0.9f, 1.6f, 0.5f, 1.1f),
            };
            int dres = td.detailResolution;
            var lush = new int[dres, dres];
            var dryL = new int[dres, dres];
            var rng = new System.Random(d.seed + 3);
            for (int dz = 0; dz < dres; dz++)
            for (int dx = 0; dx < dres; dx++)
            {
                float x = (dx + 0.5f) * d.size / dres, z = (dz + 0.5f) * d.size / dres;
                int gx = Mathf.Clamp(Mathf.RoundToInt(x / g.cell), 0, g.res - 1), gz = Mathf.Clamp(Mathf.RoundToInt(z / g.cell), 0, g.res - 1);
                int gi = g.Index(gx, gz);
                if (g.roadDist[gi] < d.roadHalfWidth + 1.2f || g.lavaDist[gi] < 6f) continue;
                float slope = g.SlopeDegrees(gx, gz);
                if (slope > 30f) continue;
                float volc = f.Volcanic(x, z);
                if (volc > 0.45f) continue;
                float forest = f.Forest(x, z);
                float dry = f.Dryness(x, z);
                float clump = f.Patch(x, z, 14f, 4);
                float density = (1f - volc * 2f) * (1f - forest * 0.75f) * Mathf.SmoothStep(0f, 1f, (clump - 0.3f) * 2.2f) * (1f - slope / 30f);
                float shoulderBoost = g.roadDist[gi] < d.roadHalfWidth + 5f ? 0.55f : 1f;
                float count = density * 5f * shoulderBoost;
                int n = Mathf.FloorToInt(count + (float)rng.NextDouble());
                if (n <= 0) continue;
                float dryShare = Mathf.Clamp01(dry * 1.2f + f.ScorchedRing(volc) * 1.5f);
                if (rng.NextDouble() < dryShare) dryL[dz, dx] = n; else lush[dz, dx] = n;
            }
            td.SetDetailLayer(0, 0, 0, lush);
            td.SetDetailLayer(0, 0, 1, dryL);
        }

        static DetailPrototype GrassPrototype(GameObject prefab, float minW, float maxW, float minH, float maxH) => new DetailPrototype
        {
            usePrototypeMesh = true,
            prototype = prefab,
            renderMode = DetailRenderMode.VertexLit,
            useInstancing = true,
            minWidth = minW, maxWidth = maxW, minHeight = minH, maxHeight = maxH,
            noiseSpread = 0.35f,
            healthyColor = Color.white,
            dryColor = new Color(0.92f, 0.9f, 0.85f),
            alignToGround = 0.6f,
            positionJitter = 1f,
            density = 1f
        };

        // ---------------------------------------------------------------- trees

        public static int PlantTrees(TerrainData td, TerrainGrid g, WorldField f, VegetationSet veg, LavaLayout lava)
        {
            var d = f.D;
            var protos = new List<TreePrototype>();
            foreach (var p in veg.firs) protos.Add(new TreePrototype { prefab = p, bendFactor = 0f });
            foreach (var p in veg.snags) protos.Add(new TreePrototype { prefab = p, bendFactor = 0f });
            td.treePrototypes = protos.ToArray();
            var instances = new List<TreeInstance>();
            var rng = new System.Random(d.seed + 11);
            const float cellSize = 4.6f;
            int cells = Mathf.FloorToInt(d.size / cellSize);
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                float x = (cx + (float)rng.NextDouble()) * cellSize, z = (cz + (float)rng.NextDouble()) * cellSize;
                float roll = (float)rng.NextDouble();
                int gi = g.Index(Mathf.Clamp(Mathf.RoundToInt(x / g.cell), 0, g.res - 1), Mathf.Clamp(Mathf.RoundToInt(z / g.cell), 0, g.res - 1));
                float roadD = g.roadDist[gi];
                if (roadD < d.roadHalfWidth + 4.5f || g.lavaDist[gi] < 14f) continue;
                float slope = g.SlopeDegrees(Mathf.RoundToInt(x / g.cell), Mathf.RoundToInt(z / g.cell));
                float h = g.height[gi];
                float volc = f.Volcanic(x, z);
                float ring = f.ScorchedRing(volc);
                float edgeD = f.EdgeDistance(x, z);

                // Burnt snags on the volcanic fringe.
                if (ring > 0.35f && slope < 32f && roll < ring * 0.06f)
                {
                    instances.Add(Tree(veg.firs.Length + (rng.NextDouble() < 0.5 ? 0 : 1), x, z, h, d, rng, 0.8f, 1.25f));
                    continue;
                }
                if (volc > 0.25f || slope > 40f || h > 175f || edgeD < 6f) continue;
                float forest = f.Forest(x, z) * (1f - f.Meadow(x, z));
                // Thin the forest near the road so the trail reads clearly, and allow sparse lone trees.
                float roadFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(d.roadHalfWidth + 4.5f, d.roadHalfWidth + 16f, roadD));
                float slopeFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 40f, slope)) * 0.75f;
                float p = (Mathf.Clamp01(forest * 1.25f) * Mathf.Lerp(0.3f, 1f, roadFade) + 0.015f * (1f - volc)) * slopeFade;
                if (roll > p) continue;
                // Variant: bushy B at forest edges, tall C in dense cores.
                int variant = forest > 0.75f && rng.NextDouble() < 0.45 ? 2 : forest < 0.4f ? 1 : (rng.NextDouble() < 0.5 ? 0 : 1);
                instances.Add(Tree(variant, x, z, h, d, rng, 0.72f, 1.22f));
            }
            td.SetTreeInstances(instances.ToArray(), true);
            return instances.Count;
        }

        static TreeInstance Tree(int proto, float x, float z, float h, WorldDesign d, System.Random rng, float minScale, float maxScale)
        {
            float s = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
            float tint = 0.85f + 0.15f * (float)rng.NextDouble();
            return new TreeInstance
            {
                prototypeIndex = proto,
                position = new Vector3(x / d.size, h / d.heightScale, z / d.size),
                widthScale = s * (0.9f + 0.2f * (float)rng.NextDouble()),
                heightScale = s,
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = new Color(tint, tint, tint, 1f),
                lightmapColor = Color.white
            };
        }

        static void PlantBackdropTrees(TerrainData td, WorldField f, Vector3 origin, VegetationSet veg)
        {
            var d = f.D;
            var protos = new List<TreePrototype>();
            foreach (var p in veg.firs) protos.Add(new TreePrototype { prefab = p });
            td.treePrototypes = protos.ToArray();
            var instances = new List<TreeInstance>();
            var rng = new System.Random((int)(origin.x * 3 + origin.z * 7) + d.seed);
            const float cellSize = 12f;
            int cells = Mathf.FloorToInt(d.size / cellSize);
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                float lx = (cx + (float)rng.NextDouble()) * cellSize, lz = (cz + (float)rng.NextDouble()) * cellSize;
                float x = origin.x + lx, z = origin.z + lz;
                // Only the band nearest the playable area (visible from inside) gets trees.
                float outside = -f.EdgeDistance(x, z);
                if (outside > 420f) continue;
                float h = td.GetInterpolatedHeight(lx / d.size, lz / d.size);
                if (h > 175f || td.GetSteepness(lx / d.size, lz / d.size) > 33f) continue;
                if (f.Volcanic(x, z) > 0.2f) continue;
                if (rng.NextDouble() > f.Forest(x, z) * 0.8f + 0.05f) continue;
                instances.Add(Tree(rng.Next(veg.firs.Length), lx, lz, h, d, rng, 0.9f, 1.3f));
            }
            td.SetTreeInstances(instances.ToArray(), true);
        }
    }
}
