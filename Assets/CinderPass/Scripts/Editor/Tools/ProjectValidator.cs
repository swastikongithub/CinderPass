using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CinderPass.Cameras;
using CinderPass.Core;
using CinderPass.Diagnostics;
using CinderPass.Environment;
using CinderPass.Hazards;
using CinderPass.Route;
using CinderPass.UI;
using CinderPass.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Automated checks for the generated scene and project: hierarchy contract, broken references,
    /// gameplay wiring (vehicle, route, hazards, beacons), presentation state and performance budgets.
    /// Results go to the console (click an entry to select the offending object) and to
    /// Logs/CinderPass_Validation.txt.
    /// </summary>
    public static class ProjectValidator
    {
        public enum Severity { Error, Warning, Info }

        public readonly struct Issue
        {
            public readonly Severity severity;
            public readonly string category, message;
            public readonly Object context;
            public Issue(Severity s, string c, string m, Object ctx) { severity = s; category = c; message = m; context = ctx; }
        }

        const string ReportPath = "Logs/CinderPass_Validation.txt";

        static readonly string[] WorldGroups = { "Environment", "Gameplay", "VFX", "Lighting", "Cameras", "Systems", "UI" };

        /// <summary>Serialized references that are intentionally allowed to be empty.</summary>
        static readonly HashSet<string> OptionalReferences = new HashSet<string>
        {
            "CameraRig.initialMode", // GameFlow selects the first camera mode at runtime
        };

        // Budgets for the PC target (GTX 1060-class at 1080p, 60 fps).
        const int MaxShadowedLocalLights = 0;
        const int MaxRealtimeLights = 16;
        const int MaxParticleBudget = 20000;
        const int MaxTextureSize = 4096;
        const int MinBeacons = 8;

        [MenuItem("Cinder Pass/Validate Project", priority = 40)]
        public static void Menu() => WorldBuildPipeline.Run("Validate", () => Run().summary);

        public static (List<Issue> issues, string summary) Run()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Validate outside Play Mode.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != CinderPaths.Scene) scene = EditorSceneManager.OpenScene(CinderPaths.Scene, OpenSceneMode.Single);

            var issues = new List<Issue>();
            void Add(Severity s, string c, string m, Object ctx = null) => issues.Add(new Issue(s, c, m, ctx));
            var stats = new StringBuilder();

            CheckHierarchy(scene, Add);
            CheckReferences(scene, Add);
            CheckSingletons(Add);
            CheckVehicle(Add);
            CheckRoute(Add);
            CheckHazards(Add);
            CheckBeacons(Add);
            CheckTerrain(Add, stats);
            CheckPresentation(Add);
            CheckProject(Add);
            CheckPerformance(scene, Add, stats);
            CheckAssets(Add);

            int errors = issues.Count(i => i.severity == Severity.Error);
            int warnings = issues.Count(i => i.severity == Severity.Warning);
            var report = new StringBuilder();
            report.AppendLine($"Cinder Pass validation - {errors} error(s), {warnings} warning(s), {issues.Count - errors - warnings} note(s)");
            foreach (var i in issues.OrderBy(i => i.severity))
                report.AppendLine($"[{i.severity.ToString().ToUpperInvariant()}] {i.category}: {i.message}");
            report.AppendLine().AppendLine("Statistics").Append(stats);
            File.WriteAllText(ReportPath, report.ToString());

            foreach (var i in issues)
            {
                string line = $"[Validate] {i.category}: {i.message}";
                if (i.severity == Severity.Error) Debug.LogError(line, i.context);
                else if (i.severity == Severity.Warning) Debug.LogWarning(line, i.context);
            }
            Debug.Log($"[Validate] {errors} error(s), {warnings} warning(s). Full report: {ReportPath}\n{stats}");
            return (issues, report.ToString());
        }

        delegate void Reporter(Severity s, string category, string message, Object context = null);

        static List<T> All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include).ToList();

        // ------------------------------------------------------------------ hierarchy & references

        static void CheckHierarchy(UnityEngine.SceneManagement.Scene scene, Reporter add)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Length != 1 || roots[0].name != "GameWorld")
            {
                add(Severity.Error, "Hierarchy", $"Expected a single root 'GameWorld', found {roots.Length}: {string.Join(", ", roots.Select(r => r.name))}");
                return;
            }
            var world = roots[0].transform;
            var names = world.Cast<Transform>().Select(t => t.name).ToList();
            foreach (var g in WorldGroups)
                if (!names.Contains(g)) add(Severity.Error, "Hierarchy", $"GameWorld is missing the '{g}' group");
            foreach (var n in names.Where(n => !WorldGroups.Contains(n)))
                add(Severity.Warning, "Hierarchy", $"Unexpected GameWorld child '{n}'", world.Find(n));

            // One authoritative environment: every terrain lives under Environment/Terrain.
            foreach (var t in All<Terrain>())
                if (t.transform.parent == null || t.transform.parent.name != "Terrain" || t.transform.parent.parent != world.Find("Environment"))
                    add(Severity.Error, "Hierarchy", $"Terrain '{t.name}' is outside GameWorld/Environment/Terrain", t);
        }

        static void CheckReferences(UnityEngine.SceneManagement.Scene scene, Reporter add)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0) add(Severity.Error, "References", $"{missing} missing script(s) on '{Path(t)}'", t.gameObject);

                if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh == null)
                    add(Severity.Error, "References", $"MeshFilter without mesh on '{Path(t)}'", t.gameObject);
                if (t.TryGetComponent<Renderer>(out var r) && !(r is ParticleSystemRenderer psr && psr.renderMode == ParticleSystemRenderMode.None))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) add(Severity.Error, "References", $"Empty material slot {i} on '{Path(t)}'", t.gameObject);
                        else if (m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader")
                            add(Severity.Error, "Shaders", $"Material '{m.name}' on '{Path(t)}' uses an unsupported shader", m);
                    }
                }

                // Unassigned serialized references on project components.
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                {
                    if (mb == null || mb.GetType().Namespace == null || !mb.GetType().Namespace.StartsWith("CinderPass")) continue;
                    var so = new SerializedObject(mb);
                    var p = so.GetIterator();
                    while (p.NextVisible(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != null) continue;
                        if (p.propertyPath.Contains(".Array.data[")) continue; // arrays are checked by their owners
                        if (OptionalReferences.Contains($"{mb.GetType().Name}.{p.propertyPath}")) continue;
                        add(Severity.Error, "References", $"{mb.GetType().Name}.{p.propertyPath} is unassigned on '{Path(t)}'", mb);
                    }
                }
            }
        }

        static void CheckSingletons(Reporter add)
        {
            void One<T>(string what) where T : Object
            {
                int n = All<T>().Count;
                if (n != 1) add(Severity.Error, "Systems", $"Expected exactly one {what}, found {n}");
            }
            One<GameFlow>("GameFlow");
            One<InputHub>("InputHub");
            One<VehicleController>("vehicle");
            One<SplineAutopilot>("spline autopilot");
            One<RespawnSystem>("RespawnSystem");
            One<CameraRig>("CameraRig");
            One<Hud>("HUD");
            One<BeaconLightPool>("BeaconLightPool");
            One<DebugOverlay>("DebugOverlay");
            One<AudioListener>("AudioListener");
            One<Camera>("camera");
            int suns = All<Light>().Count(l => l.type == LightType.Directional);
            if (suns != 1) add(Severity.Error, "Systems", $"Expected exactly one directional light, found {suns}");
            foreach (var tr in All<TelemetryRecorder>())
                add(Severity.Error, "Presentation", "TelemetryRecorder is a development tool and must not be saved in the scene", tr);
        }

        // ------------------------------------------------------------------ gameplay

        static void CheckVehicle(Reporter add)
        {
            var v = All<VehicleController>().FirstOrDefault();
            if (v == null) return;
            var rb = v.GetComponent<Rigidbody>();
            var so = new SerializedObject(v);
            var config = so.FindProperty("config").objectReferenceValue as VehicleConfig;
            var wheels = v.GetComponentsInChildren<WheelCollider>(true);
            if (wheels.Length != 4) add(Severity.Error, "Vehicle", $"Expected 4 WheelColliders, found {wheels.Length}", v);
            var wheelProp = so.FindProperty("wheels");
            for (int i = 0; i < wheelProp.arraySize; i++)
            {
                var el = wheelProp.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("collider").objectReferenceValue == null || el.FindPropertyRelative("visual").objectReferenceValue == null)
                    add(Severity.Error, "Vehicle", $"Wheel {i} is missing its collider or visual", v);
            }
            if (rb == null) { add(Severity.Error, "Vehicle", "Vehicle has no Rigidbody", v); return; }
            if (config != null && !Mathf.Approximately(rb.mass, config.mass))
                add(Severity.Warning, "Vehicle", $"Rigidbody mass {rb.mass} differs from config {config.mass} (config wins at runtime)", v);
            foreach (var c in v.GetComponentsInChildren<Collider>(true))
                if (c.gameObject.layer != VehicleAssembler.VehicleLayer)
                    add(Severity.Error, "Vehicle", $"Collider '{c.name}' is not on the Vehicle layer (camera/respawn probes would hit the car)", c);
            if (v.GetComponent<VehicleHazardSensor>() == null) add(Severity.Error, "Hazards", "Vehicle has no VehicleHazardSensor", v);
            if (v.transform.position.y < -50f) add(Severity.Error, "Vehicle", "Vehicle starts below the world", v);
        }

        static void CheckRoute(Reporter add)
        {
            var track = All<RouteTrack>().FirstOrDefault();
            if (track == null) { add(Severity.Error, "Route", "No RouteTrack in the scene"); return; }
            var container = track.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null || container.Spline.Count < 4)
            {
                add(Severity.Error, "Route", "Route spline is missing or has fewer than 4 knots", track);
                return;
            }
            if (track.Length < 500f) add(Severity.Warning, "Route", $"Route is only {track.Length:0} m long", track);

            // The intro must hand over on the road, not beyond it; the vehicle must start on the spline.
            var ap = All<SplineAutopilot>().FirstOrDefault();
            var v = All<VehicleController>().FirstOrDefault();
            if (v != null)
            {
                float off = Vector3.Distance(Flat(v.transform.position), Flat(track.PositionAt(0f)));
                if (off > 3f) add(Severity.Warning, "Route", $"Vehicle starts {off:0.0} m from the route start (the intro places it there at runtime)", v);
            }
            if (ap != null)
            {
                var aso = new SerializedObject(ap);
                var end = aso.FindProperty("handoverDistance");
                if (end != null && end.floatValue > track.Length) add(Severity.Error, "Route", "Autopilot hand-over distance is beyond the end of the route", ap);
            }
        }

        static void CheckHazards(Reporter add)
        {
            var zones = All<HazardZone>();
            if (zones.Count == 0) add(Severity.Error, "Hazards", "No hazard zones - lava would be harmless");
            foreach (var z in zones)
            {
                var cols = z.GetComponents<Collider>();
                if (cols.Length == 0) add(Severity.Error, "Hazards", $"Hazard '{Path(z.transform)}' has no collider", z);
                foreach (var c in cols)
                    if (!c.isTrigger) add(Severity.Error, "Hazards", $"Hazard collider on '{Path(z.transform)}' is not a trigger", c);
            }
            // Lava surfaces must use the animated lava shader.
            int lavaSurfaces = 0;
            foreach (var r in All<MeshRenderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || !m.shader.name.Contains("Lava")) continue;
                lavaSurfaces++;
            }
            if (lavaSurfaces == 0) add(Severity.Error, "Hazards", "No renderer uses the animated lava shader");
            if (All<VolcanicActivity>().Count == 0) add(Severity.Warning, "VFX", "No VolcanicActivity (eruptions) in the scene");
        }

        static void CheckBeacons(Reporter add)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BeaconSystem.PrefabPath);
            var style = AssetDatabase.LoadAssetAtPath<BeaconStyle>(BeaconSystem.StylePath);
            if (prefab == null) { add(Severity.Error, "Beacons", $"Beacon prefab missing at {BeaconSystem.PrefabPath}"); return; }
            if (style == null) add(Severity.Error, "Beacons", $"BeaconStyle missing at {BeaconSystem.StylePath}");

            var beacons = All<GlowBeacon>();
            if (beacons.Count < MinBeacons) add(Severity.Warning, "Beacons", $"Only {beacons.Count} beacons placed (expected at least {MinBeacons})");
            foreach (var b in beacons)
            {
                // Central configuration means every beacon is an unmodified prefab instance sharing the one style.
                if (PrefabUtility.GetCorrespondingObjectFromSource(b.gameObject) != prefab)
                {
                    add(Severity.Error, "Beacons", $"'{Path(b.transform)}' is not an instance of the beacon prefab", b);
                    continue;
                }
                if (b.Style != style) add(Severity.Error, "Beacons", $"'{Path(b.transform)}' does not use the shared BeaconStyle", b);
                foreach (var mod in PrefabUtility.GetPropertyModifications(b.gameObject) ?? new PropertyModification[0])
                {
                    if (mod.target is Transform && mod.target == prefab.transform) continue; // placement
                    if (mod.propertyPath == "m_Name") continue;
                    add(Severity.Error, "Beacons", $"'{Path(b.transform)}' overrides {mod.target?.GetType().Name}.{mod.propertyPath}; beacons must be configured centrally", b);
                }
                if (PrefabUtility.GetAddedComponents(b.gameObject).Count > 0)
                    add(Severity.Error, "Beacons", $"'{Path(b.transform)}' has components added on the instance", b);
            }
            int beaconLights = beacons.Sum(b => b.GetComponentsInChildren<Light>(true).Length);
            if (beaconLights > 0) add(Severity.Warning, "Performance", $"{beaconLights} lights baked into beacon instances; beacons should borrow pooled lights");
        }

        static void CheckTerrain(Reporter add, StringBuilder stats)
        {
            var terrains = All<Terrain>();
            if (terrains.Count == 0) { add(Severity.Error, "Terrain", "No terrain"); return; }
            int trees = 0;
            foreach (var t in terrains)
            {
                var td = t.terrainData;
                if (td == null) { add(Severity.Error, "Terrain", $"'{t.name}' has no TerrainData", t); continue; }
                if (!AssetDatabase.Contains(td)) add(Severity.Error, "Terrain", $"TerrainData of '{t.name}' is not saved as an asset", t);
                if (td.terrainLayers == null || td.terrainLayers.Length == 0 || td.terrainLayers.Any(l => l == null))
                    add(Severity.Error, "Terrain", $"'{t.name}' has missing terrain layers", t);
                else
                {
                    // A blank splat map (all weight on layer 0 everywhere) means painting was lost on save.
                    var a = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
                    int res = td.alphamapResolution, step = Mathf.Max(1, res / 64);
                    bool varied = false;
                    for (int y = 0; y < res && !varied; y += step)
                    for (int x = 0; x < res && !varied; x += step)
                        varied = a[y, x, 0] < 0.99f;
                    if (!varied) add(Severity.Error, "Terrain", $"'{t.name}' splat map is blank (only the first layer)", t);
                }
                if (!t.drawInstanced) add(Severity.Warning, "Performance", $"'{t.name}' does not use instanced terrain drawing", t);
                if (t.materialTemplate == null) add(Severity.Error, "Terrain", $"'{t.name}' has no terrain material", t);
                trees += td.treeInstanceCount;
                stats.AppendLine($"  Terrain {t.name}: {td.size.x:0}x{td.size.z:0} m, heightmap {td.heightmapResolution}, splat {td.alphamapResolution} x{td.alphamapLayers}, trees {td.treeInstanceCount}, detail {td.detailResolution}");
            }
            if (trees == 0) add(Severity.Warning, "Terrain", "No trees planted");
        }

        // ------------------------------------------------------------------ presentation & project

        static void CheckPresentation(Reporter add)
        {
            foreach (var d in All<DebugOverlay>())
                if (new SerializedObject(d).FindProperty("visible").boolValue)
                    add(Severity.Error, "Presentation", "Debug overlay is visible at start; it must be hidden (F1 toggles it)", d);
            foreach (var c in All<Collider>())
                if (c is MeshCollider mc && mc.sharedMesh == null)
                    add(Severity.Error, "References", $"MeshCollider without mesh on '{Path(c.transform)}'", c);
            foreach (var go in All<GameObject>())
                if (go.CompareTag("EditorOnly") && go.activeSelf)
                    add(Severity.Info, "Presentation", $"EditorOnly object '{Path(go.transform)}' is present (stripped from builds)", go);
        }

        static void CheckProject(Reporter add)
        {
            if (!Mathf.Approximately(Time.fixedDeltaTime, 1f / 60f)) add(Severity.Warning, "Project", $"Fixed timestep is {Time.fixedDeltaTime:0.0000}s (vehicle tuned for 1/60)");
            if (LayerMask.LayerToName(VehicleAssembler.VehicleLayer) != "Vehicle") add(Severity.Error, "Project", "Layer 8 is not named 'Vehicle'");
            if (PlayerSettings.colorSpace != ColorSpace.Linear) add(Severity.Error, "Project", "Colour space is not Linear");
            if (!PlayerSettings.runInBackground) add(Severity.Warning, "Project", "Run In Background is off (the demo pauses when the window loses focus)");
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != CinderPaths.Scene || !scenes[0].enabled)
                add(Severity.Error, "Project", "The Cinder Pass scene is not the first enabled scene in Build Settings");
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) add(Severity.Error, "Project", "No render pipeline asset assigned");
        }

        static void CheckPerformance(UnityEngine.SceneManagement.Scene scene, Reporter add, StringBuilder stats)
        {
            var lights = All<Light>().Where(l => l.enabled && l.gameObject.activeInHierarchy).ToList();
            int realtime = lights.Count(l => l.lightmapBakeType != LightmapBakeType.Baked);
            int shadowedLocal = lights.Count(l => l.type != LightType.Directional && l.shadows != LightShadows.None);
            if (shadowedLocal > MaxShadowedLocalLights) add(Severity.Warning, "Performance", $"{shadowedLocal} local lights cast shadows (budget {MaxShadowedLocalLights})");
            if (realtime > MaxRealtimeLights) add(Severity.Warning, "Performance", $"{realtime} real-time lights in the scene (budget {MaxRealtimeLights})");

            int particles = 0, systems = 0;
            foreach (var ps in All<ParticleSystem>()) { systems++; particles += ps.main.maxParticles; }
            if (particles > MaxParticleBudget) add(Severity.Warning, "Performance", $"Particle capacity {particles} exceeds budget {MaxParticleBudget}");

            long tris = 0; int renderers = 0, lodGroups = 0, shadowCasters = 0, meshLod = 0;
            var counted = new HashSet<Transform>();
            foreach (var lg in All<LODGroup>())
            {
                lodGroups++;
                var lods = lg.GetLODs();
                if (lods.Length == 0) continue;
                foreach (var r in lods[0].renderers) if (r != null) tris += Tris(r);
                foreach (var lod in lods) foreach (var r in lod.renderers) if (r != null) counted.Add(r.transform);
            }
            var unLodded = new HashSet<Mesh>();
            foreach (var r in All<MeshRenderer>())
            {
                renderers++;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowCasters++;
                if (counted.Contains(r.transform)) continue;
                long t = Tris(r);
                tris += t;
                var mesh = r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (mesh != null && mesh.lodCount > 1) meshLod++;
                else if (mesh != null && t > 5000) unLodded.Add(mesh);
            }
            foreach (var m in unLodded)
                add(Severity.Warning, "Performance", $"Mesh '{m.name}' has {MeshTris(m)} triangles and no LODs", m);
            foreach (var mc in All<MeshCollider>())
            {
                long colliderTris = mc.sharedMesh != null ? MeshTris(mc.sharedMesh) : 0;
                if (!mc.convex && colliderTris > 20000)
                    add(Severity.Warning, "Performance", $"Heavy concave MeshCollider on '{Path(mc.transform)}' ({colliderTris} tris)", mc);
            }

            stats.AppendLine($"  Mesh renderers: {renderers} ({shadowCasters} shadow casters), LOD groups: {lodGroups}, Mesh-LOD renderers: {meshLod}");
            stats.AppendLine($"  Worst-case LOD0 triangles (scene objects, excl. terrain trees, before culling/LOD): {tris.ToString("N0", CultureInfo.InvariantCulture)}");
            stats.AppendLine($"  Lights: {lights.Count} ({realtime} real-time, {shadowedLocal} shadowed local)");
            stats.AppendLine($"  Particle systems: {systems}, total capacity {particles.ToString("N0", CultureInfo.InvariantCulture)}");
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            stats.AppendLine($"  Fixed timestep {Time.fixedDeltaTime * 1000f:0.0} ms" + (urp != null ? $", shadows {urp.shadowDistance:0} m / {urp.shadowCascadeCount} cascades / {urp.mainLightShadowmapResolution}px" : ""));
        }

        static void CheckAssets(Reporter add)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CinderPaths.Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                if (ti.maxTextureSize > MaxTextureSize) add(Severity.Warning, "Assets", $"{path} imports above {MaxTextureSize}px ({ti.maxTextureSize})");
                if (!ti.mipmapEnabled && ti.textureType == TextureImporterType.Default && !path.Contains("/UI/"))
                    add(Severity.Info, "Assets", $"{path} has no mipmaps");
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { CinderPaths.Audio }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null && clip.length > 20f && clip.loadType == AudioClipLoadType.DecompressOnLoad)
                    add(Severity.Info, "Assets", $"{path} is {clip.length:0}s and decompressed on load (consider streaming)");
            }
        }

        // ------------------------------------------------------------------ helpers

        static long Tris(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? MeshTris(mf.sharedMesh) : 0;
        }

        static long MeshTris(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
