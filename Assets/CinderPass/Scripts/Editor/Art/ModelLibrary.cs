using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Turns heavy photogrammetry models into game-ready prefabs: merged, re-pivoted to bottom-centre,
    /// decimated to a triangle budget (via Unity's mesh LOD generator), given a runtime Mesh LOD chain,
    /// and paired with a coarse collision mesh.
    /// </summary>
    public static class ModelLibrary
    {
        public enum Kind { Cliff, Boulder, Basalt, Prop, Foliage }

        public sealed class ModelDef
        {
            public string id;
            public Kind kind;
            public int renderTris;
            public int collisionTris;
            public bool collider;
            public Color tint = Color.white;
            public string prefabName;
        }

        static readonly Color BasaltTint = new Color(0.3f, 0.285f, 0.28f);

        public static readonly ModelDef[] Defs =
        {
            new ModelDef { id = "rock_face_01", kind = Kind.Cliff, renderTris = 16000, collisionTris = 900, collider = true, prefabName = "Cliff_A" },
            new ModelDef { id = "rock_face_02", kind = Kind.Cliff, renderTris = 18000, collisionTris = 900, collider = true, prefabName = "Cliff_B" },
            new ModelDef { id = "namaqualand_cliff_01", kind = Kind.Cliff, renderTris = 26000, collisionTris = 1200, collider = true, prefabName = "Cliff_C" },
            new ModelDef { id = "boulder_01", kind = Kind.Boulder, renderTris = 12000, collisionTris = 500, collider = true, prefabName = "Boulder_A" },
            new ModelDef { id = "namaqualand_boulder_05", kind = Kind.Boulder, renderTris = 10000, collisionTris = 500, collider = true, prefabName = "Boulder_B" },
            new ModelDef { id = "rock_07", kind = Kind.Boulder, renderTris = 7000, collisionTris = 400, collider = true, prefabName = "Boulder_C" },
            new ModelDef { id = "rock_09", kind = Kind.Boulder, renderTris = 7000, collisionTris = 400, collider = true, prefabName = "Boulder_D" },
            new ModelDef { id = "moon_rock_01", kind = Kind.Basalt, renderTris = 7000, collisionTris = 400, collider = true, tint = BasaltTint, prefabName = "Basalt_A" },
            new ModelDef { id = "moon_rock_02", kind = Kind.Basalt, renderTris = 6000, collisionTris = 400, collider = true, tint = BasaltTint, prefabName = "Basalt_B" },
            new ModelDef { id = "moon_rock_04", kind = Kind.Basalt, renderTris = 7000, collisionTris = 400, collider = true, tint = BasaltTint, prefabName = "Basalt_C" },
            new ModelDef { id = "moon_rock_06", kind = Kind.Basalt, renderTris = 7000, collisionTris = 400, collider = true, tint = BasaltTint, prefabName = "Basalt_D" },
            new ModelDef { id = "dead_tree_trunk_02", kind = Kind.Prop, renderTris = 9000, collisionTris = 300, collider = true, prefabName = "Log_Fallen" },
            new ModelDef { id = "tree_stump_01", kind = Kind.Prop, renderTris = 6000, collisionTris = 300, collider = true, prefabName = "Stump" },
            new ModelDef { id = "old_tyre", kind = Kind.Prop, renderTris = 3000, collisionTris = 200, collider = false, prefabName = "Prop_Tyre" },
            new ModelDef { id = "metal_jerrycan", kind = Kind.Prop, renderTris = 4000, collisionTris = 150, collider = false, prefabName = "Prop_Jerrycan" },
            new ModelDef { id = "wooden_crate_01", kind = Kind.Prop, renderTris = 7000, collisionTris = 150, collider = true, prefabName = "Prop_Crate" },
            new ModelDef { id = "old_military_crate", kind = Kind.Prop, renderTris = 6000, collisionTris = 150, collider = true, prefabName = "Prop_MilitaryCrate" },
            new ModelDef { id = "propane_tank", kind = Kind.Prop, renderTris = 5300, collisionTris = 150, collider = false, prefabName = "Prop_PropaneTank" },
            new ModelDef { id = "fern_02", kind = Kind.Foliage, renderTris = 3500, collisionTris = 0, collider = false, prefabName = "Fern" },
        };

        public static string PrefabPath(ModelDef d) => $"{CinderPaths.EnvPrefabs}/{Folder(d.kind)}/{d.prefabName}.prefab";

        static string Folder(Kind k) => k switch
        {
            Kind.Cliff => "Geology",
            Kind.Boulder => "Geology",
            Kind.Basalt => "Geology",
            Kind.Foliage => "Vegetation",
            _ => "Props"
        };

        public static Dictionary<string, GameObject> BuildAll(Material fernMaterial)
        {
            var result = new Dictionary<string, GameObject>();
            foreach (var d in Defs)
            {
                var prefab = Build(d, fernMaterial);
                if (prefab != null) result[d.prefabName] = prefab;
            }
            return result;
        }

        public static GameObject Build(ModelDef d, Material overrideMaterial = null)
        {
            string dir = $"{CinderPaths.Models}/{d.id}";
            string fbx = Directory.GetFiles(dir, "*.fbx").FirstOrDefault()?.Replace('\\', '/');
            if (fbx == null) { Debug.LogError($"[ModelLibrary] No FBX for {d.id}"); return null; }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var merged = Merge(source, d.id);
            NormalizeSize(merged, d.kind);
            int srcTris = TriangleCount(merged);

            var render = srcTris > d.renderTris ? Decimate(merged, d.renderTris) : merged;
            render.name = $"{d.prefabName}";
            MeshLodUtility.GenerateMeshLods(render, (MeshLodUtility.LodGenerationFlags)0, 5);
            render = AssetUtil.SaveAsset(render, $"{CinderPaths.GenMeshes}/Models/{d.prefabName}.asset");

            Mesh collision = null;
            if (d.collider)
            {
                collision = Decimate(merged, d.collisionTris);
                collision.name = $"{d.prefabName}_Collision";
                collision = AssetUtil.SaveAsset(collision, $"{CinderPaths.GenMeshes}/Models/{d.prefabName}_Collision.asset");
            }

            var mat = overrideMaterial != null && d.kind == Kind.Foliage ? overrideMaterial : MaterialLibrary.ForModel(d.id, d.tint, 1f, d.kind == Kind.Basalt ? "_Basalt" : "");
            var root = new GameObject(d.prefabName);
            root.AddComponent<MeshFilter>().sharedMesh = render;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = d.kind == Kind.Foliage ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (collision != null)
            {
                var mc = root.AddComponent<MeshCollider>();
                mc.sharedMesh = collision;
                mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
            }
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            Debug.Log($"[ModelLibrary] {d.prefabName}: {srcTris} -> {TriangleCount(render)} tris (collision {(collision != null ? TriangleCount(collision) : 0)}), size {render.bounds.size}");
            return VegetationBuilder.SavePrefab(root, PrefabPath(d));
        }

        /// <summary>Combines every mesh in the model into one mesh with its pivot at the bottom centre.</summary>
        static Mesh Merge(GameObject source, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            // Keep the imported root rotation/scale: some scans store their axis and unit conversion there.
            instance.transform.position = Vector3.zero;
            var combines = new List<CombineInstance>();
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                for (int s = 0; s < m.subMeshCount; s++)
                    combines.Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = mf.transform.localToWorldMatrix });
            }
            var merged = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            merged.CombineMeshes(combines.ToArray(), true, true);
            Object.DestroyImmediate(instance);

            var b = merged.bounds;
            Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            var v = merged.vertices;
            for (int i = 0; i < v.Length; i++) v[i] -= pivot;
            merged.vertices = v;
            merged.RecalculateBounds();
            if (merged.tangents == null || merged.tangents.Length == 0) merged.RecalculateTangents();
            return merged;
        }

        /// <summary>
        /// Gives every rock category a canonical size so placement scales mean the same thing for all
        /// variants (source scans range from 0.2 m pebbles to 8 m cliff faces).
        /// </summary>
        static void NormalizeSize(Mesh m, Kind kind)
        {
            float target = kind switch { Kind.Boulder => 1.8f, Kind.Basalt => 1.5f, Kind.Cliff => 7f, _ => 0f };
            if (target <= 0f) return;
            var b = m.bounds;
            float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (max < 1e-4f) return;
            float k = target / max;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] *= k;
            m.vertices = v;
            m.RecalculateBounds();
        }

        static int TriangleCount(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s);
            return (int)(n / 3);
        }

        /// <summary>Generates a LOD chain on a copy, then extracts the first level within budget as a compact mesh.</summary>
        public static Mesh Decimate(Mesh source, int targetTris)
        {
            var work = Object.Instantiate(source);
            MeshLodUtility.GenerateMeshLods(work, (MeshLodUtility.LodGenerationFlags)0, 10);
            int chosen = work.lodCount - 1;
            for (int l = 0; l < work.lodCount; l++)
            {
                int tris = work.GetIndices(0, l, true).Length / 3;
                if (tris <= targetTris) { chosen = l; break; }
            }
            var result = Extract(work, chosen);
            Object.DestroyImmediate(work);
            return result;
        }

        static Mesh Extract(Mesh mesh, int lod)
        {
            int[] indices = mesh.GetIndices(0, lod, true);
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uvs = mesh.uv;
            var remap = new Dictionary<int, int>();
            var nv = new List<Vector3>();
            var nn = new List<Vector3>();
            var nt = new List<Vector4>();
            var nu = new List<Vector2>();
            var ni = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int o = indices[i];
                if (!remap.TryGetValue(o, out int k))
                {
                    k = nv.Count;
                    remap[o] = k;
                    nv.Add(verts[o]);
                    if (normals.Length > 0) nn.Add(normals[o]);
                    if (tangents.Length > 0) nt.Add(tangents[o]);
                    if (uvs.Length > 0) nu.Add(uvs[o]);
                }
                ni[i] = k;
            }
            var m = new Mesh { indexFormat = nv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(nv);
            if (nn.Count == nv.Count) m.SetNormals(nn); else m.RecalculateNormals();
            if (nu.Count == nv.Count) m.SetUVs(0, nu);
            m.SetTriangles(ni, 0);
            if (nt.Count == nv.Count) m.SetTangents(nt); else m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
