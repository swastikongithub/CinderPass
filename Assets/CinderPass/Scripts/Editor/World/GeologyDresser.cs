using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Places the geological language of the level: canyon walls, highland boulder clusters, ridge crags,
    /// basalt fields in the volcanic basin, lava-rim boulders and a sparse scatter on the plains.
    /// Everything is placed as prefab instances (shared meshes/materials), clustered rather than uniform,
    /// with scale/rotation variation and embedded into the terrain.
    /// </summary>
    public static class GeologyDresser
    {
        public sealed class RockSet
        {
            public GameObject[] cliffs, boulders, basalt;
            public GameObject log, stump, fern;
        }

        public static int Dress(Transform root, TerrainGrid g, WorldField f, RouteSamples route, LavaLayout lava, RockSet set)
        {
            var d = f.D;
            var rng = new System.Random(d.seed + 21);
            int count = 0;
            var canyon = AssetUtil.Child(root, "Canyon").transform;
            var highlands = AssetUtil.Child(root, "Highlands").transform;
            var crags = AssetUtil.Child(root, "RidgeCrags").transform;
            var basaltRoot = AssetUtil.Child(root, "BasaltFields").transform;
            var plains = AssetUtil.Child(root, "Plains").transform;
            var forest = AssetUtil.Child(root, "ForestFloor").transform;
            float w = d.roadHalfWidth;

            // --- Canyon walls: massive outcrops rooted at the foot of each wall (pushed into the slope),
            //     breaking up the stretched terrain and giving the passage a rocky, enclosed silhouette.
            for (int i = 0; i < route.points.Length; i += 14)
            {
                if (route.canyon[i] < 0.5f) continue;
                Vector3 p = route.points[i];
                Vector3 fwd = (route.points[(i + 4) % route.points.Length] - route.points[(i - 4 + route.points.Length) % route.points.Length]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, new Vector3(fwd.x, 0f, fwd.z)).normalized;
                for (int side = -1; side <= 1; side += 2)
                {
                    var prefab = rng.NextDouble() < 0.6 ? set.cliffs[2] : set.boulders[rng.Next(2)];
                    float scale = prefab == set.cliffs[2] ? 1.3f + (float)rng.NextDouble() * 0.9f : 2.4f + (float)rng.NextDouble() * 1.8f;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canyon);
                    go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    // Keep the rock's near face beyond the trail edge (plus the ditch): offset by its footprint.
                    var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                    float radius = Mathf.Max(b.extents.x, b.extents.z) * scale;
                    float off = w + 2.2f + radius * 0.85f;
                    Vector3 pos = p + right * side * off;
                    // The base sits just below road level so each outcrop grows out of the wall foot.
                    go.transform.position = new Vector3(pos.x, p.y - 0.8f, pos.z);
                    count++;
                }
            }

            // --- Highland boulder clusters (between the forest edge and the ridge).
            count += Clusters(highlands, set.boulders, g, f, rng, 40f, (x, z, h, slope, roadD, volc) =>
                volc < 0.2f && roadD > w + 3.5f && h > 38f && h < 160f && slope < 38f ? Mathf.Clamp01(1.3f - f.Forest(x, z) * 1.4f) * 0.55f : 0f,
                3, 7, 0.5f, 2.6f);

            // --- Crags on steep ridge and rim slopes.
            for (int k = 0; k < 2600; k++)
            {
                float x = (float)rng.NextDouble() * d.size, z = (float)rng.NextDouble() * d.size;
                if (f.EdgeDistance(x, z) < 30f) continue;
                int gx = Mathf.RoundToInt(x / g.cell), gz = Mathf.RoundToInt(z / g.cell);
                float slope = g.SlopeDegrees(gx, gz);
                if (slope < 36f || g.RoadDistance(x, z) < w + 6f || g.LavaDistance(x, z) < 8f) continue;
                if (rng.NextDouble() > 0.12) continue;
                bool volc = f.Volcanic(x, z) > 0.5f;
                var prefab = volc ? set.basalt[rng.Next(set.basalt.Length)] : set.cliffs[rng.Next(set.cliffs.Length)];
                float scale = volc ? 2f + (float)rng.NextDouble() * 2.5f : 1.1f + (float)rng.NextDouble() * 1.3f;
                Vector3 n = g.Normal(x, z);
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(-n, Vector3.up).normalized + Vector3.forward * 0.001f, Vector3.up) * Quaternion.Euler(0f, (float)rng.NextDouble() * 60f - 30f, 0f);
                count += Place(prefab, crags, g, new Vector3(x, 0f, z), rot, scale, 0.25f, rng, alignToNormal: 0.3f);
            }

            // --- Basalt fields in the volcanic basin (angular dark boulders, clustered).
            count += Clusters(basaltRoot, set.basalt, g, f, rng, 34f, (x, z, h, slope, roadD, volc) =>
                volc > 0.5f && roadD > w + 3.5f && slope < 40f && h < 170f ? 0.75f : 0f, 3, 8, 0.7f, 3.4f);

            // --- Lava rims: boulders ringing every pool.
            foreach (var pool in lava.pools)
            {
                int n = Mathf.RoundToInt(pool.radius * 0.9f) + 3;
                for (int k = 0; k < n; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = pool.radius * LavaLayout.OutlineAt(pool, a) + 2.5f + (float)rng.NextDouble() * 4f;
                    var pos = new Vector3(pool.centre.x + Mathf.Cos(a) * r, 0f, pool.centre.y + Mathf.Sin(a) * r);
                    if (g.RoadDistance(pos.x, pos.z) < w + 3f) continue;
                    count += Place(set.basalt[rng.Next(set.basalt.Length)], basaltRoot, g, pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                        0.6f + (float)rng.NextDouble() * 1.6f, 0.3f, rng, alignToNormal: 0.5f);
                }
            }

            // --- Sparse boulders on the plains and dry basin (landmarks, not clutter).
            count += Clusters(plains, set.boulders, g, f, rng, 95f, (x, z, h, slope, roadD, volc) =>
                volc < 0.15f && roadD > w + 5f && slope < 25f && f.Forest(x, z) < 0.35f ? 0.35f : 0f, 1, 4, 0.8f, 3.2f);

            // --- Forest floor: fallen logs, stumps and ferns (only in forest, near the trail where they're seen).
            for (int k = 0; k < 9000 && forest.childCount < 260; k++)
            {
                float x = (float)rng.NextDouble() * d.size, z = (float)rng.NextDouble() * d.size;
                float roadD = g.RoadDistance(x, z);
                if (roadD < w + 2.5f || roadD > 55f) continue;
                float fo = f.Forest(x, z);
                if (fo < 0.45f || f.Volcanic(x, z) > 0.2f) continue;
                int gx = Mathf.RoundToInt(x / g.cell), gz = Mathf.RoundToInt(z / g.cell);
                if (g.SlopeDegrees(gx, gz) > 28f) continue;
                double roll = rng.NextDouble();
                GameObject prefab = roll < 0.72 ? set.fern : roll < 0.87 ? set.stump : set.log;
                float scale = prefab == set.fern ? 0.8f + (float)rng.NextDouble() * 0.6f : prefab == set.log ? 1.4f + (float)rng.NextDouble() * 0.6f : 0.9f + (float)rng.NextDouble() * 0.4f;
                count += Place(prefab, forest, g, new Vector3(x, 0f, z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), scale, prefab == set.fern ? 0.02f : 0.12f, rng, alignToNormal: 0.8f);
            }
            return count;
        }

        delegate float Suitability(float x, float z, float h, float slope, float roadD, float volc);

        static int Clusters(Transform parent, GameObject[] prefabs, TerrainGrid g, WorldField f, System.Random rng, float spacing, Suitability suit, int minCount, int maxCount, float minScale, float maxScale)
        {
            int placed = 0;
            var d = f.D;
            int cells = Mathf.FloorToInt(d.size / spacing);
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                float x = (cx + (float)rng.NextDouble()) * spacing, z = (cz + (float)rng.NextDouble()) * spacing;
                if (f.EdgeDistance(x, z) < 40f) continue;
                int gx = Mathf.RoundToInt(x / g.cell), gz = Mathf.RoundToInt(z / g.cell);
                float s = suit(x, z, g.Height(x, z), g.SlopeDegrees(gx, gz), g.RoadDistance(x, z), f.Volcanic(x, z));
                if (rng.NextDouble() > s) continue;
                int n = rng.Next(minCount, maxCount + 1);
                float clusterScale = Mathf.Lerp(minScale, maxScale, Mathf.Pow((float)rng.NextDouble(), 1.6f));
                for (int k = 0; k < n; k++)
                {
                    // One anchor rock with smaller satellites around it.
                    float r = k == 0 ? 0f : (1.5f + (float)rng.NextDouble() * 5f) * Mathf.Sqrt(clusterScale);
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float px = x + Mathf.Cos(a) * r, pz = z + Mathf.Sin(a) * r;
                    if (g.RoadDistance(px, pz) < d.roadHalfWidth + 2.5f || g.LavaDistance(px, pz) < 2f) continue;
                    float scale = clusterScale * (k == 0 ? 1f : 0.3f + (float)rng.NextDouble() * 0.45f);
                    placed += Place(prefabs[rng.Next(prefabs.Length)], parent, g, new Vector3(px, 0f, pz), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), scale, 0.22f, rng, alignToNormal: 0.6f);
                }
            }
            return placed;
        }

        /// <summary>Instantiates a prefab embedded in the terrain (sunk by a fraction of its height).</summary>
        public static int Place(GameObject prefab, Transform parent, TerrainGrid g, Vector3 pos, Quaternion yaw, float scale, float sink, System.Random rng, float alignToNormal)
        {
            if (prefab == null) return 0;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Vector3 n = g.Normal(pos.x, pos.z);
            Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, n), alignToNormal);
            Quaternion wobble = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 10f, 0f, (float)(rng.NextDouble() - 0.5) * 10f);
            go.transform.rotation = tilt * yaw * wobble;
            go.transform.localScale = Vector3.one * scale * (0.9f + 0.2f * (float)rng.NextDouble());
            var mf = go.GetComponentInChildren<MeshFilter>();
            float height = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.y * go.transform.localScale.y : scale;
            float ground = Mathf.Min(g.Height(pos.x, pos.z), g.Height(pos.x + 1f, pos.z), g.Height(pos.x - 1f, pos.z), g.Height(pos.x, pos.z + 1f), g.Height(pos.x, pos.z - 1f));
            go.transform.position = new Vector3(pos.x, ground - height * sink, pos.z);
            return 1;
        }
    }
}
