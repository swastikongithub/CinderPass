using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Procedural vegetation: fir trees (3 LODs, branch cards built from real twig photography),
    /// burnt snags for the volcanic fringe, and grass clump meshes for terrain details.
    /// Vertex colour encodes wind/AO data for CinderPass/Foliage: R flutter, G AO, B phase, A sway.
    /// </summary>
    public static class VegetationBuilder
    {
        public sealed class FirSpec
        {
            public string name;
            public float height = 17f;
            public float crownBase = 0.18f;     // fraction of height where branches start
            public float whorlSpacing = 0.55f;  // metres
            public int branchesPerWhorl = 6;
            public float maxBranchRatio = 0.24f; // longest branch / height
            public float droop = 18f;            // degrees below horizontal at the bottom
            public int seed = 1;
        }

        public static readonly FirSpec[] FirSpecs =
        {
            new FirSpec { name = "Fir_A", height = 17f, crownBase = 0.2f, whorlSpacing = 0.55f, branchesPerWhorl = 6, maxBranchRatio = 0.24f, droop = 20f, seed = 11 },
            new FirSpec { name = "Fir_B", height = 12.5f, crownBase = 0.1f, whorlSpacing = 0.5f, branchesPerWhorl = 6, maxBranchRatio = 0.3f, droop = 16f, seed = 23 },
            new FirSpec { name = "Fir_C", height = 22f, crownBase = 0.32f, whorlSpacing = 0.62f, branchesPerWhorl = 5, maxBranchRatio = 0.19f, droop = 24f, seed = 37 },
        };

        const int SubBark = 0, SubLeaves = 1;

        public static List<GameObject> BuildFirs(Material bark, Material leaves)
        {
            var prefabs = new List<GameObject>();
            foreach (var spec in FirSpecs)
            {
                var lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var mesh = BuildFirMesh(spec, lod).ToMesh($"{spec.name}_LOD{lod}");
                    lods[lod] = AssetUtil.SaveAsset(mesh, $"{CinderPaths.GenMeshes}/Vegetation/{spec.name}_LOD{lod}.asset");
                }
                var root = new GameObject(spec.name);
                var renderers = new Renderer[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var go = new GameObject($"LOD{lod}");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = lods[lod];
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new[] { bark, leaves };
                    mr.shadowCastingMode = lod == 2 ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    renderers[lod] = mr;
                }
                var group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;
                group.SetLODs(new[]
                {
                    new LOD(0.22f, new[] { renderers[0] }),
                    new LOD(0.07f, new[] { renderers[1] }),
                    new LOD(0.006f, new[] { renderers[2] }),
                });
                group.RecalculateBounds();
                var cap = root.AddComponent<CapsuleCollider>();
                cap.radius = spec.height * 0.022f;
                cap.height = spec.height * 0.7f;
                cap.center = new Vector3(0f, spec.height * 0.35f, 0f);
                prefabs.Add(SavePrefab(root, $"{CinderPaths.EnvPrefabs}/Vegetation/{spec.name}.prefab"));
            }
            return prefabs;
        }

        static MeshBuilder BuildFirMesh(FirSpec s, int lod)
        {
            var rng = new System.Random(s.seed);
            float R() => (float)rng.NextDouble();
            var mb = new MeshBuilder(2);
            float H = s.height;

            // Trunk with a gentle random lean and root flare.
            int trunkSeg = lod == 0 ? 9 : lod == 1 ? 6 : 4;
            var path = new List<Vector3>();
            Vector2 lean = new Vector2(R() - 0.5f, R() - 0.5f) * 0.25f;
            int rings = lod == 0 ? 10 : 5;
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings;
                path.Add(new Vector3(lean.x * t * t * H * 0.05f, t * H * 0.97f, lean.y * t * t * H * 0.05f));
            }
            float r0 = H * 0.017f;
            int trunkStart = mb.Vertices.Count;
            mb.Tube(SubBark, path, r0, trunkSeg, false, 0.025f);
            for (int i = trunkStart; i < mb.Vertices.Count; i++)
            {
                var p = mb.Vertices[i];
                float h01 = Mathf.Clamp01(p.y / H);
                // Root flare: widen the bottom metre.
                if (p.y < 1f && lod < 2)
                {
                    Vector3 radial = new Vector3(p.x, 0f, p.z);
                    float flare = 1f + (1f - p.y) * 0.45f;
                    mb.Vertices[i] = new Vector3(radial.x * flare, p.y, radial.z * flare);
                }
                mb.UVs[i] = new Vector2(mb.UVs[i].x, mb.UVs[i].y / 1.6f);
                mb.Colors[i] = new Color(0f, Mathf.Lerp(0.55f, 1f, h01), 0f, Mathf.Pow(h01, 1.4f));
            }

            // Branch whorls.
            float start = H * s.crownBase;
            float top = H * 0.95f;
            int whorlStep = lod == 0 ? 1 : lod == 1 ? 2 : 3;
            int whorlIndex = 0;
            var uv = new Rect(0f, 0f, 1f, 1f);
            for (float y = start; y < top; y += s.whorlSpacing, whorlIndex++)
            {
                if (whorlIndex % whorlStep != 0) continue;
                float h01 = (y - start) / (top - start);
                // Conical crown with a slightly rounded top.
                float len = H * s.maxBranchRatio * Mathf.Pow(1f - h01, 0.85f) + 0.35f;
                int count = Mathf.Max(3, s.branchesPerWhorl - (lod == 2 ? 1 : 0) - (h01 > 0.8f ? 1 : 0));
                float yawOffset = R() * 360f;
                for (int b = 0; b < count; b++)
                {
                    float yaw = yawOffset + b * 360f / count + (R() - 0.5f) * 25f;
                    float droop = Mathf.Lerp(s.droop, 4f, h01) + (R() - 0.5f) * 8f;
                    float bl = len * (0.85f + R() * 0.3f) * (lod == 0 ? 1f : lod == 1 ? 1.08f : 1.15f);
                    float widthScale = lod == 0 ? 1f : lod == 1 ? 1.45f : 2.1f;
                    float phase = R();
                    Vector3 attach = new Vector3(0f, y + (R() - 0.5f) * s.whorlSpacing * 0.4f, 0f);
                    Quaternion rot = Quaternion.Euler(droop, yaw, (R() - 0.5f) * 40f);
                    AddBranchCard(mb, attach, rot, bl, bl * 0.5f * widthScale, lod == 2 ? 1 : 3, h01, phase, H, uv);
                    if (lod == 0)
                        AddBranchCard(mb, attach + Vector3.up * 0.05f, rot * Quaternion.Euler(0f, 0f, 65f + R() * 20f), bl * 0.9f, bl * 0.32f, 2, h01, phase, H, uv);
                }
            }
            // Leader at the top.
            for (int k = 0; k < (lod == 2 ? 1 : 2); k++)
            {
                Quaternion rot = Quaternion.Euler(-88f, k * 90f, 90f);
                AddBranchCard(mb, new Vector3(0f, top - H * 0.02f, 0f), rot, H * 0.1f, H * 0.05f, 1, 1f, 0.3f, H, uv);
            }
            return mb;
        }

        /// <summary>A drooping branch card from <paramref name="attach"/> along rot*forward.</summary>
        static void AddBranchCard(MeshBuilder mb, Vector3 attach, Quaternion rot, float length, float width, int segments, float h01, float phase, float treeHeight, Rect uv)
        {
            Vector3 fwd = rot * Vector3.forward;
            Vector3 side = rot * Vector3.right;
            Vector3 cardUp = Vector3.Cross(fwd, side).normalized;
            if (cardUp.y < 0f) { cardUp = -cardUp; side = -side; }
            float curl = length * 0.12f;
            int baseIndex = mb.Vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 centre = attach + fwd * (length * t) + Vector3.down * (curl * t * t) + Vector3.up * (curl * 0.35f * t * t * t);
                float w = width * (0.55f + 0.45f * Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI * 0.85f)) * 0.5f;
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 p = centre + side * (w * e);
                    // Normals bent outward from the trunk give the crown a rounded, volumetric shading.
                    Vector3 outward = new Vector3(p.x, 0f, p.z).normalized;
                    Vector3 n = (cardUp * 0.45f + outward * 0.55f + Vector3.up * 0.25f).normalized;
                    float ao = Mathf.Lerp(0.42f, 1f, t) * Mathf.Lerp(0.72f, 1f, h01);
                    float sway = Mathf.Clamp01(p.y / treeHeight);
                    mb.Vertex(p, n, new Vector2(Mathf.Lerp(uv.xMin, uv.xMax, t), e < 0 ? uv.yMin : uv.yMax),
                        new Color(t, ao, phase, sway * sway));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int a = baseIndex + i * 2;
                // Winding (a, a+2, a+3, a+1) makes the front face point along cardUp.
                mb.Quad(SubLeaves, a, a + 2, a + 3, a + 1);
            }
        }

        // ------------------------------------------------------------------ burnt snags

        public static List<GameObject> BuildSnags(Material charred)
        {
            var result = new List<GameObject>();
            for (int v = 0; v < 2; v++)
            {
                float H = v == 0 ? 9f : 6.5f;
                var meshes = new Mesh[2];
                for (int lod = 0; lod < 2; lod++)
                {
                    var r2 = new System.Random(101 + v * 17);
                    var mb = new MeshBuilder(1);
                    var trunk = new List<Vector3>();
                    Vector3 p = Vector3.zero;
                    Vector3 dir = Vector3.up;
                    int steps = lod == 0 ? 8 : 4;
                    for (int i = 0; i <= steps; i++)
                    {
                        trunk.Add(p);
                        dir = (dir + new Vector3((float)r2.NextDouble() - 0.5f, 0f, (float)r2.NextDouble() - 0.5f) * 0.18f).normalized;
                        p += dir * (H / steps);
                    }
                    mb.Tube(0, trunk, H * 0.035f, lod == 0 ? 8 : 5, true, H * 0.008f);
                    int limbs = lod == 0 ? 7 : 4;
                    for (int b = 0; b < limbs; b++)
                    {
                        float t = 0.35f + 0.55f * b / limbs;
                        Vector3 a = trunk[Mathf.Clamp(Mathf.RoundToInt(t * steps), 0, steps)];
                        float yaw = (float)r2.NextDouble() * 360f;
                        Vector3 d = Quaternion.Euler(-(10f + (float)r2.NextDouble() * 35f), yaw, 0f) * Vector3.forward;
                        float bl = H * (0.12f + (float)r2.NextDouble() * 0.12f) * (1.2f - t);
                        var limb = new List<Vector3> { a, a + d * bl * 0.5f, a + d * bl + Vector3.up * bl * 0.15f };
                        mb.Tube(0, limb, H * 0.012f, lod == 0 ? 5 : 3, true, H * 0.003f);
                    }
                    for (int i = 0; i < mb.Colors.Count; i++) mb.Colors[i] = new Color(0f, 0.8f, 0f, 0f);
                    meshes[lod] = AssetUtil.SaveAsset(mb.ToMesh($"Snag_{v}_LOD{lod}"), $"{CinderPaths.GenMeshes}/Vegetation/Snag_{v}_LOD{lod}.asset");
                }
                var root = new GameObject($"Snag_{(char)('A' + v)}");
                var rs = new Renderer[2];
                for (int lod = 0; lod < 2; lod++)
                {
                    var go = new GameObject($"LOD{lod}");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[lod];
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = charred;
                    rs[lod] = mr;
                }
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.12f, new[] { rs[0] }), new LOD(0.01f, new[] { rs[1] }) });
                var cap = root.AddComponent<CapsuleCollider>();
                cap.radius = H * 0.04f;
                cap.height = H * 0.8f;
                cap.center = new Vector3(0f, H * 0.4f, 0f);
                result.Add(SavePrefab(root, $"{CinderPaths.EnvPrefabs}/Vegetation/Snag_{(char)('A' + v)}.prefab"));
            }
            return result;
        }

        // ------------------------------------------------------------------ grass

        /// <summary>Three crossed 2:1 cards, each showing a different clump from the atlas.</summary>
        public static Mesh BuildGrassClump(string name, int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder(1);
            for (int c = 0; c < 3; c++)
            {
                float yaw = c * 60f + (float)rng.NextDouble() * 20f;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 right = q * Vector3.right * 0.5f;
                Vector3 lean = q * Vector3.forward * (((float)rng.NextDouble() - 0.5f) * 0.12f);
                Vector3 offset = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.2f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.2f);
                int cell = (c + seed) % 4;
                var uv = new Rect((cell % 2) * 0.5f, (cell / 2) * 0.5f, 0.5f, 0.5f);
                Vector3 a = offset - right, b = offset + right;
                Vector3 up = Vector3.up * 0.5f + lean;
                float ph = (float)rng.NextDouble();
                Color bottom = new Color(0f, 0.55f, ph, 0f), topC = new Color(1f, 1f, ph, 1f);
                mb.Card(0, a, b, b + up, a + up, uv, bottom, bottom, topC, topC, Vector3.up);
            }
            return AssetUtil.SaveAsset(mb.ToMesh(name), $"{CinderPaths.GenMeshes}/Vegetation/{name}.asset");
        }

        public static GameObject SavePrefab(GameObject root, string path)
        {
            AssetUtil.EnsureFolder(System.IO.Path.GetDirectoryName(path));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
