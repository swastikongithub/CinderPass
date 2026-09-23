using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Entry point for all content generation. Steps are separate so art can be iterated without rebuilding
    /// everything; "Build Everything" runs them in order. Each run reports to the console and to
    /// Temp/cp_job.txt (used by automation to poll long builds).
    /// </summary>
    public static class WorldBuildPipeline
    {
        const string StatusFile = "Temp/cp_job.txt";

        [MenuItem("Cinder Pass/Build/1  Project Settings + Source Assets", priority = 1)]
        public static void MenuAssets() => Run("Assets", BuildAssets);

        [MenuItem("Cinder Pass/Build/2  Prefabs (vegetation, geology, props, beacon, vehicle, VFX)", priority = 2)]
        public static void MenuPrefabs() => Run("Prefabs", BuildPrefabs);

        [MenuItem("Cinder Pass/Build/3  World + Scene", priority = 3)]
        public static void MenuScene() => Run("Scene", BuildScene);

        [MenuItem("Cinder Pass/Build/4  Bake Environment Lighting", priority = 4)]
        public static void MenuLighting() => Run("Lighting", BakeLighting);

        [MenuItem("Cinder Pass/Data/Reset World Design To Defaults", priority = 60)]
        public static void ResetWorldDesign()
        {
            // Copies code defaults into the existing asset (GUID and references preserved).
            var design = AssetUtil.LoadOrCreate<WorldDesign>(SceneAssembler.DesignPath);
            var fresh = ScriptableObject.CreateInstance<WorldDesign>();
            EditorUtility.CopySerialized(fresh, design);
            UnityEngine.Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(design);
            AssetDatabase.SaveAssets();
            Debug.Log("[CinderPass] World design reset to defaults. Rebuild the scene to apply.");
        }

        [MenuItem("Cinder Pass/Build/Build Everything", priority = 20)]
        public static void MenuAll() => Run("All", () => BuildAssets() + BuildPrefabs() + BuildScene() + BakeLighting());

        /// <summary>Runs a step on the next editor tick (so automation calls return immediately) and records the outcome.</summary>
        public static void Run(string name, Func<string> step)
        {
            File.WriteAllText(StatusFile, $"running {name}");
            EditorApplication.delayCall += () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    string report = step();
                    AssetDatabase.SaveAssets();
                    File.WriteAllText(StatusFile, $"done {name} {sw.Elapsed.TotalSeconds:0.0}s\n{report}");
                    Debug.Log($"[CinderPass] {name} finished in {sw.Elapsed.TotalSeconds:0.0}s\n{report}");
                }
                catch (Exception e)
                {
                    File.WriteAllText(StatusFile, $"error {name}\n{e}");
                    Debug.LogException(e);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            };
        }

        public static string BuildAssets()
        {
            ProjectConfigurator.Apply();
            TextureBaker.BakeNoise();
            TextureBaker.BakeParticleSprites();
            TextureBaker.BakeBranchCard();
            TextureBaker.BakeGrassCards();
            TextureBaker.BakeCharredBark();
            TextureBaker.BakeFernAlbedo();
            TextureBaker.BakeGroundMasks();
            AudioFactory.BuildAll();
            MaterialLibrary.BuildTerrainLayers();
            MaterialLibrary.TerrainMaterial();
            VfxFactory.BuildMaterials();
            return "Project settings, baked textures, audio, terrain layers, FX materials\n";
        }

        public static string BuildPrefabs()
        {
            VfxFactory.BuildMaterials();
            var fern = MaterialLibrary.Foliage("M_Fern", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.FernAlbedoPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{CinderPaths.Models}/fern_02/textures/fern_02_nor_gl_1k.exr"),
                new Color(0.85f, 0.9f, 0.8f), 0.4f, 0.6f, false, new Color(0.5f, 0.6f, 0.2f));
            var models = ModelLibrary.BuildAll(fern);

            var bark = MaterialLibrary.Lit("Foliage", "M_Bark", new MaterialLibrary.LitSpec
            {
                albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CinderPaths.FoliageTextures}/pine_tree_01_bark_diff.jpg"),
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CinderPaths.FoliageTextures}/pine_tree_01_bark_nor_gl.jpg"),
                color = new Color(0.8f, 0.78f, 0.76f), smoothness = 0.12f, tiling = new Vector2(1f, 1f)
            });
            var branches = MaterialLibrary.Foliage("M_FirBranches", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.BranchAlbedoPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.BranchNormalPath), new Color(0.84f, 0.9f, 0.84f), 0.42f, 1f, false, new Color(0.45f, 0.55f, 0.18f));
            var charred = MaterialLibrary.Lit("Foliage", "M_CharredBark", new MaterialLibrary.LitSpec
            {
                albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.CharredBarkPath),
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CinderPaths.FoliageTextures}/pine_tree_01_bark_nor_gl.jpg"),
                smoothness = 0.08f
            });
            var grass = MaterialLibrary.Foliage("M_Grass", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.GrassAlbedoPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.GrassNormalPath), new Color(0.9f, 0.92f, 0.82f), 0.38f, 1.4f, true, new Color(0.55f, 0.62f, 0.2f));
            var grassDry = MaterialLibrary.Foliage("M_GrassDry", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.GrassDryAlbedoPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.GrassNormalPath), new Color(0.86f, 0.8f, 0.66f), 0.38f, 1.4f, true, new Color(0.6f, 0.5f, 0.2f));

            var firs = VegetationBuilder.BuildFirs(bark, branches);
            var snags = VegetationBuilder.BuildSnags(charred);
            GrassPrefab("Grass_Lush", VegetationBuilder.BuildGrassClump("Grass_Lush", 1), grass);
            GrassPrefab("Grass_Dry", VegetationBuilder.BuildGrassClump("Grass_Dry", 2), grassDry);

            VfxFactory.VolcanoPlume();
            VfxFactory.Eruption();
            VfxFactory.Fumarole();
            VfxFactory.AshFall();
            VfxFactory.HazardBurst();

            BeaconSystem.BuildPrefab();
            VehicleAssembler.Build();
            return $"Prefabs: {models.Count} models, {firs.Count} firs, {snags.Count} snags, grass, VFX, beacon, vehicle\n";
        }

        static void GrassPrefab(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            VegetationBuilder.SavePrefab(go, $"{CinderPaths.EnvPrefabs}/Vegetation/{name}.prefab");
        }

        public static string BuildScene() => SceneAssembler.Build();

        public static string BakeLighting()
        {
            var settingsPath = $"{CinderPaths.Settings}/LS_CinderPass.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "LS_CinderPass" };
                AssetUtil.EnsureFolder(CinderPaths.Settings);
                AssetDatabase.CreateAsset(ls, settingsPath);
            }
            // Fully real-time lighting: only the environment (sky ambient + reflections) is generated.
            ls.bakedGI = false;
            ls.realtimeGI = false;
            Lightmapping.lightingSettings = ls;
            Lightmapping.Bake();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "Environment lighting generated\n";
        }
    }
}
