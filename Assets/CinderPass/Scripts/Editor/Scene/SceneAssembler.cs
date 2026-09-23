using System.Collections.Generic;
using CinderPass.Audio;
using CinderPass.Cameras;
using CinderPass.Core;
using CinderPass.Diagnostics;
using CinderPass.Environment;
using CinderPass.Hazards;
using CinderPass.Route;
using CinderPass.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Builds the single authoritative gameplay scene from the WorldDesign: clears the scene, generates
    /// terrain, dressing, the volcanic region, route, vehicle, cameras, lighting, systems and HUD, and wires
    /// every reference explicitly. Re-running replaces everything (there is never a second environment).
    /// </summary>
    public static class SceneAssembler
    {
        public const string DesignPath = CinderPaths.Data + "/WorldDesign.asset";
        public const string SurfacesPath = CinderPaths.Data + "/SurfaceLibrary.asset";
        public const int IgnoreRaycastLayer = 2, UILayer = 5;

        public static string Build()
        {
            var log = new System.Text.StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            void Step(string msg) { log.AppendLine($"[{sw.Elapsed.TotalSeconds:0.0}s] {msg}"); Debug.Log($"[SceneAssembler] [{sw.Elapsed.TotalSeconds:0.0}s] {msg}"); }

            var design = AssetUtil.LoadOrCreate<WorldDesign>(DesignPath);
            var scene = System.IO.File.Exists(CinderPaths.Scene)
                ? EditorSceneManager.OpenScene(CinderPaths.Scene, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var r in scene.GetRootGameObjects()) Object.DestroyImmediate(r);

            var world = new GameObject("GameWorld").transform;
            var env = AssetUtil.Child(world, "Environment").transform;
            var gameplay = AssetUtil.Child(world, "Gameplay").transform;
            var vfx = AssetUtil.Child(world, "VFX").transform;
            var lighting = AssetUtil.Child(world, "Lighting").transform;
            var cameras = AssetUtil.Child(world, "Cameras").transform;
            var systems = AssetUtil.Child(world, "Systems").transform;
            var ui = AssetUtil.Child(world, "UI").transform;

            // --- Route first: it is gameplay infrastructure and drives everything else.
            var field = new WorldField(design);
            var container = RouteBuilder.BuildSpline(design, gameplay);
            var route = RouteBuilder.Sample(container, design);
            Step($"Route: {route.length:0} m, {design.route.Length} knots");

            var grid = TerrainSculptor.Sculpt(field, route, out var lava);
            Step("Terrain sculpted (macro, erosion, road, lava)");

            var layers = MaterialLibrary.BuildTerrainLayers();
            var terrainMat = MaterialLibrary.TerrainMaterial();
            var terrainRoot = AssetUtil.Child(env, "Terrain").transform;
            var terrain = TerrainBuilder.CreateMain(grid, design, layers, terrainMat, terrainRoot);
            TerrainBuilder.PaintMain(terrain.terrainData, grid, field);
            Persist(terrain.terrainData);
            Step($"Main terrain painted ({DescribeSplat(terrain.terrainData, route.points[0])} at the start)");

            var veg = LoadVegetation();
            TerrainBuilder.ScatterGrass(terrain.terrainData, grid, field, veg);
            int trees = TerrainBuilder.PlantTrees(terrain.terrainData, grid, field, veg, lava);
            Persist(terrain.terrainData);
            Step($"Vegetation: {trees} trees, grass details scattered");

            var backdrop = TerrainBuilder.CreateBackdrop(field, layers, terrainMat, terrainRoot, veg);
            foreach (var b in backdrop) Persist(b.terrainData);
            Step($"Backdrop: {backdrop.Count} tiles");

            var allTerrains = new List<Terrain> { terrain };
            allTerrains.AddRange(backdrop);
            terrainRoot.gameObject.AddComponent<TerrainTextureArrays>().Configure(layers, MaterialLibrary.TerrainHeightBiases(), allTerrains.ToArray(),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.LavaNoisePath));

            var surfaces = BuildSurfaceLibrary(layers);
            var surfaceMap = terrain.gameObject.AddComponent<TerrainSurfaceMap>();
            Wiring.Set(surfaceMap, "library", surfaces);
            RouteBuilder.ConformKnotsToGround(container, terrain);

            int rocks = GeologyDresser.Dress(AssetUtil.Child(env, "Geology").transform, grid, field, route, lava, LoadRocks());
            int props = PropDresser.Dress(AssetUtil.Child(env, "Props").transform, grid, field, route);
            Step($"Dressing: {rocks} geology/forest-floor instances, {props} props");

            var volc = VolcanoBuilder.Build(env, vfx, grid, field, lava);
            Step($"Volcanic region: {lava.pools.Count} pools, river, crater, {volc.hazardZones} hazard zones");

            int beacons = BeaconSystem.Place(AssetUtil.Child(gameplay, "Beacons").transform, route, grid, field);
            Step($"Beacons: {beacons}");

            LightingSetup.Build(lighting);
            Vector2 basinMin = Vector2.Min(design.basin - Vector2.one * design.basinRadius, design.volcano - Vector2.one * design.volcanoRadius);
            Vector2 basinMax = Vector2.Max(design.basin + Vector2.one * design.basinRadius, design.volcano + Vector2.one * design.volcanoRadius);
            Vector2 basinMid = (basinMin + basinMax) * 0.5f, basinSize = basinMax - basinMin;
            LightingSetup.BuildBasinAtmosphere(lighting, new Vector3(basinMid.x, 150f, basinMid.y), new Vector3(basinSize.x, 500f, basinSize.y));
            Step("Lighting & post-processing");

            // --- Vehicle.
            var vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAssembler.PrefabPath);
            var vehicleGo = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab, gameplay);
            vehicleGo.name = "Vehicle";
            var track = container.GetComponent<RouteTrack>();
            track.SampleFrame(0f, out var startPos, out var startFwd, out _);
            vehicleGo.transform.SetPositionAndRotation(startPos + Vector3.up * 0.3f, Quaternion.LookRotation(startFwd, Vector3.up));
            var vehicle = vehicleGo.GetComponent<VehicleController>();
            var autopilot = vehicleGo.GetComponent<SplineAutopilot>();
            var playerInput = vehicleGo.GetComponent<PlayerVehicleInput>();
            var sensor = vehicleGo.GetComponent<VehicleHazardSensor>();
            var camTarget = vehicleGo.transform.Find("CameraTarget");
            Wiring.Set(vehicle, "surfaceMap", surfaceMap);

            // --- Systems.
            var input = AssetUtil.Child(systems, "InputHub").AddComponent<InputHub>();
            Wiring.Set(playerInput, "input", input);
            float handover = route.knotDistances[Mathf.Clamp(design.introHandoverKnot, 1, design.route.Length - 1)];
            var k = route.knotDistances;
            autopilot.Configure(vehicle, track, 0f, handover, 13f, new[]
            {
                new SplineAutopilot.SpeedZone { label = "Forest", fromDistance = 0f, toDistance = k[4], speed = 12.5f },
                new SplineAutopilot.SpeedZone { label = "Highlands", fromDistance = k[4] + 20f, toDistance = k[7] - 30f, speed = 15f },
                new SplineAutopilot.SpeedZone { label = "Canyon", fromDistance = k[7], toDistance = k[9], speed = 10.5f },
                new SplineAutopilot.SpeedZone { label = "Overlook", fromDistance = k[9] + 10f, toDistance = handover + 40f, speed = 6.5f },
            });
            EditorUtility.SetDirty(autopilot);

            var respawn = AssetUtil.Child(systems, "RespawnSystem").AddComponent<RespawnSystem>();
            Wiring.Set(respawn, "vehicle", vehicle);
            Wiring.Set(respawn, "route", track);
            int groundMask = ~((1 << VehicleAssembler.VehicleLayer) | (1 << IgnoreRaycastLayer) | (1 << UILayer));
            Wiring.Set(respawn, "groundMask", groundMask);

            var windGo = AssetUtil.Child(systems, "FoliageWind");
            windGo.transform.rotation = Quaternion.LookRotation(new Vector3(1f, 0f, 0.35f));
            windGo.AddComponent<FoliageWind>();

            // --- Cameras.
            var camGo = AssetUtil.Child(cameras, "MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 3200f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camData.dithering = true;
            camGo.AddComponent<AudioListener>();
            var wind = camGo.AddComponent<AudioSource>();
            wind.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath("AMB_Wind_Loop"));
            wind.loop = true;
            wind.playOnAwake = true;
            wind.volume = 0.22f;
            wind.spatialBlend = 0f;
            camGo.transform.SetPositionAndRotation(startPos + new Vector3(8f, 4f, -8f), Quaternion.LookRotation(startPos - (startPos + new Vector3(8f, 4f, -8f))));

            var rig = camGo.AddComponent<CameraRig>();
            var chase = camGo.AddComponent<ChaseCamera>();
            var intro = camGo.AddComponent<IntroCameraDirector>();
            var overview = camGo.AddComponent<OverviewCamera>();
            int camMask = groundMask;
            Wiring.Set(chase, "target", camTarget);
            Wiring.Set(chase, "targetBody", vehicle.GetComponent<Rigidbody>());
            Wiring.Set(chase, "input", input);
            Wiring.Set(chase, "collisionMask", camMask);
            Wiring.Set(intro, "autopilot", autopilot);
            Wiring.Set(intro, "target", camTarget);
            Wiring.Set(intro, "targetBody", vehicle.GetComponent<Rigidbody>());
            Wiring.Set(intro, "groundMask", camMask);
            intro.SetShots(BuildIntroShots(track, route, grid));
            EditorUtility.SetDirty(intro);
            overview.SetViewpoints(BuildViewpoints(AssetUtil.Child(cameras, "Viewpoints").transform, design, grid, track, route));
            EditorUtility.SetDirty(overview);

            var pool = AssetUtil.Child(systems, "BeaconLightPool").AddComponent<BeaconLightPool>();
            Wiring.Set(pool, "viewer", camGo.transform);
            Wiring.Set(volc.activity, "cameraRig", rig);
            Wiring.Set(volc.activity, "viewer", camGo.transform);
            Wiring.Set(volc.ashFall, "follow", camGo.transform);

            // --- UI & flow.
            var hud = HudBuilder.Build(ui, vehicle);
            var flow = AssetUtil.Child(systems, "GameFlow").AddComponent<GameFlow>();
            Wiring.Set(flow, "input", input);
            Wiring.Set(flow, "vehicle", vehicle);
            Wiring.Set(flow, "autopilot", autopilot);
            Wiring.Set(flow, "playerInput", playerInput);
            Wiring.Set(flow, "hazardSensor", sensor);
            Wiring.Set(flow, "respawn", respawn);
            Wiring.Set(flow, "hazardEffect", volc.hazardEffect);
            Wiring.Set(flow, "cameraRig", rig);
            Wiring.Set(flow, "introCamera", intro);
            Wiring.Set(flow, "chaseCamera", chase);
            Wiring.Set(flow, "overviewCamera", overview);
            Wiring.Set(flow, "hud", hud);

            var debug = AssetUtil.Child(systems, "DebugOverlay").AddComponent<DebugOverlay>();
            Wiring.Set(debug, "input", input);
            Wiring.Set(debug, "flow", flow);
            Wiring.Set(debug, "vehicle", vehicle);
            Wiring.Set(debug, "autopilot", autopilot);
            Wiring.Set(debug, "route", track);

            Persist(terrain.terrainData);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, CinderPaths.Scene);
            Step($"Scene saved (splat check: {DescribeSplat(terrain.terrainData, route.points[0])})");
            return log.ToString();
        }

        /// <summary>
        /// TerrainData edits (splat/detail/tree sub-assets) live in memory until saved; any later asset import
        /// can reload the file and silently discard them, so terrain data is saved after every stage.
        /// </summary>
        static void Persist(TerrainData td)
        {
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssetIfDirty(td);
        }

        static string DescribeSplat(TerrainData td, Vector3 world)
        {
            int ax = Mathf.Clamp((int)(world.x / td.size.x * td.alphamapResolution), 0, td.alphamapResolution - 1);
            int az = Mathf.Clamp((int)(world.z / td.size.z * td.alphamapResolution), 0, td.alphamapResolution - 1);
            var m = td.GetAlphamaps(ax, az, 1, 1);
            int best = 0;
            for (int l = 1; l < td.alphamapLayers; l++) if (m[0, 0, l] > m[0, 0, best]) best = l;
            return $"{td.terrainLayers[best].name} {m[0, 0, best]:0.00}";
        }

        static TerrainBuilder.VegetationSet LoadVegetation()
        {
            GameObject V(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Vegetation/{n}.prefab");
            return new TerrainBuilder.VegetationSet
            {
                firs = new[] { V("Fir_A"), V("Fir_B"), V("Fir_C") },
                snags = new[] { V("Snag_A"), V("Snag_B") },
                grassLush = V("Grass_Lush"),
                grassDry = V("Grass_Dry"),
            };
        }

        static GeologyDresser.RockSet LoadRocks()
        {
            GameObject G(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Geology/{n}.prefab");
            GameObject P(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Props/{n}.prefab");
            return new GeologyDresser.RockSet
            {
                cliffs = new[] { G("Cliff_A"), G("Cliff_B"), G("Cliff_C") },
                boulders = new[] { G("Boulder_A"), G("Boulder_B"), G("Boulder_C"), G("Boulder_D") },
                basalt = new[] { G("Basalt_A"), G("Basalt_B"), G("Basalt_C"), G("Basalt_D") },
                log = P("Log_Fallen"),
                stump = P("Stump"),
                fern = AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Vegetation/Fern.prefab"),
            };
        }

        static SurfaceLibrary BuildSurfaceLibrary(TerrainLayer[] layers)
        {
            var lib = AssetUtil.LoadOrCreate<SurfaceLibrary>(SurfacesPath);
            SurfaceInfo S(string name, int layer, float grip, float roll, Color dust, float amount, float noise) =>
                new SurfaceInfo { name = name, terrainLayer = layers[layer], grip = grip, rollingResistance = roll, dustColor = dust, dustAmount = amount, rollNoise = noise };
            lib.SetSurfaces(
                new SurfaceInfo { name = "Rock / object", grip = 1.05f, dustColor = new Color(0.4f, 0.38f, 0.35f, 0.4f), dustAmount = 0.15f, rollNoise = 0.8f },
                new[]
                {
                    S("Grass", MaterialLibrary.LayerGrass, 0.92f, 60f, new Color(0.46f, 0.43f, 0.34f, 0.45f), 0.45f, 0.45f),
                    S("Forest floor", MaterialLibrary.LayerForest, 0.9f, 80f, new Color(0.36f, 0.29f, 0.21f, 0.5f), 0.6f, 0.5f),
                    S("Gravel trail", MaterialLibrary.LayerTrail, 1f, 20f, new Color(0.64f, 0.59f, 0.5f, 0.55f), 1f, 0.85f),
                    S("Dirt", MaterialLibrary.LayerDirt, 0.95f, 40f, new Color(0.52f, 0.44f, 0.34f, 0.55f), 1.2f, 0.7f),
                    S("Rock", MaterialLibrary.LayerRock, 1.05f, 0f, new Color(0.45f, 0.43f, 0.4f, 0.4f), 0.25f, 0.9f),
                    S("Scorched earth", MaterialLibrary.LayerScorched, 0.9f, 60f, new Color(0.26f, 0.24f, 0.22f, 0.55f), 1.3f, 0.7f),
                    S("Volcanic ash", MaterialLibrary.LayerAsh, 0.82f, 120f, new Color(0.44f, 0.43f, 0.42f, 0.62f), 1.8f, 0.55f),
                    S("Basalt", MaterialLibrary.LayerBasalt, 1f, 10f, new Color(0.22f, 0.21f, 0.21f, 0.45f), 0.6f, 0.95f),
                });
            EditorUtility.SetDirty(lib);
            return lib;
        }

        // ---------------------------------------------------------------- presentation

        static IntroCameraDirector.Shot[] BuildIntroShots(RouteTrack track, RouteSamples route, TerrainGrid g)
        {
            var k = route.knotDistances;
            Vector3 Anchor(float distance, float side, float up)
            {
                track.SampleFrame(distance, out var p, out _, out var right);
                Vector3 a = p + right * side;
                a.y = Mathf.Max(a.y, g.Height(a.x, a.z)) + up;
                return a;
            }
            return new[]
            {
                new IntroCameraDirector.Shot { label = "Basecamp orbit", startTravelled = 0f, type = IntroCameraDirector.ShotType.Orbit, offset = new Vector3(0f, 2.4f, 8.5f), orbitSpeed = -9f, lookHeight = 1.1f, fieldOfView = 48f, blendIn = 0f },
                new IntroCameraDirector.Shot { label = "Forest - leading", startTravelled = 45f, type = IntroCameraDirector.ShotType.Tracking, offset = new Vector3(3.2f, 1.4f, 9f), lookHeight = 1f, fieldOfView = 52f, blendIn = 1.6f },
                new IntroCameraDirector.Shot { label = "Forest climb - chase", startTravelled = k[3] - 20f, type = IntroCameraDirector.ShotType.Tracking, offset = new Vector3(-1.5f, 3.2f, -10f), lookHeight = 1.3f, fieldOfView = 58f, blendIn = 2f },
                new IntroCameraDirector.Shot { label = "Highlands crane", startTravelled = k[5] - 10f, type = IntroCameraDirector.ShotType.Anchored, anchorPosition = Anchor(k[6] + 5f, 7.5f, 8f), anchorDrift = new Vector3(0.4f, 0.15f, 0f), lookHeight = 1f, fieldOfView = 45f, blendIn = 0f },
                new IntroCameraDirector.Shot { label = "Canyon entry - low", startTravelled = k[7] - 25f, type = IntroCameraDirector.ShotType.Tracking, offset = new Vector3(-2.2f, 1.5f, -7.5f), lookHeight = 1.2f, fieldOfView = 64f, blendIn = 1.2f },
                new IntroCameraDirector.Shot { label = "Canyon - pass-by", startTravelled = k[8] + 5f, type = IntroCameraDirector.ShotType.Anchored, anchorPosition = Anchor(k[9] - 5f, -2.6f, 3.2f), anchorDrift = Vector3.zero, lookHeight = 1f, fieldOfView = 50f, blendIn = 0f },
                new IntroCameraDirector.Shot { label = "Overlook reveal", startTravelled = k[9] + 4f, type = IntroCameraDirector.ShotType.Tracking, offset = new Vector3(0f, 5.5f, -15f), lookHeight = 2.2f, fieldOfView = 60f, blendIn = 2.2f },
            };
        }

        /// <summary>The farthest point on the given stretch of route with an unobstructed view of the summit.</summary>
        static Vector3 VolcanoVista(RouteTrack track, TerrainGrid g, float from, float to, Vector3 summit)
        {
            Vector3 best = track.PositionAt(from) + Vector3.up * 12f;
            float bestDist = 0f;
            for (float dist = from; dist < to; dist += 10f)
            {
                Vector3 eye = track.PositionAt(dist) + Vector3.up * 12f;
                float span = Vector3.Distance(eye, summit);
                if (span <= bestDist) continue;
                bool clear = true;
                for (float t = 0.04f; t < 0.96f && clear; t += 0.01f)
                {
                    Vector3 q = Vector3.Lerp(eye, summit, t);
                    clear = g.Height(q.x, q.z) < q.y - 3f;
                }
                if (clear) { best = eye; bestDist = span; }
            }
            return best;
        }

        static OverviewCamera.Viewpoint[] BuildViewpoints(Transform root, WorldDesign d, TerrainGrid g, RouteTrack track, RouteSamples route)
        {
            AssetUtil.DestroyChildren(root);
            var list = new List<OverviewCamera.Viewpoint>();
            void Add(string label, Vector3 pos, Vector3 lookAt, float fov, Vector3 drift)
            {
                var go = new GameObject($"VP_{list.Count + 1}_{label.Replace(' ', '_')}");
                go.transform.SetParent(root, false);
                pos.y = Mathf.Max(pos.y, g.Height(pos.x, pos.z) + 2f);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
                list.Add(new OverviewCamera.Viewpoint { label = label, anchor = go.transform, fieldOfView = fov, drift = drift, driftPeriod = 45f });
            }
            var k = route.knotDistances;
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
            Vector3 volcanoTop = V(d.volcano.x, g.Height(d.volcano.x, d.volcano.y) + 40f, d.volcano.y);
            Add("Aerial overview", V(70f, 420f, 40f), V(600f, 60f, 620f), 55f, new Vector3(0.8f, 0f, 0f));
            Vector3 camp = track.PositionAt(0f);
            Add("Pine Hollow basecamp", camp + V(-26f, 9f, -22f), camp + V(20f, 0f, 14f), 50f, new Vector3(0.3f, 0f, 0.1f));
            Vector3 canyon = track.PositionAt(k[8]);
            Add("The canyon", track.PositionAt(k[7] - 10f) + V(0f, 7f, 0f), track.PositionAt(k[9] + 30f) + V(0f, 5f, 0f), 55f, new Vector3(0f, 0.05f, 0.25f));
            Vector3 overlook = track.PositionAt(k[10]);
            Vector3 basinCentre = V(d.basin.x, g.Height(d.basin.x, d.basin.y) + 30f, d.basin.y);
            Add("Ember Overlook", overlook + V(-10f, 7f, -12f), Vector3.Lerp(basinCentre, volcanoTop, 0.45f), 56f, new Vector3(0.25f, 0f, 0f));
            Vector3 cauldron = new Vector3(d.lavaPools[0].centre.x, 0f, d.lavaPools[0].centre.y);
            cauldron.y = g.Height(cauldron.x, cauldron.z);
            Add("The Cauldron", cauldron + V(-34f, 16f, -30f), cauldron, 48f, new Vector3(0.35f, 0f, 0f));
            Vector3 field = track.PositionAt(k[14]);
            Add("Lava field beacons", track.PositionAt(k[13] - 15f) + V(0f, 3.5f, 0f), field + V(0f, 1f, 0f), 50f, new Vector3(0f, 0f, 0.3f));
            Add("Volcano from the plains", VolcanoVista(track, g, k[16], route.length, volcanoTop), volcanoTop + V(0f, -30f, 0f), 42f, new Vector3(0.4f, 0f, 0f));
            return list.ToArray();
        }
    }
}
