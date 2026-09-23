using System.IO;
using CinderPass.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CinderPass.EditorTools
{
    /// <summary>Project-level settings the game depends on (layers, physics rate, URP quality, build scene).</summary>
    public static class ProjectConfigurator
    {
        public static void Apply()
        {
            // Layer 8 = Vehicle (camera/respawn probes exclude it).
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(VehicleAssembler.VehicleLayer).stringValue = "Vehicle";
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            // 60 Hz physics for stable wheel/suspension simulation. Unity 6 stores the step as a rational
            // (count / rate); write it to the TimeManager asset so it survives an editor restart.
            Time.fixedDeltaTime = 1f / 60f;
            Time.maximumDeltaTime = 0.1f;
            var timeManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            var count = timeManager.FindProperty("Fixed Timestep.m_Count");
            var numerator = timeManager.FindProperty("Fixed Timestep.m_Rate.m_Numerator");
            var denominator = timeManager.FindProperty("Fixed Timestep.m_Rate.m_Denominator");
            if (count != null && numerator != null && denominator != null && denominator.intValue > 0)
                count.intValue = (int)System.Math.Round((double)numerator.intValue / denominator.intValue / 60.0);
            else
                SetFloat(timeManager, "Fixed Timestep", 1f / 60f);
            SetFloat(timeManager, "Maximum Allowed Timestep", 0.1f);
            timeManager.ApplyModifiedPropertiesWithoutUndo();

            // Physics: a little more solver precision for the vehicle, default contact offset.
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;

            // URP asset of the active quality level (PC): far, soft, 4-cascade shadows suited to a large
            // outdoor level. The project assigns pipelines per quality level, so the global default is empty.
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                var so = new SerializedObject(pipeline);
                Set(so, "m_ShadowDistance", 170f);
                Set(so, "m_ShadowCascadeCount", 4);
                var split = so.FindProperty("m_Cascade4Split");
                if (split != null) split.vector3Value = new Vector3(0.05f, 0.16f, 0.4f);
                SetRaw(so, "m_MainLightShadowmapResolution", 4096);
                Set(so, "m_SoftShadowsSupported", true);
                SetRaw(so, "m_SoftShadowQuality", 2); // Medium
                Set(so, "m_ShadowDepthBias", 0.6f);
                Set(so, "m_ShadowNormalBias", 0.6f);
                Set(so, "m_RequireDepthTexture", true);
                Set(so, "m_RequireOpaqueTexture", true);
                Set(so, "m_SupportsHDR", true);
                Set(so, "m_EnableLODCrossFade", true);
                SetRaw(so, "m_ColorGradingMode", 1); // HDR grading
                so.ApplyModifiedPropertiesWithoutUndo();
                if (pipeline is UniversalRenderPipelineAsset urp) EnsureAtmosphereFeature(urp);
            }
            QualitySettings.lodBias = 1.3f;

            // HUD font (SIL Open Font License) copied with its licence into the project's UI folder.
            AssetUtil.EnsureFolder(CinderPaths.Fonts);
            const string fontSrc = "Assets/PROMETEO - Car Controller/Fonts/Teko-Bold.ttf";
            string fontDst = $"{CinderPaths.Fonts}/Teko-Bold.ttf";
            if (!File.Exists(fontDst) && File.Exists(fontSrc))
            {
                AssetDatabase.CopyAsset(fontSrc, fontDst);
                File.Copy("Assets/PROMETEO - Car Controller/Fonts/Licence.txt", $"{CinderPaths.Fonts}/Teko-OFL-Licence.txt", true);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(CinderPaths.Scene, true) };
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Keep simulating when the window loses focus (editor play mode and builds) - important for demos.
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds the AtmosphereFeature to every renderer of the pipeline asset (once), referencing its shader so builds include it.</summary>
        static void EnsureAtmosphereFeature(UniversalRenderPipelineAsset pipeline)
        {
            var list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                AtmosphereFeature feature = null;
                foreach (var f in data.rendererFeatures) if (f is AtmosphereFeature a) feature = a;
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<AtmosphereFeature>();
                    feature.name = "CinderPass Atmosphere";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var dso = new SerializedObject(data);
                    var features = dso.FindProperty("m_RendererFeatures");
                    var map = dso.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                }
                var fso = new SerializedObject(feature);
                fso.FindProperty("shader").objectReferenceValue = AssetUtil.FindShader("Hidden/CinderPass/Atmosphere");
                fso.ApplyModifiedPropertiesWithoutUndo();
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }
        }

        static void SetFloat(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
        }

        /// <summary>Sets an enum field by its underlying value (e.g. a shadow resolution of 4096), not its index.</summary>
        static void SetRaw(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[ProjectConfigurator] Missing pipeline field {name}"); return; }
            p.intValue = value;
        }

        static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[ProjectConfigurator] Missing pipeline field {name}"); return; }
            Wiring.Assign(p, value);
        }
    }
}
