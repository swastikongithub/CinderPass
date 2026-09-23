using System.Collections.Generic;
using CinderPass.Audio;
using CinderPass.Hazards;
using CinderPass.Vehicle;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Assembles the player vehicle from the Prometeo "Prometheus" car model (bundled asset pack), set up as a
    /// lifted rally conversion: larger tyres and extra ride height for off-road clearance. Only the pack's
    /// meshes and materials are reused - physics, input, autopilot, audio and FX are Cinder Pass systems.
    /// Scene-specific references (route, input hub, terrain surface map) are wired by the SceneAssembler.
    /// </summary>
    public static class VehicleAssembler
    {
        public const string PrefabPath = CinderPaths.Prefabs + "/Vehicle/Vehicle_Prometheus.prefab";
        public const string ConfigPath = CinderPaths.Data + "/VehicleConfig_Prometheus.asset";
        public const int VehicleLayer = 8;

        const string PackRoot = "Assets/PROMETEO - Car Controller";
        const string BodyModel = PackRoot + "/Meshes/Prometheus.fbx";
        const string ColliderModel = PackRoot + "/Meshes/Prometheus - Collider.fbx";

        /// <summary>Tyre scale over stock (bigger all-terrain tyres).</summary>
        const float TyreScale = 1.12f;
        /// <summary>How far the wheel centres hang below their stock position (suspension lift).</summary>
        const float LiftDrop = 0.085f;
        /// <summary>Suspension offset from wheel centre to collider anchor (travel above rest plus static sag).</summary>
        const float AnchorOffset = 0.265f;

        public static GameObject Build()
        {
            var config = AssetUtil.LoadOrCreate<VehicleConfig>(ConfigPath);
            Mesh Find(string path, string name)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Mesh m && m.name == name) return m;
                throw new System.InvalidOperationException($"Mesh '{name}' not found in {path}");
            }
            var bodyMesh = Find(BodyModel, "Body");
            var colliderMesh = Find(ColliderModel, "Collider");
            var paint = AssetDatabase.LoadAssetAtPath<Material>(PackRoot + "/Materials/PCC_Col_Mat.mat");
            var lights = AssetDatabase.LoadAssetAtPath<Material>(PackRoot + "/Materials/PCC_Light_Mat.mat");

            var root = new GameObject("Vehicle_Prometheus");
            var visual = AssetUtil.Child(root.transform, "Visual").transform;
            var physics = AssetUtil.Child(root.transform, "Physics").transform;

            var bodyGo = AssetUtil.Child(visual, "Body");
            bodyGo.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            bodyGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { paint, lights };

            // --- Wheels: stock positions from the model, lifted and fitted with larger tyres.
            var defs = new[]
            {
                ("FL", "FrontLeftWheel", new Vector3(-0.87f, 0.36f, 1.54f), true, Axle.Front),
                ("FR", "FrontRightWheel", new Vector3(0.87f, 0.36f, 1.54f), false, Axle.Front),
                ("RL", "RearLeftWheel", new Vector3(-0.89f, 0.38f, -1.56f), true, Axle.Rear),
                ("RR", "RearRightWheel", new Vector3(0.89f, 0.38f, -1.56f), false, Axle.Rear),
            };
            var colliders = AssetUtil.Child(physics, "WheelColliders").transform;
            var wheelVisuals = AssetUtil.Child(visual, "Wheels").transform;
            var wheelData = new List<(WheelCollider wc, Transform vis, bool left, Axle axle)>();
            foreach (var (id, meshName, stock, left, axle) in defs)
            {
                var mesh = Find(BodyModel, meshName);
                float radius = mesh.bounds.extents.y * TyreScale;
                Vector3 centre = stock + Vector3.down * LiftDrop;

                var wcGo = AssetUtil.Child(colliders, $"WC_{id}");
                wcGo.transform.localPosition = centre + Vector3.up * AnchorOffset;
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = radius;

                var vis = AssetUtil.Child(wheelVisuals, $"Wheel_{id}");
                vis.transform.localPosition = centre;
                vis.transform.localScale = Vector3.one * TyreScale;
                vis.AddComponent<MeshFilter>().sharedMesh = mesh;
                vis.AddComponent<MeshRenderer>().sharedMaterial = paint;
                wheelData.Add((wc, vis.transform, left, axle));
            }

            // --- Body collision: the pack's low-poly hull (convex) with a low-friction material.
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{CinderPaths.Data}/PM_VehicleBody.asset");
            if (pm == null)
            {
                pm = new PhysicsMaterial("PM_VehicleBody") { dynamicFriction = 0.25f, staticFriction = 0.3f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
                AssetUtil.EnsureFolder(CinderPaths.Data);
                AssetDatabase.CreateAsset(pm, $"{CinderPaths.Data}/PM_VehicleBody.asset");
            }
            var hull = AssetUtil.Child(physics, "BodyCollider");
            var mc = hull.AddComponent<MeshCollider>();
            mc.sharedMesh = colliderMesh;
            mc.convex = true;
            mc.sharedMaterial = pm;

            var com = AssetUtil.Child(root.transform, "CenterOfMass");
            com.transform.localPosition = new Vector3(0f, 0.5f, -0.05f);
            var camTarget = AssetUtil.Child(root.transform, "CameraTarget");
            camTarget.transform.localPosition = new Vector3(0f, 1.05f, 0.2f);

            // --- Audio.
            var audioRoot = AssetUtil.Child(root.transform, "Audio").transform;
            var engine = AddSource(audioRoot, "Engine", "SFX_Engine_Loop", true, 0.55f, 0.6f);
            var tyres = AddSource(audioRoot, "Tyres", "SFX_TyreGravel_Loop", true, 0f, 0.5f);
            var impacts = AddSource(audioRoot, "Impacts", null, false, 1f, 0.6f);

            // --- FX.
            var dust = VfxFactory.WheelDust(root.transform);

            // --- Components.
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = config.mass;
            var controller = root.AddComponent<VehicleController>();
            root.AddComponent<VehicleHazardSensor>();
            root.AddComponent<PlayerVehicleInput>();
            root.AddComponent<SplineAutopilot>();
            var audio = root.AddComponent<VehicleAudio>();
            var wheelDust = root.AddComponent<WheelDust>();

            Wiring.Set(controller, "config", config);
            Wiring.Set(controller, "centerOfMass", com.transform);
            var so = new SerializedObject(controller);
            var wheelsProp = so.FindProperty("wheels");
            wheelsProp.arraySize = wheelData.Count;
            for (int i = 0; i < wheelData.Count; i++)
            {
                var el = wheelsProp.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("collider").objectReferenceValue = wheelData[i].wc;
                el.FindPropertyRelative("visual").objectReferenceValue = wheelData[i].vis;
                el.FindPropertyRelative("axle").enumValueIndex = (int)wheelData[i].axle;
                el.FindPropertyRelative("isLeft").boolValue = wheelData[i].left;
                el.FindPropertyRelative("driven").boolValue = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            Wiring.Set(audio, "vehicle", controller);
            Wiring.Set(audio, "engine", engine);
            Wiring.Set(audio, "tyres", tyres);
            Wiring.Set(audio, "impacts", impacts);
            Wiring.Set(audio, "impactClip", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath("SFX_Impact")));
            Wiring.Set(wheelDust, "vehicle", controller);
            Wiring.Set(wheelDust, "dust", dust);

            SetLayerRecursive(root, VehicleLayer);
            dust.gameObject.layer = 0; // particles render normally; the layer only matters for physics queries
            return VegetationBuilder.SavePrefab(root, PrefabPath);
        }

        static AudioSource AddSource(Transform parent, string name, string clip, bool loop, float volume, float spatial)
        {
            var go = AssetUtil.Child(parent, name);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath(clip)) : null;
            src.loop = loop;
            src.playOnAwake = loop;
            src.volume = volume;
            src.spatialBlend = spatial;
            src.dopplerLevel = 0f;
            src.minDistance = 6f;
            src.maxDistance = 120f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            return src;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
