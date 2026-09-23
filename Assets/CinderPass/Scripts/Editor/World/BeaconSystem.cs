using System.Collections.Generic;
using CinderPass.Environment;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// The reusable glowing object: one RouteBeacon prefab (a survey mast with a glowing glass lantern),
    /// one BeaconStyle asset, two shared materials. Instances are placed along the route as prefab instances,
    /// so editing the prefab or the style updates every beacon consistently.
    /// </summary>
    public static class BeaconSystem
    {
        public const string PrefabPath = CinderPaths.Prefabs + "/Beacon/RouteBeacon.prefab";
        public const string StylePath = CinderPaths.Data + "/BeaconStyle.asset";

        public static GameObject BuildPrefab()
        {
            var glow = AssetUtil.Material(MaterialLibrary.MatPath("Beacon", "M_BeaconGlow"), AssetUtil.FindShader("CinderPass/BeaconGlow"));
            glow.enableInstancing = true;
            var halo = AssetUtil.Material(MaterialLibrary.MatPath("Beacon", "M_BeaconHalo"), AssetUtil.FindShader("CinderPass/BeaconHalo"));
            halo.enableInstancing = true;
            var steel = MaterialLibrary.Lit("Beacon", "M_Beacon_Steel", new MaterialLibrary.LitSpec { color = new Color(0.2f, 0.19f, 0.18f), metallic = 0.75f, smoothness = 0.32f });
            var band = MaterialLibrary.Lit("Beacon", "M_Beacon_Band", new MaterialLibrary.LitSpec { color = new Color(0.78f, 0.36f, 0.1f), smoothness = 0.45f });

            var style = AssetUtil.LoadOrCreate<BeaconStyle>(StylePath);
            style.glowMaterial = glow;
            style.haloMaterial = halo;
            style.Apply();
            EditorUtility.SetDirty(style);

            string meshDir = $"{CinderPaths.GenMeshes}/Beacon";
            var body = AssetUtil.SaveAsset(BodyMesh(), $"{meshDir}/Beacon_Body.asset");
            var glass = AssetUtil.SaveAsset(GlassMesh(), $"{meshDir}/Beacon_Glass.asset");
            var quad = AssetUtil.SaveAsset(HaloQuad(), $"{meshDir}/Beacon_HaloQuad.asset");

            var root = new GameObject("RouteBeacon");
            var bodyGo = AssetUtil.Child(root.transform, "Body");
            bodyGo.AddComponent<MeshFilter>().sharedMesh = body;
            bodyGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { steel, band };
            var glassGo = AssetUtil.Child(root.transform, "Lantern");
            glassGo.transform.localPosition = new Vector3(0f, 2.62f, 0f);
            glassGo.AddComponent<MeshFilter>().sharedMesh = glass;
            var gr = glassGo.AddComponent<MeshRenderer>();
            gr.sharedMaterial = glow;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var haloGo = AssetUtil.Child(glassGo.transform, "Halo");
            haloGo.AddComponent<MeshFilter>().sharedMesh = quad;
            var hr = haloGo.AddComponent<MeshRenderer>();
            hr.sharedMaterial = halo;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hr.receiveShadows = false;
            var anchor = AssetUtil.Child(root.transform, "LightAnchor");
            anchor.transform.localPosition = new Vector3(0f, 2.9f, 0f);

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.12f;
            col.height = 3f;
            col.center = new Vector3(0f, 1.5f, 0f);

            var beacon = root.AddComponent<GlowBeacon>();
            Wiring.Set(beacon, "style", style);
            Wiring.Set(beacon, "lightAnchor", anchor.transform);
            GameObjectUtility.SetStaticEditorFlags(bodyGo, StaticEditorFlags.BatchingStatic);
            return VegetationBuilder.SavePrefab(root, PrefabPath);
        }

        static Mesh BodyMesh()
        {
            var mb = new MeshBuilder(2);
            // Tripod legs splayed to the ground.
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2f + 0.3f;
                var foot = new Vector3(Mathf.Cos(a) * 0.62f, -0.15f, Mathf.Sin(a) * 0.62f);
                mb.Tube(0, new List<Vector3> { foot, new Vector3(0f, 1.05f, 0f) }, 0.028f, 6);
                mb.Box(0, Matrix4x4.TRS(foot + Vector3.up * 0.05f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), Vector3.one), new Vector3(0.16f, 0.04f, 0.16f), 0.01f);
            }
            // Mast with a hazard band.
            mb.Cylinder(0, new Vector3(0f, -0.2f, 0f), new Vector3(0f, 2.45f, 0f), 0.05f, 0.042f, 10, true);
            for (int i = 0; i < 3; i++)
                mb.Cylinder(1, new Vector3(0f, 1.55f + i * 0.2f, 0f), new Vector3(0f, 1.65f + i * 0.2f, 0f), 0.053f, 0.053f, 10, false);
            // Lantern cage: base ring, top cap, four uprights.
            mb.Cylinder(0, new Vector3(0f, 2.42f, 0f), new Vector3(0f, 2.48f, 0f), 0.16f, 0.16f, 14, true);
            mb.Cylinder(0, new Vector3(0f, 2.9f, 0f), new Vector3(0f, 2.95f, 0f), 0.17f, 0.12f, 14, true);
            mb.Cylinder(0, new Vector3(0f, 2.95f, 0f), new Vector3(0f, 3.05f, 0f), 0.05f, 0.02f, 8, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * Mathf.PI * 2f + Mathf.PI / 4f;
                var off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.15f;
                mb.Tube(0, new List<Vector3> { off + Vector3.up * 2.46f, off + Vector3.up * 2.91f }, 0.012f, 5);
            }
            return mb.ToMesh("Beacon_Body");
        }

        static Mesh GlassMesh()
        {
            var mb = new MeshBuilder(1);
            // Rounded glass capsule (lathe around Y via an X-axis lathe rotated) and an inner filament.
            var profile = new List<Vector2>();
            const int steps = 10;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float y = Mathf.Lerp(-0.2f, 0.26f, t);
                float r = 0.12f * Mathf.Sin(Mathf.Lerp(0.25f, Mathf.PI - 0.25f, t));
                profile.Add(new Vector2(Mathf.Max(r, 0.02f), y));
            }
            var rotToY = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f));
            mb.DefaultColor = Color.black;
            mb.Lathe(0, profile, 16, rotToY, false);
            mb.DefaultColor = Color.red; // vertex colour R marks the bright core
            mb.Cylinder(0, new Vector3(0f, -0.12f, 0f), new Vector3(0f, 0.18f, 0f), 0.035f, 0.035f, 8, true, Color.red);
            return mb.ToMesh("Beacon_Glass");
        }

        static Mesh HaloQuad()
        {
            var m = new Mesh { name = "Beacon_HaloQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            // The quad is billboarded in the shader; give it bounds that cover the halo in any orientation.
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        /// <summary>
        /// Places beacons along the route: dense through the volcanic region (marking the safe line past
        /// the lava), sparse waypoints elsewhere, alternating sides just off the trail edge.
        /// </summary>
        public static int Place(Transform parent, RouteSamples route, TerrainGrid g, WorldField f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            AssetUtil.DestroyChildren(parent);
            var d = f.D;
            int placed = 0, side = 1;
            float nextAt = 0f;
            float travelled = 0f;
            for (int i = 1; i < route.points.Length; i++)
            {
                travelled += Vector3.Distance(route.points[i], route.points[i - 1]);
                if (travelled < nextAt) continue;
                Vector3 p = route.points[i];
                float volc = f.Volcanic(p.x, p.z);
                float spacing = volc > 0.45f ? 34f : 150f;
                nextAt = travelled + spacing;
                Vector3 fwd = (route.points[(i + 6) % route.points.Length] - route.points[Mathf.Max(0, i - 6)]);
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 pos = p + right * side * (d.roadHalfWidth + 1.8f);
                // Keep beacons on the safe side: never between the road and nearby lava.
                if (g.LavaDistance(pos.x, pos.z) < 12f) pos = p - right * side * (d.roadHalfWidth + 1.8f);
                side = -side;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.name = $"RouteBeacon_{placed:00}";
                go.transform.position = new Vector3(pos.x, g.Height(pos.x, pos.z), pos.z);
                go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                placed++;
            }
            return placed;
        }
    }
}
